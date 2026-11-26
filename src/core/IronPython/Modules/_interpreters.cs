// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using IronPython.Hosting;
using IronPython.Runtime;
using IronPython.Runtime.Binding;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;

[assembly: PythonModule(
    "_interpreters",
    typeof(IronPython.Modules.PythonInterpreters))]

namespace IronPython.Modules {
    public static class PythonInterpreters {
        private sealed class InterpreterState {
            internal InterpreterState(
                int id,
                int whence,
                string configName,
                ScriptEngine? engine,
                ScriptScope? scope,
                PythonContext? context) {
                Id = id;
                Whence = whence;
                ConfigName = configName;
                Engine = engine;
                Scope = scope;
                Context = context;
            }

            internal int Id { get; }
            internal int Whence { get; }
            internal string ConfigName { get; }
            internal ScriptEngine? Engine { get; }
            internal ScriptScope? Scope { get; }
            internal PythonContext? Context { get; }
            internal int References { get; set; } = 1;
            internal bool Running { get; set; }
        }

        private sealed class CurrentInterpreter {
            internal CurrentInterpreter(int id) {
                Id = id;
            }

            internal int Id { get; }
        }

        public const int WHENCE_UNKNOWN = 0;
        public const int WHENCE_RUNTIME = 1;
        public const int WHENCE_LEGACY_CAPI = 2;
        public const int WHENCE_CAPI = 3;
        public const int WHENCE_XI = 4;
        public const int WHENCE_STDLIB = 5;

        private static readonly object _sync = new object();
        private static readonly Dictionary<int, InterpreterState> _states = new();
        private static readonly ConditionalWeakTable<PythonContext, CurrentInterpreter>
            _current = new();
        private static int _nextId = 1;
        private static int? _mainId;

        private static PythonType? _interpreterError;
        private static PythonType? _interpreterNotFoundError;
        private static PythonType? _notShareableError;

        public static PythonType InterpreterError {
            get {
                if (_interpreterError is null) {
                    Interlocked.CompareExchange(
                        ref _interpreterError,
                        PythonExceptions.CreateSubType(
                            DefaultContext.Default.LanguageContext,
                            PythonExceptions.Exception,
                            "InterpreterError",
                            "_interpreters",
                            null,
                            (message, inner) => new RuntimeException(message, inner)),
                        null);
                }
                return _interpreterError;
            }
        }

        public static PythonType InterpreterNotFoundError {
            get {
                if (_interpreterNotFoundError is null) {
                    Interlocked.CompareExchange(
                        ref _interpreterNotFoundError,
                        PythonExceptions.CreateSubType(
                            DefaultContext.Default.LanguageContext,
                            InterpreterError,
                            "InterpreterNotFoundError",
                            "_interpreters",
                            null,
                            (message, inner) => new RuntimeException(message, inner)),
                        null);
                }
                return _interpreterNotFoundError;
            }
        }

        public static PythonType NotShareableError {
            get {
                if (_notShareableError is null) {
                    Interlocked.CompareExchange(
                        ref _notShareableError,
                        PythonExceptions.CreateSubType(
                            DefaultContext.Default.LanguageContext,
                            InterpreterError,
                            "NotShareableError",
                            "_interpreters",
                            null,
                            (message, inner) => new RuntimeException(message, inner)),
                        null);
                }
                return _notShareableError;
            }
        }

        public static PythonTuple get_current(CodeContext context) {
            int id = GetCurrentId(context);
            return PythonTuple.MakeTuple(id, GetRequired(id).Whence);
        }

        public static PythonTuple get_main() {
            EnsureMain(DefaultContext.Default);
            return PythonTuple.MakeTuple(0, WHENCE_RUNTIME);
        }

        public static int create(
            CodeContext context,
            object? config = null,
            bool reqrefs = false) {
            string configName = config as string ?? "isolated";
            return CreateInterpreter(context, configName);
        }

        public static PythonList list_all(bool require_ready = true) {
            EnsureMain(DefaultContext.Default);
            var result = new PythonList();
            lock (_sync) {
                foreach (InterpreterState state in _states.Values.OrderBy(x => x.Id)) {
                    result.Add(PythonTuple.MakeTuple(state.Id, state.Whence));
                }
            }
            return result;
        }

