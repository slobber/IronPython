// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

[assembly: PythonModule("sys", typeof(IronPython.Modules.SysModule))]
namespace IronPython.Modules {
    public static class SysModule {
        public const string __doc__ = "Provides access to functions which query or manipulate the Python runtime.";

        public const int api_version = 0;
        // argv is set by PythonContext and only on the initial load
        public static readonly string byteorder = BitConverter.IsLittleEndian ? "little" : "big";
        // builtin_module_names is set by PythonContext and updated on reload
        public const string copyright = "Copyright (c) .NET Foundation and Contributors";

        private static string GetPrefix() {
            string prefix;
            try {
                prefix = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            } catch (SecurityException) {
                prefix = String.Empty;
            } catch (ArgumentException) {
                prefix = String.Empty;
            } catch (MethodAccessException) {
                prefix = String.Empty;
            }
            return prefix;
        }

        /// <summary>
        /// Returns detailed call statistics.  Not implemented in IronPython and always returns None.
        /// </summary>
        public static object callstats() {
            return null;
        }

        public static bool is_finalizing() => false;

        /// <summary>
        /// Handles output of the expression statement.
        /// Prints the value and sets the __builtin__._
        /// </summary>
        [PythonHidden]
        [Documentation("displayhook(object) -> None\r\n\r\nPrint an object to sys.stdout and also save it in __builtin__._")]
        public static void displayhookImpl(CodeContext/*!*/ context, object value) {
            // based on the pseudcode: https://docs.python.org/3/library/sys.html#sys.displayhook
            if (value == null) return;
            context.LanguageContext.BuiltinModuleDict["_"] = null;
            var text = PythonOps.Repr(context, value);
            var stdout = context.LanguageContext.SystemStandardOut;
            try {
                PythonOps.PrintWithDest(context, stdout, text);
            } catch (EncoderFallbackException) {
                var encoding = PythonOps.GetBoundAttr(context, stdout, "encoding") as string;
                var bytes = StringOps.encode(context, text, encoding, "backslashreplace");
                // TODO: write the bytes directly to the buffer if possible
                text = bytes.decode(context, encoding, "strict");
                PythonOps.PrintWithDest(context, stdout, text);
            }
            context.LanguageContext.BuiltinModuleDict["_"] = value;
        }

        public static BuiltinFunction displayhook = BuiltinFunction.MakeFunction(
            nameof(displayhook),
            ArrayUtils.ConvertAll(typeof(SysModule).GetMember(nameof(displayhookImpl)), (x) => (MethodBase)x),
            typeof(SysModule)
        );

        public static readonly BuiltinFunction __displayhook__ = displayhook;

        public const int dllhandle = 0;
        public static string pycache_prefix;
        public const string platlibdir = "lib";
        public const string _vpath = "";
        public static readonly JitInfo _jit = new JitInfo();
        public static readonly Monitoring monitoring = new Monitoring();
        public static FrozenSetCollection stdlib_module_names = CreateStdlibModuleNames();

        private static FrozenSetCollection CreateStdlibModuleNames() {
            var names = new HashSet<string>(StringComparer.Ordinal) {
                "builtins", "sys", "array", "asyncio", "collections", "concurrent",
                "contextvars", "datetime", "importlib", "io", "logging", "math",
                "ntpath", "os", "pathlib", "profile", "profiling", "re", "typing"
            };

            string stdlib = Environment.GetEnvironmentVariable("CYBERSTONE_IRONPYTHON_STDLIB");
            string libDirectory = !string.IsNullOrEmpty(stdlib)
                ? Path.Combine(stdlib, "lib")
                : Path.Combine(GetPrefix(), "lib");
            if (Directory.Exists(libDirectory)) {
                foreach (string entry in Directory.EnumerateFileSystemEntries(libDirectory)) {
                    string name = Path.GetFileNameWithoutExtension(entry);
                    if (name == "__pycache__" || name.Length == 0) continue;
                    names.Add(name);
                }
            }
            var list = new PythonList();
            foreach (string name in names) list.AddNoLock(name);
            return FrozenSetCollection.Make((object)list);
        }

        private sealed class CoroutineOriginTrackingState {
            public int Depth;
        }

        private static readonly ConditionalWeakTable<PythonContext, CoroutineOriginTrackingState> _coroutineOriginTracking = new();
        private sealed class AsyncGeneratorHookState {
            public object FirstIter;
            public object Finalizer;
        }

        private sealed class LazyImportState {
            public string Mode = "normal";
            public object Filter;
            public readonly SetCollection Modules = new();
        }

        private static readonly ConditionalWeakTable<PythonContext, LazyImportState> _lazyImportStates =
            new();

        internal static SetCollection GetLazyModules(PythonContext context)
            => _lazyImportStates.GetOrCreateValue(context).Modules;

