// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#if FEATURE_SYNC_SOCKETS

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting.Runtime;

[assembly: PythonModule("select", typeof(IronPython.Modules.PythonSelect))]
namespace IronPython.Modules {
    public static class PythonSelect {
        public const string __doc__ = "Provides support for asynchronous socket operations.";

        public static PythonType error => PythonExceptions.OSError;

        #region Public API

        [Documentation("select(iwtd, owtd, ewtd[, timeout]) -> readlist, writelist, errlist\n\n"
            + "Block until sockets are available for reading or writing, until an error\n"
            + "occurs, or until a the timeout expires. The first three parameters are\n"
            + "sequences of socket objects (opened using the socket module). The last is a\n"
            + "timeout value, given in seconds as a float. If timeout is omitted, select()\n"
            + "blocks until at least one socket is ready. A timeout of zero never blocks, but\n"
            + "can be used for polling.\n"
            + "\n"
            + "The return value is a tuple of lists of sockets that are ready (subsets of\n"
            + "iwtd, owtd, and ewtd). If the timeout occurs before any sockets are ready, a\n"
            + "tuple of three empty lists is returned.\n"
            + "\n"
            + "Note that select() on IronPython works only with sockets; it will not work with\n"
            + "files or other objects."
            )]
        public static PythonTuple select(CodeContext/*!*/ context, object iwtd, object owtd, object ewtd, object timeout=null) {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                return SelectUnix(context, iwtd, owtd, ewtd, timeout);
            }

            PythonList readerList, writerList, errorList;
            Dictionary<Socket, object> readerOriginals, writerOriginals, errorOriginals;
            ProcessSocketSequence(context, iwtd, out readerList, out readerOriginals);
            ProcessSocketSequence(context, owtd, out writerList, out writerOriginals);
            ProcessSocketSequence(context, ewtd, out errorList, out errorOriginals);

            int timeoutMicroseconds;

            if (timeout == null) {
                // -1 doesn't really work as infinite, but it appears that any other negative value does
                timeoutMicroseconds = -2;
            } else {
                double timeoutSeconds;
                if (!Converter.TryConvertToDouble(timeout, out timeoutSeconds)) {
                    throw PythonOps.TypeErrorForTypeMismatch("float or None", timeout);
                }
                timeoutMicroseconds = (int) (1000000 * timeoutSeconds);
            }

            try {
                Socket.Select(readerList, writerList, errorList, timeoutMicroseconds);
            } catch (ArgumentNullException) {
                throw PythonSocket.MakeException(context, new SocketException((int)SocketError.InvalidArgument));
            } catch (SocketException e) {
                throw PythonSocket.MakeException(context, e);
            }

            // Convert back to what the user originally passed in
            for (int i = 0; i < readerList.__len__(); i++) readerList[i] = readerOriginals[(Socket)readerList[i]];
            for (int i = 0; i < writerList.__len__(); i++) writerList[i] = writerOriginals[(Socket)writerList[i]];
            for (int i = 0; i < errorList.__len__(); i++) errorList[i] = errorOriginals[(Socket)errorList[i]];

            return PythonTuple.MakeTuple(readerList, writerList, errorList);
        }

        private const short PollIn = 0x001;
        private const short PollOut = 0x004;
        private const short PollError = 0x008;
        private const short PollHup = 0x010;
        private const short PollNVal = 0x020;

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd {
            internal int fd;
            internal short events;
            internal short revents;
        }

