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
        private readonly object _thunk;
        private readonly PythonTuple _typeParameters;
        private object? _value;

        internal TypeAliasObject(
            string name,
            object thunk,
            PythonTuple typeParameters) {
            _name = name;
            _thunk = thunk;
            _typeParameters = typeParameters;
        }

        public string __name__ => _name;

        public PythonTuple __type_params__ => _typeParameters;

        public object __getitem__([NotNone] object parameters) {
            object?[] arguments = parameters is PythonTuple tuple
                ? tuple.ToArray()
                : new[] { parameters };
            return new GenericAlias(this, arguments);
        }

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
