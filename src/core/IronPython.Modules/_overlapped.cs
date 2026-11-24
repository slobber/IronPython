// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

using IronPython.Runtime;
using IronPython.Runtime.Operations;

[assembly: PythonModule("_overlapped", typeof(IronPython.Modules.PythonOverlapped), PlatformsAttribute.PlatformFamily.Windows)]
namespace IronPython.Modules {

    [SupportedOSPlatform("windows")]
    public static class PythonOverlapped {
        public const int ERROR_NETNAME_DELETED = 64;
        public const int ERROR_SEM_TIMEOUT = 121;
        public const int ERROR_PIPE_BUSY = 231;
        public const int ERROR_IO_PENDING = 997;
        public const int ERROR_OPERATION_ABORTED = 995;
        public const int WAIT_TIMEOUT = 258;

        public const int NULL = 0;
        public const uint INFINITE = 0xffffffff;
        public const uint TF_REUSE_SOCKET = 2;
        public const int SO_UPDATE_ACCEPT_CONTEXT = 0x700b;
        public const int SO_UPDATE_CONNECT_CONTEXT = 0x7010;

        public static readonly BigInteger INVALID_HANDLE_VALUE = IntPtr.Size == 4 ? unchecked((uint)new IntPtr(-1)) : unchecked((ulong)new IntPtr(-1));

        private static readonly ConcurrentDictionary<ulong, IntPtr> _completionPorts = new();

        [DllImport("kernel32.dll", EntryPoint = "CreateIoCompletionPort", SetLastError = true)]
        private static extern IntPtr _CreateIoCompletionPort(IntPtr FileHandle, IntPtr ExistingCompletionPort, UIntPtr CompletionKey, uint NumberOfConcurrentThreads);

