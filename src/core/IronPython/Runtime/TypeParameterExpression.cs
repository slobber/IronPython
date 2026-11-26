// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using IronPython.Runtime.Operations;

using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("_TypeParameterExpression")]
    public sealed class TypeParameterExpression {
        private readonly CodeContext _context;
        private readonly string _source;
        private readonly object? _fallback;
        private PythonTuple _typeParameters = PythonTuple.EMPTY;
        private object? _classOwner;

        internal TypeParameterExpression(
            CodeContext context,
            string source,
            object? fallback = null) {
            _context = context;
            _source = source;
            _fallback = fallback;
        }

        public object? __call__() {
            try {
                return LightExceptions.CheckAndThrow(PythonOps.EvalAnnotationSource(
                    _context,
                    _source,
                    _typeParameters,
                    classOwner: _classOwner));
            } catch (System.Exception) when (_fallback is not null) {
                return LightExceptions.CheckAndThrow(
                    PythonCalls.Call(_context, _fallback));
            }
        }

        public void _set_type_parameters([NotNone] PythonTuple typeParameters) {
            _typeParameters = typeParameters;
        }

        internal void SetClassOwner(object owner) {
            _classOwner = owner;
        }

        public override string ToString() => $"<type parameter expression {_source}>";
    }
}