        internal static void TrackLazyModule(CodeContext context, string name) {
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            lock (state) {
                state.Modules.add(name);
            }
        }

        internal static void UntrackLazyModule(CodeContext context, string name) {
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            lock (state) {
                state.Modules.discard(name);
            }
        }

        public static void set_lazy_imports(CodeContext context, object mode) {
            if (mode is not string value) {
                throw PythonOps.TypeError("set_lazy_imports() argument must be str");
            }
            if (value is not ("normal" or "all")) {
                throw PythonOps.ValueError(
                    "invalid lazy imports mode: {0}",
                    value);
            }
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            lock (state) {
                state.Mode = value;
            }
        }

        public static string get_lazy_imports(CodeContext context) {
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            lock (state) {
                return state.Mode;
            }
        }

        public static void set_lazy_imports_filter(CodeContext context, object filter) {
            if (filter is not null && !PythonOps.IsCallable(context, filter)) {
                throw PythonOps.TypeError("lazy imports filter must be callable or None");
            }
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            lock (state) {
                state.Filter = filter;
            }
        }

        public static object get_lazy_imports_filter(CodeContext context) {
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            lock (state) {
                return state.Filter;
            }
        }

        internal static bool ShouldUseExplicitLazyImport(
            CodeContext context,
            string name,
            object fromlist) {
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            object filter;
            lock (state) {
                filter = state.Filter;
            }
            return filter is null
                || PythonOps.IsTrue(PythonCalls.Call(
                    context,
                    filter,
                    context.GlobalDict.TryGetValue(
                        "__name__",
                        out object moduleName)
                        ? moduleName
                        : null!,
                    name,
                    fromlist));
        }

        internal static bool ShouldImportLazily(
            CodeContext context,
            string name,
            object fromlist,
            bool moduleLevel) {
            LazyImportState state = _lazyImportStates.GetOrCreateValue(context.LanguageContext);
            object filter;
            lock (state) {
                if (!moduleLevel || state.Mode != "all") {
                    return false;
                }
                filter = state.Filter;
            }
            return filter is null
                || PythonOps.IsTrue(PythonCalls.Call(
                    context,
                    filter,
                    null!,
                    name,
                    fromlist));
        }

        private static readonly ConditionalWeakTable<PythonContext, AsyncGeneratorHookState> _asyncGeneratorHooks = new();

        public static int get_coroutine_origin_tracking_depth(CodeContext context)
            => _coroutineOriginTracking.GetOrCreateValue(context.LanguageContext).Depth;

        public static void set_coroutine_origin_tracking_depth(CodeContext context, int depth) {
            if (depth < 0) throw PythonOps.ValueError("depth must be non-negative");
            _coroutineOriginTracking.GetOrCreateValue(context.LanguageContext).Depth = depth;
        }

        public static AsyncGenHooks get_asyncgen_hooks(CodeContext context) {
            AsyncGeneratorHookState state = _asyncGeneratorHooks.GetOrCreateValue(context.LanguageContext);
            lock (state) return new AsyncGenHooks(state.FirstIter, state.Finalizer);
        }

        public static void set_asyncgen_hooks(CodeContext context, object firstiter = null, object finalizer = null) {
            if (firstiter is not null && !PythonOps.IsCallable(context, firstiter)) {
                throw PythonOps.TypeError("firstiter must be callable");
            }
            if (finalizer is not null && !PythonOps.IsCallable(context, finalizer)) {
                throw PythonOps.TypeError("finalizer must be callable");
            }

            AsyncGeneratorHookState state = _asyncGeneratorHooks.GetOrCreateValue(context.LanguageContext);
            lock (state) {
                state.FirstIter = firstiter;
                state.Finalizer = finalizer;
            }
        }

        [PythonType("asyncgen_hooks")]
        public sealed class AsyncGenHooks : PythonTuple {
            public AsyncGenHooks(object firstiter, object finalizer)
                : base(new[] { firstiter, finalizer }) {
                this.firstiter = firstiter;
                this.finalizer = finalizer;
            }

            public readonly object firstiter;
            public readonly object finalizer;
        }

        [PythonType("_JitInfo")]
        public sealed class JitInfo {
            public bool is_available() => false;

            public bool is_enabled() => false;

            public bool is_active() => false;
        }

        [PythonType("monitoring")]
        public sealed class Monitoring {
            public const int DEBUGGER_ID = 0;
            public const int COVERAGE_ID = 1;
            public const int PROFILER_ID = 2;
            public const int OPTIMIZER_ID = 5;
            public static readonly object MISSING = new();
            public const int DISABLE = 0;

            private readonly string[] _tools = new string[6];
            private readonly int[] _events = new int[6];
            private readonly Dictionary<(int Tool, int Event), object> _callbacks = new();

