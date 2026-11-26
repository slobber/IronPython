// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime.Exceptions {
    [PythonType("FrameLocalsProxy")]
    public sealed class FrameLocalsProxy : IEnumerable<object> {
        [ThreadStatic]
        private static int _reprDepth;

        private readonly TraceBackFrame _frame;

        internal FrameLocalsProxy([NotNone] TraceBackFrame frame) {
            _frame = frame;
        }

        public static object __new__(
            CodeContext context,
            [NotNone] PythonType type,
            [NotNone] object value,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            if (kwargs.Count != 0) {
                throw PythonOps.TypeError(
                    "FrameLocalsProxy() takes no keyword arguments");
            }
            if (value is not TraceBackFrame traceBackFrame) {
                throw PythonOps.TypeError(
                    "FrameLocalsProxy() argument must be a frame");
            }
            return new FrameLocalsProxy(traceBackFrame);
        }

        private PythonDictionary Locals => _frame.GetLocalsDictionary();

        public int __len__() => VisibleItems().Count();

        public bool __contains__(object key)
            => TryGetVisible(key, out _);

        public object __getitem__(CodeContext context, object key) {
            object normalized = NormalizeKey(context, key);
            if (!TryGetVisible(normalized, out object value)) {
                throw PythonOps.KeyError(key);
            }
            return value;
        }

        public void __setitem__(CodeContext context, object key, object value)
            => Locals[NormalizeKey(context, key)] = value;

        public void __delitem__(CodeContext context, object key) {
            object normalized = NormalizeKey(context, key);
            if (!TryGetVisible(normalized, out _)) {
                throw PythonOps.KeyError(key);
            }
            if (_frame.IsTrackedLocal(normalized)) {
                throw PythonOps.ValueError(
                    "cannot delete local variables");
            }
            Locals.Remove(normalized);
        }

        public object get(CodeContext context, object key, object defaultValue = null)
            => TryGetVisible(NormalizeKey(context, key), out object value)
                ? value
                : defaultValue;

        public PythonList keys() {
            var result = new PythonList();
            foreach (KeyValuePair<object, object> item in VisibleItems()) {
                result.Add(item.Key);
            }
            return result;
        }

        public PythonList values() {
            var result = new PythonList();
            foreach (KeyValuePair<object, object> item in VisibleItems()) {
                result.Add(item.Value);
            }
            return result;
        }

        public PythonList items() {
            var result = new PythonList();
            foreach (KeyValuePair<object, object> item in VisibleItems()) {
                result.Add(PythonTuple.MakeTuple(item.Key, item.Value));
            }
            return result;
        }

        public PythonDictionary copy(CodeContext context)
            => Snapshot(context);

        public PythonList __reversed__() {
            var result = keys();
            result.reverse();
            return result;
        }

        public void update(CodeContext context, object other)
            => DictionaryOps.update(context, Locals, other);

        public void update(
            CodeContext context,
            object other,
            [ParamDictionary] IDictionary<object, object> otherArgs)
            => Locals.update(context, other, otherArgs);

        public object setdefault(
            CodeContext context,
            object key,
            object defaultValue = null) {
            object normalized = NormalizeKey(context, key);
            if (TryGetVisible(normalized, out object value)) {
                return value;
            }
            Locals[normalized] = defaultValue;
            return defaultValue;
        }

        public object pop(CodeContext context, object key) {
            object normalized = NormalizeKey(context, key);
            if (!TryGetVisible(normalized, out object value)) {
                throw PythonOps.KeyError(key);
            }
            if (_frame.IsTrackedLocal(normalized)) {
                throw PythonOps.ValueError(
                    "cannot remove local variables");
            }
            Locals.Remove(normalized);
            return value;
        }

        public object pop(
            CodeContext context,
            object key,
            object defaultValue) {
            object normalized = NormalizeKey(context, key);
            if (!TryGetVisible(normalized, out object value)) {
                return defaultValue;
            }
            if (_frame.IsTrackedLocal(normalized)) {
                throw PythonOps.ValueError(
                    "cannot remove local variables");
            }
            Locals.Remove(normalized);
            return value;
        }

        public PythonDictionary __or__(
            CodeContext context,
            object other) {
            PythonDictionary result = Snapshot(context);
            DictionaryOps.update(context, result, other);
            return result;
        }

        public object __ior__(CodeContext context, object other) {
            if (!PythonOps.IsMappingType(context, other)) {
                throw PythonOps.TypeError(
                    "unsupported operand type(s) for |=: "
                    + "'FrameLocalsProxy' and '{0}'",
                    PythonOps.GetPythonTypeName(other));
            }
            DictionaryOps.update(context, Locals, other);
            return this;
        }

        public string __repr__(CodeContext context) {
            if (_reprDepth >= 100) {
                throw PythonOps.RecursionError(
                    "maximum recursion depth exceeded");
            }
            _reprDepth++;
            List<object> infinite = PythonOps.GetAndCheckInfinite(this);
            if (infinite is null) {
                _reprDepth--;
                return "{...}";
            }
            int index = infinite.Count;
            infinite.Add(this);
            try {
                return PythonOps.Repr(context, Snapshot(context));
            } finally {
                infinite.RemoveAt(index);
                _reprDepth--;
            }
        }

        public bool __eq__(CodeContext context, object other)
            => PythonOps.EqualRetBool(context, Snapshot(context), other);

        public object __reduce_ex__(CodeContext context, int protocol)
            => throw PythonOps.TypeError(
                "cannot pickle FrameLocalsProxy");

        public IEnumerator<object> GetEnumerator() {
            foreach (KeyValuePair<object, object> item in VisibleItems()) {
                yield return item.Key;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private object NormalizeKey(CodeContext context, object key) {
            PythonOps.Hash(context, key);
            if (Converter.TryConvertToString(key, out string name)
                && _frame.f_code.ContainsLocalVariable(name)) {
                return name;
            }
            foreach (string localName in _frame.f_code.LocalVariableNames) {
                if (PythonOps.EqualRetBool(context, localName, key)) {
                    return localName;
                }
            }
            foreach (object existing in Locals.Keys) {
                if (existing is string
                    && PythonOps.EqualRetBool(context, existing, key)) {
                    return existing;
                }
            }
            return key;
        }

        private bool TryGetVisible(object key, out object value) {
            return Locals.TryGetValue(key, out value)
                && !ReferenceEquals(value, Uninitialized.Instance);
        }

        private IEnumerable<KeyValuePair<object, object>> VisibleItems() {
            foreach (KeyValuePair<object, object> item in Locals) {
                if (!ReferenceEquals(item.Value, Uninitialized.Instance)) {
                    yield return item;
                }
            }
        }

        private PythonDictionary Snapshot(CodeContext context) {
            var result = new PythonDictionary();
            foreach (KeyValuePair<object, object> item in VisibleItems()) {
                result[item.Key] = item.Value;
            }
            return result;
        }
    }
}
