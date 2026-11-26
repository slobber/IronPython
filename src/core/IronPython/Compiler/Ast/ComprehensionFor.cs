// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using MSAst = System.Linq.Expressions;

using System;
using System.Collections;
using System.Collections.Generic;

using IronPython.Runtime.Binding;
using IronPython.Runtime.Exceptions;

using AstUtils = Microsoft.Scripting.Ast.Utils;

namespace IronPython.Compiler.Ast {
    using Ast = MSAst.Expression;

    public class ComprehensionFor : ComprehensionIterator {
        private static int _asyncCounter;

        public ComprehensionFor(Expression lhs, Expression list) {
            Left = lhs;
            List = list;
        }

        public Expression Left { get; }

        public Expression List { get; }

        public bool IsAsync { get; set; }

        internal override MSAst.Expression Transform(MSAst.Expression body) {
            if (IsAsync) {
                return TransformAsync(body);
            }

            MSAst.ParameterExpression temp = Ast.Parameter(typeof(KeyValuePair<IEnumerator, IDisposable>), "list_comprehension_for");

            return Ast.Block(
                new[] { temp },
                ForStatement.TransformFor(Parent, temp, List, Left, body, null, Span, GlobalParent.IndexToLocation(Left.EndIndex), null, null, false)
            );
        }

        private MSAst.Expression TransformAsync(MSAst.Expression body) {
            int id = System.Threading.Interlocked.Increment(ref _asyncCounter);
            MSAst.ParameterExpression iterator = Ast.Variable(
                typeof(object),
                "$async_comprehension_iter" + id);
            MSAst.ParameterExpression item = Ast.Variable(
                typeof(object),
                "$async_comprehension_item" + id);
            MSAst.LabelTarget complete = Ast.Label(
                typeof(void),
                "$async_comprehension_complete" + id);

            MSAst.Expression getIterator = Parent.GlobalParent.Operation(
                typeof(object),
                PythonOperationKind.AIter,
                AstUtils.Convert(List, typeof(object)));
            MSAst.Expression awaitNext = AstUtils.Await(
                Ast.Call(
                    AstMethods.AsTaskForAwait,
                    Parent.GlobalParent.Operation(
                        typeof(object),
                        PythonOperationKind.ANext,
                        iterator)));

            return Ast.Block(
                new[] { iterator, item },
                Ast.Assign(iterator, getIterator),
                AstUtils.Loop(
                    Ast.Constant(true),
                    null,
                    Ast.Block(
                        Ast.TryCatch(
                            Ast.Block(
                                Ast.Assign(item, awaitNext),
                                Ast.Empty()),
                            Ast.Catch(
                                typeof(StopAsyncIterationException),
                                Ast.Block(
                                    Ast.Break(complete),
                                    Ast.Empty()))),
                        Left.TransformSet(
                            Microsoft.Scripting.SourceSpan.None,
                            item,
                            PythonOperationKind.None),
                        body),
                    null,
                    complete,
                    null));
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                Left?.Walk(walker);
                List?.Walk(walker);
            }
            walker.PostWalk(this);
        }
    }
}