            public readonly SimpleNamespace events = new SimpleNamespace(
                new Dictionary<string, object> {
                    ["PY_START"] = 1 << 1,
                    ["PY_RESUME"] = 1 << 2,
                    ["PY_RETURN"] = 1 << 3,
                    ["PY_YIELD"] = 1 << 4,
                    ["CALL"] = 1 << 5,
                    ["LINE"] = 1 << 6,
                    ["INSTRUCTION"] = 1 << 7,
                    ["JUMP"] = 1 << 8,
                    ["BRANCH"] = 1 << 9,
                    ["BRANCH_LEFT"] = 1 << 10,
                    ["BRANCH_RIGHT"] = 1 << 11,
                    ["STOP_ITERATION"] = 1 << 12,
                    ["RAISE"] = 1 << 13,
                    ["RERAISE"] = 1 << 14,
                    ["EXCEPTION_HANDLED"] = 1 << 15,
                    ["PY_UNWIND"] = 1 << 16,
                    ["PY_THROW"] = 1 << 17,
                    ["C_RETURN"] = 1 << 18,
                    ["C_RAISE"] = 1 << 19
                });

            public void use_tool_id(int toolId, [NotNone] string name) {
                ValidateTool(toolId);
                if (_tools[toolId] is not null) {
                    throw PythonOps.ValueError("tool {0} is already in use", toolId);
                }
                _tools[toolId] = name;
            }

            public void free_tool_id(int toolId) {
                ValidateTool(toolId);
                _tools[toolId] = null;
                _events[toolId] = 0;
                foreach (var key in _callbacks.Keys.Where(key => key.Tool == toolId).ToArray()) {
                    _callbacks.Remove(key);
                }
            }

            public void clear_tool_id(int toolId) {
                ValidateTool(toolId);
                _events[toolId] = 0;
                foreach (var key in _callbacks.Keys.Where(key => key.Tool == toolId).ToArray()) {
                    _callbacks.Remove(key);
                }
            }

            public string get_tool(int toolId) {
                ValidateTool(toolId);
                return _tools[toolId];
            }

            public int get_events(int toolId) {
                ValidateTool(toolId);
                RequireTool(toolId);
                return _events[toolId];
            }

            public void set_events(int toolId, int eventSet) {
                ValidateTool(toolId);
                RequireTool(toolId);
                _events[toolId] = eventSet;
            }

            public void set_local_events(int toolId, object code, int eventSet) {
                ValidateTool(toolId);
                RequireTool(toolId);
            }

            public object register_callback(int toolId, int eventId, object callback) {
                ValidateTool(toolId);
                RequireTool(toolId);
                var key = (toolId, eventId);
                _callbacks.TryGetValue(key, out object previous);
                if (callback is null) {
                    _callbacks.Remove(key);
                } else {
                    _callbacks[key] = callback;
                }
                return previous ?? MISSING;
            }

            public void restart_events() { }

            public PythonDictionary _all_events() {
                var result = new PythonDictionary();
                foreach (KeyValuePair<object, object> pair in events.__dict__) {
                    if (pair.Key is not string eventName || pair.Value is not int eventId) continue;
                    int tools = 0;
                    for (int tool = 0; tool < _events.Length; tool++) {
                        if ((_events[tool] & eventId) != 0) {
                            tools |= 1 << tool;
                        }
                    }
                    if (tools != 0) result[eventName] = tools;
                }
                return result;
            }

            private static void ValidateTool(int toolId) {
                if (toolId < 0 || toolId >= 6) {
                    throw PythonOps.ValueError("invalid tool id");
                }
            }

            private void RequireTool(int toolId) {
                if (_tools[toolId] is null) {
                    throw PythonOps.ValueError("tool {0} is not in use", toolId);
                }
            }
        }

        public static void audit(CodeContext context, [NotNone] string eventName, [NotNone] params object[] args) {
            if (eventName is null) throw PythonOps.TypeError("audit() argument 1 must be str, not None");

            PythonContext pythonContext = context.LanguageContext;
            object[] hooks;
            lock (pythonContext.AuditHooksLock) {
                hooks = pythonContext.AuditHooks.ToArray();
            }

            object eventArgs = PythonTuple.MakeTuple(args);
            foreach (object hook in hooks) {
                PythonCalls.Call(context, hook, eventName, eventArgs);
            }
        }

        public static void addaudithook(CodeContext context, object hook) {
            if (hook is null) throw PythonOps.TypeError("addaudithook() argument must be callable, not None");

            PythonContext pythonContext = context.LanguageContext;
            lock (pythonContext.AuditHooksLock) {
                pythonContext.AuditHooks.Add(hook);
            }
        }

