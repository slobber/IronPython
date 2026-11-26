// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Threading;

using Microsoft.Scripting;

using IronPython.Runtime.Binding;

using MSAst = System.Linq.Expressions;

using AstUtils = Microsoft.Scripting.Ast.Utils;

namespace IronPython.Compiler.Ast {
    using Ast = MSAst.Expression;

    /// <summary>
    /// Represents an async with statement.
    /// Desugared to Python AST that uses await on __aenter__ and __aexit__.
    /// </summary>
    public class AsyncWithStatement : Statement {
        private static int _counter;
        private Statement? _desugared;

        public AsyncWithStatement(Expression contextManager, Expression? var, Statement body) {
            ContextManager = contextManager;
            Variable = var;
            Body = body;
        }

        public int HeaderIndex { private get; set; }

        public Expression ContextManager { get; }

        public new Expression? Variable { get; }

        public Statement Body { get; }

        /// <summary>
        /// Build the desugared tree. Called during Walk when Parent and IndexSpan are available.
        /// </summary>
        private Statement BuildDesugared() {
            var parent = Parent;
            var span = IndexSpan;

            // async with EXPR as VAR:
            //     BLOCK
            //
            // desugars to:
            //
            // mgr = EXPR
            // exited = False
            // try:
            //     VAR = await mgr.__aenter__()  (or just await mgr.__aenter__())
            //     BLOCK
            // except BaseException as exc:
            //     exited = True
            //     if not await mgr.__aexit__(type(exc), exc, exc.__traceback__):
            //         raise
            // finally:
            //     if not exited:
            //         await mgr.__aexit__(None, None, None)
            //
            // `exited` is set before waiting for __aexit__, so an exception raised
            // by the exceptional exit call cannot cause a duplicate normal exit.

            int id = Interlocked.Increment(ref _counter);
            string managerName = $"__asyncwith_mgr{id}";
            string valueName = $"__asyncwith_value{id}";
            string exitedName = $"__asyncwith_exited{id}";
            string exceptionName = $"__asyncwith_exception{id}";

            // Helper to assign proper parent and span to nodes
            T SetScope<T>(T node) where T : Node {
                node.Parent = parent;
                node.IndexSpan = span;
                return node;
            }

            NameExpression Name(string name, bool compilerGenerated = false) {
                var result = SetScope(new NameExpression(name));
                result.IsCompilerGenerated = compilerGenerated;
                return result;
            }

            SuiteStatement Suite(params Statement[] statements)
                => SetScope(new SuiteStatement(statements));

            // mgr = EXPR
            var assignMgr = SetScope(new AssignmentStatement(
                [
                    Name(managerName, compilerGenerated: true)
                ],
                ContextManager));

            // exited = False
            var assignExited = SetScope(new AssignmentStatement(
                [
                    Name(exitedName, compilerGenerated: true)
                ],
                SetScope(new ConstantExpression(false))));

            // await mgr.__aenter__()
            var aenterAttr = SetScope(new MemberExpression(
                Name(managerName, compilerGenerated: true),
                "__aenter__"));
            var aenterCall = SetScope(new CallExpression(aenterAttr, null, null));
            var awaitEnter = new AwaitExpression(aenterCall);

            // value = await mgr.__aenter__()
            var assignEnter = SetScope(new AssignmentStatement(
                [
                    Name(valueName, compilerGenerated: true)
                ],
                awaitEnter));

            Statement bodyStmt;
            if (Variable is not null) {
                // VAR = await value; BLOCK
                var assignVar = SetScope(new AssignmentStatement(
                    [Variable],
                    Name(valueName, compilerGenerated: true)));
                bodyStmt = SetScope(new SuiteStatement([assignVar, Body]));
            } else {
                bodyStmt = Body;
            }

            // except BaseException as exc:
            var exception = Name(exceptionName, compilerGenerated: true);
            var markExited = SetScope(new AssignmentStatement(
                [
                    Name(exitedName, compilerGenerated: true)
                ],
                SetScope(new ConstantExpression(true))));

            // await mgr.__aexit__(None, None, None)
            var aexitAttr = SetScope(new MemberExpression(
                Name(managerName, compilerGenerated: true),
                "__aexit__"));
            var none = SetScope(new ConstantExpression(null));
            var aexitCallNormal = SetScope(new CallExpression(aexitAttr, [none, none, none], null));
            var awaitExitNormal = new AwaitExpression(aexitCallNormal);

            // await mgr.__aexit__(type(exc), exc, exc.__traceback__)
            var exceptionType = SetScope(new CallExpression(
                Name("type"),
                [exception],
                null));
            var exceptionTraceback = SetScope(new MemberExpression(
                Name(exceptionName, compilerGenerated: true),
                "__traceback__"));
            var aexitCallExceptional = SetScope(new CallExpression(
                aexitAttr,
                [exceptionType, exception, exceptionTraceback],
                null));
            var awaitExitExceptional = new AwaitExpression(aexitCallExceptional);

            var rethrow = SetScope(new RaiseStatement(null, null));
            var ifNotSuppressed = SetScope(new IfStatement(
                [
                    new IfStatementTest(
                        SetScope(new UnaryExpression(
                            PythonOperationKind.IsFalse,
                            awaitExitExceptional)),
                        rethrow)
                ],
                null));
            var handlerBody = Suite(markExited, ifNotSuppressed);
            var handler = SetScope(new TryStatementHandler(
                Name("BaseException"),
                exception,
                handlerBody) {
                HeaderIndex = span.End
            });

            // finally:
            //     if not exited:
            //         await mgr.__aexit__(None, None, None)
            var ifNormalExit = SetScope(new IfStatement(
                [
                    new IfStatementTest(
                        SetScope(new UnaryExpression(
                            PythonOperationKind.IsFalse,
                            Name(exitedName, compilerGenerated: true))),
                        SetScope(new ExpressionStatement(awaitExitNormal)))
                ],
                null));
            var tryExceptFinally = SetScope(new TryStatement(
                bodyStmt,
                [handler],
                null,
                ifNormalExit) {
                HeaderIndex = span.End
            });

            // __aenter__ runs before the try block: if it fails, __aexit__ is not
            // called. The binding of `as VAR` remains inside the try so unpacking
            // failures are still passed to __aexit__.
            return Suite(assignMgr, assignEnter, assignExited, tryExceptFinally);
        }

        public override MSAst.Expression Reduce() {
            return _desugared!.Reduce();
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                // Build the desugared tree on first walk (when Parent and IndexSpan are set)
                if (_desugared == null && IsParentDefined) {
                    _desugared = BuildDesugared();
                }
                if (_desugared is not null) {
                    _desugared.Walk(walker);
                } else {
                    ContextManager.Walk(walker);
                    Variable?.Walk(walker);
                    Body.Walk(walker);
                }
            }
            walker.PostWalk(this);
        }
    }
}