        [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
        private static extern int poll(
            [In, Out] PollFd[] fds,
            nuint count,
            int timeout);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static PythonTuple SelectUnix(
            CodeContext context,
            object iwtd,
            object owtd,
            object ewtd,
            object timeout) {
            var readers = ReadFileDescriptorSequence(context, iwtd);
            var writers = ReadFileDescriptorSequence(context, owtd);
            var errors = ReadFileDescriptorSequence(context, ewtd);

            int count = readers.Count + writers.Count + errors.Count;
            var pollFds = new PollFd[count];
            int index = 0;
            foreach (SelectItem item in readers) {
                pollFds[index++] = new PollFd {
                    fd = item.Descriptor,
                    events = PollIn
                };
            }
            foreach (SelectItem item in writers) {
                pollFds[index++] = new PollFd {
                    fd = item.Descriptor,
                    events = PollOut
                };
            }
            foreach (SelectItem item in errors) {
                pollFds[index++] = new PollFd {
                    fd = item.Descriptor,
                    events = 0
                };
            }

            int timeoutMilliseconds;
            if (timeout is null) {
                timeoutMilliseconds = -1;
            } else {
                if (!Converter.TryConvertToDouble(
                        timeout,
                        out double seconds)) {
                    throw PythonOps.TypeErrorForTypeMismatch(
                        "float or None",
                        timeout);
                }
                if (seconds < 0) {
                    timeoutMilliseconds = -1;
                } else {
                    timeoutMilliseconds = (int)Math.Min(
                        Math.Ceiling(seconds * 1000),
                        int.MaxValue);
                }
            }

            int result;
            do {
                result = poll(
                    pollFds,
                    (nuint)pollFds.Length,
                    timeoutMilliseconds);
            } while (result < 0
                && Marshal.GetLastPInvokeError() == 4);
            if (result < 0) {
                throw PythonOps.OSError(
                    Marshal.GetLastPInvokeError(),
                    "poll failed");
            }

            var readyReaders = new PythonList();
            var readyWriters = new PythonList();
            var readyErrors = new PythonList();
            index = 0;
            foreach (SelectItem item in readers) {
                short events = pollFds[index++].revents;
                if ((events & (PollIn | PollHup | PollError)) != 0) {
                    readyReaders.append(item.Original);
                }
            }
            foreach (SelectItem item in writers) {
                short events = pollFds[index++].revents;
                if ((events & (PollOut | PollHup | PollError)) != 0) {
                    readyWriters.append(item.Original);
                }
            }
            foreach (SelectItem item in errors) {
                short events = pollFds[index++].revents;
                if ((events & (PollError | PollHup | PollNVal)) != 0) {
                    readyErrors.append(item.Original);
                }
            }
            return PythonTuple.MakeTuple(
                readyReaders,
                readyWriters,
                readyErrors);
        }

        private static List<SelectItem> ReadFileDescriptorSequence(
            CodeContext context,
            object sequence) {
            var result = new List<SelectItem>();
            IEnumerator cursor = PythonOps.GetEnumerator(context, sequence);
            while (cursor.MoveNext()) {
                object original = cursor.Current;
                int descriptor;
                if (original is PythonSocket.socket socket) {
                    descriptor = checked((int)socket.fileno());
                } else if (Converter.TryConvertToInt32(
                               original,
                               out int fd)) {
                    descriptor = fd;
                } else {
                    object fileno = PythonCalls.Call(
                        context,
                        PythonOps.GetBoundAttr(
                            context,
                            original,
                            "fileno"));
                    descriptor = Converter.ConvertToInt32(fileno);
                }
                if (descriptor < 0) {
                    throw PythonOps.ValueError(
                        "file descriptor cannot be a negative number ({0})",
                        descriptor);
                }
                result.Add(new SelectItem(descriptor, original));
            }
            return result;
        }

        private readonly struct SelectItem {
            internal int Descriptor { get; }
            internal object Original { get; }

            internal SelectItem(int descriptor, object original) {
                Descriptor = descriptor;
                Original = original;
            }
        }

        /// <summary>
        /// Process a sequence of objects that are compatible with ObjectToSocket(). Return two
        /// things as out params: an in-order List of sockets that correspond to the original
        /// objects in the passed-in sequence, and a mapping of these socket objects to their
        /// original objects.
        /// 
        /// The socketToOriginal mapping is generated because the CPython select module supports
        /// passing to select either file descriptor numbers or an object with a fileno() method.
        /// We try to be faithful to what was originally requested when we return.
        /// </summary>
        private static void ProcessSocketSequence(CodeContext context, object sequence, out PythonList socketList, out Dictionary<Socket, object> socketToOriginal) {
            socketToOriginal = new Dictionary<Socket, object>();
            socketList = new PythonList();

            IEnumerator cursor = PythonOps.GetEnumerator(context, sequence);
            while (cursor.MoveNext()) {
                object original = cursor.Current;
                Socket socket = ObjectToSocket(context, original);
                socketList.append(socket);
                socketToOriginal[socket] = original;
            }
        }

        /// <summary>
        /// Return the System.Net.Sockets.Socket object that corresponds to the passed-in
        /// object. obj can be a System.Net.Sockets.Socket, a PythonSocket.SocketObj, a
        /// long integer (representing a socket handle), or a Python object with a fileno()
        /// method (whose result is used to look up an existing PythonSocket.SocketObj,
        /// which is in turn converted to a Socket.
        /// </summary>
        private static Socket ObjectToSocket(CodeContext context, object obj) {
            Socket socket;
            PythonSocket.socket pythonSocket = obj as PythonSocket.socket;
            if (pythonSocket != null) {
                return pythonSocket._socket;
            }

            Int64 handle;
            if (!Converter.TryConvertToInt64(obj, out handle)) {
                object userSocket = obj;
                object filenoCallable = PythonOps.GetBoundAttr(context, userSocket, "fileno");
                object fileno = PythonCalls.Call(context, filenoCallable);
                handle = Converter.ConvertToInt64(fileno);
            }
            if (handle < 0) {
                throw PythonOps.ValueError("file descriptor cannot be a negative number ({0})", handle);
            }
            socket = PythonSocket.socket.HandleToSocket(handle);
            if (socket == null) {
                throw PythonSocket.MakeException(context, new SocketException((int)SocketError.NotSocket));
            }
            return socket;
        }

        #endregion
    }
}

#endif