        [PythonHidden]
        [Documentation(@"excepthook(exctype, value, traceback) -> None

Handle an exception by displaying it with a traceback on sys.stderr._")]
        public static void excepthookImpl(CodeContext/*!*/ context, object exctype, object value, object traceback) {
            PythonContext pc = context.LanguageContext;
            var exc = PythonExceptions.ToClr(value);
            if (exc is null) throw PythonOps.TypeError($"Exception expected for {nameof(value)}, {PythonOps.GetPythonTypeName(value)} found");

            PythonOps.PrintWithDest(
                context,
                pc.SystemStandardError,
                pc.FormatException(PythonExceptions.ToClr(value))
            );
        }

        public static readonly BuiltinFunction excepthook = BuiltinFunction.MakeFunction(
            "excepthook",
            ArrayUtils.ConvertAll(typeof(SysModule).GetMember(nameof(SysModule.excepthookImpl)), (x) => (MethodBase)x),
            typeof(SysModule)
        );

        public static readonly BuiltinFunction __excepthook__ = excepthook;

        public static int getcheckinterval() {
            throw PythonOps.NotImplementedError("IronPython does not support sys.getcheckinterval");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "value")]
        public static void setcheckinterval(int value) {
            throw PythonOps.NotImplementedError("IronPython does not support sys.setcheckinterval");
        }

        // warnoptions is set by PythonContext and updated on each reload

        public static PythonTuple exc_info(CodeContext/*!*/ context) {
            return PythonOps.GetExceptionInfo(context);
        }

        public static object exception(CodeContext/*!*/ context) {
            return PythonOps.GetExceptionInfo(context)[1];
        }

        // exec_prefix and executable are set by PythonContext and updated on each reload

        public static string intern(object o) {
            if (!(o is string s)) {
                throw PythonOps.TypeError("intern: argument must be string");
            }
            return string.Intern(s);
        }

        public static void exit() {
            exit(null);
        }

        public static void exit(object code) {
            if (code == null) {
                throw new PythonExceptions._SystemExit().InitAndGetClrException();
            } else if (code is PythonTuple pt) {
                throw new PythonExceptions._SystemExit().InitAndGetClrException(pt._data);
            } else {
                // throw as a python exception here to get the args set.
                throw new PythonExceptions._SystemExit().InitAndGetClrException(code);
            }
        }

        public static string getdefaultencoding(CodeContext/*!*/ context) {
            return context.LanguageContext.GetDefaultEncodingName();
        }

        public static string getfilesystemencoding(CodeContext/*!*/ context) {
            if (Environment.OSVersion.Platform == PlatformID.Unix || context.LanguageContext.PythonOptions.Utf8Mode)
                return "utf-8";
            return "mbcs";
        }

        public static string getfilesystemencodeerrors() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                return "surrogatepass";
            } else {
                return "surrogateescape";
            }
        }

        [PythonHidden]
        public static TraceBackFrame/*!*/ _getframeImpl(CodeContext/*!*/ context) {
            return _getframeImpl(context, 0);
        }

        [PythonHidden]
        public static TraceBackFrame/*!*/ _getframeImpl(CodeContext/*!*/ context, int depth) {
            return _getframeImpl(context, depth, PythonOps.GetFunctionStack());
        }

        internal static TraceBackFrame/*!*/ _getframeImpl(CodeContext/*!*/ context, int depth, List<FunctionStack> stack) {
            if (depth < stack.Count) {
                TraceBackFrame cur = null;

                for (int i = 0; i < stack.Count - depth; i++) {
                    var elem = stack[i];
                    CodeContext frameContext =
                        elem.Context;

                    if (elem.Frame != null) {
                        // we previously handed out a frame here, hand out the same one now
                        cur = elem.Frame;
                        cur._lineNo = elem.Line;
                    } else {
                        // create a new frame and save it for future calls
                        cur = new TraceBackFrame(
                            frameContext,
                            Builtin.globals(frameContext),
                            Builtin.locals(frameContext),
                            elem.Code,
                            cur
                        );

                        cur._lineNo = elem.Line;
                        stack[i] = new FunctionStack(elem.Context, elem.Code, cur);
                    }
                }
                return cur;
            }

            throw PythonOps.ValueError("call stack is not deep enough");
        }

        public static int getsizeof(object o) {
            return ObjectOps.__sizeof__(o);
        }

        public static PythonType _ironpython_cancelled_error
            => PythonExceptions.CancelledError;

        [SupportedOSPlatform("windows"), PythonHidden(PlatformsAttribute.PlatformFamily.Unix)]
        public static PythonTuple getwindowsversion()
            => windows_version.GetWindowsVersion();

