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

        public override object Handle_sum_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.Sub());
        }

        public override object Handle_term_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.Mult());
        }

        public override object Handle_term_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.Div());
        }

        public override object Handle_term_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.FloorDiv());
        }

        public override object Handle_term_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.Mod());
        }

        public override object Handle_term_4(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.MatMult());
        }

        public override object Handle_power_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.Pow());
        }

        public override object Handle_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.BitOr());
        }

        public override object Handle_bitwise_xor_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.BitXor());
        }

        public override object Handle_bitwise_and_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.BitAnd());
        }

        public override object Handle_shift_expr_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.LShift());
        }

        public override object Handle_shift_expr_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBinary(arguments, new AstNodes.RShift());
        }

        public override object Handle_factor_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildUnary(arguments, new AstNodes.UAdd());
        }

        public override object Handle_factor_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildUnary(arguments, new AstNodes.USub());
        }

        public override object Handle_factor_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildUnary(arguments, new AstNodes.Invert());
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

        public override object Handle_dotted_as_name_1(
            PegActionContext context,
            PegActionArguments arguments) {
            string name = ToText(Required(arguments, "a"));
            string? asName = arguments.TryGetValue("b", out object? rawAsName) && rawAsName is not null
                ? ToText(rawAsName)
                : null;
            AstSpan span = SpanOf(arguments["a"], arguments.TryGetValue("b", out object? value) ? value : null);
            return new AstNodes.Alias(
                name,
                asName,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_dotted_as_name_1_1_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_dotted_as_names_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).OfType<AstNodes.Alias>().ToArray();
        }

        public override object Handle_import_name_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.Alias[] aliases = Sequence(arguments["a"]).OfType<AstNodes.Alias>().ToArray();
            int? isLazy = arguments.TryGetValue("lazy", out object? lazy) && lazy is not null ? 1 : null;
            AstSpan span = SpanOf(aliases);
            return new AstNodes.Import(
                aliases,
                isLazy,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_import_from_as_name_1(
            PegActionContext context,
            PegActionArguments arguments) {
            string name = ToText(Required(arguments, "a"));
            string? asName = arguments.TryGetValue("b", out object? rawAsName) && rawAsName is not null
                ? ToText(rawAsName)
                : null;
            AstSpan span = SpanOf(arguments["a"], arguments.TryGetValue("b", out object? value) ? value : null);
            return new AstNodes.Alias(
                name,
                asName,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_import_from_as_name_1_1_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_import_from_as_names_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).OfType<AstNodes.Alias>().ToArray();
        }

        public override object Handle_import_from_1(
            PegActionContext context,
            PegActionArguments arguments) {
            int dotCount = Sequence(arguments["a"]).Count;
            string? module = arguments.TryGetValue("b", out object? moduleValue) && moduleValue is not null
                ? ToText(moduleValue)
                : null;
            AstNodes.Alias[] names = Sequence(arguments["c"]).OfType<AstNodes.Alias>().ToArray();
            int? isLazy = arguments.TryGetValue("lazy", out object? lazy) && lazy is not null ? 1 : null;
            AstSpan span = SpanOf(names);
            return new AstNodes.ImportFrom(
                module,
                names,
                dotCount,
                isLazy,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
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

        public override object Handle_simple_stmt_3(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase expression = ToExpression(Required(arguments, "e"));
            AstSpan span = SpanOf(expression);
            return new AstNodes.Expr(
                expression,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_pass_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Pass(span.Line, span.Column, span.EndLine, span.EndColumn);
        }

        public override object Handle_return_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase? value = arguments.TryGetValue("a", out object? rawValue) && rawValue is not null
                ? ToExpression(rawValue)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Return(
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_annotation_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "e");
        }

        public override object Handle_type_param_0(
            PegActionContext context,
            PegActionArguments arguments) {
            string name = ToText(Required(arguments, "a"));
            AstNodes.ExprBase? bound = arguments.TryGetValue("b", out object? rawBound) && rawBound is not null
                ? ToExpression(rawBound)
                : null;
            AstNodes.ExprBase? defaultValue = arguments.TryGetValue("c", out object? rawDefault) && rawDefault is not null
                ? ToExpression(rawDefault)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.TypeVar(
                name,
                bound,
                defaultValue,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_type_param_2(
            PegActionContext context,
            PegActionArguments arguments) {
            string name = ToText(Required(arguments, "a"));
            AstNodes.ExprBase? defaultValue = arguments.TryGetValue("b", out object? rawDefault) && rawDefault is not null
                ? ToExpression(rawDefault)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.TypeVarTuple(
                name,
                defaultValue,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_type_param_3(
            PegActionContext context,
            PegActionArguments arguments) {
            string name = ToText(Required(arguments, "a"));
            AstNodes.ExprBase? defaultValue = arguments.TryGetValue("b", out object? rawDefault) && rawDefault is not null
                ? ToExpression(rawDefault)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.ParamSpec(
                name,
                defaultValue,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_type_param_seq_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).OfType<AstNodes.TypeParamBase>().ToArray();
        }

        public override object Handle_type_params_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["t"]).OfType<AstNodes.TypeParamBase>().ToArray();
        }

        public override object Handle_type_alias_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase name = SetContext(Required(arguments, "n"), new AstNodes.Store());
            AstNodes.TypeParamBase[] typeParams = arguments.TryGetValue("t", out object? rawParams)
                && rawParams is not null
                ? Sequence(rawParams).OfType<AstNodes.TypeParamBase>().ToArray()
                : Array.Empty<AstNodes.TypeParamBase>();
            AstNodes.ExprBase value = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(name, value);
            return new AstNodes.TypeAlias(
                name,
                typeParams,
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tstring_middle_1(
            PegActionContext context,
            PegActionArguments arguments) {
            object? value;
            if (arguments.TryGetValue("t", out object? namedValue) && namedValue is not null) {
                value = namedValue;
            } else if (arguments.Count > 0 && arguments[0] is not null) {
                value = arguments[0];
            } else {
                throw new InvalidOperationException("TSTRING_MIDDLE did not provide a token value.");
            }
            string text = value is PegToken token ? token.Value : ToText(value);
            AstSpan span = SpanOf(value);
            return new AstNodes.Constant(
                text.Replace("{{", "{", StringComparison.Ordinal)
                    .Replace("}}", "}", StringComparison.Ordinal),
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tstring_replacement_field_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            int conversion = -1;
            AstNodes.ExprBase? formatSpec = null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Interpolation(
                value,
                string.Empty,
                conversion,
                formatSpec,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tstring_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] values = Sequence(arguments["b"])
                .Select(ToExpression)
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.TemplateStr(
                values,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_strings_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] strings = Sequence(arguments["a"])
                .Select(ToExpression)
                .ToArray();
            return strings.Length == 1 ? strings[0] : strings;
        }

        public override object Handle_block_0(
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

        public override object Handle_literal_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "value"));
            AstSpan span = SpanOf(value);
            return new AstNodes.MatchValue(
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_or_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.Pattern[] patterns = Sequence(arguments["patterns"])
                .OfType<AstNodes.Pattern>()
                .ToArray();
            if (patterns.Length == 1) return patterns[0];
            AstSpan span = SpanOf(patterns);
            return new AstNodes.MatchOr(
                patterns,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_case_block_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.Pattern pattern = Required(arguments, "pattern") as AstNodes.Pattern
                ?? throw new InvalidOperationException("Match case pattern was not a pattern node.");
            AstNodes.ExprBase? guard = arguments.TryGetValue("guard", out object? rawGuard) && rawGuard is not null
                ? ToExpression(rawGuard)
                : null;
            AstNodes.Stmt[] body = Sequence(arguments["body"])
                .OfType<AstNodes.Stmt>()
                .ToArray();
            return new AstNodes.MatchCase(pattern, guard, body);
        }

        public override object Handle_match_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase subject = ToExpression(Required(arguments, "subject"));
            AstNodes.MatchCase[] cases = Sequence(arguments["cases"])
                .OfType<AstNodes.MatchCase>()
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Match(
                subject,
                cases,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
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

        private static string ToText(object? value) {
            return value switch {
                null => throw new InvalidOperationException("Expected a non-null name."),
                string text => text,
                PegToken token => token.Value,
                AstNodes.Name name => name.Id,
                PegActionArguments arguments => arguments
                    .Where(item => item is not null)
                    .Select(ToText)
                    .First(),
                _ => throw new InvalidOperationException(
                    $"Value {value.GetType().FullName} cannot be converted to an identifier.")
            };
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

        private static AstNodes.BinOp BuildBinary(
            PegActionArguments arguments,
            AstNodes.OperatorKind @operator) {
            AstNodes.ExprBase left = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase right = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(left, right);
            return new AstNodes.BinOp(
                left,
                @operator,
                right,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.UnaryOp BuildUnary(
            PegActionArguments arguments,
            AstNodes.UnaryOpKind @operator) {
            AstNodes.ExprBase operand = ToExpression(Required(arguments, "a"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.UnaryOp(
                @operator,
                operand,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
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
