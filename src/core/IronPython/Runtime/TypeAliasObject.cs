// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Runtime.CompilerServices;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("TypeAliasType")]
    public sealed class TypeAliasObject {
        private readonly string _name;
        private readonly string _qualname;
        private readonly object? _module;
        private readonly object _thunk;
        private readonly PythonTuple _typeParameters;
        private object? _value;

        internal TypeAliasObject(
            string name,
            string qualname,
            object? module,
            object thunk,
            PythonTuple typeParameters) {
            _name = name;
            _qualname = qualname;
            _module = module;
            _thunk = thunk;
            _typeParameters = typeParameters;
        }

        public string __name__ => _name;

        public string __qualname__ => _qualname;

        public object? __module__ => _module;

        public PythonTuple __type_params__ => _typeParameters;

        public PythonTuple __parameters__ => _typeParameters;

        public object __getitem__([NotNone] object parameters) {
            object?[] arguments = parameters is PythonTuple tuple
                ? tuple.ToArray()
                : new[] { parameters };
            if (_typeParameters.Count == 0) {
                throw PythonOps.TypeError("Only generic type aliases are subscriptable");
            }
            if (_typeParameters.Count == 1 && arguments.Length != 1) {
                throw PythonOps.TypeError(
                    $"Too many arguments for {_name}; actual {arguments.Length}, expected 1");
            }
            if (_typeParameters.Count > 1 && arguments.Length != _typeParameters.Count) {
                throw PythonOps.TypeError(
                    $"Too {(arguments.Length > _typeParameters.Count ? "many" : "few")} arguments " +
                    $"for {_name}; actual {arguments.Length}, expected {_typeParameters.Count}");
            }
            return new GenericAlias(this, arguments);
        }

        public object __or__(object? other) => new UnionType(this, other);

        public object __ror__(object? other) => new UnionType(other, this);

        [SpecialName]
        public object? GetCustomMember(CodeContext context, [NotNone] string name) {
            if (name == "__value__") {
                return _value ??= PythonCalls.Call(context, _thunk);
            }
            return OperationFailed.Value;
        }

        public object __repr__(CodeContext context) =>
            _name;
    }
}