        [PythonType("sys.getwindowsversion"), SupportedOSPlatform("windows"), PythonHidden]
        public sealed class windows_version : PythonTuple {
            internal static windows_version GetWindowsVersion() {
                unsafe {
                    var version = new Interop.Kernel32.OSVERSIONINFOEX() { dwOSVersionInfoSize = sizeof(Interop.Kernel32.OSVERSIONINFOEX) };

                    if (!Interop.Kernel32.GetVersionExW(ref version)) {
                        // TODO: can this fail? throw an OSError?
                        throw new InvalidOperationException();
                    }

                    return new windows_version(version);
                }
            }

            private unsafe windows_version(Interop.Kernel32.OSVERSIONINFOEX version)
                : this(version.dwMajorVersion, version.dwMinorVersion, version.dwBuildNumber, version.dwPlatformId, new string(version.szCSDVersion)) {
                product_type = version.wProductType;
                service_pack_major = version.wServicePackMajor;
                service_pack_minor = version.wServicePackMinor;
                suite_mask = version.wSuiteMask;

                if (Interop.NtDll.RtlGetVersionEx(out var osVer) == 0) {
                    platform_version = PythonTuple.MakeTuple((int)osVer.dwMajorVersion, (int)osVer.dwMinorVersion, (int)osVer.dwBuildNumber);
                } else {
                    platform_version = PythonTuple.MakeTuple(major, minor, build);
                }
            }

            private windows_version(int major, int minor, int build, int platform, string service_pack)
                : base(new object[] { major, minor, build, platform, service_pack }) {

                this.major = major;
                this.minor = minor;
                this.build = build;
                this.platform = platform;
                this.service_pack = service_pack;
            }

            public readonly int major;
            public readonly int minor;
            public readonly int build;
            public readonly int platform;
            public readonly string service_pack;

            public readonly int product_type;
            public readonly int service_pack_major;
            public readonly int service_pack_minor;
            public readonly int suite_mask;
            public readonly PythonTuple platform_version;

            public PythonTuple _platform_version => platform_version; // TODO: remove in 3.6 (for compat with 3.5)

            public const int n_fields = 10;
            public const int n_sequence_fields = 5;
            public const int n_unnamed_fields = 0;

            public override string __repr__(CodeContext context) {
                return
                    $"sys.getwindowsversion(major={major}, minor={minor}, build={build}, platform={platform}, service_pack='{service_pack}')";
            }
        }

        // hex_version is set by PythonContext

        public const int maxsize = int.MaxValue;
        public const int maxunicode = char.MaxValue;

        // modules is set by PythonContext and only on the initial load

        // path is set by PythonContext and only on the initial load

        public static readonly string prefix = GetPrefix();

        // ps1 and ps2 are set by PythonContext and only on the initial load

        // setdefaultencoding has been removed in Python 3

#if PROFILE_SUPPORT
        // not enabled because we don't yet support tracing built-in functions.  Doing so is a little
        // difficult because it's hard to flip tracing on/off for them w/o a perf overhead in the
        // non-profiling case.
        public static void setprofile(CodeContext/*!*/ context, TracebackDelegate o) {
            PythonContext pyContext = context.LanguageContext;
            pyContext.EnsureDebugContext();

            if (o == null) {
                pyContext.UnregisterTracebackHandler();
            } else {
                pyContext.RegisterTracebackHandler();
            }

            // Register the trace func with the listener
            pyContext.TracebackListener.SetProfile(o);
        }
#endif

        public static void settrace(CodeContext/*!*/ context, object o) {
            context.LanguageContext.SetTrace(o);
        }

        public static object call_tracing(CodeContext/*!*/ context, object func, PythonTuple args) {
            return context.LanguageContext.CallTracing(func, args);
        }

        public static object gettrace(CodeContext/*!*/ context) {
            return context.LanguageContext.GetTrace();
        }

        public static void setrecursionlimit(CodeContext/*!*/ context, int limit) {
            context.LanguageContext.RecursionLimit = limit;
        }

        public static int getrecursionlimit(CodeContext/*!*/ context) {
            return context.LanguageContext.RecursionLimit;
        }

        // stdin, stdout, stderr, __stdin__, __stdout__, and __stderr__ added by PythonContext

        [PythonHidden(PlatformsAttribute.PlatformFamily.Windows)]
        public static readonly string abiflags = string.Empty;

        // version and version_info are set by PythonContext

        public static PythonTuple _git = PythonTuple.MakeTuple("IronPython", "", ""); // TODO: fill this up!

        [PythonHidden(PlatformsAttribute.PlatformFamily.Unix)]
        public static readonly string winver = CurrentVersion.Series;

        #region Special types

