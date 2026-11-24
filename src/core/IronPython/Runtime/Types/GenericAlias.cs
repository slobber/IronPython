// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime.Types {
    [PythonType("GenericAlias")]
    public class GenericAlias : ICodeFormattable {
        protected GenericAlias() {
            __origin__ = TypeCache.Object;
            __args__ = PythonTuple.MakeTuple(Array.Empty<object?>());
        }

        public GenericAlias([NotNone] PythonType origin, [NotNone] params object?[] arguments)
            : this((object)origin, arguments) { }

        public GenericAlias([NotNone] object origin, [NotNone] params object?[] arguments) {
            __origin__ = origin;
            __args__ = PythonTuple.MakeTuple(arguments);
        }

        public object __origin__ { get; }

        public PythonTuple __args__ { get; }

        public string __module__ =>
            __origin__ is PythonType type ? type.Name : string.Empty;

        public object __getitem__([NotNone] object item) {
            object?[] arguments = item is PythonTuple tuple
                ? tuple.ToArray()
                : new[] { item };
            return new GenericAlias(__origin__, arguments);
        }

        public PythonTuple __mro_entries__([NotNone] object bases) =>
            PythonTuple.MakeTuple(__origin__);

        public object __or__(object? other) {
            return new UnionType(this, other);
        }

        public object __ror__(object? other) {
            return new UnionType(other, this);
        }

        public string __repr__(CodeContext context) {
            string arguments = string.Join(
                ", ",
                __args__.Select(item => item is PythonType type
                    ? type.Name
                    : PythonOps.Repr(context, item)));
            string origin = __origin__ is PythonType type
                ? type.Name
                : PythonOps.Repr(context, __origin__);
            return $"{origin}[{arguments}]";
        }

        public int __hash__() {
            int result = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__origin__);
            foreach (object? argument in __args__) {
                result = unchecked(result * 31 + (argument?.GetHashCode() ?? 0));
            }
            return result;
        }

        public object __eq__([NotNone] object other) {
            return other is GenericAlias alias
                && ReferenceEquals(__origin__, alias.__origin__)
                && __args__.SequenceEqual(alias.__args__);
        }

        public object __ne__([NotNone] object other) =>
            other is GenericAlias alias
                && ReferenceEquals(__origin__, alias.__origin__)
                && __args__.SequenceEqual(alias.__args__)
                ? false
                : true;
    }

    [PythonType("UnionType")]
    public sealed class UnionType : ICodeFormattable {
        public UnionType(object? left, object? right) {
            var members = new List<object>();
            AddMembers(members, left);
            AddMembers(members, right);
            __args__ = PythonTuple.MakeTuple(members.ToArray());
        }

        public PythonTuple __args__ { get; }

        public object __or__(object? other) {
            return new UnionType(this, other);
        }

        public object __ror__(object? other) {
            return new UnionType(other, this);
        }

        public string __repr__(CodeContext context) {
            return string.Join(
                " | ",
                __args__.Select(item => PythonOps.Repr(context, item)));
        }

        public int __hash__() {
            int result = 0;
            foreach (object? argument in __args__) {
                result ^= argument?.GetHashCode() ?? 0;
            }
            return result;
        }

        public object __eq__([NotNone] object other) {
            return other is UnionType union && __args__.SequenceEqual(union.__args__);
        }

        public object __ne__([NotNone] object other) {
            return other is UnionType union && __args__.SequenceEqual(union.__args__)
                ? false
                : true;
        }

        private static void AddMembers(List<object> members, object? value) {
            if (value is UnionType union) {
                foreach (object? member in union.__args__) AddMembers(members, member);
            } else if (!members.Any(member => member.Equals(value))) {
                members.Add(value!);
            }
        }
    }
}
