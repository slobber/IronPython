// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using MSAst = System.Linq.Expressions;

using System;
using System.Diagnostics;
using Microsoft.Scripting;
using AstUtils = Microsoft.Scripting.Ast.Utils;

namespace IronPython.Compiler.Ast {
    using Ast = MSAst.Expression;

    public class YieldFromExpression : Expression {
        private readonly Statement statement;
        private readonly NameExpression result;

        public YieldFromExpression(Expression expression) {
            statement = GenYieldFromStatement(expression);
            result = new NameExpression("_yieldfromprefix_r");

            Expression = expression;
        }

        public Expression Expression { get; }

        private static Modules._ast.AST? _yieldFromAst;
        private static readonly object YieldFromAstLock = new object();

        private static Modules._ast.AST YieldFromAst {
            get {
                if (_yieldFromAst is not null) return _yieldFromAst;
                lock (YieldFromAstLock) {
                    if (_yieldFromAst is null) {
                        bool skipBuildAstBinding = Modules._ast._skipBuildAstBinding;
                        Modules._ast._skipBuildAstBinding = true;
                        try {
                            _yieldFromAst = (Modules._ast.AST)Modules.Builtin.compile(Runtime.DefaultContext.Default, @"
from builtins import iter as _yieldfromprefix_iter, next as _yieldfromprefix_next
_yieldfromprefix_i = _yieldfromprefix_iter(_yieldfromprefix_EXPR)
try:
    _yieldfromprefix_y = _yieldfromprefix_next(_yieldfromprefix_i)
except StopIteration as _yieldfromprefix_e:
    _yieldfromprefix_r = _yieldfromprefix_e.value
else:
    while 1:
        try:
            _yieldfromprefix_s = yield _yieldfromprefix_y
        except GeneratorExit as _yieldfromprefix_e:
            try:
                _yieldfromprefix_m = _yieldfromprefix_i.close
            except AttributeError:
                pass
            else:
                _yieldfromprefix_m()
            raise _yieldfromprefix_e
        except BaseException as _yieldfromprefix_e:
            from sys import exc_info as _yieldfromprefix_exc_info
            _yieldfromprefix_x = _yieldfromprefix_exc_info()
            try:
                _yieldfromprefix_m = _yieldfromprefix_i.throw
            except AttributeError:
                raise _yieldfromprefix_e
            else:
                try:
                    _yieldfromprefix_y = _yieldfromprefix_m(*_yieldfromprefix_x)
                except StopIteration as _yieldfromprefix_e:
                    _yieldfromprefix_r = _yieldfromprefix_e.value
                    break
        else:
            try:
                if _yieldfromprefix_s is None:
                    _yieldfromprefix_y = _yieldfromprefix_next(_yieldfromprefix_i)
                else:
                    _yieldfromprefix_y = _yieldfromprefix_i.send(_yieldfromprefix_s)
            except StopIteration as _yieldfromprefix_e:
                _yieldfromprefix_r = _yieldfromprefix_e.value
                break
", "", "exec", flags: Modules._ast.PyCF_ONLY_AST);
                        } finally {
                            Modules._ast._skipBuildAstBinding = skipBuildAstBinding;
                        }
                    }
                }
                return _yieldFromAst!;
            }
        }

        private static Statement GenYieldFromStatement(Expression expression) {
            var expr = Modules._ast.ConvertToPythonAst(Runtime.DefaultContext.Default, YieldFromAst, "").Body;
            Modules._ast._containsYield = false; // reset state of _ast module

            return new SuiteStatement(new[] {
                new AssignmentStatement(new[] { new NameExpression("_yieldfromprefix_EXPR") }, expression),
                    expr
                });
        }

        public override MSAst.Expression Reduce() {
            return Ast.Block(
                typeof(object),
                statement,
                AstUtils.Convert(result, typeof(object))
            ).Reduce();
        }

        public override void Walk(PythonWalker walker) {
            if (walker.Walk(this)) {
                // Expression is already walked as part of statement so skip it
                statement.Walk(walker);
                result.Walk(walker);
            }
            walker.PostWalk(this);
        }

        public override string NodeName {
            get {
                return "yield expression";
            }
        }
    }
}