        public static int whence(int id) => GetRequired(id).Whence;
        public static bool is_running(int id) => GetRequired(id).Running;

        public static void incref(int id) {
            lock (_sync) {
                GetRequired(id).References++;
            }
        }

        public static void decref(int id) {
            lock (_sync) {
                InterpreterState state = GetRequired(id);
                state.References--;
                if (state.References <= 0 && id != _mainId) {
                    Destroy(state);
                }
            }
        }

        public static void destroy(CodeContext context, int id, bool restrict = false) {
            lock (_sync) {
                InterpreterState state = GetRequired(id);
                if (id == _mainId) {
                    throw Error("cannot destroy the main interpreter");
                }
                if (id == GetCurrentId(context)) {
                    throw Error("cannot destroy the current interpreter");
                }
                if (restrict && state.Whence != WHENCE_STDLIB) {
                    throw Error("interpreter was not created by this module");
                }
                Destroy(state);
            }
        }

        public static void set___main___attrs(
            CodeContext context,
            int id,
            [NotNone] PythonDictionary values,
            bool restrict = false) {
            InterpreterState state = GetRequired(id);
            if (restrict && state.Whence != WHENCE_STDLIB && id != _mainId) {
                throw Error("interpreter was not created by this module");
            }
            if (values.Count == 0) {
                throw PythonOps.ValueError(
                    "at least one attribute must be prepared");
            }
            if (state.Running) {
                throw Error("interpreter is running");
            }
            foreach (KeyValuePair<object, object?> pair in values) {
                if (pair.Key is not string) {
                    throw PythonOps.TypeError("attribute name must be str");
                }
                if (!is_shareable(pair.Value)) {
                    throw NotShareable(pair.Value);
                }
            }
            foreach (KeyValuePair<object, object?> pair in values) {
                string name = (string)pair.Key;
                if (state.Scope is not null) {
                    state.Scope.SetVariable(name, pair.Value);
                    if (pair.Value is int fd && state.Context is not null) {
                        StreamBox? sharedStreams = null;
                        if (context.LanguageContext.FileManager.TryGetStreams(
                                fd,
                                out StreamBox? streams)) {
                            sharedStreams = new StreamBox(streams);
                        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                            || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                            sharedStreams = new StreamBox(new PosixFileStream(fd));
                        }
                        if (sharedStreams is not null) {
                            state.Context.FileManager.Remove(fd);
                            state.Context.FileManager.Add(fd, sharedStreams);
                        }
                    }
                } else if (id == _mainId) {
                    context.GlobalDict[name] = pair.Value;
                }
            }
        }

        public static object? exec(
            CodeContext context,
            int id,
            [NotNone] object code,
            PythonDictionary? shared = null,
            bool restrict = false) {
            return run_string(context, id, code, shared, restrict);
        }

        public static object? run_string(
            CodeContext context,
            int id,
            [NotNone] object code,
            PythonDictionary? shared = null,
            bool restrict = false) {
            if (code is not string source) {
                throw PythonOps.TypeError("code must be str, not {0}",
                    PythonOps.GetPythonTypeName(code));
            }
            InterpreterState state = GetRequired(id);
            if (restrict && state.Whence != WHENCE_STDLIB && id != _mainId) {
                throw Error("interpreter was not created by this module");
            }
            if (shared is not null) {
                set___main___attrs(context, id, shared, false);
            }
            if (state.Engine is null || state.Scope is null) {
                throw Error("cannot run code in the main interpreter");
            }
            lock (state) {
                if (state.Running) {
                    throw Error("interpreter is already running");
                }
                state.Running = true;
            }
            try {
                state.Engine.Execute(source, state.Scope);
                return null;
            } catch (Exception exception) {
                return CaptureException(context, exception);
            } finally {
                lock (state) {
                    state.Running = false;
                }
            }
        }

        public static object? run_func(
            CodeContext context,
            int id,
            [NotNone] object function,
            PythonDictionary? shared = null,
            bool restrict = false) {
            InterpreterState state = GetRequired(id);
            if (shared is not null) {
                set___main___attrs(context, id, shared, false);
            }
            return CallInInterpreter(context, state, function, Array.Empty<object>(), null);
        }

