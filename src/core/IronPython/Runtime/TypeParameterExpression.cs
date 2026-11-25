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
        private PythonTuple _typeParameters = PythonTuple.EMPTY;

        internal TypeParameterExpression(CodeContext context, string source) {
            _context = context;
            _source = source;
        }

        public object? __call__() =>
            LightExceptions.CheckAndThrow(PythonOps.EvalAnnotationSource(
                _context,
                _source,
                _typeParameters));

        public void _set_type_parameters([NotNone] PythonTuple typeParameters) {
            _typeParameters = typeParameters;
        }

        public override string ToString() => $"<type parameter expression {_source}>";
    }
}
