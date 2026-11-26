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

        private const short POSIX_SPAWN_CLOEXEC_DEFAULT = 0x4000;
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
        private static extern int posix_spawnattr_setflags(
            IntPtr attributes,
            short flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn_file_actions_addinherit_np(
            IntPtr fileActions,
            int descriptor);

        [DllImport("libc", SetLastError = true)]
        private static extern int posix_spawn_file_actions_addclose(
            IntPtr fileActions,
            int descriptor);

        [DllImport(
            "libc",
            EntryPoint = "posix_spawn_file_actions_addclosefrom_np",
            SetLastError = true)]
        private static extern int posix_spawn_file_actions_addclosefrom_np(
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

        [DllImport("libc", EntryPoint = "posix_spawn", SetLastError = true)]
        private static extern int posix_spawn(
            out int pid,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
            IntPtr fileActions,
            IntPtr attributes,
            IntPtr[] argv,
            IntPtr[] envp);

        [DllImport("libc", EntryPoint = "posix_spawnp", SetLastError = true)]
        private static extern int posix_spawnp(
            out int pid,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
            IntPtr fileActions,
            IntPtr attributes,
            IntPtr[] argv,
            IntPtr[] envp);

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

            IntPtr actions = IntPtr.Zero;
            bool actionsInitialized = false;
            bool useCloseOnExecDefault = false;
            int rc;
            if (kwargs is not null
                && kwargs.TryGetValue("file_actions", out object? value)
                && value is not null) {
                actions = Marshal.AllocHGlobal(SpawnFileActionsBufferSize);
                rc = posix_spawn_file_actions_init(actions);
                if (rc != 0) {
                    Marshal.FreeHGlobal(actions);
                    throw GetLastUnixError(executable);
                }
                actionsInitialized = true;
                try {
                    ApplySpawnFileActions(
                        context,
                        actions,
                        value,
                        out useCloseOnExecDefault);
                } catch {
                    posix_spawn_file_actions_destroy(actions);
                    Marshal.FreeHGlobal(actions);
                    throw;
                }
            }

            IntPtr attributes = IntPtr.Zero;
            bool attributesInitialized = false;
            if (useCloseOnExecDefault) {
                attributes = Marshal.AllocHGlobal(
                    SpawnAttributesBufferSize);
                rc = posix_spawnattr_init(attributes);
                if (rc != 0) {
                    Marshal.FreeHGlobal(attributes);
                    if (actionsInitialized) {
                        posix_spawn_file_actions_destroy(actions);
                        Marshal.FreeHGlobal(actions);
                    }
                    throw GetLastUnixError(executable);
                }
                attributesInitialized = true;
                rc = posix_spawnattr_setflags(
                    attributes,
                    POSIX_SPAWN_CLOEXEC_DEFAULT);
                if (rc != 0) {
                    posix_spawnattr_destroy(attributes);
                    Marshal.FreeHGlobal(attributes);
                    if (actionsInitialized) {
                        posix_spawn_file_actions_destroy(actions);
                        Marshal.FreeHGlobal(actions);
                    }
                    throw GetLastUnixError(executable);
                }
            }

            try {
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
                            nativeArgv.Pointers,
                            nativeEnv.Pointers)
                        : posix_spawnp(
                            out pid,
                            executable,
                            actions,
                            attributes,
                            nativeArgv.Pointers,
                            nativeEnv.Pointers);
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
                if (attributesInitialized) {
                    posix_spawnattr_destroy(attributes);
                    Marshal.FreeHGlobal(attributes);
                }
                if (actionsInitialized) {
                    posix_spawn_file_actions_destroy(actions);
                    Marshal.FreeHGlobal(actions);
                }
            }
        }

        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static int posix_spawn_probe() {
            using NativeStringArray argv = new(
                new[] { "/bin/sh", "-c", "exit 0" });
            using NativeStringArray envp = new(
                new[] { "PATH=/usr/bin:/bin" });
            int pid;
            int error = posix_spawn(
                out pid,
                "/bin/sh",
                IntPtr.Zero,
                IntPtr.Zero,
                argv.Pointers,
                envp.Pointers);
            if (error != 0) {
                return -error;
            }
            Syscall.waitpid(pid, out _, 0);
            return 0;
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
            object fileActions,
            out bool useCloseOnExecDefault) {
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
                var closedDescriptors = new HashSet<int>();
                var namedDescriptors = new HashSet<int>();
                useCloseOnExecDefault = false;
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
                            int openDescriptor = ToInt(action[1]!);
                            namedDescriptors.Add(openDescriptor);
                            rc = posix_spawn_file_actions_addopen(
                                actions,
                                openDescriptor,
                                ConvertToFsString(
                                    context,
                                    action[2]!,
                                    "file action path",
                                    "posix_spawn"),
                                ToInt(action[3]!),
                                (uint)ToInt(action[4]!));
                            break;
                        case POSIX_SPAWN_CLOSE:
                            int descriptor = ToInt(action[1]!);
                            namedDescriptors.Add(descriptor);
                            rc = posix_spawn_file_actions_addclose(
                                actions,
                                descriptor);
                            if (rc == 0) {
                                closedDescriptors.Add(descriptor);
                            }
                            break;
                        case POSIX_SPAWN_DUP2:
                            namedDescriptors.Add(ToInt(action[2]!));
                            rc = posix_spawn_file_actions_adddup2(
                                actions,
                                ToInt(action[1]!),
                                ToInt(action[2]!));
                            break;
                        case POSIX_SPAWN_CLOSEFROM:
                            int start = ToInt(action[1]!);
                            if (OperatingSystem.IsMacOS()) {
                                useCloseOnExecDefault = true;
                                rc = 0;
                                break;
                            }
                            try {
                                rc = posix_spawn_file_actions_addclosefrom_np(
                                    actions,
                                    start);
                            } catch (EntryPointNotFoundException) {
                                rc = AddCloseFromFallback(
                                    actions,
                                    start,
                                    closedDescriptors);
                            }
                            if (rc == (int)Errno.ENOSYS) {
                                rc = AddCloseFromFallback(
                                    actions,
                                    start,
                                    closedDescriptors);
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
                if (useCloseOnExecDefault) {
                    for (int fd = 0; fd <= 2; fd++) {
                        if (namedDescriptors.Contains(fd)
                            || Syscall.fcntl(
                                fd,
                                FcntlCommand.F_GETFD) == -1) {
                            continue;
                        }
                        int rc = posix_spawn_file_actions_addinherit_np(
                            actions,
                            fd);
                        if (rc != 0) {
                            throw GetLastUnixError();
                        }
                    }
                }
            } finally {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        private static int AddCloseFromFallback(
            IntPtr actions,
            int start,
            HashSet<int> closedDescriptors) {
            for (int fd = start; fd < 1024; fd++) {
                if (closedDescriptors.Contains(fd)) {
                    continue;
                }
                if (Syscall.fcntl(
                        fd,
                        FcntlCommand.F_GETFD) == -1) {
                    continue;
                }
                int rc = posix_spawn_file_actions_addclose(
                    actions,
                    fd);
                if (rc != 0) {
                    return rc;
                }
                closedDescriptors.Add(fd);
            }
            return 0;
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
            if (!PythonTypeOps.TryInvokeUnaryOperator(
                    context,
                    env,
                    "keys",
                    out object? keys)) {
                throw PythonOps.TypeError("env must be a mapping");
            }

            var result = new List<string>();
            IEnumerator? enumerator;
            if (!PythonOps.TryGetEnumerator(context, keys, out enumerator)
                || enumerator is null) {
                throw PythonOps.TypeError("env keys must be iterable");
            }
            try {
                while (enumerator.MoveNext()) {
                    object? key = enumerator.Current;
                    result.Add(
                        ConvertToFsString(
                            context,
                            key,
                            "environment key",
                            "posix_spawn")
                        + "="
                        + ConvertToFsString(
                            context,
                            PythonOps.GetIndex(context, env, key),
                            "environment value",
                            "posix_spawn"));
                }
            } finally {
                (enumerator as IDisposable)?.Dispose();
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
            internal IntPtr[] Pointers { get; }

            internal NativeStringArray(string[]? values) {
                values ??= Array.Empty<string>();
                Pointers = new IntPtr[values.Length + 1];
                _strings = new IntPtr[values.Length];
                for (int i = 0; i < values.Length; i++) {
                    _strings[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
                    Pointers[i] = _strings[i];
                }
            }

            public void Dispose() {
                foreach (IntPtr value in _strings) {
                    Marshal.FreeCoTaskMem(value);
                }
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
