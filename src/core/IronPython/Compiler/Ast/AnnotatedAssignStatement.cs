// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;

using IronPython.Runtime.Binding;

using MSAst = System.Linq.Expressions;

namespace IronPython.Compiler.Ast {

    public class AnnotatedAssignStatement : Statement {
        public AnnotatedAssignStatement(Expression target, Expression annotation, Expression? value, bool simple) {
            Target = target;
            Annotation = annotation;
            Value = value;
            Simple = simple;
        }

        public Expression Target { get; }

        public Expression Annotation { get; }

        public Expression? Value { get; }

        public bool Simple { get; }

        public override MSAst.Expression Reduce() {
            if (Parent is PythonAst || Parent is ClassDefinition) {
                if (Target is NameExpression name) {
                    MSAst.Expression annotation = MSAst.Expression.Call(
                        AstMethods.SetAnnotation,
                        Parent.LocalContext,
                        MSAst.Expression.Constant(Parent is PythonAst),
                        MSAst.Expression.Constant(name.Name),
                        MSAst.Expression.Constant(GlobalParent.GetSourceText(Annotation.IndexSpan)),
                        MSAst.Expression.Lambda<Func<object>>(
                            MSAst.Expression.Convert(Annotation, typeof(object))),
                        MSAst.Expression.Constant(GlobalParent.FutureAnnotations));
                    if (Value is not null) {
                        annotation = MSAst.Expression.Block(
                            annotation,
                            MSAst.Expression.Empty(),
                            Target.TransformSet(Span, Value, PythonOperationKind.None));
                    }
                    return annotation;
                }
            }

            return Value is null
                ? Empty()
                : Target.TransformSet(Span, Value, PythonOperationKind.None);
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                Target.Walk(walker);
                Annotation.Walk(walker);
                Value?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