        public static PythonTuple call(
            CodeContext context,
            int id,
            [NotNone] object callable,
            [NotNone] PythonTuple args,
            PythonDictionary? kwargs = null,
            bool restrict = false) {
            InterpreterState state = GetRequired(id);
            if (PythonOps.TryGetBoundAttr(
                    context,
                    callable,
                    "__name__",
                    out object? name)
                && name is string callableName
                && callableName is "globals" or "locals" or "vars") {
                throw NotShareable(callable);
            }
            if (HasNonEmptyClosure(context, callable)) {
                throw NotShareable(callable);
            }

            try {
                object? result = CallInInterpreter(
                    context,
                    state,
                    callable,
                    args.ToArray(),
                    kwargs);
                return PythonTuple.MakeTuple(
                    IsStatelessFunction(context, result)
                        ? result
                        : CloneValue(context, result),
                    null);
            } catch (Exception exception) {
                if (IsNotShareableException(exception)) throw;
                return PythonTuple.MakeTuple(
                    null,
                    CaptureException(context, exception));
            }
        }

        private static object? CallInInterpreter(
            CodeContext context,
            InterpreterState state,
            object callable,
            object?[] args,
            PythonDictionary? kwargs) {
            if (state.Scope is null || state.Context is null) {
                throw Error("cannot call code in the main interpreter");
            }

            CodeContext targetContext = GetInterpreterContext(state);
            lock (state) {
                if (state.Running) {
                    throw Error("interpreter is already running");
                }
                state.Running = true;
            }

            int previousId = GetCurrentId(context);
            _current.AddOrUpdate(
                context.LanguageContext,
                new CurrentInterpreter(state.Id));
            try {
                object?[] argumentCopies = args
                    .Select(argument => CloneValue(context, argument))
                    .ToArray();
                PythonDictionary? kwargsCopy = kwargs is null
                    ? null
                    : (PythonDictionary)CloneValue(context, kwargs)!;
                object targetCallable = PrepareCallable(
                    context,
                    targetContext,
                    callable);
                return kwargsCopy is null || kwargsCopy.Count == 0
                    ? PythonCalls.Call(
                        targetContext,
                        targetCallable,
                        argumentCopies)
                    : PythonCalls.CallWithKeywordArgs(
                        targetContext,
                        targetCallable,
                        argumentCopies,
                        kwargsCopy);
            } finally {
                _current.AddOrUpdate(
                    context.LanguageContext,
                    new CurrentInterpreter(previousId));
                lock (state) {
                    state.Running = false;
                }
            }
        }

        private static CodeContext GetInterpreterContext(
            InterpreterState state) {
            Scope targetScope = HostingHelpers.GetScope(state.Scope!);
            PythonScopeExtension extension =
                targetScope.GetExtension(state.Context!.ContextId)
                    as PythonScopeExtension
                ?? throw Error("interpreter scope is not initialized");
            return extension.ModuleContext.GlobalContext;
        }

        private static object PrepareCallable(
            CodeContext sourceContext,
            CodeContext targetContext,
            object callable) {
            if (callable is PythonFunction function) {
                return PrepareFunction(
                    sourceContext,
                    targetContext,
                    function);
            }
            if (callable is Method method) {
                object prepared = method.__func__ is PythonFunction methodFunction
                    ? PrepareFunction(
                        sourceContext,
                        targetContext,
                        methodFunction)
                    : method.__func__;
                if (prepared is Method resolvedMethod) {
                    return resolvedMethod;
                }
                return new Method(prepared, method.__self__);
            }
            return callable;
        }

        private static object PrepareFunction(
            CodeContext sourceContext,
            CodeContext targetContext,
            PythonFunction function) {
            string? moduleName = PythonOps.TryGetBoundAttr(
                    sourceContext,
                    function,
                    "__module__",
                    out object? moduleValue)
                ? moduleValue as string
                : null;
            string qualname = function.__qualname__;
            CodeContext functionContext = targetContext;
            if (moduleName is not null
                && moduleName != "__main__"
                && !qualname.Contains("<locals>", StringComparison.Ordinal)
                && function.__globals__ is PythonDictionary globals) {
                functionContext = new CodeContext(
                    new PythonDictionary(),
                    new ModuleContext(
                        globals,
                        targetContext.LanguageContext));
            }
            return new PythonFunction(
                functionContext,
                function.__code__,
                function.__module__,
                function.__defaults__?.ToArray() ?? Array.Empty<object>(),
                function.__kwdefaults__,
                function.__annotations__,
                function.Closure);
        }