        [PythonHidden, PythonType("flags"), DontMapIEnumerableToIter]
        public sealed class SysFlags : PythonTuple {
            public static readonly PythonTuple __match_args__ = PythonTuple.MakeTuple(
                nameof(debug), nameof(inspect), nameof(interactive), nameof(optimize),
                nameof(dont_write_bytecode), nameof(no_user_site), nameof(no_site),
                nameof(ignore_environment), nameof(verbose), nameof(bytes_warning),
                nameof(quiet), nameof(hash_randomization), nameof(isolated),
                nameof(dev_mode), nameof(utf8_mode), nameof(warn_default_encoding),
                nameof(safe_path), nameof(int_max_str_digits), nameof(gil),
                nameof(lazy_imports), nameof(context_aware_warnings),
                nameof(thread_inherit_context));

            internal SysFlags() : base(new object[n_fields] {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                false, 1, 0, true, 4300,
                1, -1, 0, 0
            }) { }

            private const int INDEX_DEBUG = 0;
            private const int INDEX_INSPECT = 1;
            private const int INDEX_INTERACTIVE = 2;
            private const int INDEX_OPTIMIZE = 3;
            private const int INDEX_DONT_WRITE_BYTECODE = 4;
            private const int INDEX_NO_USER_SITE = 5;
            private const int INDEX_NO_SITE = 6;
            private const int INDEX_IGNORE_ENVIRONMENT = 7;
            private const int INDEX_VERBOSE = 8;
            private const int INDEX_BYTES_WARNING = 9;
            private const int INDEX_QUIET = 10;
            private const int INDEX_HASH_RANDOMIZATION = 11;
            private const int INDEX_ISOLATED = 12;
            private const int INDEX_DEV_MODE = 13;
            private const int INDEX_UTF8_MODE = 14;
            private const int INDEX_WARN_DEFAULT_ENCODING = 15;
            private const int INDEX_SAFE_PATH = 16;
            private const int INDEX_INT_MAX_STR_DIGITS = 17;
            private const int INDEX_GIL = 18;
            private const int INDEX_LAZY_IMPORTS = 19;
            private const int INDEX_CONTEXT_AWARE_WARNINGS = 20;
            private const int INDEX_THREAD_INHERIT_CONTEXT = 21;

            public const int n_fields = 22;
            public const int n_sequence_fields = 18;
            public const int n_unnamed_fields = 0;

            public override string __repr__(CodeContext context) {
                var fields = new string[n_sequence_fields] {
                    $"{nameof(debug)}={debug}",
                    $"{nameof(inspect)}={inspect}",
                    $"{nameof(interactive)}={interactive}",
                    $"{nameof(optimize)}={optimize}",
                    $"{nameof(dont_write_bytecode)}={dont_write_bytecode}",
                    $"{nameof(no_user_site)}={no_user_site}",
                    $"{nameof(no_site)}={no_site}",
                    $"{nameof(ignore_environment)}={ignore_environment}",
                    $"{nameof(verbose)}={verbose}",
                    $"{nameof(bytes_warning)}={bytes_warning}",
                    $"{nameof(quiet)}={quiet}",
                    $"{nameof(hash_randomization)}={hash_randomization}",
                    $"{nameof(isolated)}={isolated}",
                    $"{nameof(dev_mode)}={dev_mode}",
                    $"{nameof(utf8_mode)}={utf8_mode}",
                    $"{nameof(warn_default_encoding)}={warn_default_encoding}",
                    $"{nameof(safe_path)}={safe_path}",
                    $"{nameof(int_max_str_digits)}={int_max_str_digits}",
                };
                return $"sys.flags({string.Join(", ", fields)})";
            }

            #region sys.flags API

            public int debug {
                get => (int)_data[INDEX_DEBUG];
                internal set => _data[INDEX_DEBUG] = value;
            }

            public int inspect {
                get => (int)_data[INDEX_INSPECT];
                internal set => _data[INDEX_INSPECT] = value;
            }

            public int interactive {
                get => (int)_data[INDEX_INTERACTIVE];
                internal set => _data[INDEX_INTERACTIVE] = value;
            }

            public int optimize {
                get => (int)_data[INDEX_OPTIMIZE];
                internal set => _data[INDEX_OPTIMIZE] = value;
            }

            public int dont_write_bytecode {
                get => (int)_data[INDEX_DONT_WRITE_BYTECODE];
                internal set => _data[INDEX_DONT_WRITE_BYTECODE] = value;
            }

            public int no_user_site {
                get => (int)_data[INDEX_NO_USER_SITE];
                internal set => _data[INDEX_NO_USER_SITE] = value;
            }

            public int no_site {
                get => (int)_data[INDEX_NO_SITE];
                internal set => _data[INDEX_NO_SITE] = value;
            }

            public int ignore_environment {
                get => (int)_data[INDEX_IGNORE_ENVIRONMENT];
                internal set => _data[INDEX_IGNORE_ENVIRONMENT] = value;
            }

