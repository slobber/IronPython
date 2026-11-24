#nullable enable
using System;
using System.Linq;
using IronPython.Runtime;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using CompilerAst = IronPython.Compiler.Ast;
using AstNodes = IronPython.Compiler.Syntax.Ast;

namespace IronPython.Compiler.PublicAst {
    public static class PublicAstLowerer {
        public static CompilerAst.PythonAst LowerModule(
            AstNodes.Module module,
            CompilerContext context) {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (context == null) throw new ArgumentNullException(nameof(context));

            CompilerAst.Statement[] statements = module.Body
                .Select(LowerStatement)
                .ToArray();
            return new CompilerAst.PythonAst(
                new CompilerAst.SuiteStatement(statements),
                true,
                ModuleOptions.ExecOrEvalCode,
                false,
                context,
                Array.Empty<int>());
        }

        public static CompilerAst.Statement LowerStatement(AstNodes.Stmt statement) {
            return statement switch {
                AstNodes.Assign assign => new CompilerAst.AssignmentStatement(
                    assign.Targets.Select(LowerExpression).ToArray(),
                    LowerExpression(assign.Value)),
                _ => throw Unsupported(statement)
            };
        }

        public static CompilerAst.Expression LowerExpression(AstNodes.ExprBase expression) {
            return expression switch {
                AstNodes.Name name => new CompilerAst.NameExpression(name.Id),
                AstNodes.Constant constant => new CompilerAst.ConstantExpression(constant.Value),
                AstNodes.BinOp binOp => new CompilerAst.BinaryExpression(
                    LowerOperator(binOp.Op),
                    LowerExpression(binOp.Left),
                    LowerExpression(binOp.Right)),
                _ => throw Unsupported(expression)
            };
        }

        public static object? ExecuteModule(
            AstNodes.Module module,
            PythonContext context,
            out PythonDictionary globals) {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (context == null) throw new ArgumentNullException(nameof(context));

            SourceUnit sourceUnit = context.CreateSnippet(
                string.Empty,
                "<public-ast>",
                SourceCodeKind.File);
            var options = new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode);
            var compilerContext = new CompilerContext(sourceUnit, options, ErrorSink.Default);
            CompilerAst.PythonAst ast = LowerModule(module, compilerContext);
            ast.Bind();

            globals = new PythonDictionary();
            var moduleContext = new ModuleContext(globals, context);
            return ast.ToScriptCode().Run(moduleContext.GlobalScope);
        }

        private static PythonOperator LowerOperator(AstNodes.OperatorKind @operator) {
            return @operator switch {
                AstNodes.Add => PythonOperator.Add,
                AstNodes.Sub => PythonOperator.Subtract,
                AstNodes.Mult => PythonOperator.Multiply,
                AstNodes.MatMult => PythonOperator.MatMult,
                AstNodes.Div => PythonOperator.TrueDivide,
                AstNodes.FloorDiv => PythonOperator.FloorDivide,
                AstNodes.Mod => PythonOperator.Mod,
                AstNodes.Pow => PythonOperator.Power,
                AstNodes.LShift => PythonOperator.LeftShift,
                AstNodes.RShift => PythonOperator.RightShift,
                AstNodes.BitOr => PythonOperator.BitwiseOr,
                AstNodes.BitXor => PythonOperator.Xor,
                AstNodes.BitAnd => PythonOperator.BitwiseAnd,
                _ => throw Unsupported(@operator)
            };
        }

        private static NotSupportedException Unsupported(AstNodes.PythonAstNode node) {
            return new NotSupportedException(
                $"Public AST node '{node.NodeKind}' has no compiler-AST lowering yet.");
        }
    }
}