        private static bool IsNotShareableException(Exception exception) {
            PythonExceptions.BaseException? pythonException =
                exception.GetPythonException();
            return pythonException is not null
                && PythonOps.IsSubClass(
                    DynamicHelpers.GetPythonType(pythonException),
                    NotShareableError);
        }

        private static object? CloneValue(CodeContext context, object? value) {
            string? typeName = value is null
                ? null
                : PythonOps.GetPythonTypeName(value);
            if (typeName is "function" or "method"
                or "builtin_function_or_method") {
                if (HasNonEmptyClosure(context, value)) {
                    throw NotShareable(value);
                }
                if (IsDefinedInMain(context, value)) {
                    throw NotShareable(value);
                }
                return value;
            }
            switch (value) {
                case null:
                case string:
                case Bytes:
                case int:
                case bool:
                case double:
                case BigInteger:
                case MemoryView:
                case PythonType:
                case PythonModule:
                case PythonFunction:
                case Method:
                case BuiltinFunction:
                    return value;
                case PythonTuple tuple:
                    return PythonTuple.MakeTuple(
                        tuple.Select(item => CloneValue(context, item)).ToArray());
                case PythonList list:
                    var listCopy = new PythonList(list.Count);
                    foreach (object? item in list) {
                        listCopy.Add(CloneValue(context, item));
                    }
                    return listCopy;
                case PythonDictionary dictionary:
                    var dictionaryCopy = new PythonDictionary();
                    foreach (KeyValuePair<object, object?> pair in dictionary) {
                        dictionaryCopy[pair.Key] =
                            Equals(pair.Key, "__builtins__")
                                ? pair.Value
                                : CloneValue(context, pair.Value);
                    }
                    return dictionaryCopy;
                default:
                    if (typeName?.EndsWith(
                            "SimpleNamespace",
                            StringComparison.Ordinal) == true) {
                        return value;
                    }
                    return RoundTripInCurrent(context, value);
            }
        }

        private static bool HasNonEmptyClosure(
            CodeContext context,
            object? value) {
            if (value is PythonFunction function) {
                return function.__code__.HasFreeVariables;
            }
            if (value is Method method
                && method.__func__ is PythonFunction methodFunction) {
                return methodFunction.__code__.HasFreeVariables;
            }
            if (!PythonOps.TryGetBoundAttr(
                    context,
                    value,
                    "__closure__",
                    out object? closure)
                || closure is null) {
                return false;
            }
            return closure switch {
                PythonTuple tuple => tuple.Count != 0,
                PythonList list => list.Count != 0,
                _ => true
            };
        }

        private static bool IsDefinedInMain(
            CodeContext context,
            object? value) {
            return PythonOps.TryGetBoundAttr(
                    context,
                    value,
                    "__globals__",
                    out object? globals)
                && globals is PythonDictionary globalsDict
                && globalsDict.TryGetValue("__name__", out object? moduleName)
                && Equals(moduleName, "__main__");
        }

        private static bool IsStatelessFunction(
            CodeContext context,
            object? value) {
            if (value is Method) return true;
            return PythonOps.TryGetBoundAttr(
                    context,
                    value,
                    "__code__",
                    out object? code)
                && code is FunctionCode functionCode
                && !functionCode.HasFreeVariables
                && PythonOps.TryGetBoundAttr(
                    context,
                    value,
                    "__code__",
                    out _);
        }

        private static object RoundTripInCurrent(
            CodeContext context,
            object? value) {
            try {
                object pickle = Importer.ImportModule(
                    context,
                    null,
                    "pickle",
                    false,
                    0);
                object dumps = PythonOps.GetBoundAttr(context, pickle, "dumps");
                object loads = PythonOps.GetBoundAttr(context, pickle, "loads");
                object payload = PythonCalls.Call(context, dumps, value, 5)!;
                return PythonCalls.Call(context, loads, payload)!;
            } catch (Exception exception) {
                throw PythonExceptions.CreateThrowable(
                    NotShareableError,
                    $"object is not shareable: {exception.Message}");
            }
        }

