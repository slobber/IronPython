// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using Microsoft.Win32.SafeHandles;

using Mono.Unix;
using Mono.Unix.Native;

using IronPython.Runtime;
using IronPython.Runtime.Operations;

namespace IronPython.Modules {
    // This file contains exclusively functions used on POSIX systems, heavily dependent on Mono.Unix.
    // Every function in this part of PythonNT must have a platform guard preventing it from being used on Windows.
    // This isolates Mono.Unix from the rest of the code so that we don't try to load the Mono.Unix assembly on Windows.
    // The main implementation of module `nt` still may contain some code that is POSIX-specific, but without using Mono.Unix.
    public static partial class PythonNT {
#if FEATURE_NATIVE

        public const int POSIX_SPAWN_OPEN = 0;
        public const int POSIX_SPAWN_CLOSE = 1;
        public const int POSIX_SPAWN_DUP2 = 2;
        public const int POSIX_SPAWN_CLOSEFROM = 3;

        private const int SpawnFileActionsBufferSize = 256;
        private const int SpawnAttributesBufferSize = 4096;

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn_file_actions_init(
            IntPtr fileActions);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn_file_actions_destroy(
            IntPtr fileActions);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawnattr_init(
            IntPtr attributes);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawnattr_destroy(
            IntPtr attributes);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn_file_actions_addclose(
            IntPtr fileActions,
            int descriptor);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn_file_actions_adddup2(
            IntPtr fileActions,
            int descriptor,
            int newDescriptor);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn_file_actions_addopen(
            IntPtr fileActions,
            int descriptor,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int flags,
            uint mode);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn(
            out int pid,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
            IntPtr fileActions,
            IntPtr attributes,
            IntPtr argv,
            IntPtr envp);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawnp(
            out int pid,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
            IntPtr fileActions,
            IntPtr attributes,
            IntPtr argv,
            IntPtr envp);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static int posix_spawn(
            CodeContext context,
            [NotNone] object path,
            [NotNone] object args,
            [AllowNull] object env,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            if (kwargs is not null && kwargs.Count > 0) {
                foreach (object key in kwargs.Keys) {
                    if (key is not "file_actions" and not "setsigdef") {
                        throw PythonOps.NotImplementedError(
                            $"posix_spawn option {key!} is not supported");
                    }
                }
            }

            string executable = ConvertToFsString(
                context,
                path,
                nameof(path),
                "posix_spawn");
            string[] argv = ConvertSpawnArguments(context, args);
            string[]? envp = env is null
                ? GetCurrentEnvironment()
                : ConvertSpawnEnvironment(context, env);

            IntPtr actions = Marshal.AllocHGlobal(
                SpawnFileActionsBufferSize);
            int rc = posix_spawn_file_actions_init(actions);
            if (rc != 0) {
                Marshal.FreeHGlobal(actions);
                throw GetLastUnixError(executable);
            }

            IntPtr attributes = Marshal.AllocHGlobal(
                SpawnAttributesBufferSize);
            rc = posix_spawnattr_init(attributes);
            if (rc != 0) {
                posix_spawn_file_actions_destroy(actions);
                Marshal.FreeHGlobal(actions);
                Marshal.FreeHGlobal(attributes);
                throw GetLastUnixError(executable);
            }

            try {
                if (kwargs is not null
                    && kwargs.TryGetValue("file_actions", out object? value)
                    && value is not null) {
                    ApplySpawnFileActions(context, actions, value);
                }

                NativeStringArray nativeArgv = new NativeStringArray(argv);
                NativeStringArray nativeEnv = new NativeStringArray(envp);
                try {
                    int pid;
                    rc = executable.Contains(Path.DirectorySeparatorChar)
                        ? posix_spawn(
                            out pid,
                            executable,
                            actions,
                            attributes,
                            nativeArgv.Pointer,
                            nativeEnv.Pointer)
                        : posix_spawnp(
                            out pid,
                            executable,
                            actions,
                            attributes,
                            nativeArgv.Pointer,
                            nativeEnv.Pointer);
                    if (rc != 0) {
                        throw PythonOps.OSError(
                            rc,
                            $"posix_spawn failed: {executable}",
                            executable);
                    }
                    return pid;
                } finally {
                    nativeArgv.Dispose();
                    nativeEnv.Dispose();
                }
            } finally {
                posix_spawnattr_destroy(attributes);
                Marshal.FreeHGlobal(attributes);
                posix_spawn_file_actions_destroy(actions);
                Marshal.FreeHGlobal(actions);
            }
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        internal static PythonTuple WaitPidUnix(int pid, int options) {
            int status;
            int result = Syscall.waitpid(
                pid,
                out status,
                (WaitOptions)options);
            if (result < 0) {
                throw GetLastUnixError();
            }
            return PythonTuple.MakeTuple(result, status);
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void ApplySpawnFileActions(
            CodeContext context,
            IntPtr actions,
            object fileActions) {
            IEnumerator? enumerator;
            if (!PythonOps.TryGetEnumerator(
                    context,
                    fileActions,
                    out enumerator)
                || enumerator is null) {
                throw PythonOps.TypeError(
                    "file_actions must be a sequence");
            }

            try {
                while (enumerator.MoveNext()) {
                    if (enumerator.Current is not PythonTuple action
                        || action.Count == 0) {
                        throw PythonOps.TypeError(
                            "file_actions entries must be tuples");
                    }
                    int kind = PythonOps.Index(action[0]) is int value
                        ? value
                        : throw PythonOps.OverflowError(
                            "file action kind is too large");
                    int rc;
                    switch (kind) {
                        case POSIX_SPAWN_OPEN:
                            rc = posix_spawn_file_actions_addopen(
                                actions,
                                ToInt(action[1]!),
                                ConvertToFsString(
                                    context,
                                    action[2]!,
                                    "file action path",
                                    "posix_spawn"),
                                ToInt(action[3]!),
                                (uint)ToInt(action[4]!));
                            break;
                        case POSIX_SPAWN_CLOSE:
                            rc = posix_spawn_file_actions_addclose(
                                actions,
                                ToInt(action[1]!));
                            break;
                        case POSIX_SPAWN_DUP2:
                            rc = posix_spawn_file_actions_adddup2(
                                actions,
                                ToInt(action[1]!),
                                ToInt(action[2]!));
                            break;
                        case POSIX_SPAWN_CLOSEFROM:
                            int start = ToInt(action[1]!);
                            rc = 0;
                            for (int fd = start; fd < 1024; fd++) {
                                rc = posix_spawn_file_actions_addclose(
                                    actions,
                                    fd);
                                if (rc != 0) {
                                    break;
                                }
                            }
                            break;
                        default:
                            throw PythonOps.ValueError(
                                "invalid file action");
                    }

                    if (rc != 0) {
                        throw GetLastUnixError();
                    }
                }
            } finally {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        private static string[] ConvertSpawnArguments(
            CodeContext context,
            object args) {
            IEnumerator? enumerator;
            if (!PythonOps.TryGetEnumerator(context, args, out enumerator)
                || enumerator is null) {
                throw PythonOps.TypeError("args must be a sequence");
            }

            var result = new List<string>();
            try {
                while (enumerator.MoveNext()) {
                    result.Add(ConvertToFsString(
                        context,
                        enumerator.Current,
                        "args element",
                        "posix_spawn"));
                }
            } finally {
                (enumerator as IDisposable)?.Dispose();
            }
            return result.ToArray();
        }

        private static string[] ConvertSpawnEnvironment(
            CodeContext context,
            object env) {
            if (env is not IEnumerable<KeyValuePair<object?, object?>> values) {
                throw PythonOps.TypeError("env must be a mapping");
            }

            var result = new List<string>();
            foreach (KeyValuePair<object?, object?> item in values) {
                result.Add(
                    ConvertToFsString(
                        context,
                        item.Key,
                        "environment key",
                        "posix_spawn")
                    + "="
                    + ConvertToFsString(
                        context,
                        item.Value,
                        "environment value",
                        "posix_spawn"));
            }
            return result.ToArray();
        }

        private static string[] GetCurrentEnvironment() {
            var result = new List<string>();
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables()) {
                result.Add(item.Key + "=" + item.Value);
            }
            return result.ToArray();
        }

        private static int ToInt(object value) {
            object index = PythonOps.Index(value);
            return index is int result
                ? result
                : throw PythonOps.OverflowError(
                    "file action value is too large");
        }

        private sealed class NativeStringArray : IDisposable {
            private readonly IntPtr[] _strings;
            internal IntPtr Pointer { get; }

            internal NativeStringArray(string[]? values) {
                values ??= Array.Empty<string>();
                _strings = new IntPtr[values.Length];
                Pointer = Marshal.AllocHGlobal(
                    (values.Length + 1) * IntPtr.Size);
                for (int i = 0; i < values.Length; i++) {
                    _strings[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
                    Marshal.WriteIntPtr(
                        Pointer,
                        i * IntPtr.Size,
                        _strings[i]);
                }
                Marshal.WriteIntPtr(
                    Pointer,
                    values.Length * IntPtr.Size,
                    IntPtr.Zero);
            }

            public void Dispose() {
                foreach (IntPtr value in _strings) {
                    Marshal.FreeCoTaskMem(value);
                }
                Marshal.FreeHGlobal(Pointer);
            }
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        internal static Exception GetLastUnixError(string? filename = null, string? filename2 = null)
            // On POSIX, GetLastWin32Error returns the errno value, same as GetLastPInvokeError
            => GetOsError(Marshal.GetLastWin32Error(), filename, filename2);


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static int strerror_r(int code, StringBuilder buffer)
            => Syscall.strerror_r(NativeConvert.ToErrno(code), buffer);


#if FEATURE_PIPES
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static Tuple<int, Stream, int, Stream> CreatePipeStreamsUnix() {
            UnixPipes pipes = UnixPipes.CreatePipes();
            return Tuple.Create<int, Stream, int, Stream>(pipes.Reading.Handle, pipes.Reading, pipes.Writing.Handle, pipes.Writing);
        }
#endif


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static int DuplicateStreamDescriptorUnix(int fd, int targetfd, out Stream? stream) {
            int res = targetfd < 0 ? Syscall.dup(fd) : Syscall.dup2(fd, targetfd);
            if (res < 0) throw GetLastUnixError();

            if (ClrModule.IsMono) {
                // Elaborate workaround on Mono to avoid UnixStream as out
                stream = new UnixStream(res, ownsHandle: false);
                FileAccess fileAccess = stream.CanWrite ? stream.CanRead ? FileAccess.ReadWrite : FileAccess.Write : FileAccess.Read;
                stream.Dispose();
                try {
                    // FileStream on Mono created with a file descriptor might not work: https://github.com/mono/mono/issues/12783
                    // Test if it does, without closing the handle if it doesn't
                    var sfh = new SafeFileHandle((IntPtr)res, ownsHandle: false);
                    stream = new FileStream(sfh, fileAccess);
                    // No exception? Great! We can use FileStream.
                    stream.Dispose();
                    sfh.Dispose();
                    stream = null; // Create outside of try block
                } catch (IOException) {
                    // Fall back to UnixStream
                    stream = new UnixStream(res, ownsHandle: true);
                }
                if (stream is null) {
                    // FileStream is safe
                    var sfh = new SafeFileHandle((IntPtr)res, ownsHandle: true);
                    stream = new FileStream(sfh, fileAccess);
                }
            } else {
                // normal case
                stream = new PosixFileStream(res);
            }
            return res;
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        internal static int dupUnix(int fd, bool closeOnExec) {
            int fd2 = Syscall.dup(fd);
            if (fd2 == -1) throw GetLastUnixError();

            if (closeOnExec) {
                try {
                    // set close-on-exec flag
                    int flags = Syscall.fcntl(fd2, FcntlCommand.F_GETFD);
                    if (flags == -1) throw GetLastUnixError();

                    flags |= PythonFcntl.FD_CLOEXEC;
                    flags = Syscall.fcntl(fd2, FcntlCommand.F_SETFD, flags);
                    if (flags == -1) throw GetLastUnixError();
                } catch {
                    Syscall.close(fd2);
                    throw;
                }
            }

            return fd2;
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void chmodUnix(string path, int mode) {
            if (Syscall.chmod(path, NativeConvert.ToFilePermissions((uint)mode)) == 0) return;
            throw GetLastUnixError(path);
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static int umaskUnix(int mask)
            => (int)NativeConvert.FromFilePermissions(Syscall.umask(NativeConvert.ToFilePermissions((uint)mask)));

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void setBlockingUnix(int fd, bool blocking) {
            int flags = Syscall.fcntl(fd, FcntlCommand.F_GETFL);
            if (flags == -1) {
                throw GetLastUnixError();
            }
            if (blocking) {
                flags &= ~(int)OpenFlags.O_NONBLOCK;
            } else {
                flags |= (int)OpenFlags.O_NONBLOCK;
            }
            if (Syscall.fcntl(
                    fd,
                    FcntlCommand.F_SETFL,
                    flags) == -1) {
                throw GetLastUnixError();
            }
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static bool getBlockingUnix(int fd) {
            int flags = Syscall.fcntl(fd, FcntlCommand.F_GETFL);
            if (flags == -1) {
                throw GetLastUnixError();
            }
            return (flags & (int)OpenFlags.O_NONBLOCK) == 0;
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void linkUnix(string src, string dst) {
            if (Syscall.link(src, dst) == 0) return;
            throw GetLastUnixError(src, dst);
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void symlinkUnix(string src, string dst) {
            if (Syscall.symlink(src, dst) == 0) return;
            throw GetLastUnixError(src, dst);
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void renameUnix(string src, string dst) {
            if (Syscall.rename(src, dst) == 0) return;
            throw GetLastUnixError(src, dst);
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static object statUnix(string path) {
            if (Syscall.stat(path, out Stat buf) == 0) {
                return new stat_result(buf);
            }
            return LightExceptions.Throw(GetLastUnixError(path));
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static object lstatUnix(string path) {
            if (Syscall.lstat(path, out Stat buf) == 0) {
                return new stat_result(buf);
            }
            return LightExceptions.Throw(GetLastUnixError(path));
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static object fstatUnix(int fd) {
            if (Syscall.fstat(fd, out Stat buf) == 0) {
                return new stat_result(buf);
            }
            return LightExceptions.Throw(GetLastUnixError());
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        internal static void ftruncateUnix(int fd, long length) {
            int result;
            Errno errno;
            do {
                result = Syscall.ftruncate(fd, length);
            } while (UnixMarshal.ShouldRetrySyscall(result, out errno));

            if (errno != 0)
                throw GetOsError(NativeConvert.FromErrno(errno));
        }


        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static uname_result uname() {
            if (Syscall.uname(out Utsname info) == 0) {
                return new uname_result(info.sysname, info.nodename, info.release, info.version, info.machine);
            }
            throw GetLastUnixError();  // rare
        }

        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [Documentation("Return a string representing the path to which the symbolic link points.")]
        public static string readlink(CodeContext context, [NotNone] string path, [ParamDictionary] IDictionary<string, object> kwargs)
            => readlinkUnix(path, kwargs).ToFsString(context);

        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [Documentation("")]
        public static Bytes readlink(CodeContext context, [NotNone] Bytes path, [ParamDictionary] IDictionary<string, object> kwargs)
            => readlinkUnix(path.ToFsString(context), kwargs);

        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [Documentation("")]
        public static object readlink(CodeContext context, object? path, [ParamDictionary] IDictionary<string, object> kwargs) {
            return ToFsPath(context, path, nameof(path)) switch {
                string s => readlink(context, s, kwargs),
                Extensible<string> es => readlink(context, es.Value, kwargs),
                Bytes b => readlink(context, b, kwargs),
                _ => throw new InvalidOperationException(),
            };
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static Bytes readlinkUnix(string path, IDictionary<string, object> kwargs) {
            foreach (var key in kwargs.Keys) {
                switch (key) {
                    case "dir_fd":
                        if (kwargs[key] is not null) throw new NotImplementedException();
                        break;
                    default:
                        throw PythonOps.TypeError("'{0}' is an invalid keyword argument for readlink()", key);
                }
            }

            var buf = new byte[4096];
            while (true) {
                long len = Syscall.readlink(path, buf);
                if (len < 0) throw GetLastUnixError(path);
                if (len < buf.Length) return Bytes.Make(buf.AsSpan(0, (int)len).ToArray());
                buf = new byte[buf.Length * 2];
            }
        }

        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static BigInteger getuid() {
            return Syscall.getuid();
        }

        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static BigInteger geteuid() {
            return Syscall.geteuid();
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void utimeUnix(string path, long atime_ns, long utime_ns) {
            var atime = new Timespec {
                tv_sec = atime_ns / 1_000_000_000,
                tv_nsec = atime_ns % 1_000_000_000
            };
            var utime = new Timespec {
                tv_sec = utime_ns / 1_000_000_000,
                tv_nsec = utime_ns % 1_000_000_000
            };

            if (Syscall.utimensat(Syscall.AT_FDCWD, path, [atime, utime], 0) == 0) return;
            throw GetLastUnixError(path);
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void killUnix(int pid, int sig) {
            if (Syscall.kill(pid, NativeConvert.ToSignum(sig)) == 0) return;
            throw GetLastUnixError();
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static bool isattyUnix(int fd) {
            return Syscall.isatty(fd);
        }

#endif
    }
}
