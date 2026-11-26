// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

#if NETCOREAPP3_1 // IDictionary<object?, object?> is incorrectly annotated with TKey : notnull
#pragma warning disable CS8714 // The type cannot be used as type parameter in the generic type or method. Nullability of type argument doesn't match 'notnull' constraint.
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Scripting.Runtime;

using IronPython.Runtime.Operations;

namespace IronPython.Runtime.Types {
    [PythonType("mappingproxy")]
    public sealed class MappingProxy {
        internal PythonDictionary GetDictionary(CodeContext context) {
            if (dictionary is not null) return dictionary;
            if (type is not null) return type.GetMemberDictionary(context, false);
            var result = new PythonDictionary();
            result.update(context, source!);
            return result;
        }

        private object GetMapping(CodeContext context) {
            if (dictionary is not null) return dictionary;
            if (type is not null) return type.GetMemberDictionary(context, false);
            return source!;
        }

        private bool TryGetValue(CodeContext context, object? key, out object? value) {
            if (dictionary is not null) {
                return dictionary.TryGetValue(key, out value);
            }

            string? name = key as string ?? (key as Extensible<string>)?.Value;
            if (name is not null && type!.TryLookupSlot(context, name, out PythonTypeSlot slot)) {
                value = slot is PythonTypeUserDescriptorSlot dts ? dts.Value : slot;
                return true;
            }

            value = null;
            return false;
        }

        private readonly PythonDictionary? dictionary;
        private readonly PythonType? type;
        private readonly object? source;

        internal MappingProxy(CodeContext context, PythonType/*!*/ dt) {
            Debug.Assert(dt != null);
            type = dt;
        }

        public MappingProxy(CodeContext context, [NotNone] object value) {
            if (value is PythonDictionary dict) {
                dictionary = dict;
                return;
            }
            if (!PythonOps.TryGetBoundAttr(
                    context,
                    value,
                    "keys",
                    out object? keys)
                || keys is null) {
                throw PythonOps.TypeError(
                    "mappingproxy() argument must be a mapping, not '{0}'",
                    PythonOps.GetPythonTypeName(value));
            }
            source = value;
        }

        #region Python Public API Surface

        public int __len__(CodeContext context)
            => PythonOps.Length(GetMapping(context));

        public bool __contains__(CodeContext/*!*/ context, object? value) {
            return PythonTypeOps.TryInvokeBinaryOperator(
                    context,
                    GetMapping(context),
                    value!,
                    "__contains__",
                    out object? result)
                && PythonOps.IsTrue(result);
        }

        public object? __getitem__(CodeContext context, object? key) {
            object mapping = GetMapping(context);
            if (PythonOps.TryGetBoundAttr(
                    context,
                    mapping,
                    "__getitem__",
                    out object? getItem)
                && getItem is not null) {
                return PythonCalls.Call(context, getItem, key);
            }
            return PythonOps.GetIndex(context, mapping, key!);
        }

        public string/*!*/ __str__(CodeContext/*!*/ context) => __repr__(context);

        public string __repr__(CodeContext/*!*/ context) {
            var dict = GetDictionary(context);
            List<object>? infinite = PythonOps.GetAndCheckInfinite(this);
            if (infinite == null) {
                return "mappingproxy({...})";
            }

            int infiniteIndex = infinite.Count;
            infinite.Add(this);
            try {
                return $"mappingproxy({PythonOps.Repr(context, dict)})";
            } finally {
                System.Diagnostics.Debug.Assert(infiniteIndex == infinite.Count - 1);
                infinite.RemoveAt(infiniteIndex);
            }
        }

        public object? get(CodeContext/*!*/ context, object? k, object? d = null) {
            object mapping = GetMapping(context);
            if (PythonOps.TryGetBoundAttr(
                    context,
                    mapping,
                    "get",
                    out object? getMethod)
                && getMethod is not null) {
                return PythonCalls.Call(context, getMethod, k, d);
            }
            if (TryGetValue(context, k, out object? res)) {
                return res;
            }

            return d;
        }

        public object keys(CodeContext context) {
            object mapping = GetMapping(context);
            return PythonTypeOps.TryInvokeUnaryOperator(
                    context,
                    mapping,
                    "keys",
                    out object? result)
                ? result!
                : throw PythonOps.TypeError("'mappingproxy' object has no attribute 'keys'");
        }

        public object values(CodeContext context) {
            object mapping = GetMapping(context);
            return PythonTypeOps.TryInvokeUnaryOperator(
                    context,
                    mapping,
                    "values",
                    out object? result)
                ? result!
                : throw PythonOps.TypeError("'mappingproxy' object has no attribute 'values'");
        }

        public object items(CodeContext context) {
            object mapping = GetMapping(context);
            return PythonTypeOps.TryInvokeUnaryOperator(
                    context,
                    mapping,
                    "items",
                    out object? result)
                ? result!
                : throw PythonOps.TypeError("'mappingproxy' object has no attribute 'items'");
        }