            public int verbose {
                get => (int)_data[INDEX_VERBOSE];
                internal set => _data[INDEX_VERBOSE] = value;
            }

            public int bytes_warning {
                get => (int)_data[INDEX_BYTES_WARNING];
                internal set => _data[INDEX_BYTES_WARNING] = value;
            }

            public int quiet {
                get => (int)_data[INDEX_QUIET];
                internal set => _data[INDEX_QUIET] = value;
            }

            public int hash_randomization {
                get => (int)_data[INDEX_HASH_RANDOMIZATION];
                internal set => _data[INDEX_HASH_RANDOMIZATION] = value;
            }

            public int isolated {
                get => (int)_data[INDEX_ISOLATED];
                internal set => _data[INDEX_ISOLATED] = value;
            }

            public bool dev_mode {
                get => (bool)_data[INDEX_DEV_MODE];
                internal set => _data[INDEX_DEV_MODE] = value;
            }

            public int utf8_mode {
                get => (int)_data[INDEX_UTF8_MODE];
                internal set => _data[INDEX_UTF8_MODE] = value;
            }

            public int warn_default_encoding {
                get => (int)_data[INDEX_WARN_DEFAULT_ENCODING];
                internal set => _data[INDEX_WARN_DEFAULT_ENCODING] = value;
            }

            public bool safe_path {
                get => (bool)_data[INDEX_SAFE_PATH];
                internal set => _data[INDEX_SAFE_PATH] = value;
            }

            public int int_max_str_digits {
                get => (int)_data[INDEX_INT_MAX_STR_DIGITS];
                internal set => _data[INDEX_INT_MAX_STR_DIGITS] = value;
            }

            public int gil {
                get => (int)_data[INDEX_GIL];
                internal set => _data[INDEX_GIL] = value;
            }

            public int lazy_imports {
                get => (int)_data[INDEX_LAZY_IMPORTS];
                internal set => _data[INDEX_LAZY_IMPORTS] = value;
            }

            public int context_aware_warnings {
                get => (int)_data[INDEX_CONTEXT_AWARE_WARNINGS];
                internal set => _data[INDEX_CONTEXT_AWARE_WARNINGS] = value;
            }

            public int thread_inherit_context {
                get => (int)_data[INDEX_THREAD_INHERIT_CONTEXT];
                internal set => _data[INDEX_THREAD_INHERIT_CONTEXT] = value;
            }

            #endregion
        }

        #endregion

        // These values are based on the .NET BigInteger in System.Numerics.BigInteger
        public static intinfo int_info = new intinfo(32, 4);

        [PythonType("int_info"), PythonHidden]
        public class intinfo : PythonTuple {
            internal intinfo(int bits_per_digit, int sizeof_digit)
                : base(new object[] { bits_per_digit, sizeof_digit }) {

                this.bits_per_digit = bits_per_digit;
                this.sizeof_digit = sizeof_digit;
            }

            public readonly int bits_per_digit;
            public readonly int sizeof_digit;

            public const int n_fields = 2;
            public const int n_sequence_fields = 2;
            public const int n_unnamed_fields = 0;

            public override string __repr__(CodeContext context) {
                return $"sys.int_info(bits_per_digit={bits_per_digit}, sizeof_digit={sizeof_digit})";
            }
        }

        public static string float_repr_style = "short";

        public static floatinfo float_info = new floatinfo(
            Double.MaxValue,    // DBL_MAX
            1024,               // DBL_MAX_EXP
            308,                // DBL_MAX_10_EXP
                                // DBL_MIN
            BitConverter.Int64BitsToDouble(BitConverter.IsLittleEndian ? 0x0010000000000000 : 0x0000000000001000),
            -1021,              // DBL_MIN_EXP
            -307,               // DBL_MIN_10_EXP
            15,                 // DBL_DIG
            53,                 // DBL_MANT_DIG
                                // DBL_EPSILON
            BitConverter.Int64BitsToDouble(BitConverter.IsLittleEndian ? 0x3cb0000000000000 : 0x000000000000b03c),
            2,                  // FLT_RADIX
            1);                 // FLT_ROUNDS

        [PythonType("float_info"), PythonHidden]
        public class floatinfo : PythonTuple {
            internal floatinfo(double max, int max_exp, int max_10_exp,
                               double min, int min_exp, int min_10_exp,
                               int dig, int mant_dig, double epsilon, int radix, int rounds)
                : base(new object[] { max,  max_exp,  max_10_exp,
                                min,  min_exp,  min_10_exp,
                                dig,  mant_dig,  epsilon,  radix, rounds}) {

                this.max = max;
                this.max_exp = max_exp;
                this.max_10_exp = max_10_exp;
                this.min = min;
                this.min_exp = min_exp;
                this.min_10_exp = min_10_exp;
                this.dig = dig;
                this.mant_dig = mant_dig;
                this.epsilon = epsilon;
                this.radix = radix;
                this.rounds = rounds;
            }

