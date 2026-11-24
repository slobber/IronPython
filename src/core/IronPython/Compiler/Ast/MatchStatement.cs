// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Scripting.Actions;
using Microsoft.Scripting.Interpreter;
using Microsoft.Scripting.Utils;

using MSAst = System.Linq.Expressions;

namespace IronPython.Compiler.Ast {
    using Ast = MSAst.Expression;
    using AstUtils = Microsoft.Scripting.Ast.Utils;

    /// <summary>
    /// Pattern-matching cases lower to a condition chain. Unlike a regular
    /// if-statement, match cases must not create a trailing debug sequence point
    /// (CPython only reports the match and matched case/body lines).
    /// </summary>
    internal sealed class MatchStatement : IfStatement, IInstructionProvider {
        private readonly bool _hasElse;

        public MatchStatement(
            IfStatementTest[] tests,
            Statement elseStatement = null)
            : base(tests, elseStatement ?? new EmptyStatement()) {
            _hasElse = elseStatement is not null;
        }

        public override MSAst.Expression Reduce() {
            MSAst.Expression result = _hasElse
                ? ElseStatement.Reduce()
                : AstUtils.Empty();
            for (int i = Tests.Count - 1; i >= 0; i--) {
                IfStatementTest test = Tests[i];
                MSAst.Expression condition = Ast.Condition(
                    GlobalParent.Convert(
                        typeof(bool),
                        ConversionResultKind.ExplicitCast,
                        test.Test),
                    test.Body.Reduce(),
                    result);
                result = Ast.Block(
                    Ast.DebugInfo(
                        GlobalParent.DebugDocument,
                        test.Span.Start.Line,
                        test.Span.Start.Column,
                        test.Span.End.Line,
                        test.Span.End.Column),
                    condition);
            }
            return result;
        }

        void IInstructionProvider.AddInstructions(LightCompiler compiler) {
            compiler.Compile(Reduce());
        }
    }
}
