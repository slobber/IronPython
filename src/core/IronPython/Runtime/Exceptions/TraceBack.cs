// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime.Exceptions {
    [PythonType("traceback")]
    [Serializable]
    public sealed class TraceBack {
        private readonly TraceBack _next;
        private readonly TraceBackFrame _frame;
        private int _line;

        public TraceBack(TraceBack nextTraceBack, TraceBackFrame fromFrame) {
            _next = nextTraceBack;
            _frame = fromFrame;
        }

        public TraceBack tb_next {
            get {
                return _next;
            }
        }

        public TraceBackFrame tb_frame {
            get {
                return _frame;
            }
        }

        public int tb_lineno {
            get {
                return _line;
            }
        }

        public int tb_lasti {
            get {
                return _frame.f_code.GetTracebackPositionIndex(_line) * 2;
            }
        }

        internal void SetLine(int lineNumber) {
            _line = lineNumber;
        }

        /// <summary>
        /// returns string containing human readable representation of traceback
        /// </summary>
        internal string Extract() {
            var sb = new StringBuilder();
            var tb = this;
            while (tb != null) {
                var f = tb._frame;
                var lineno = tb._line;
                var co = f.f_code;
                var filename = co.co_filename;
                var name = co.co_name;
                sb.AppendFormat("  File \"{0}\", line {1}, in {2}{3}", filename, lineno, name, Environment.NewLine);
                tb = tb._next;
            }
            return sb.ToString();
        }
    }

    [PythonType("frame")]
    [DebuggerDisplay("Code = {f_code.co_name}, Line = {f_lineno}")]
    [Serializable]
    public sealed class TraceBackFrame {
        private readonly PythonTracebackListener _traceAdapter;
        private TracebackDelegate _trace;
        private object _traceObject;
        internal int _lineNo;
        private readonly PythonDebuggingPayload _debugProperties;
        private Func<IDictionary<object, object>> _scopeCallback;

        private readonly PythonDictionary _globals;
        private readonly object _locals;
        private readonly FunctionCode _code;
        private readonly CodeContext/*!*/ _context;
        private TraceBackFrame _back;
        private FrameLocalsProxy _localsProxy;
        private PythonDictionary _traceLocals;
        private bool _cleared;
        private bool _traceLines = true;
        private bool _traceOpcodes;
        private PythonGenerator _generator;
        private AsyncFrameState _asyncState;

        internal TraceBackFrame(CodeContext/*!*/ context, PythonDictionary globals, object locals, FunctionCode code) {
            _globals = globals;
            _locals = locals;
            _code = code;
            _context = context;
            _asyncState = AsyncFrameState.TryGet(locals);
            PythonGenerator current = PythonGenerator.Current;
            if (current is not null && current.gi_code == code) {
                _generator = current;
            }
        }

        internal TraceBackFrame(CodeContext/*!*/ context, PythonDictionary globals, object locals, FunctionCode code, TraceBackFrame back) {
            _globals = globals;
            _locals = locals;
            _code = code;
            _context = context;
            _back = back;
            _asyncState = AsyncFrameState.TryGet(locals);
            PythonGenerator current = PythonGenerator.Current;
            if (current is not null && current.gi_code == code) {
                _generator = current;
            }
        }

        internal TraceBackFrame(CodeContext/*!*/ context, PythonDictionary globals, object locals, FunctionCode code, AsyncFrameState asyncState) {
            _globals = globals;
            _locals = locals;
            _code = code;
            _context = context;
            _asyncState = asyncState;
        }

        internal TraceBackFrame(PythonTracebackListener traceAdapter, FunctionCode code, TraceBackFrame back, PythonDebuggingPayload debugProperties, Func<IDictionary<object, object>> scopeCallback, bool pushedByCaller) {
            _traceAdapter = traceAdapter;
            _code = code;
            _back = back;
            _debugProperties = debugProperties;
            _scopeCallback = scopeCallback;
            PushedByCaller = pushedByCaller;
        }

        /// <summary>
        ///   True if the frame stack entry of this frame was pushed, and is popped, by the caller of the code rather than the trace listener.
        /// </summary>
        /// <remarks>
        ///   This is the case for module code run by <c>exec</c> or a script runner, which push the entry for it since module code has
        ///   no frame push of its own (see <see cref="PythonTracebackListener"/>).
        /// </remarks>
        internal bool PushedByCaller { get; }

        internal void UpdateTraceFrame(
            TraceBackFrame back,
            Func<IDictionary<object, object>> scopeCallback) {
            _back = back;
            _scopeCallback = scopeCallback;
        }

        [SpecialName, PropertyMethod]
        public object Getf_trace() => _traceObject;

        [SpecialName, PropertyMethod]
        public void Setf_trace(object value) {
            _traceObject = value;
            if (_traceAdapter != null && value is not null) {
                _trace = (TracebackDelegate)Converter.ConvertToDelegate(value, typeof(TracebackDelegate));
            } else {
                _trace = null;
            }
        }

        [SpecialName, PropertyMethod]
        public void Deletef_trace() {
            Setf_trace(null);
        }

        public void clear() {
            if (IsExecuting()) {
                throw PythonOps.RuntimeError(
                    "cannot clear an executing frame");
            }
            if (_asyncState?.IsActive == true) {
                throw PythonOps.RuntimeError(
                    "cannot clear a suspended frame");
            }
            if (_generator is not null && !_generator.IsFinished) {
                throw PythonOps.RuntimeError(
                    "cannot clear a suspended frame");
            }
            PythonDictionary locals = GetLocalsDictionary();
            var names = new List<object>(locals.Count);
            foreach (KeyValuePair<object, object> item in locals) {
                names.Add(item.Key);
            }
            foreach (object key in names) {
                if (!Converter.TryConvertToString(key, out string name)
                    || !_code.IsFreeVariable(name)) {
                    locals.Remove(key);
                }
            }
            _cleared = true;
        }

        internal CodeContext Context {
            get {
                return _context;
            }
        }

        internal TracebackDelegate TraceDelegate {
            get {
                if (_traceAdapter != null) {
                    return _trace;
                } else {
                    return null;
                }
            }
        }

        public PythonDictionary f_globals {
            get {
                object context;
                if (_scopeCallback != null &&
                    _scopeCallback().TryGetValue(Compiler.Ast.PythonAst.GlobalContextName, out context) && context != null) {
                    return ((CodeContext)context).GlobalDict;
                } else {
                    return _globals;
                }
            }
        }

        public object f_locals {
            get {
                return _localsProxy ??= new FrameLocalsProxy(this);
            }
        }

        internal PythonDictionary GetLocalsDictionary() {
            if (_cleared) {
                return _localsProxy is not null
                    ? GetClearedDictionary()
                    : new PythonDictionary();
            }
            if (_context is not null) {
                return _context.Dict;
            }
            if (_scopeCallback is not null) {
                return _traceLocals ??=
                    new PythonDictionary(
                        new DebuggerDictionaryStorage(
                            _scopeCallback()));
            }
            if (_locals is PythonDictionary dictionary) {
                return dictionary;
            }
            return _traceLocals ??=
                new PythonDictionary(
                    new ObjectAttributesAdapter(
                        DefaultContext.Default,
                        _locals));
        }

        private PythonDictionary _clearedDictionary;

        private PythonDictionary GetClearedDictionary() {
            return _clearedDictionary ??= new PythonDictionary();
        }

        internal bool IsTrackedLocal(object key) {
            return Converter.TryConvertToString(key, out string name)
                && name is not null
                && _code.ContainsLocalVariable(name);
        }

        internal bool IsExecuting() {
            IList<FunctionStack> stack = PythonOps.GetFunctionStackNoCreate();
            if (stack is null) {
                return false;
            }
            foreach (FunctionStack entry in stack) {
                if (ReferenceEquals(entry.Frame, this)
                    || entry.Code == _code) {
                    return true;
                }
            }
            return false;
        }

        public FunctionCode f_code {
            get {
                return _code;
            }
        }

        public object f_builtins {
            get {
                // a frame created by the trace listener has no context of its own, but the listener knows the language context
                PythonContext context = _context?.LanguageContext ?? _traceAdapter.PythonContext;
                return context.BuiltinModuleDict;
            }
        }

        public TraceBackFrame f_back {
            get {
                if (_back is not null) {
                    return _back;
                }
                IList<FunctionStack> stack = PythonOps.GetFunctionStackNoCreate();
                if (stack is null) {
                    return null;
                }
                for (int i = 1; i < stack.Count; i++) {
                    FunctionStack current = stack[i];
                    if (current.Code == _code
                        && _context is not null
                        && ReferenceEquals(
                            current.Context.Dict,
                            _context.Dict)) {
                        FunctionStack previous = stack[i - 1];
                        TraceBackFrame back = previous.Frame
                            ?? new TraceBackFrame(
                                previous.Context,
                                previous.Context.GlobalDict,
                                previous.Context.Dict,
                                previous.Code);
                        back._lineNo = previous.Line;
                        stack[i - 1] = new FunctionStack(
                            previous.Context,
                            previous.Code,
                            back);
                        _back = back;
                        return back;
                    }
                }
                return null;
            }
        }

        [SpecialName, PropertyMethod]
        public void Deletef_trace_lines()
            => throw PythonOps.TypeError(
                "can't delete numeric/char attribute");

        [SpecialName, PropertyMethod]
        public void Deletef_trace_opcodes()
            => throw PythonOps.AttributeError(
                "cannot be deleted");

        public object f_trace_lines {
            get => _traceLines;
            set {
                if (value is not bool enabled) {
                    throw PythonOps.TypeError(
                        "attribute value type must be bool");
                }
                _traceLines = enabled;
            }
        }

        public object f_trace_opcodes {
            get => _traceOpcodes;
            set {
                if (value is not bool enabled) {
                    throw PythonOps.TypeError(
                        "attribute value type must be bool");
                }
                _traceOpcodes = enabled;
            }
        }

        public object f_generator {
            get {
                if (_generator is not null) {
                    return _generator;
                }
                PythonGenerator current = PythonGenerator.Current;
                if (current is not null && current.gi_code == _code) {
                    return current;
                }
                PythonCoroutine coroutine = PythonCoroutine.Current;
                return coroutine is not null && coroutine.Code == _code
                    ? coroutine
                    : null;
            }
        }

        internal void SetGenerator(PythonGenerator generator) {
            _generator = generator;
        }

        public string __repr__(CodeContext context) {
            return string.Format(
                "<frame at 0x{0:x}, file '{1}', line {2}, code {3}>",
                PythonOps.Id(this),
                _code.co_filename,
                f_lineno,
                _code.co_name);
        }

        public object f_lineno {
            get {
                if (_lineNo > 0) {
                    return _lineNo;
                }
                return 1;
            }
            set {
                if (!(value is int)) {
                    throw PythonOps.ValueError("lineno must be an integer");
                }

                SetLineNumber((int)value);
            }
        }

        private void SetLineNumber(int newLineNum) {
            var pyThread = PythonOps.GetFunctionStackNoCreate();
            if (_traceAdapter == null || !IsTopMostFrame(pyThread)) {
                throw PythonOps.ValueError("f_lineno can only be set by a trace function on the topmost frame");
            }

            FunctionCode funcCode = _debugProperties.Code;
#if FEATURE_NET_ASYNC
            // The frame of a traced async body reports its location but cannot change it.
            if ((funcCode.Flags & FunctionAttributes.Coroutine) != 0) {
                throw PythonOps.ValueError("can't jump in an async function or async generator");
            }
#endif
            Dictionary<int, Dictionary<int, bool>> loopAndFinallyLocations = _debugProperties.LoopAndFinallyLocations;
            Dictionary<int, bool> handlerLocations = _debugProperties.HandlerLocations;

            Dictionary<int, bool> currentLoopIds = null;
            bool inForLoopOrFinally = loopAndFinallyLocations != null && loopAndFinallyLocations.TryGetValue(_lineNo, out currentLoopIds);

            int originalNewLine = newLineNum;

            if (newLineNum < funcCode.Span.Start.Line) {
                throw PythonOps.ValueError("line {0} comes before the current code block", newLineNum);
            } else if (newLineNum > funcCode.Span.End.Line) {
                throw PythonOps.ValueError("line {0} comes after the current code block", newLineNum);
            }


            while (newLineNum <= funcCode.Span.End.Line) {
                var span = new SourceSpan(new SourceLocation(0, newLineNum, 1), new SourceLocation(0, newLineNum, Int32.MaxValue));

                // Check if we're jumping onto a handler
                bool handlerIsFinally;
                if (handlerLocations != null && handlerLocations.TryGetValue(newLineNum, out handlerIsFinally)) {
                    throw PythonOps.ValueError("can't jump to 'except' line");
                }

                // Check if we're jumping into a for-loop
                Dictionary<int, bool> jumpIntoLoopIds;
                if (loopAndFinallyLocations != null && loopAndFinallyLocations.TryGetValue(newLineNum, out jumpIntoLoopIds)) {
                    // If we're not in any loop already - then we can't jump into a loop
                    if (!inForLoopOrFinally) {
                        throw BadForOrFinallyJump(newLineNum, jumpIntoLoopIds);
                    }

                    // If we're in loops - we can only jump if we're not entering a new loop
                    foreach (int jumpIntoLoopId in jumpIntoLoopIds.Keys) {
                        if (!currentLoopIds.ContainsKey(jumpIntoLoopId)) {
                            throw BadForOrFinallyJump(newLineNum, currentLoopIds);
                        }
                    }
                } else if (currentLoopIds != null) {
                    foreach (bool isFinally in currentLoopIds.Values) {
                        if (isFinally) {
                            throw PythonOps.ValueError("can't jump out of 'finally block'");
                        }
                    }
                }

                if (_traceAdapter.PythonContext.TracePipeline.TrySetNextStatement(_code.co_filename, span)) {
                    _lineNo = newLineNum;
                    return;
                }

                ++newLineNum;
            }

            throw PythonOps.ValueError("line {0} is invalid jump location ({1} - {2} are valid)", originalNewLine, funcCode.Span.Start.Line, funcCode.Span.End.Line);
        }

        private bool IsTopMostFrame(List<FunctionStack> pyThread) {
            return pyThread != null && pyThread.Count != 0 && pyThread[pyThread.Count - 1].Frame == this;
        }

        private static Exception BadForOrFinallyJump(int newLineNum, Dictionary<int, bool> jumpIntoLoopIds) {
            foreach (bool isFinally in jumpIntoLoopIds.Values) {
                if (isFinally) {
                    return PythonOps.ValueError("can't jump into 'finally block'");
                }
            }
            return PythonOps.ValueError("can't jump into 'for loop'");
        }
    }

    internal sealed class AsyncFrameState {
        private static readonly ConditionalWeakTable<object, AsyncFrameState> s_states = new();

        internal bool IsActive { get; set; }

        internal static void Register(object locals, AsyncFrameState state) {
            s_states.AddOrUpdate(locals, state);
        }

        internal static AsyncFrameState TryGet(object locals) {
            return locals is not null && s_states.TryGetValue(locals, out AsyncFrameState state)
                ? state
                : null;
        }
    }

    public delegate TracebackDelegate TracebackDelegate(TraceBackFrame frame, string result, object payload);
}
