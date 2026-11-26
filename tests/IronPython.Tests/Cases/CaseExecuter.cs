// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;

using IronPython.Hosting;
using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;

using IronPythonTest.Util;

namespace IronPythonTest.Cases {
    internal class CaseExecuter {
        private static string Executable {
            get {
                var folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string runner;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    runner = Path.Combine(folder, "ipy.exe");
                    if (File.Exists(runner)) return runner;
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    runner = Path.Combine(folder, "ipy");
                    if (File.Exists(runner)) return runner;
                    runner = Path.Combine(folder, "ipy.sh");
                    if (File.Exists(runner)) return runner;
                }
                throw new FileNotFoundException();
            }
        }

        private static readonly string IRONPYTHONPATH = GetIronPythonPath();

        static CaseExecuter() {
            // Mono writes runtime warnings to stdout (e.g. "abort_threads: Failed aborting id" during shutdown),
            // which breaks tests that check the output of child processes
            if (ClrModule.IsMono && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MONO_LOG_DEST"))) {
                Environment.SetEnvironmentVariable("MONO_LOG_DEST", "/dev/null");
            }
        }

        private ScriptEngine defaultEngine;

        public static ScriptEngine CreateEngine(TestOptions options) {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                {"Debug", options.Debug },
                {"Frames", options.Frames || options.FullFrames },
                {"FullFrames", options.FullFrames },
                {"RecursionLimit", options.MaxRecursion },
                {"Tracing", options.Tracing }
            });

            engine.SetHostVariables(
                Path.GetDirectoryName(Executable),
                Executable,
                "");

            AddSearchPaths(engine);
            AddExtensionAssemblies(engine);
            return engine;
        }

        internal static string FindRoot() {
            // we start at the current directory and look up until we find the "src" directory
            var current = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var found = false;
            while (!found && !string.IsNullOrEmpty(current)) {
                var test = Path.Combine(current, "src", "core", "IronPython");
                if (Directory.Exists(test)) {
                    return current;
                }

                current = Path.GetDirectoryName(current);
            }
            return string.Empty;
        }

        internal static string GetStdLibPath() {
            var configured = Environment.GetEnvironmentVariable("CYBERSTONE_IRONPYTHON_STDLIB");
            if (!string.IsNullOrEmpty(configured)) {
                var configuredPath = Path.Combine(configured, "lib");
                if (Directory.Exists(configuredPath)) return configuredPath;
            }

            var root = FindRoot();
            var siblingPath = Path.GetFullPath(Path.Combine(root, "..", "IronPython.StdLib", "lib"));
            if (Directory.Exists(siblingPath)) return siblingPath;

            return Path.Combine(root, "src", "core", "IronPython.StdLib", "lib");
        }

        private static void AddSearchPaths(ScriptEngine engine) {
            var paths = new List<string>(engine.GetSearchPaths());
            if (!paths.Any(x => x.Contains("stdlib", StringComparison.OrdinalIgnoreCase))) {
                var root = FindRoot();
                if (!string.IsNullOrEmpty(root)) {
                    paths.Insert(0, GetStdLibPath());
                }
            }
            engine.SetSearchPaths(paths);
        }

        private static void AddExtensionAssemblies(ScriptEngine engine) {
            string directory = Path.Combine(Path.GetDirectoryName(Executable)!, "DLLs");
            if (!Directory.Exists(directory)) return;

            foreach (string file in Directory.EnumerateFiles(directory, "*.dll")) {
                try {
                    engine.Runtime.LoadAssembly(Assembly.LoadFrom(file));
                } catch {
                    // Platform-specific extensions are absent from some test hosts.
                }
            }
        }

        public CaseExecuter() {
            this.defaultEngine = Python.CreateEngine();

            this.defaultEngine.SetHostVariables(
                Path.GetDirectoryName(Executable),
                Executable,
                "");
            AddSearchPaths(this.defaultEngine);
            AddExtensionAssemblies(this.defaultEngine);
        }

        public int RunTest(TestInfo testcase) {
            int retryCount = testcase.Options.RetryCount;
            for (int i = 0; i < retryCount; i++) {
                try {
                    var res = RunTestImpl(testcase);
                    if (res == 0) return res;
                    NUnit.Framework.TestContext.Progress.WriteLine($"Test {testcase.Name} failed, retrying again. Retry #{i + 1}");
                } catch { }
            }

            return RunTestImpl(testcase);
        }

        private int RunTestImpl(TestInfo testcase) {
            var isolationLevel = testcase.Options.IsolationLevel;
            // when RUN_IGNORED is set, always isolate ignored tests
            if (testcase.Options.Ignore) {
                isolationLevel = TestIsolationLevel.PROCESS;
            }

            switch (isolationLevel) {
                case TestIsolationLevel.SCOPE:
                    return GetScopeTest(testcase);

                case TestIsolationLevel.ENGINE:
                    return GetEngineTest(testcase);

                case TestIsolationLevel.PROCESS:
                    return GetProcessTest(testcase);

                default:
                    throw new ArgumentException($"IsolationLevel {testcase.Options.IsolationLevel} is not supported.", "testcase.IsolationLevel");
            }
        }

        public string FormatException(Exception ex) {
            return this.defaultEngine.GetService<ExceptionOperations>().FormatException(ex);
        }

        private static string GetIronPythonPath() {
            var path = GetStdLibPath();
            if (Directory.Exists(path)) {
                return path;
            }
            return string.Empty;
        }

        // the module name of a StdLib file, for packages with relative imports to run with -m
        private static string GetModuleName(string path) {
            string root = string.IsNullOrEmpty(IRONPYTHONPATH)
                ? GetStdLibPath()
                : IRONPYTHONPATH;
            return Path.GetRelativePath(root, Path.ChangeExtension(path, null)).Replace(Path.DirectorySeparatorChar, '.');
        }

        private string ReplaceVariables(string input, Dictionary<string, string> replacements) {
            Regex variableRegex = new Regex(@"\$\(([^}]+)\)", RegexOptions.Compiled);

            var result = input;
            var match = variableRegex.Match(input);
            while (match.Success) {
                var variable = match.Groups[1].Value;
                if (replacements.TryGetValue(variable, out var replacement)) {
                    result = result.Replace(match.Groups[0].Value, replacement);
                }
                match = match.NextMatch();
            }

            return result;
        }

        private int GetEngineTest(TestInfo testcase) {
            if (testcase.Options.Arguments != null) {
                throw new Exception("Arguments have no effect with IsolationLevel=SCOPE or ENGINE, use PROCESS instead.");
            }

            var engine = CreateEngine(testcase.Options);
            var source = engine.CreateScriptSourceFromFile(
               testcase.Path, System.Text.Encoding.UTF8, SourceCodeKind.File);

            return GetResult(testcase, engine, source, testcase.Path, testcase.Options.WorkingDirectory);
        }

        private int GetProcessTest(TestInfo testcase) {
            int exitCode = -1;
            var argReplacements = new Dictionary<string, string>() {
                { "TEST_FILE", testcase.Path },
                { "TEST_FILE_DIR", Path.GetDirectoryName(testcase.Path) },
                { "TEST_MODULE", GetModuleName(testcase.Path) }
            };

            var wdReplacements = new Dictionary<string, string>() {
                { "ROOT", FindRoot() },
                { "TEST_FILE_DIR", Path.GetDirectoryName(testcase.Path) }
            };

            // add the arguments - in the normal case no arguments should be added
            var arguments = new List<string>();
            if (testcase.Options.Debug)
                arguments.Add("-X:Debug");
            if (!testcase.Options.Frames)
                arguments.Add("-X:NoFrames");
            if (testcase.Options.FullFrames)
                arguments.Add("-X:FullFrames");
            if (testcase.Options.MaxRecursion != int.MaxValue)
                arguments.Add($"-X:MaxRecursion {testcase.Options.MaxRecursion}");
            if (testcase.Options.Tracing)
                arguments.Add("-X:Tracing");
            string configuredArguments = testcase.Options.Arguments;
            if (configuredArguments is null
                && testcase.Name.StartsWith("CPython.", StringComparison.Ordinal)) {
                arguments.Add("-m");
                arguments.Add("unittest");
                arguments.Add(GetModuleName(testcase.Path));
            } else if (configuredArguments is null && IsPackageTestCase(testcase.Path)) {
                arguments.Add("-m");
                arguments.Add(GetModuleName(testcase.Path));
            } else {
                arguments.Add(ReplaceVariables(configuredArguments ?? "\"$(TEST_FILE)\"", argReplacements));
            }

            using (Process proc = new Process()) {
                proc.StartInfo.FileName = Executable;
                proc.StartInfo.Arguments = string.Join(" ", arguments);

                if (!string.IsNullOrEmpty(IRONPYTHONPATH)) {
                    proc.StartInfo.EnvironmentVariables["IRONPYTHONPATH"] = IRONPYTHONPATH;
                }
                foreach (string variable in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64" }) {
                    string value = Environment.GetEnvironmentVariable(variable);
                    if (!string.IsNullOrEmpty(value)) {
                        proc.StartInfo.EnvironmentVariables[variable] = value;
                    }
                }
                string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                if (string.IsNullOrEmpty(dotnetHost)
                    && string.Equals(
                        Path.GetFileNameWithoutExtension(Environment.ProcessPath),
                        "dotnet",
                        StringComparison.OrdinalIgnoreCase)) {
                    dotnetHost = Environment.ProcessPath;
                }
                dotnetHost ??= FindOnPath("dotnet");
                if (!string.IsNullOrEmpty(dotnetHost)
                    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT"))) {
                    proc.StartInfo.EnvironmentVariables["DOTNET_ROOT"] = Path.GetDirectoryName(dotnetHost)!;
                }

                if (!string.IsNullOrEmpty(testcase.Options.WorkingDirectory)) {
                    proc.StartInfo.WorkingDirectory = ReplaceVariables(testcase.Options.WorkingDirectory, wdReplacements);
                }

                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardError = proc.StartInfo.RedirectStandardOutput = testcase.Options.Redirect;
                proc.Start();

                if (testcase.Options.Redirect) {
                    AsyncStreamReader(proc.StandardOutput, data => NUnit.Framework.TestContext.Out.Write(data));
                    AsyncStreamReader(proc.StandardError, data => NUnit.Framework.TestContext.Error.Write(data));
                }

                if (!proc.WaitForExit(testcase.Options.Timeout)) {
                    proc.Kill();
                    NUnit.Framework.TestContext.Error.WriteLine($"{testcase.Name} timed out after {testcase.Options.Timeout / 1000.0} seconds.");
                }
                exitCode = proc.ExitCode;
            }
            return exitCode;

            void AsyncStreamReader(StreamReader reader, Action<string> handler) {
                byte[] buffer = new byte[4096];
                BeginReadAsync();

                void BeginReadAsync() {
                    reader.BaseStream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReadCallback), null);
                }

                void ReadCallback(IAsyncResult asyncResult) {
                    var bytesRead = reader.BaseStream.EndRead(asyncResult);

                    if (bytesRead > 0) {
                        var data = reader.CurrentEncoding.GetString(buffer, 0, bytesRead);
                        handler?.Invoke(data);
                        BeginReadAsync();
                    }
                }
            }
        }

        private static string FindOnPath(string executable) {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;

            foreach (string directory in path.Split(Path.PathSeparator)) {
                string candidate = Path.Combine(directory, executable);
                if (OperatingSystem.IsWindows()) {
                    candidate += ".exe";
                }
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
            return null;
        }

        private static bool IsPackageTestCase(string path) {
            string root = string.IsNullOrEmpty(IRONPYTHONPATH)
                ? GetStdLibPath()
                : IRONPYTHONPATH;
            string directory = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(directory)
                   && directory.StartsWith(root, StringComparison.Ordinal)) {
                if (File.Exists(Path.Combine(directory, "__init__.py"))) {
                    return true;
                }
                directory = Path.GetDirectoryName(directory);
            }
            return false;
        }

        private int GetScopeTest(TestInfo testcase) {
            if (testcase.Options.Debug
                    || !testcase.Options.Frames
                    || testcase.Options.FullFrames
                    || testcase.Options.MaxRecursion != int.MaxValue
                    || testcase.Options.Tracing) {
                throw new Exception("Options have no effect with IsolationLevel=SCOPE, use ENGINE or PROCESS instead.");
            }

            if (testcase.Options.Arguments != null) {
                throw new Exception("Arguments have no effect with IsolationLevel=SCOPE or ENGINE, use PROCESS instead.");
            }

            var source = this.defaultEngine.CreateScriptSourceFromString(
                testcase.Text, testcase.Path, SourceCodeKind.File);

            return GetResult(testcase, this.defaultEngine, source, testcase.Path, testcase.Options.WorkingDirectory);
        }

        private int GetResult(TestInfo testcase, ScriptEngine engine, ScriptSource source, string testPath, string workingDir) {
            var path = Environment.GetEnvironmentVariable("IRONPYTHONPATH");
            if (string.IsNullOrEmpty(path)) {
                Environment.SetEnvironmentVariable("IRONPYTHONPATH", IRONPYTHONPATH);
            }

            var cwd = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(workingDir)) {
                var replacements = new Dictionary<string, string>() {
                    { "ROOT", FindRoot() },
                    { "TEST_FILE_DIR", Path.GetDirectoryName(testPath) }
                };
                Environment.CurrentDirectory = ReplaceVariables(workingDir, replacements);
            }

            try {
                var scope = engine.CreateScope();
                engine.GetSysModule().SetVariable("argv", PythonList.FromArrayNoCopy(new object[] { source.Path }));
                var compiledCode = source.Compile(new IronPython.Compiler.PythonCompilerOptions() { ModuleName = "__main__" });

                ExceptionDispatchInfo exceptionInfo = null;

                int res = -1;
                int maxStackSize = 2 * 1024 * 1024; // 2 MiB
                var thread = new Thread(() => {
                    try {
                        res = engine.Operations.ConvertTo<int>(compiledCode.Execute(scope) ?? 0);
                    } catch (SystemExitException ex) {
                        res = ex.GetExitCode(out object otherCode);
                    } catch (ThreadAbortException) {
                        #pragma warning disable SYSLIB0006 // 'Thread.ResetAbort is not supported and throws PlatformNotSupportedException.' 
                        Thread.ResetAbort();
                        #pragma warning restore SYSLIB0006
                    } catch (Exception ex) {
                        if (ex.GetPythonException() is object pex && DynamicHelpers.GetPythonType(pex).Name == "SkipTest") {
                            NUnit.Framework.TestContext.Progress.WriteLine($"Test {testcase.Name} skipped: {pex}");
                            res = 0;
                        } else {
                            exceptionInfo = ExceptionDispatchInfo.Capture(ex);
                        }
                    }
                }, maxStackSize) {
                    IsBackground = true
                };

                thread.Start();

                if (!thread.Join(testcase.Options.Timeout)) {
                    if(!ClrModule.IsNetCoreApp) {
                        #pragma warning disable SYSLIB0006 // 'Thread.Abort is not supported and throws PlatformNotSupportedException.' 
                        thread.Abort();
                        #pragma warning restore SYSLIB0006
                    }
                    NUnit.Framework.TestContext.Error.WriteLine($"{testcase.Name} timed out after {testcase.Options.Timeout / 1000.0} seconds.");
                    return -1;
                }

                exceptionInfo?.Throw();

                return res;
            } finally {
                Environment.CurrentDirectory = cwd;
            }
        }
    }

#if NETFRAMEWORK
    internal static class StringExtensions {
        public static bool Contains(this string s, string value, StringComparison comparisonType) {
            return s.IndexOf(value, comparisonType) >= 0;
        }
    }
#endif
}
