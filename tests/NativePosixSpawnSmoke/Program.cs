using System.Runtime.InteropServices;

if (OperatingSystem.IsWindows()) {
    Console.WriteLine("NATIVE_POSIX_SPAWN_SKIP windows");
    return;
}

const string executable = "/bin/sh";
string[] arguments = [executable, "-c", "exit 0"];
string[] environment = ["PATH=/usr/bin:/bin"];

Run("managed-string", MarshalledSpawn);
Run("native-path", NativePathSpawn);
return;

void Run(string name, Func<int> action) {
    int error = action();
    if (error != 0) {
        throw new InvalidOperationException(
            $"posix_spawn variant {name} failed with errno {error}");
    }
    Console.WriteLine($"POSIX_SPAWN_OK {name}");
}

int MarshalledSpawn() {
    NativeArray argv = new(arguments);
    NativeArray envp = new(environment);
    try {
        int pid;
        int error = posix_spawn_managed(
            out pid,
            executable,
            IntPtr.Zero,
            IntPtr.Zero,
            argv.Pointers,
            envp.Pointers);
        return error == 0 ? Wait(pid) : error;
    } finally {
        argv.Dispose();
        envp.Dispose();
    }
}

int NativePathSpawn() {
    IntPtr path = Marshal.StringToCoTaskMemUTF8(executable);
    NativeArray argv = new(arguments);
    NativeArray envp = new(environment);
    try {
        int pid;
        int error = posix_spawn_native(
            out pid,
            path,
            IntPtr.Zero,
            IntPtr.Zero,
            argv.Block,
            envp.Block);
        return error == 0 ? Wait(pid) : error;
    } finally {
        Marshal.FreeCoTaskMem(path);
        argv.Dispose();
        envp.Dispose();
    }
}

static int Wait(int pid) {
    int status;
    return waitpid(pid, out status, 0) == pid ? 0 : Marshal.GetLastPInvokeError();
}

[DllImport("libc", EntryPoint = "posix_spawn", SetLastError = true)]
static extern int posix_spawn_managed(
    out int pid,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
    IntPtr fileActions,
    IntPtr attributes,
    IntPtr[] argv,
    IntPtr[] envp);

[DllImport("libc", EntryPoint = "posix_spawn", SetLastError = true)]
static extern int posix_spawn_native(
    out int pid,
    IntPtr file,
    IntPtr fileActions,
    IntPtr attributes,
    IntPtr argv,
    IntPtr envp);

[DllImport("libc", SetLastError = true)]
static extern int waitpid(int pid, out int status, int options);

sealed class NativeArray : IDisposable {
    private readonly IntPtr[] _strings;
    internal IntPtr[] Pointers { get; }
    internal IntPtr Block { get; }

    internal NativeArray(string[] values) {
        _strings = new IntPtr[values.Length];
        Pointers = new IntPtr[values.Length + 1];
        Block = Marshal.AllocHGlobal((values.Length + 1) * IntPtr.Size);
        for (int i = 0; i < values.Length; i++) {
            _strings[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
            Pointers[i] = _strings[i];
            Marshal.WriteIntPtr(Block, i * IntPtr.Size, _strings[i]);
        }
    }

    public void Dispose() {
        foreach (IntPtr value in _strings) {
            Marshal.FreeCoTaskMem(value);
        }
        Marshal.FreeHGlobal(Block);
    }
}