        public static object capture_exception(CodeContext context, object? exception) {
            PythonExceptions.BaseException? pythonException =
                exception as PythonExceptions.BaseException;
            if (pythonException is null && exception is Exception clrException) {
                pythonException = PythonExceptions.ToPython(clrException);
            }
            pythonException ??=
                new PythonExceptions.BaseException(PythonExceptions.Exception);
            return MakeExceptionInfo(
                context,
                DynamicHelpers.GetPythonType(pythonException),
                PythonOps.ToString(pythonException),
                PythonOps.ToString(pythonException),
                PythonOps.ToString(pythonException));
        }

        public static bool is_shareable(object? value) {
            if (value is null) {
                return true;
            }
            string typeName = PythonOps.GetPythonTypeName(value);
            if (typeName is "str" or "bytes" or "int"
                or "bool" or "float" or "memoryview") {
                return true;
            }
            if (typeName == "tuple" && value is PythonTuple tuple) {
                return tuple.All(is_shareable);
            }
            if (PythonOps.TryGetBoundAttr(value, "_id", out _)) {
                if (typeName is "Queue" or "Interpreter") {
                    return true;
                }
            }
            return false;
        }

        public static object new_config(
            CodeContext context,
            string? name = null,
            [ParamDictionary] IDictionary<object, object>? kwargs = null) {
            string configName = string.IsNullOrEmpty(name) ? "isolated" : name;
            Dictionary<string, object?> values = DefaultConfig(configName);
            if (kwargs is not null) {
                foreach (KeyValuePair<object, object> pair in kwargs) {
                    string key = pair.Key.ToString() ?? string.Empty;
                    if (!values.ContainsKey(key)) {
                        throw PythonOps.ValueError("invalid config option: {0}", key);
                    }
                    if (key == "gil") {
                        if (pair.Value is not string gil
                            || gil is not ("default" or "shared" or "own")) {
                            throw PythonOps.TypeError("invalid gil value");
                        }
                    } else if (pair.Value is not bool) {
                        throw PythonOps.TypeError("{0} must be bool", key);
                    }
                    values[key] = pair.Value;
                }
            }
            return MakeNamespace(context, values);
        }

        public static object get_config(int id, bool restrict = false) {
            InterpreterState state = GetRequired(id);
            return MakeNamespace(
                DefaultContext.Default,
                DefaultConfig(state.ConfigName));
        }

