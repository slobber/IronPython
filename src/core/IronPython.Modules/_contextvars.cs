// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

[assembly: PythonModule("_contextvars", typeof(IronPython.Modules.PythonContextVars))]
namespace IronPython.Modules {
    public static class PythonContextVars {
        internal static readonly object Missing = new object();

        private static readonly ConcurrentDictionary<PythonContext, AsyncLocal<ContextFrame?>> States =
            new ConcurrentDictionary<PythonContext, AsyncLocal<ContextFrame?>>();

        public static PythonType Context =>
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.Context));

        public static PythonType ContextVar =>
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.ContextVar));

        public static PythonType Token =>
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.Token));

        internal static ContextFrame Current(PythonContext context) {
            AsyncLocal<ContextFrame?> state = States.GetOrAdd(
                context,
                _ => new AsyncLocal<ContextFrame?>());
            return state.Value ??= new ContextFrame();
        }

        internal static ContextFrame? ReplaceCurrent(
            PythonContext context,
            ContextFrame? frame) {
            AsyncLocal<ContextFrame?> state = States.GetOrAdd(
                context,
                _ => new AsyncLocal<ContextFrame?>());
            ContextFrame? previous = state.Value;
            state.Value = frame;
            return previous;
        }

        public static Context copy_context(CodeContext context) {
            return new Context(Current(context.LanguageContext).Snapshot().Values);
        }

        internal sealed class ContextFrame {
            public ContextFrame(Dictionary<ContextVar, object?>? values = null) {
                Values = values ?? new Dictionary<ContextVar, object?>(ReferenceEqualityComparer.Instance);
            }

            public Dictionary<ContextVar, object?> Values { get; }

            public ContextFrame Snapshot() {
                return new ContextFrame(
                    new Dictionary<ContextVar, object?>(
                        Values,
                        ReferenceEqualityComparer.Instance));
            }
        }
    }

    [PythonType("ContextVar")]
    public sealed class ContextVar {
        public ContextVar([NotNone] string name) : this(name, PythonContextVars.Missing) { }

        private ContextVar([NotNone] string name, object? @default) {
            Name = name;
            Default = @default;
        }

        public static ContextVar __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] string name) {
            return new ContextVar(name);
        }

        public static ContextVar __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] string name,
            [ParamDictionary] IDictionary<object, object> keywordArguments) {
            object? @default = PythonContextVars.Missing;
            if (keywordArguments.TryGetValue("default", out object? suppliedDefault)) {
                @default = suppliedDefault;
            }
            if (keywordArguments.Count > (keywordArguments.ContainsKey("default") ? 1 : 0)) {
                throw PythonOps.TypeError(
                    "ContextVar() got an unexpected keyword argument");
            }
            return new ContextVar(name, @default);
        }

        public string __name__ => Name;

        public string name => Name;

        internal string Name { get; }

        internal object? Default { get; }

        public object? get(CodeContext context) {
            PythonContextVars.ContextFrame frame =
                PythonContextVars.Current(context.LanguageContext);
            if (frame.Values.TryGetValue(this, out object? value)) return value;
            if (!ReferenceEquals(Default, PythonContextVars.Missing)) return Default;
            throw PythonOps.LookupError(Name);
        }

        public object? get(CodeContext context, object? @default) {
            return PythonContextVars.Current(context.LanguageContext).Values
                .TryGetValue(this, out object? value)
                ? value
                : @default;
        }

        public Token set(CodeContext context, object? value) {
            PythonContextVars.ContextFrame frame =
                PythonContextVars.Current(context.LanguageContext);
            bool hadValue = frame.Values.TryGetValue(this, out object? oldValue);
            frame.Values[this] = value;
            return new Token(
                this,
                hadValue,
                oldValue,
                frame);
        }

        public void reset(CodeContext context, [NotNone] Token token) {
            if (!ReferenceEquals(token.Var, this)) {
                throw PythonOps.ValueError(
                    "Token was created by a different ContextVar");
            }
            PythonContextVars.ContextFrame frame =
                PythonContextVars.Current(context.LanguageContext);
            if (!ReferenceEquals(frame, token.Frame)) {
                throw PythonOps.ValueError("Token was created in a different Context");
            }
            token.Use();
            if (token.HadValue) frame.Values[this] = token.OldValue;
            else frame.Values.Remove(this);
        }

        public string __repr__() => $"<ContextVar name='{Name}' at {Id()}>";

        public int __hash__() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

        private string Id() => "0x" + __hash__().ToString("x", System.Globalization.CultureInfo.InvariantCulture);
    }

    [PythonType("Token")]
    public sealed class Token {
        internal Token(
            ContextVar @var,
            bool hadValue,
            object? oldValue,
            PythonContextVars.ContextFrame frame) {
            Var = @var;
            HadValue = hadValue;
            OldValue = oldValue;
            Frame = frame;
        }

        internal ContextVar Var { get; }
        internal bool HadValue { get; private set; }
        internal object? OldValue { get; private set; }
        internal PythonContextVars.ContextFrame Frame { get; private set; }
        internal bool Used { get; private set; }

        public ContextVar var => Var;

        public object? old_value => HadValue ? OldValue : PythonContextVars.Missing;

        internal void Use() {
            if (Used) throw PythonOps.RuntimeError("Token has already been used once");
            Used = true;
        }

        public string __repr__() =>
            $"<Token var={Var.__repr__()} at {System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}>";
    }

    [PythonType("Context")]
    public sealed class Context : IEnumerable<ContextVar> {
        private readonly Dictionary<ContextVar, object?> _values;
        private int _entered;

        internal Context(Dictionary<ContextVar, object?> values) {
            _values = values;
        }

        public object? __getitem__([NotNone] ContextVar key) {
            if (_values.TryGetValue(key, out object? value)) return value;
            throw PythonExceptions.CreateThrowable(PythonExceptions.KeyError, key);
        }

        public bool __contains__([NotNone] ContextVar key) => _values.ContainsKey(key);

        public int __len__() => _values.Count;

        public IEnumerator<ContextVar> __iter__() => _values.Keys.GetEnumerator();

        public object? get([NotNone] ContextVar key) {
            return _values.TryGetValue(key, out object? value) ? value : null;
        }

        public object? get([NotNone] ContextVar key, object? @default) {
            return _values.TryGetValue(key, out object? value) ? value : @default;
        }

        public object? run(
            CodeContext context,
            [NotNone] object callable,
            [NotNone] params object[] args) {
            if (Interlocked.Exchange(ref _entered, 1) != 0) {
                throw PythonOps.RuntimeError("cannot enter context: already entered");
            }
            PythonContextVars.ContextFrame? previous = PythonContextVars.ReplaceCurrent(
                context.LanguageContext,
                new PythonContextVars.ContextFrame(_values));
            try {
                return PythonCalls.Call(context, callable, args);
            } finally {
                PythonContextVars.ReplaceCurrent(context.LanguageContext, previous);
                Volatile.Write(ref _entered, 0);
            }
        }

        public object? run(CodeContext context, [NotNone] object callable) {
            return run(context, callable, Array.Empty<object>());
        }

        public Context copy() {
            return new Context(
                new Dictionary<ContextVar, object?>(
                    _values,
                    ReferenceEqualityComparer.Instance));
        }

        public string __repr__() => "<Context at 0x" +
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this).ToString("x") + ">";

        IEnumerator<ContextVar> IEnumerable<ContextVar>.GetEnumerator() => __iter__();

        IEnumerator IEnumerable.GetEnumerator() => __iter__();
    }
}