        public object copy(CodeContext/*!*/ context) {
            object mapping = GetMapping(context);
            if (mapping is PythonDictionary dictionary
                && DynamicHelpers.GetPythonType(mapping).Name == "dict") {
                return dictionary.copy(context);
            }
            if (PythonOps.TryGetBoundAttr(
                    context,
                    mapping,
                    "copy",
                    out object? copyMethod)
                && copyMethod is not null) {
                return PythonOps.Invoke(context, mapping, "copy")!;
            }
            return GetDictionary(context).copy(context);
        }

        public int __hash__(CodeContext context)
            => PythonOps.Hash(context, GetMapping(context));

        public object __eq__(CodeContext/*!*/ context, object? other) {
            if (other is MappingProxy proxy) {
                other = proxy.GetMapping(context);
            }
            if (other is null) {
                return false;
            }
            if (PythonTypeOps.TryInvokeBinaryOperator(
                    context,
                    GetMapping(context),
                    other,
                    "__eq__",
                    out object? result)) {
                if (!ReferenceEquals(result, NotImplementedType.Value)) {
                    return result!;
                }
            }
            object mapping = GetMapping(context);
            object copy = DynamicHelpers.GetPythonType(mapping).Name == "dict"
                ? ShallowCopy(context, mapping)
                : mapping;
            if (PythonTypeOps.TryInvokeBinaryOperator(
                    context,
                    other,
                    copy,
                    "__eq__",
                    out result)
                && !ReferenceEquals(result, NotImplementedType.Value)) {
                return result!;
            }
            return false;
        }

        public object __ne__(CodeContext context, object? other) {
            object result = __eq__(context, other);
            return !PythonOps.IsTrue(result);
        }

        public object __or__(CodeContext context, object? other) {
            object value = other is MappingProxy proxy
                ? proxy.GetMapping(context)
                : other!;
            if (PythonTypeOps.TryInvokeBinaryOperator(
                    context,
                    GetMapping(context),
                    value,
                    "__or__",
                    out object? result)
                && !ReferenceEquals(result, NotImplementedType.Value)) {
                return result!;
            }
            throw PythonOps.TypeError(
                "unsupported operand type(s) for |: 'mappingproxy' and '{0}'",
                PythonOps.GetPythonTypeName(other));
        }

        public object __ror__(CodeContext context, object? other) {
            object value = other is MappingProxy proxy
                ? proxy.GetMapping(context)
                : other!;
            if (PythonTypeOps.TryInvokeBinaryOperator(
                    context,
                    value,
                    GetDictionary(context),
                    "__or__",
                    out object? result)
                && !ReferenceEquals(result, NotImplementedType.Value)) {
                return result!;
            }
            throw PythonOps.TypeError(
                "unsupported operand type(s) for |: '{0}' and 'mappingproxy'",
                PythonOps.GetPythonTypeName(other));
        }

        public object __ior__(CodeContext context, object? other) {
            throw PythonOps.TypeError(
                "unsupported operand type(s) for |=: 'mappingproxy' and '{0}'",
                PythonOps.GetPythonTypeName(other));
        }

        public object __iter__(CodeContext context) {
            object mapping = GetMapping(context);
            if (PythonTypeOps.TryInvokeUnaryOperator(
                    context,
                    mapping,
                    "__iter__",
                    out object? result)) {
                return result!;
            }
            return PythonOps.GetEnumerator(context, mapping);
        }

        public IEnumerator __reversed__(CodeContext context) {
            var keys = new List<object?>();
            IEnumerator enumerator = PythonOps.GetEnumerator(
                context,
                GetMapping(context));
            while (enumerator.MoveNext()) {
                keys.Add(enumerator.Current);
            }
            keys.Reverse();
            return keys.GetEnumerator();
        }

        public static GenericAlias __class_getitem__(
            [NotNone] PythonTuple parameters) {
            return new GenericAlias(
                DynamicHelpers.GetPythonTypeFromType(typeof(MappingProxy)),
                parameters.ToArray());
        }

        public object __lt__(CodeContext context, object? other)
            => ThrowUnsupportedComparison(context, other);

        public object __le__(CodeContext context, object? other)
            => ThrowUnsupportedComparison(context, other);

        public object __gt__(CodeContext context, object? other)
            => ThrowUnsupportedComparison(context, other);

        public object __ge__(CodeContext context, object? other)
            => ThrowUnsupportedComparison(context, other);

        private static object ThrowUnsupportedComparison(
            CodeContext context,
            object? other) {
            throw PythonOps.TypeError(
                "not supported between instances of 'mappingproxy' and '{0}'",
                GetQualifiedTypeName(other));
        }

        private static object ShallowCopy(CodeContext context, object value) {
            object copyModule = Importer.ImportModule(
                context,
                null,
                "copy",
                false,
                0);
            object copy = PythonOps.GetBoundAttr(
                context,
                copyModule,
                "copy");
            return PythonCalls.Call(context, copy, value)!;
        }

        private static string GetQualifiedTypeName(object? value) {
            if (value is MappingProxy) return "mappingproxy";
            PythonType type = DynamicHelpers.GetPythonType(value);
            string? module = PythonType.Get__module__(DefaultContext.Default, type) as string;
            return module == "collections"
                ? module + "." + type.Name
                : type.Name;
        }

        #endregion

    }
}