        private static int CreateInterpreter(CodeContext context, string configName) {
            for (int i = 0; i < configName.Length; i++) {
                if (char.IsSurrogate(configName[i])) {
                    throw PythonOps.UnicodeEncodeError(
                        "surrogates not allowed",
                        configName[i],
                        i);
                }
            }
            if (configName is not ("isolated" or "legacy")) {
                throw PythonOps.ValueError("unsupported interpreter config: {0}", configName);
            }
            ScriptEngine engine = Python.CreateEngine();
            ScriptScope scope = engine.CreateScope();
            object frozenImportlib = Importer.ImportModule(
                context,
                null,
                "_frozen_importlib",
                false,
                0);
            object builtinImporter = PythonOps.GetBoundAttr(
                context,
                frozenImportlib,
                "BuiltinImporter");
            scope.SetVariable("__cyberstone_loader", builtinImporter);
            engine.Execute(@"
__name__ = '__main__'
__package__ = None
__loader__ = __cyberstone_loader
__spec__ = None
__doc__ = None
try:
    del __file__
except NameError:
    pass
del __cyberstone_loader
", scope);
            var pythonContext =
                (PythonContext)HostingHelpers.GetLanguageContext(engine);
            lock (_sync) {
                int id = _nextId++;
                var state = new InterpreterState(
                    id,
                    WHENCE_STDLIB,
                    configName,
                    engine,
                    scope,
                    pythonContext);
                _states.Add(id, state);
                _current.AddOrUpdate(pythonContext, new CurrentInterpreter(id));
                return id;
            }
        }

        private static void EnsureMain(CodeContext context) {
            if (_mainId is not null) return;
            lock (_sync) {
                if (_mainId is not null) return;
                _mainId = 0;
                _states.Add(
                    0,
                    new InterpreterState(
                        0,
                        WHENCE_RUNTIME,
                        "legacy",
                        null,
                        null,
                        context.LanguageContext) {
                        Running = true
                    });
                _current.AddOrUpdate(
                    context.LanguageContext,
                    new CurrentInterpreter(0));
            }
        }

        private static int GetCurrentId(CodeContext context) {
            EnsureMain(context);
            lock (_sync) {
                if (_current.TryGetValue(
                        context.LanguageContext,
                        out CurrentInterpreter? current)) {
                    return current.Id;
                }
                int id = _nextId++;
                _states.Add(
                    id,
                    new InterpreterState(
                        id,
                        WHENCE_RUNTIME,
                        "legacy",
                        null,
                        null,
                        context.LanguageContext));
                _current.AddOrUpdate(
                    context.LanguageContext,
                    new CurrentInterpreter(id));
                return id;
            }
        }

        private static InterpreterState GetRequired(int id) {
            EnsureMain(DefaultContext.Default);
            if (id < 0) {
                throw PythonOps.ValueError(
                    "interpreter id must be non-negative");
            }
            lock (_sync) {
                if (!_states.TryGetValue(id, out InterpreterState? state)) {
                    throw NotFound(id);
                }
                return state;
            }
        }

        private static void Destroy(InterpreterState state) {
            _states.Remove(state.Id);
            PythonInterpreterQueues.OnInterpreterDestroyed(state.Id);
            if (state.Engine is not null) {
                state.Engine.Runtime.Shutdown();
            }
        }

        private static Exception Error(string message)
            => PythonExceptions.CreateThrowable(InterpreterError, message);

        private static Exception NotFound(int id)
            => PythonExceptions.CreateThrowable(
                InterpreterNotFoundError,
                $"interpreter {id} not found");

        private static Exception NotShareable(object? value)
            => PythonExceptions.CreateThrowable(
                NotShareableError,
                $"object is not shareable: {PythonOps.Repr(DefaultContext.Default, value)}");

        private static object CaptureException(CodeContext context, Exception exception) {
            PythonExceptions.BaseException pythonException =
                PythonExceptions.ToPython(exception);
            string text = PythonOps.ToString(pythonException);
            return MakeExceptionInfo(
                context,
                DynamicHelpers.GetPythonType(pythonException),
                text,
                text,
                text);
        }

        private static object MakeExceptionInfo(
            CodeContext context,
            PythonType type,
            string? message,
            string? formatted,
            string? display) {
            return MakeNamespace(
                context,
                new Dictionary<string, object?> {
                    ["type"] = type,
                    ["msg"] = message,
                    ["formatted"] = formatted,
                    ["errdisplay"] = display
                });
        }

        private static object MakeNamespace(
            CodeContext context,
            IDictionary<string, object?> values) {
            object types = Importer.ImportModule(context, null, "types", false, 0);
            object simpleNamespace =
                PythonOps.GetBoundAttr(context, types, "SimpleNamespace");
            var kwargs = new PythonDictionary();
            foreach (KeyValuePair<string, object?> pair in values) {
                kwargs[pair.Key] = pair.Value;
            }
            return PythonCalls.CallWithKeywordArgs(
                context,
                simpleNamespace,
                Array.Empty<object>(),
                kwargs)!;
        }

        private static Dictionary<string, object?> DefaultConfig(string name) {
            bool isolated = name == "isolated";
            bool legacy = name == "legacy";
            return new Dictionary<string, object?> {
                ["use_main_obmalloc"] = legacy,
                ["allow_fork"] = legacy,
                ["allow_exec"] = legacy,
                ["allow_threads"] = !string.Equals(name, "empty", StringComparison.Ordinal),
                ["allow_daemon_threads"] = legacy,
                ["check_multi_interp_extensions"] = false,
                ["gil"] = isolated ? "own" : legacy ? "shared" : "default"
            };
        }
    }
}