            public readonly double max;
            public readonly int max_exp;
            public readonly int max_10_exp;
            public readonly double min;
            public readonly int min_exp;
            public readonly int min_10_exp;
            public readonly int dig;
            public readonly int mant_dig;
            public readonly double epsilon;
            public readonly int radix;
            public readonly int rounds;

            public const int n_fields = 11;
            public const int n_sequence_fields = 11;
            public const int n_unnamed_fields = 0;

            public override string __repr__(CodeContext context) {
                return string.Format("sys.float_info(max={0}, max_exp={1}, max_10_exp={2}, " +
                                     "min={3}, min_exp={4}, min_10_exp={5}, " +
                                     "dig={6}, mant_dig={7}, epsilon={8}, radix={9}, rounds={10})",
                    max, max_exp, max_10_exp,
                    min, min_exp, min_10_exp,
                    dig, mant_dig, epsilon, radix, rounds);
            }
        }

        public static hashinfo hash_info = new hashinfo(width: 32, modulus: int.MaxValue, inf: 314159, nan: 0, imag: 1000003, algorithm: "dotnet", hash_bits: 0, seed_bits: 0, cutoff: 0);

        [PythonType("hash_info"), PythonHidden]
        public class hashinfo : PythonTuple {
            internal hashinfo(int width, int modulus, int inf, int nan, int imag, string algorithm, int hash_bits, int seed_bits, int cutoff)
                : base(new object[] { width, modulus, inf, nan, imag, algorithm, hash_bits, seed_bits, cutoff }) {

                this.width = width;
                this.modulus = modulus;
                this.inf = inf;
                this.nan = nan;
                this.imag = imag;
                this.algorithm = algorithm;
                this.hash_bits = hash_bits;
                this.seed_bits = seed_bits;
                this.cutoff = cutoff;
            }

            public readonly int width;
            public readonly int modulus;
            public readonly int inf;
            public readonly int nan;
            public readonly int imag;
            public readonly string algorithm;
            public readonly int hash_bits;
            public readonly int seed_bits;
            public readonly int cutoff;

            public const int n_fields = 11;
            public const int n_sequence_fields = 11;
            public const int n_unnamed_fields = 0;

            public override string __repr__(CodeContext context) {
                return $"sys.hash_info(width={width}, modulus={modulus}, inf={inf}, nan={nan}, imag={imag}, algorithm='{algorithm}', hash_bits={hash_bits}, seed_bits={seed_bits}, cutoff={cutoff})";
            }
        }

        [SpecialName]
        public static void PerformModuleReload(PythonContext/*!*/ context, PythonDictionary/*!*/ dict) {
            dict["stdin"] = dict["__stdin__"];
            dict["stdout"] = dict["__stdout__"];
            dict["stderr"] = dict["__stderr__"];

            dict["platform"] = PythonContext.GetPlatform();

            // !!! These fields do need to be reset on "reload(sys)". However, the initial value is specified by the
            // engine elsewhere. For now, we initialize them just once to some default value
            dict["warnoptions"] = new PythonList(0);

            PublishBuiltinModuleNames(context, dict);
            context.SetHostVariables(dict);

            dict["meta_path"] = new PythonList(0);
            dict["path_hooks"] = new PythonList(0);

            // add zipimport to the path hooks for importing from zip files.
            try {
                if (Importer.ImportModule(
                    context.SharedClsContext, context.SharedClsContext.GlobalDict,
                    "zipimport", false, 0) is PythonModule zipimport) {
                    object zipimporter = PythonOps.GetBoundAttr(
                        context.SharedClsContext, zipimport, "zipimporter");
                    if (dict["path_hooks"] is PythonList path_hooks && zipimporter != null) {
                        path_hooks.Add(zipimporter);
                    }
                }
            } catch {
                // this is not a fatal error, so we don't do anything.
            }

            dict["path_importer_cache"] = new PythonDictionary();
        }

        internal static void PublishBuiltinModuleNames(PythonContext/*!*/ context, PythonDictionary/*!*/ dict) {
            object[] keys = new object[context.BuiltinModules.Keys.Count];
            int index = 0;
            foreach (object key in context.BuiltinModules.Keys) {
                keys[index++] = key;
            }
            //fix the sort of the keys prior to publication in sys.builtin_module_names for issue #875
            // See issue https://github.com/IronLanguages/ironpython3/issues/875 for full details
            Array.Sort(keys);
            dict["builtin_module_names"] = PythonTuple.MakeTuple(keys);
        }

    }
}
