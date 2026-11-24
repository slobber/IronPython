// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;
using System.Text;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("frozendict")]
    public sealed class FrozenPythonDictionary :
        IDictionary<object, object>,
        IReadOnlyDictionary<object, object>,
        ICodeFormattable,
        IStructuralEquatable {
        private readonly PythonDictionary _data;
        private readonly FrozenDictionary<object, object> _lookup;

        private FrozenPythonDictionary(PythonDictionary data) {
            _data = data;
            _lookup = data.ToFrozenDictionary(
                item => item.Key,
                item => item.Value,
                (IEqualityComparer<object>)
                    DefaultContext.DefaultPythonContext.EqualityComparerNonGeneric);
        }

        public static FrozenPythonDictionary __new__(
            CodeContext context,
            [NotNone] PythonType cls) {
            ValidateType(cls);
            return new FrozenPythonDictionary(new PythonDictionary());
        }

        public static FrozenPythonDictionary __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [ParamDictionary] IDictionary<object, object> keywordArguments) {
            ValidateType(cls);
            return new FrozenPythonDictionary(new PythonDictionary(context, keywordArguments));
        }

        public static FrozenPythonDictionary __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] object source) {
            ValidateType(cls);
            if (source is FrozenPythonDictionary frozen) return frozen;
            return new FrozenPythonDictionary(new PythonDictionary(context, source));
        }

        public static FrozenPythonDictionary __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] object source,
            [ParamDictionary] IDictionary<object, object> keywordArguments) {
            ValidateType(cls);
            if (source is FrozenPythonDictionary frozen && keywordArguments.Count == 0) {
                return frozen;
            }
            var data = new PythonDictionary(context, source);
            data.update(context, keywordArguments);
            return new FrozenPythonDictionary(data);
        }

        private static void ValidateType(PythonType cls) {
            if (cls != TypeCache.FrozenDict) {
                throw PythonOps.TypeError(
                    "frozendict cannot be subclassed");
            }
        }

        public int Count => _lookup.Count;

        public IEnumerable<object> Keys => _data.Keys;

        ICollection<object> IDictionary<object, object>.Keys => _data.Keys;

        ICollection<object> IDictionary<object, object>.Values => _data.Values;

        IEnumerable<object> IReadOnlyDictionary<object, object>.Keys => _data.Keys;

        IEnumerable<object> IReadOnlyDictionary<object, object>.Values => _data.Values;

        public IEnumerable<object> Values => _data.Values;

        object IDictionary<object, object>.this[object key] {
            get => _lookup[key];
            set => throw Immutable();
        }

        object IReadOnlyDictionary<object, object>.this[object key] => _lookup[key];

        public object __getitem__([NotNone] object key) => _lookup[key];

        public object? get([NotNone] object key) {
            return _lookup.TryGetValue(key, out object? value) ? value : null;
        }

        public object? get([NotNone] object key, object? defaultValue) {
            return _lookup.TryGetValue(key, out object? value) ? value : defaultValue;
        }

        public bool __contains__([NotNone] object key) => _lookup.ContainsKey(key);

        public int __len__() => Count;

        public IEnumerator<object> __iter__() => _data.Keys.GetEnumerator();

        public DictionaryKeyView keys() => _data.keys();

        public DictionaryValueView values() => _data.values();

        public DictionaryItemView items() => _data.items();

        public FrozenPythonDictionary copy(CodeContext context) => this;

        [return: MaybeNotImplemented]
        public object __eq__(CodeContext context, [NotNone] object other) {
            if (other is FrozenPythonDictionary frozen) other = frozen._data;
            if (other is PythonDictionary dictionary) {
                return PythonOps.IsOrEqualsRetBool(_data, dictionary);
            }
            return NotImplementedType.Value;
        }

        [return: MaybeNotImplemented]
        public object __ne__(CodeContext context, [NotNone] object other) {
            object result = __eq__(context, other);
            return ReferenceEquals(result, NotImplementedType.Value)
                ? result
                : !PythonOps.IsTrue(result);
        }

        public object __or__(CodeContext context, [NotNone] object other) {
            if (other is not IDictionary<object, object> mapping) {
                return NotImplementedType.Value;
            }
            var result = new PythonDictionary(_data);
            result.update(context, mapping);
            return new FrozenPythonDictionary(result);
        }

        public object __ror__(CodeContext context, [NotNone] object other) {
            if (other is not IDictionary<object, object> mapping) {
                return NotImplementedType.Value;
            }
            var result = new PythonDictionary(context, mapping);
            result.update(context, this);
            return new FrozenPythonDictionary(result);
        }

        public string __repr__(CodeContext context) {
            var builder = new StringBuilder("frozendict({");
            bool first = true;
            foreach (KeyValuePair<object, object> item in _data) {
                if (!first) builder.Append(", ");
                first = false;
                builder.Append(PythonOps.Repr(context, item.Key));
                builder.Append(": ");
                builder.Append(PythonOps.Repr(context, item.Value));
            }
            builder.Append("})");
            return builder.ToString();
        }

        public int __hash__(CodeContext context) {
            return ((IStructuralEquatable)this).GetHashCode(
                context.LanguageContext.EqualityComparerNonGeneric);
        }

        bool IDictionary<object, object>.ContainsKey(object key) => _lookup.ContainsKey(key);

        void IDictionary<object, object>.Add(object key, object value) => throw Immutable();

        bool IDictionary<object, object>.Remove(object key) => throw Immutable();

        bool IDictionary<object, object>.TryGetValue(object key, out object value) {
            return _lookup.TryGetValue(key, out value!);
        }

        void ICollection<KeyValuePair<object, object>>.Add(
            KeyValuePair<object, object> item) => throw Immutable();

        void ICollection<KeyValuePair<object, object>>.Clear() => throw Immutable();

        bool ICollection<KeyValuePair<object, object>>.Contains(
            KeyValuePair<object, object> item) {
            return _lookup.TryGetValue(item.Key, out object? value)
                && PythonOps.IsOrEqualsRetBool(value, item.Value);
        }

        void ICollection<KeyValuePair<object, object>>.CopyTo(
            KeyValuePair<object, object>[] array,
            int arrayIndex) {
            _data.CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<object, object>>.IsReadOnly => true;

        bool ICollection<KeyValuePair<object, object>>.Remove(
            KeyValuePair<object, object> item) => throw Immutable();

        bool IReadOnlyDictionary<object, object>.ContainsKey(object key) {
            return _lookup.ContainsKey(key);
        }

        bool IReadOnlyDictionary<object, object>.TryGetValue(object key, out object value) {
            return _lookup.TryGetValue(key, out value!);
        }

        public IEnumerator<KeyValuePair<object, object>> GetEnumerator() {
            return _data.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => _data.Keys.GetEnumerator();

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
            if (ReferenceEquals(this, other)) return true;
            if (other is FrozenPythonDictionary frozen) other = frozen._data;
            return ((IStructuralEquatable)_data).Equals(other, comparer);
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
            return ((IStructuralEquatable)_data).GetHashCode(comparer);
        }

        private static Exception Immutable() {
            return PythonOps.TypeError("'frozendict' object is immutable");
        }
    }
}
