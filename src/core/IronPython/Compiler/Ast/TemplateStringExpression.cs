// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Linq;

using MSAst = System.Linq.Expressions;
using AstUtils = Microsoft.Scripting.Ast.Utils;

namespace IronPython.Compiler.Ast {
    using Ast = MSAst.Expression;

    public sealed class TemplateStringExpression : Expression {
        private readonly string[] _strings;
        private readonly TemplateInterpolation[] _interpolations;

        public TemplateStringExpression(
            string[] strings,
            TemplateInterpolation[] interpolations) {
            _strings = strings ?? throw new ArgumentNullException(nameof(strings));
            _interpolations = interpolations
                ?? throw new ArgumentNullException(nameof(interpolations));
            if (_strings.Length != _interpolations.Length + 1) {
                throw new ArgumentException(
                    "Template strings must contain one more item than interpolations.");
            }
        }

        internal string[] Strings => _strings;
        internal TemplateInterpolation[] Interpolations => _interpolations;

        public override MSAst.Expression Reduce() {
            MSAst.Expression strings = Ast.NewArrayInit(
                typeof(string),
                _strings.Select(value => Ast.Constant(value)).ToArray());
            MSAst.Expression interpolations = Ast.NewArrayInit(
                typeof(object),
                _interpolations.Select(item => (MSAst.Expression)Ast.Call(
                    AstMethods.MakeInterpolation,
                    AstUtils.Convert(item.Value, typeof(object)),
                    Ast.Constant(item.Expression),
                    Ast.Constant(item.Conversion, typeof(string)),
                    item.FormatSpec is null
                        ? Ast.Constant(null, typeof(object))
                        : AstUtils.Convert(item.FormatSpec, typeof(object)))).ToArray());
            return Ast.Call(AstMethods.MakeTemplate, strings, interpolations);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                foreach (TemplateInterpolation item in _interpolations) {
                    item.Value.Walk(walker);
                    item.FormatSpec?.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }

        public override string NodeName => "template string";
    }

    public sealed class TemplateInterpolation {
        public TemplateInterpolation(
            Expression value,
            string expression,
            string? conversion,
            Expression? formatSpec) {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            Conversion = conversion;
            FormatSpec = formatSpec;
        }

        public Expression Value { get; }
        public string Expression { get; }
        public string? Conversion { get; }
        public Expression? FormatSpec { get; }
    }
}
