#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IronPython.Compiler.Generated;
using IronPython.Compiler.Syntax;
using AstNodes = IronPython.Compiler.Syntax.Ast;

namespace IronPython.Compiler.PublicAst {
    public sealed class PythonAstBuilder : PythonGrammarActionHandler {
        public override object Handle_star_atom_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return SetContext(Required(arguments, "a"), new AstNodes.Store());
        }

        public override object Handle_star_targets_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_assignment_2_0_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_sum_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase left = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase right = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(left, right);
            return new AstNodes.BinOp(left, new AstNodes.Add(), right, span.Line, span.Column,
                span.EndLine, span.EndColumn);
        }

        public override object Handle_assignment_2(
            PegActionContext context,
            PegActionArguments arguments) {
            IReadOnlyList<object?> rawTargets = Sequence(arguments["a"]);
            var targets = rawTargets.Select(ToExpression).ToArray();
            AstNodes.ExprBase value = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(targets[0], value);
            return new AstNodes.Assign(targets, value, null, span.Line, span.Column,
                span.EndLine, span.EndColumn);
        }

        public override object Handle_simple_stmts_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new[] { Required(arguments, "a") };
        }

        public override object Handle_statement_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_statements_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var statements = new List<AstNodes.Stmt>();
            foreach (object? item in Sequence(arguments["a"])) {
                foreach (object? statement in Sequence(item)) {
                    if (statement is AstNodes.Stmt stmt) statements.Add(stmt);
                }
            }
            return statements;
        }

        public override object Handle_file_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var body = new List<AstNodes.Stmt>();
            if (arguments.TryGetValue("a", out object? rawBody) && rawBody is not null) {
                foreach (object? item in Sequence(rawBody)) {
                    if (item is AstNodes.Stmt stmt) body.Add(stmt);
                }
            }
            return new AstNodes.Module(body, Array.Empty<AstNodes.TypeIgnoreBase>());
        }

        private static object Required(PegActionArguments arguments, string name) {
            return arguments[name] ?? throw new InvalidOperationException($"Missing PEG action field '{name}'.");
        }

        private static IReadOnlyList<object?> Sequence(object? value) {
            if (value is null) return Array.Empty<object?>();
            if (value is IReadOnlyList<object?> list) return list;
            if (value is System.Collections.IEnumerable enumerable) {
                return enumerable.Cast<object?>().ToArray();
            }
            return new[] { value };
        }

        private static AstNodes.ExprBase ToExpression(object? value) {
            if (value is AstNodes.ExprBase expression) return expression;
            if (value is PegToken token) {
                AstSpan span = SpanOf(token);
                switch (token.Kind) {
                    case "NAME":
                        return new AstNodes.Name(
                            token.Value,
                            new AstNodes.Load(),
                            span.Line,
                            span.Column,
                            span.EndLine,
                            span.EndColumn);
                    case "NUMBER":
                        return new AstNodes.Constant(
                            ParseNumber(token.Value),
                            null,
                            span.Line,
                            span.Column,
                            span.EndLine,
                            span.EndColumn);
                    default:
                        throw new NotSupportedException($"Unsupported token expression kind '{token.Kind}'.");
                }
            }
            if (value is PegActionArguments arguments) {
                for (int i = arguments.Count - 1; i >= 0; i--) {
                    if (arguments[i] is not null) {
                        try {
                            return ToExpression(arguments[i]);
                        } catch (NotSupportedException) {
                        }
                    }
                }
            }
            throw new InvalidOperationException(
                $"PEG action value {value?.GetType().FullName ?? "<null>"} cannot be lowered to an expression.");
        }

        private static AstNodes.ExprBase SetContext(object? value, AstNodes.ExprContext context) {
            AstSpan span = SpanOf(value);
            return value switch {
                PegToken token when token.Kind == "NAME" => new AstNodes.Name(
                    token.Value,
                    context,
                    span.Line,
                    span.Column,
                    span.EndLine,
                    span.EndColumn),
                AstNodes.Name name => new AstNodes.Name(
                    name.Id,
                    context,
                    name.LineNumber ?? span.Line,
                    name.ColumnOffset ?? span.Column,
                    name.EndLineNumber ?? span.EndLine,
                    name.EndColumnOffset ?? span.EndColumn),
                _ => throw new InvalidOperationException("Only name targets are supported in the initial lowering slice.")
            };
        }

        private static object ParseNumber(string text) {
            string normalized = text.Replace("_", string.Empty);
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                return long.Parse(normalized.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            if (normalized.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) {
                return Convert.ToInt64(normalized.Substring(2), 8);
            }
            if (normalized.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
                return Convert.ToInt64(normalized.Substring(2), 2);
            }
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) {
                return integer;
            }
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)) {
                return real;
            }
            throw new FormatException($"Invalid numeric literal '{text}'.");
        }

        private static AstSpan SpanOf(params object?[] values) {
            AstSpan? first = null;
            AstSpan? last = null;
            foreach (object? value in values) {
                AstSpan? span = TryGetSpan(value);
                if (span is null) continue;
                first ??= span;
                last = span;
            }
            return first.HasValue && last.HasValue
                ? new AstSpan(
                    first.Value.Line,
                    first.Value.Column,
                    last.Value.EndLine,
                    last.Value.EndColumn)
                : new AstSpan(1, 0, 1, 0);
        }

        private static AstSpan? TryGetSpan(object? value) {
            if (value is AstNodes.PythonAstNode node) {
                return new AstSpan(
                    node.LineNumber ?? 1,
                    node.ColumnOffset ?? 0,
                    node.EndLineNumber ?? node.LineNumber ?? 1,
                    node.EndColumnOffset ?? node.ColumnOffset ?? 0);
            }
            if (value is PegToken token) {
                return new AstSpan(
                    token.StartLine,
                    token.StartByteColumn,
                    token.EndLine,
                    token.EndByteColumn);
            }
            if (value is IReadOnlyList<object?> sequence && sequence.Count > 0) {
                return SpanOf(sequence[0], sequence[sequence.Count - 1]);
            }
            return null;
        }

        private readonly struct AstSpan {
            public AstSpan(int line, int column, int endLine, int endColumn) {
                Line = line;
                Column = column;
                EndLine = endLine;
                EndColumn = endColumn;
            }

            public int Line { get; }
            public int Column { get; }
            public int EndLine { get; }
            public int EndColumn { get; }
        }
    }
}
