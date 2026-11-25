// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Runtime.CompilerServices;
using System.Linq;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("TypeAliasType")]
    public sealed class TypeAliasObject {
        private readonly string _name;
        private readonly string _qualname;
        private object? _module;
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "IPY01",
            Justification = "The setter deliberately raises a Python-facing read-only error.")]
        public string __name__ {
            get => _name;
            set => throw PythonOps.AttributeError("readonly attribute '__name__'");
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "IPY01",
            Justification = "The setter deliberately raises a Python-facing read-only error.")]
        public string __qualname__ {
            get => _qualname;
            set => throw PythonOps.AttributeError("readonly attribute '__qualname__'");
        }

        public object? __module__ {
            get => _module;
            set => _module = value;
        }

        public void __setattr__(
            CodeContext context,
            [NotNone] string name,
            [NotNone] object value) {
            if (name == "__module__") {
                _module = value;
                return;
            }
            throw PythonOps.TypeError("readonly attribute '{0}'", name);
        }

        public PythonTuple __type_params__ => _typeParameters;

        public PythonTuple __parameters__ {
            get {
                var parameters = new System.Collections.Generic.List<object?>();
                foreach (object parameter in _typeParameters) {
                    if (DynamicHelpers.GetPythonType(parameter).Name == "TypeVarTuple") {
                        var enumerator = PythonOps.GetEnumerator(parameter);
                        while (enumerator.MoveNext()) {
                            parameters.Add(enumerator.Current);
                        }
                    } else {
                        parameters.Add(parameter);
                    }
                }
                return PythonTuple.MakeTuple(parameters.ToArray());
            }
        }

        public object __getitem__([NotNone] object parameters) {
            object?[] arguments = parameters is PythonTuple tuple
                ? tuple.ToArray()
                : new[] { parameters };
            if (_typeParameters.Count == 0) {
                throw PythonOps.TypeError("Only generic type aliases are subscriptable");
            }
            bool variadic = _typeParameters.Any(parameter =>
                DynamicHelpers.GetPythonType(parameter).Name == "TypeVarTuple");
            if (!variadic && _typeParameters.Count == 1 && arguments.Length != 1) {
                throw PythonOps.TypeError(
                    $"Too many arguments for {_name}; actual {arguments.Length}, expected 1");
            }
            if (!variadic && _typeParameters.Count > 1 && arguments.Length != _typeParameters.Count) {
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
                return _value ??= PythonCalls.Call(
                    context,
                    _thunk,
                    _typeParameters.ToArray());
            }
            return OperationFailed.Value;
        }

        public object __repr__(CodeContext context) =>
            _qualname;
    }
}