        [DllImport("kernel32.dll", EntryPoint = "GetQueuedCompletionStatus", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool _GetQueuedCompletionStatus(
            IntPtr CompletionPort,
            out uint NumberOfBytesTransferred,
            out UIntPtr CompletionKey,
            out IntPtr Overlapped,
            uint Milliseconds);

        [DllImport("kernel32.dll", EntryPoint = "PostQueuedCompletionStatus", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool _PostQueuedCompletionStatus(
            IntPtr CompletionPort,
            uint NumberOfBytesTransferred,
            UIntPtr CompletionKey,
            IntPtr Overlapped);

        [DllImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr _CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetEvent(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ResetEvent(IntPtr handle);

        public static BigInteger CreateIoCompletionPort(
            [NotNone] object handle,
            [NotNone] object port,
            [NotNone] object key,
            uint concurrency) {
            BigInteger nativeHandle = AsBigInteger(handle);
            BigInteger nativePort = AsBigInteger(port);
            BigInteger nativeKey = AsBigInteger(key);
            var res = _CreateIoCompletionPort(
                ToNativeInt(nativeHandle),
                ToNativeInt(nativePort),
                checked((UIntPtr)(ulong)nativeKey),
                concurrency);
            if (res == IntPtr.Zero) {
                throw PythonNT.GetLastWin32Error();
            }

            if (nativeHandle != INVALID_HANDLE_VALUE && nativePort != BigInteger.Zero) {
                _completionPorts[checked((ulong)nativeHandle)] = ToNativeInt(nativePort);
            }
            return new BigInteger(unchecked((ulong)res));
        }

        public static PythonTuple? GetQueuedCompletionStatus([NotNone] object port, uint milliseconds) {
            IntPtr nativePort = ToNativeInt(AsBigInteger(port));
            bool succeeded = _GetQueuedCompletionStatus(
                nativePort,
                out uint transferred,
                out UIntPtr key,
                out IntPtr address,
                milliseconds);

            int error = succeeded ? 0 : Marshal.GetLastWin32Error();
            if (!succeeded && address == IntPtr.Zero && error == WAIT_TIMEOUT) {
                return null;
            }

            return PythonTuple.MakeTuple(
                error,
                transferred,
                new BigInteger(unchecked((ulong)key)),
                new BigInteger(unchecked((ulong)address)));
        }

        public static void PostQueuedCompletionStatus(
            [NotNone] object port,
            uint transferred,
            [NotNone] object key,
            [NotNone] object address) {
            if (!_PostQueuedCompletionStatus(
                ToNativeInt(AsBigInteger(port)),
                transferred,
                new UIntPtr(unchecked((ulong)AsBigInteger(key))),
                ToNativeInt(AsBigInteger(address)))) {
                throw PythonNT.GetLastWin32Error();
            }
        }

        public static BigInteger CreateEvent(
            object? eventAttributes,
            bool manualReset,
            bool initialState,
            string? name) {
            IntPtr handle = _CreateEvent(
                eventAttributes is null ? IntPtr.Zero : ToNativeInt(AsBigInteger(eventAttributes)),
                manualReset,
                initialState,
                name);
            if (handle == IntPtr.Zero) throw PythonNT.GetLastWin32Error();
            return new BigInteger(unchecked((ulong)handle));
        }

        public static bool SetEvent([NotNone] object handle) {
            if (!SetEvent(ToNativeInt(AsBigInteger(handle)))) {
                throw PythonNT.GetLastWin32Error();
            }
            return true;
        }

        public static bool ResetEvent([NotNone] object handle) {
            if (!ResetEvent(ToNativeInt(AsBigInteger(handle)))) {
                throw PythonNT.GetLastWin32Error();
            }
            return true;
        }

        [PythonType("Overlapped")]
        public sealed class Overlapped : IDisposable {
            private readonly NativeOverlapped[] _native = new NativeOverlapped[1];
            private GCHandle _pinned;
            private readonly IntPtr _event;
            private byte[]? _buffer;
            private object? _result;
            private int _error;
            private int _pending;
            private int _completed;
            private int _completionPosted;
            private int _cancelled;
            private IntPtr _completionPort;

            public Overlapped(object? eventHandle) {
                BigInteger eventValue = eventHandle is null ? BigInteger.Zero : AsBigInteger(eventHandle);
                _event = eventValue.IsZero
                    ? _CreateEvent(IntPtr.Zero, true, false, null)
                    : ToNativeInt(eventValue);
                if (_event == IntPtr.Zero) throw PythonNT.GetLastWin32Error();

                _native[0].EventHandle = _event;
                _pinned = GCHandle.Alloc(_native, GCHandleType.Pinned);
            }

            public BigInteger address => new(unchecked((ulong)_pinned.AddrOfPinnedObject()));

            public BigInteger @event => new(unchecked((ulong)_event));

            public int error => _error;

            public bool pending => Volatile.Read(ref _pending) != 0;

            public void WSARecv([NotNone] object handle, int nbytes, int flags) {
                if (nbytes < 0) throw PythonOps.ValueError("buffer size must not be negative");

                byte[] buffer = new byte[nbytes];
                RunRead(handle, buffer, nbytes);
            }

            public object getresult() {
                if (Volatile.Read(ref _completed) == 0) throw PythonNT.GetOsError(ERROR_IO_PENDING);
                Interlocked.Exchange(ref _pending, 0);
                if (_error != 0) throw PythonNT.GetOsError(_error);
                return _result ?? 0;
            }

            public void cancel() {
                if (Interlocked.Exchange(ref _cancelled, 1) == 0) {
                    Complete(ERROR_OPERATION_ABORTED, 0);
                }
            }

            private void RunRead(object handle, byte[] buffer, int count) {
                _buffer = buffer;
                Interlocked.Exchange(ref _pending, 1);
                Interlocked.Exchange(ref _cancelled, 0);

                ulong nativeHandle = checked((ulong)AsBigInteger(handle));
                if (!_completionPorts.TryGetValue(nativeHandle, out IntPtr completionPort)) {
                    throw PythonOps.ValueError("handle is not associated with a completion port");
                }
                _completionPort = completionPort;

                Task.Run(() => {
                    try {
                        using var socket = new Socket(new SafeSocketHandle(new IntPtr(unchecked((long)nativeHandle)), ownsHandle: false));
                        var readable = new[] { socket };
                        Socket.Select(readable, null, null, -1);

                        if (Volatile.Read(ref _cancelled) != 0) {
                            Complete(ERROR_OPERATION_ABORTED, 0);
                            return;
                        }

                        int received = socket.Receive(buffer, 0, count, SocketFlags.None);
                        _result = new Bytes(buffer[..received]);
                        _error = 0;
                        Complete(0, checked((uint)received));
                    } catch (ObjectDisposedException) {
                        Complete(ERROR_OPERATION_ABORTED, 0);
                    } catch (SocketException exception) {
                        Complete(exception.ErrorCode, 0);
                    }
                });
            }

            private void Complete(int error, uint transferred) {
                if (Interlocked.Exchange(ref _completionPosted, 1) != 0) return;
                _error = error;
                Interlocked.Exchange(ref _completed, 1);

                if (_completionPort != IntPtr.Zero) Post(_completionPort, transferred);
            }

            private void Post(IntPtr completionPort, long transferred) {
                if (!_PostQueuedCompletionStatus(
                    completionPort,
                    checked((uint)transferred),
                    UIntPtr.Zero,
                    _pinned.AddrOfPinnedObject())) {
                    _error = Marshal.GetLastWin32Error();
                }
            }

            public void Dispose() {
                if (_pinned.IsAllocated) _pinned.Free();
            }

            ~Overlapped() => Dispose();
        }

        private static BigInteger AsBigInteger(object value)
            => value is BigInteger integer ? integer : Converter.ConvertToBigInteger(value);

        private static IntPtr ToNativeInt(BigInteger value) {
            if (value >= long.MinValue && value <= long.MaxValue) {
                return new IntPtr((long)value);
            }
            if (value >= 0 && value <= ulong.MaxValue) {
                return new IntPtr(unchecked((long)(ulong)value));
            }
            throw new OverflowException();
        }
    }
}
