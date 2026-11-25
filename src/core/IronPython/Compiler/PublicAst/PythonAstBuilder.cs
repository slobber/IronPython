#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using IronPython.Compiler.Generated;
using IronPython.Compiler.Syntax;
using IronPython.Runtime.Operations;
using AstNodes = IronPython.Compiler.Syntax.Ast;

namespace IronPython.Compiler.PublicAst {
    public sealed class PythonAstBuilder : PythonGrammarActionHandler {
        public override object Handle_star_atom_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return SetContext(Required(arguments, "a"), new AstNodes.Store());
        }

        public override object Handle_target_with_star_atom_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Attribute(
                value,
                ToText(Required(arguments, "b")),
                new AstNodes.Store(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_target_with_star_atom_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase slice = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Subscript(
                value,
                slice,
                new AstNodes.Store(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_single_target_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return SetContext(Required(arguments, "a"), new AstNodes.Store());
        }

        public override object Handle_single_target_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_single_subscript_attribute_target_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_target_with_star_atom_0(context, arguments);
        }

        public override object Handle_single_subscript_attribute_target_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_target_with_star_atom_1(context, arguments);
        }

        public override object Handle_star_targets_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_star_targets_1(
            PegActionContext context,
            PegActionArguments arguments) {
            var targets = new List<AstNodes.ExprBase> {
                ToExpression(Required(arguments, "a"))
            };
            targets.AddRange(Sequence(arguments["b"]).Select(ToExpression));
            AstSpan span = SpanOf(targets.ToArray());
            return new AstNodes.Tuple(
                targets.ToArray(),
                new AstNodes.Store(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_star_targets_1_1_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "c");
        }

        public override object Handle_star_targets_list_seq_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).Select(ToExpression).ToArray();
        }

        public override object Handle_star_targets_tuple_seq_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var targets = new List<AstNodes.ExprBase> {
                ToExpression(Required(arguments, "a"))
            };
            targets.AddRange(Sequence(arguments["b"]).Select(ToExpression));
            return targets.ToArray();
        }

        public override object Handle_star_targets_tuple_seq_0_1_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "c");
        }

        public override object Handle_star_targets_tuple_seq_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return new[] { ToExpression(Required(arguments, "a")) };
        }

        public override object Handle_star_target_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = SetContext(Required(arguments, "a"), new AstNodes.Store());
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Starred(
                value,
                new AstNodes.Store(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_star_atom_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return SetContext(Required(arguments, "a"), new AstNodes.Store());
        }

        public override object Handle_star_atom_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] targets = arguments.TryGetValue("a", out object? rawTargets)
                && rawTargets is not null
                ? Sequence(rawTargets).Select(ToExpression).ToArray()
                : Array.Empty<AstNodes.ExprBase>();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Tuple(
                targets,
                new AstNodes.Store(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_star_atom_3(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] targets = arguments.TryGetValue("a", out object? rawTargets)
                && rawTargets is not null
                ? Sequence(rawTargets).Select(ToExpression).ToArray()
                : Array.Empty<AstNodes.ExprBase>();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.List(
                targets,
                new AstNodes.Store(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
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

        public override object Handle_disjunction_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBoolean(arguments, new AstNodes.Or());
        }

        public override object Handle_disjunction_0_1_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "c");
        }

        public override object Handle_conjunction_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBoolean(arguments, new AstNodes.And());
        }

        public override object Handle_conjunction_0_1_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "c");
        }

        public override object Handle_inversion_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildUnary(arguments, new AstNodes.Not());
        }

        public override object Handle_comparison_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase left = ToExpression(Required(arguments, "a"));
            CompareEntry[] comparisons = Sequence(arguments["b"])
                .OfType<CompareEntry>()
                .ToArray();
            AstSpan span = SpanOf(left, comparisons);
            return new AstNodes.Compare(
                left,
                comparisons.Select(item => item.Operator).ToArray(),
                comparisons.Select(item => item.Value).ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_eq_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.Eq());
        }

        public override object Handle_noteq_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.NotEq());
        }

        public override object Handle_lte_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.LtE());
        }

        public override object Handle_lt_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.Lt());
        }

        public override object Handle_gte_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.GtE());
        }

        public override object Handle_gt_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.Gt());
        }

        public override object Handle_notin_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.NotIn());
        }

        public override object Handle_in_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.In());
        }

        public override object Handle_isnot_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.IsNot());
        }

        public override object Handle_is_bitwise_or_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildCompareEntry(arguments, new AstNodes.Is());
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

        public override object Handle_assignment_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase target = SetContext(Required(arguments, "a"), new AstNodes.Store());
            AstNodes.ExprBase annotation = ToExpression(Required(arguments, "b"));
            AstNodes.ExprBase? value = GetOptionalExpression(arguments, "c", "d");
            AstSpan span = SpanOf(arguments);
            return new AstNodes.AnnAssign(
                target,
                annotation,
                value,
                1,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_assignment_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase target = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase annotation = ToExpression(Required(arguments, "b"));
            AstNodes.ExprBase? value = GetOptionalExpression(arguments, "c", "d");
            AstSpan span = SpanOf(arguments);
            return new AstNodes.AnnAssign(
                target,
                annotation,
                value,
                0,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_assignment_3(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase target = ToExpression(Required(arguments, "a"));
            AstNodes.OperatorKind @operator = Required(arguments, "b") as AstNodes.OperatorKind
                ?? throw new InvalidOperationException("Augmented assignment operator had an unexpected representation.");
            AstNodes.ExprBase value = ToExpression(Required(arguments, "c"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.AugAssign(
                target,
                @operator,
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_assignment_expression_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase target = SetContext(Required(arguments, "a"), new AstNodes.Store());
            AstNodes.ExprBase value = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.NamedExpr(
                target,
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_augassign_0(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.Add();

        public override object Handle_augassign_1(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.Sub();

        public override object Handle_augassign_2(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.Mult();

        public override object Handle_augassign_3(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.MatMult();

        public override object Handle_augassign_4(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.Div();

        public override object Handle_augassign_5(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.Mod();

        public override object Handle_augassign_6(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.BitAnd();

        public override object Handle_augassign_7(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.BitOr();

        public override object Handle_augassign_8(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.BitXor();

        public override object Handle_augassign_9(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.LShift();

        public override object Handle_augassign_10(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.RShift();

        public override object Handle_augassign_11(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.Pow();

        public override object Handle_augassign_12(
            PegActionContext context,
            PegActionArguments arguments) => new AstNodes.FloorDiv();

        public override object Handle_star_expressions_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var expressions = new List<AstNodes.ExprBase> {
                ToExpression(Required(arguments, "a"))
            };
            expressions.AddRange(Sequence(arguments["b"]).Select(ToExpression));
            AstSpan span = SpanOf(expressions.ToArray());
            return new AstNodes.Tuple(
                expressions.ToArray(),
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_star_expressions_0_1_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "c");
        }

        public override object Handle_star_expressions_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            AstSpan span = SpanOf(value);
            return new AstNodes.Tuple(
                new[] { value },
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_star_expression_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildStarred(arguments, "a");
        }

        public override object Handle_star_named_expressions_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).Select(ToExpression).ToArray();
        }

        public override object Handle_star_named_expressions_sequence_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).Select(ToExpression).ToArray();
        }

        public override object Handle_star_named_expression_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildStarred(arguments, "a");
        }

        public override object Handle_list_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] elements = arguments.TryGetValue("a", out object? rawElements)
                && rawElements is not null
                ? Sequence(rawElements).Select(ToExpression).ToArray()
                : Array.Empty<AstNodes.ExprBase>();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.List(
                elements,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tuple_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] elements = arguments.TryGetValue("a", out object? rawElements)
                && rawElements is not null
                ? Sequence(rawElements).Select(ToExpression).ToArray()
                : Array.Empty<AstNodes.ExprBase>();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Tuple(
                elements,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tuple_0_1_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var elements = new List<object?> { Required(arguments, "y") };
            elements.AddRange(Sequence(arguments["z"]));
            return elements.ToArray();
        }

        public override object Handle_set_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] elements = Sequence(arguments["a"])
                .Select(ToExpression)
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Set(
                elements,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_dict_0(
            PegActionContext context,
            PegActionArguments arguments) {
            KeyValueEntry[] entries = arguments.TryGetValue("a", out object? rawEntries)
                && rawEntries is not null
                ? Sequence(rawEntries).OfType<KeyValueEntry>().ToArray()
                : Array.Empty<KeyValueEntry>();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Dict(
                entries.Select(entry => entry.Key).ToArray(),
                entries.Select(entry => ToExpression(entry.Value)).ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_double_starred_kvpairs_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).OfType<KeyValueEntry>().ToArray();
        }

        public override object Handle_double_starred_kvpair_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new KeyValueEntry(null, Required(arguments, "a"));
        }

        public override object Handle_kvpair_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new KeyValueEntry(
                ToExpression(Required(arguments, "a")),
                Required(arguments, "b"));
        }

        public override object Handle_for_if_clauses_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"])
                .OfType<AstNodes.Comprehension>()
                .ToArray();
        }

        public override object Handle_for_if_clause_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildComprehension(arguments, 1);
        }

        public override object Handle_for_if_clause_0_6_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_for_if_clause_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildComprehension(arguments, 0);
        }

        public override object Handle_for_if_clause_1_5_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_listcomp_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase element = ToExpression(Required(arguments, "a"));
            AstNodes.Comprehension[] generators = Sequence(arguments["b"])
                .OfType<AstNodes.Comprehension>()
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.ListComp(
                element,
                generators,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_setcomp_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase element = ToExpression(Required(arguments, "a"));
            AstNodes.Comprehension[] generators = Sequence(arguments["b"])
                .OfType<AstNodes.Comprehension>()
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.SetComp(
                element,
                generators,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_genexp_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase element = ToExpression(Required(arguments, "a"));
            AstNodes.Comprehension[] generators = Sequence(arguments["b"])
                .OfType<AstNodes.Comprehension>()
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.GeneratorExp(
                element,
                generators,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_if_expression_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase body = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase test = ToExpression(Required(arguments, "b"));
            AstNodes.ExprBase orelse = ToExpression(Required(arguments, "c"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.IfExp(
                test,
                body,
                orelse,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_expressions_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var values = new List<AstNodes.ExprBase> {
                ToExpression(Required(arguments, "a"))
            };
            values.AddRange(Sequence(arguments["b"]).Select(ToExpression));
            AstSpan span = SpanOf(values.ToArray());
            return new AstNodes.Tuple(
                values.ToArray(),
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_expressions_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_subject_expr_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var values = new List<AstNodes.ExprBase> {
                ToExpression(Required(arguments, "value"))
            };
            if (arguments.TryGetValue("values", out object? rawValues) && rawValues is not null) {
                values.AddRange(Sequence(rawValues).Select(ToExpression));
            }
            AstSpan span = SpanOf(values.ToArray());
            return new AstNodes.Tuple(
                values.ToArray(),
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_expressions_0_1_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "c");
        }

        public override object Handle_expression_without_invalid_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_dictcomp_0(
            PegActionContext context,
            PegActionArguments arguments) {
            KeyValueEntry pair = Required(arguments, "a") as KeyValueEntry
                ?? throw new InvalidOperationException("Dictionary comprehension pair had an unexpected representation.");
            AstNodes.Comprehension[] generators = Sequence(arguments["b"])
                .OfType<AstNodes.Comprehension>()
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.DictComp(
                pair.Key ?? throw new InvalidOperationException(
                    "Dictionary comprehension unpacking requires a key expression."),
                ToExpression(pair.Value),
                generators,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_dictcomp_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase unpacked = ToExpression(Required(arguments, "a"));
            AstNodes.Comprehension[] generators = Sequence(arguments["b"])
                .OfType<AstNodes.Comprehension>()
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.DictComp(
                unpacked,
                null,
                generators,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_primary_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            string attribute = ToText(Required(arguments, "b"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Attribute(
                value,
                attribute,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_primary_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase function = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase generator = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Call(
                function,
                new[] { generator },
                Array.Empty<AstNodes.Keyword>(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_t_primary_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_primary_0(context, arguments);
        }

        public override object Handle_t_primary_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_primary_3(context, arguments);
        }

        public override object Handle_t_primary_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_primary_1(context, arguments);
        }

        public override object Handle_t_primary_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_primary_2(context, arguments);
        }

        public override object Handle_t_primary_4(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_await_primary_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Await(
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_atom_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildConstant(arguments, true);
        }

        public override object Handle_atom_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildConstant(arguments, false);
        }

        public override object Handle_atom_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildConstant(arguments, null);
        }

        public override object Handle_atom_9(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildConstant(arguments, IronPython.Runtime.Operations.PythonOps.Ellipsis);
        }

        public override object Handle_literal_expr_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildConstant(arguments, null);
        }

        public override object Handle_literal_expr_4(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildConstant(arguments, true);
        }

        public override object Handle_literal_expr_5(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildConstant(arguments, false);
        }

        public override object Handle_literal_pattern_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchValue(Required(arguments, "value"));
        }

        public override object Handle_literal_pattern_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchValue(Required(arguments, "value"));
        }

        public override object Handle_real_number_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNumberConstant(Required(arguments, "real"));
        }

        public override object Handle_imaginary_number_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNumberConstant(Required(arguments, "imag"));
        }

        public override object Handle_signed_number_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSignedNumber(arguments, context, "number");
        }

        public override object Handle_signed_number_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSignedNumber(arguments, context, "number");
        }

        public override object Handle_signed_real_number_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSignedNumber(arguments, context, "real");
        }

        public override object Handle_signed_real_number_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSignedNumber(arguments, context, "real");
        }

        public override object Handle_complex_number_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildComplexNumber(arguments, new AstNodes.Add());
        }

        public override object Handle_complex_number_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildComplexNumber(arguments, new AstNodes.Sub());
        }

        public override object Handle_group_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_lambdef_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.Arguments parameters = arguments.TryGetValue("a", out object? rawParameters)
                && rawParameters is AstNodes.Arguments suppliedParameters
                ? suppliedParameters
                : EmptyArguments();
            AstNodes.ExprBase body = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Lambda(
                parameters,
                body,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_primary_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase function = ToExpression(Required(arguments, "a"));
            CallArguments callArguments = arguments.TryGetValue("b", out object? rawArguments)
                && rawArguments is not null
                ? rawArguments as CallArguments
                    ?? throw new InvalidOperationException("Call arguments had an unexpected representation.")
                : CallArguments.Empty;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Call(
                function,
                callArguments.Positional,
                callArguments.Keywords,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_primary_3(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            if (!arguments.TryGetValue("b", out object? rawSlice) || rawSlice is null) {
                return value;
            }
            if (rawSlice is CallArguments callArguments) {
                AstSpan callSpan = SpanOf(arguments);
                return new AstNodes.Call(
                    value,
                    callArguments.Positional,
                    callArguments.Keywords,
                    callSpan.Line,
                    callSpan.Column,
                    callSpan.EndLine,
                    callSpan.EndColumn);
            }
            AstNodes.ExprBase slice = ToExpression(rawSlice);
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Subscript(
                value,
                slice,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_slices_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_slices_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] elements = Sequence(arguments["a"])
                .Select(ToExpression)
                .ToArray();
            AstSpan span = SpanOf(elements);
            return new AstNodes.Tuple(
                elements,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_slice_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase? lower = arguments.TryGetValue("a", out object? rawLower)
                && rawLower is not null
                ? ToExpression(rawLower)
                : null;
            AstNodes.ExprBase? upper = arguments.TryGetValue("b", out object? rawUpper)
                && rawUpper is not null
                ? ToExpression(rawUpper)
                : null;
            AstNodes.ExprBase? step = arguments.TryGetValue("c", out object? rawStep)
                && rawStep is not null
                ? ToExpression(rawStep)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Slice(
                lower,
                upper,
                step,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_slice_0_3_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return arguments.TryGetValue("d", out object? value) ? value! : null!;
        }

        public override object Handle_slice_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_arguments_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_args_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var positional = new List<AstNodes.ExprBase>();
            var keywords = new List<AstNodes.Keyword>();
            foreach (object? item in Sequence(arguments["a"])) {
                if (item is AstNodes.Keyword keyword) keywords.Add(keyword);
                else positional.Add(ToExpression(item));
            }
            foreach (object? item in Sequence(
                arguments.TryGetValue("b", out object? rawKeywords) ? rawKeywords : null)) {
                if (item is AstNodes.Keyword keyword) keywords.Add(keyword);
                else if (item is AstNodes.Starred starred) positional.Add(starred);
                else if (item is not null) {
                    throw new InvalidOperationException(
                        $"Call keyword segment {Describe(item)} had an unexpected representation.");
                }
            }
            return new CallArguments(positional.ToArray(), keywords.ToArray());
        }

        public override object Handle_args_0_1_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "k");
        }

        public override object Handle_args_1(
            PegActionContext context,
            PegActionArguments arguments) {
            var positional = new List<AstNodes.ExprBase>();
            var keywords = new List<AstNodes.Keyword>();
            foreach (object? item in Sequence(arguments["a"])) {
                if (item is AstNodes.Keyword keyword) keywords.Add(keyword);
                else positional.Add(ToExpression(item));
            }
            return new CallArguments(positional.ToArray(), keywords.ToArray());
        }

        public override object Handle_kwargs_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var keywords = new List<object?>();
            keywords.AddRange(Sequence(arguments.TryGetValue("a", out object? rawFirst) ? rawFirst : null));
            keywords.AddRange(Sequence(arguments.TryGetValue("b", out object? rawSecond) ? rawSecond : null));
            return keywords.ToArray();
        }

        public override object Handle_starred_expression_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildStarred(arguments, "a");
        }

        public override object Handle_kwarg_or_starred_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildKeyword(ToText(Required(arguments, "a")), Required(arguments, "b"));
        }

        public override object Handle_kwarg_or_starred_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_kwarg_or_double_starred_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildKeyword(ToText(Required(arguments, "a")), Required(arguments, "b"));
        }

        public override object Handle_kwarg_or_double_starred_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildKeyword(null, Required(arguments, "a"));
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

        public override object Handle_dotted_name_0(
            PegActionContext context,
            PegActionArguments arguments) {
            string name = $"{ToText(Required(arguments, "a"))}.{ToText(Required(arguments, "b"))}";
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Name(
                name,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
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

        public override object Handle_import_from_targets_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_import_from_targets_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return new[] { new AstNodes.Alias("*", null, 1, 0, 1, 1) };
        }

        public override object Handle_import_from_1(
            PegActionContext context,
            PegActionArguments arguments) {
            int dotCount = Sequence(arguments["a"]).Count;
            string? module = arguments.TryGetValue("b", out object? moduleValue) && moduleValue is not null
                ? ToText(moduleValue)
                : null;
            object? rawNames = arguments["c"];
            AstNodes.Alias[] names = Sequence(rawNames).OfType<AstNodes.Alias>().ToArray();
            if (names.Length == 0 && ContainsStar(rawNames)) {
                names = new[] { new AstNodes.Alias("*", null, 1, 0, 1, 1) };
            }
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

        public override object Handle_import_from_2(
            PegActionContext context,
            PegActionArguments arguments) {
            int dotCount = Sequence(arguments["a"]).Count;
            object? rawNames = arguments["b"];
            AstNodes.Alias[] names = Sequence(rawNames).OfType<AstNodes.Alias>().ToArray();
            if (names.Length == 0 && ContainsStar(rawNames)) {
                names = new[] { new AstNodes.Alias("*", null, 1, 0, 1, 1) };
            }
            int? isLazy = arguments.TryGetValue("lazy", out object? lazy) && lazy is not null ? 1 : null;
            AstSpan span = SpanOf(names);
            return new AstNodes.ImportFrom(
                null,
                names,
                dotCount,
                isLazy,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_decorators_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).Select(ToExpression).ToArray();
        }

        public override object Handle_decorators_0_0_child_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "f");
        }

        public override object Handle_class_def_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] decorators = Sequence(arguments["a"])
                .Select(ToExpression)
                .ToArray();
            AstNodes.ClassDef classDefinition = Required(arguments, "b") as AstNodes.ClassDef
                ?? throw new InvalidOperationException("Class definition data had an unexpected representation.");
            return new AstNodes.ClassDef(
                classDefinition.Name,
                classDefinition.Bases,
                classDefinition.Keywords,
                classDefinition.Body,
                decorators,
                classDefinition.TypeParams,
                classDefinition.LineNumber ?? 1,
                classDefinition.ColumnOffset ?? 0,
                classDefinition.EndLineNumber,
                classDefinition.EndColumnOffset);
        }

        public override object Handle_class_def_raw_1(
            PegActionContext context,
            PegActionArguments arguments) {
            string name = ToText(Required(arguments, "a"));
            CallArguments classArguments = arguments.TryGetValue("b", out object? rawArguments)
                && rawArguments is CallArguments callArguments
                ? callArguments
                : CallArguments.Empty;
            AstNodes.TypeParamBase[] typeParams = Sequence(
                arguments.TryGetValue("t", out object? rawTypeParams) ? rawTypeParams : null)
                .OfType<AstNodes.TypeParamBase>()
                .ToArray();
            AstNodes.Stmt[] body = Sequence(arguments["c"])
                .OfType<AstNodes.Stmt>()
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new ClassSpec(
                name,
                classArguments,
                body,
                typeParams,
                span).Build(Array.Empty<AstNodes.ExprBase>());
        }

        public override object Handle_class_def_raw_1_3_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return arguments.TryGetValue("z", out object? value) ? value! : null!;
        }

        public override object Handle_function_def_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] decorators = Sequence(arguments["d"])
                .Select(ToExpression)
                .ToArray();
            return BuildDecoratedFunction(Required(arguments, "f"), decorators);
        }

        public override object Handle_function_def_raw_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildFunctionSpec(arguments, false).Build(Array.Empty<AstNodes.ExprBase>());
        }

        public override object Handle_function_def_raw_1_6_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_function_def_raw_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildFunctionSpec(arguments, true).Build(Array.Empty<AstNodes.ExprBase>());
        }

        public override object Handle_function_def_raw_2_7_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_parameters_0(
            PegActionContext context,
            PegActionArguments arguments) {
            SlashParameters slash = Required(arguments, "a") as SlashParameters
                ?? throw new InvalidOperationException("Slash parameter data had an unexpected representation.");
            var defaults = slash.Defaults.ToList();
            var keywordDefaults = new List<AstNodes.ExprBase?>();
            AstNodes.Arg[] positional = Sequence(arguments["b"])
                .OfType<AstNodes.Arg>()
                .ToArray();
            var regularArguments = positional.ToList();
            foreach (NameDefaultPair item in Sequence(arguments["c"]).OfType<NameDefaultPair>()) {
                regularArguments.Add(item.Parameter);
                if (item.Default is not null) defaults.Add(item.Default);
            }
            StarParameters star = arguments.TryGetValue("d", out object? rawStar)
                && rawStar is StarParameters parameters
                ? parameters
                : StarParameters.Empty;
            keywordDefaults.AddRange(star.KeywordParameters.Select(item => item.Default));
            return new AstNodes.Arguments(
                slash.PositionalOnly,
                regularArguments.ToArray(),
                star.Vararg,
                star.KeywordParameters.Select(item => item.Parameter).ToArray(),
                keywordDefaults.ToArray(),
                star.Kwarg,
                defaults.ToArray());
        }

        public override object Handle_parameters_1(
            PegActionContext context,
            PegActionArguments arguments) {
            SlashParameters slash = Required(arguments, "a") as SlashParameters
                ?? throw new InvalidOperationException("Slash parameter data had an unexpected representation.");
            var defaults = slash.Defaults.ToList();
            var keywordDefaults = new List<AstNodes.ExprBase?>();
            var positional = new List<AstNodes.Arg>();
            foreach (NameDefaultPair item in Sequence(arguments["b"]).OfType<NameDefaultPair>()) {
                positional.Add(item.Parameter);
                if (item.Default is not null) defaults.Add(item.Default);
            }
            StarParameters star = arguments.TryGetValue("c", out object? rawStar)
                && rawStar is StarParameters parameters
                ? parameters
                : StarParameters.Empty;
            keywordDefaults.AddRange(star.KeywordParameters.Select(item => item.Default));
            return new AstNodes.Arguments(
                slash.PositionalOnly,
                positional.ToArray(),
                star.Vararg,
                star.KeywordParameters.Select(item => item.Parameter).ToArray(),
                keywordDefaults.ToArray(),
                star.Kwarg,
                defaults.ToArray());
        }

        public override object Handle_parameters_2(
            PegActionContext context,
            PegActionArguments arguments) {
            var argumentsList = new List<AstNodes.Arg>();
            var defaults = new List<AstNodes.ExprBase>();
            argumentsList.AddRange(Sequence(arguments["a"]).OfType<AstNodes.Arg>());
            foreach (NameDefaultPair item in Sequence(arguments["b"]).OfType<NameDefaultPair>()) {
                argumentsList.Add(item.Parameter);
                if (item.Default is not null) defaults.Add(item.Default);
            }
            StarParameters star = arguments.TryGetValue("c", out object? rawStar)
                && rawStar is StarParameters parameters
                ? parameters
                : StarParameters.Empty;
            return new AstNodes.Arguments(
                Array.Empty<AstNodes.Arg>(),
                argumentsList.ToArray(),
                star.Vararg,
                star.KeywordParameters.Select(item => item.Parameter).ToArray(),
                star.KeywordParameters.Select(item => item.Default).ToArray(),
                star.Kwarg,
                defaults.ToArray());
        }

        public override object Handle_parameters_3(
            PegActionContext context,
            PegActionArguments arguments) {
            var argumentsList = new List<AstNodes.Arg>();
            var defaults = new List<AstNodes.ExprBase>();
            foreach (NameDefaultPair item in Sequence(arguments["a"]).OfType<NameDefaultPair>()) {
                argumentsList.Add(item.Parameter);
                if (item.Default is not null) defaults.Add(item.Default);
            }
            StarParameters star = arguments.TryGetValue("b", out object? rawStar)
                && rawStar is StarParameters parameters
                ? parameters
                : StarParameters.Empty;
            return new AstNodes.Arguments(
                Array.Empty<AstNodes.Arg>(),
                argumentsList.ToArray(),
                star.Vararg,
                star.KeywordParameters.Select(item => item.Parameter).ToArray(),
                star.KeywordParameters.Select(item => item.Default).ToArray(),
                star.Kwarg,
                defaults.ToArray());
        }

        public override object Handle_parameters_4(
            PegActionContext context,
            PegActionArguments arguments) {
            StarParameters star = Required(arguments, "a") as StarParameters
                ?? throw new InvalidOperationException("Star parameter data had an unexpected representation.");
            return new AstNodes.Arguments(
                Array.Empty<AstNodes.Arg>(),
                Array.Empty<AstNodes.Arg>(),
                star.Vararg,
                star.KeywordParameters.Select(item => item.Parameter).ToArray(),
                star.KeywordParameters.Select(item => item.Default).ToArray(),
                star.Kwarg,
                Array.Empty<AstNodes.ExprBase>());
        }

        public override object Handle_lambda_parameters_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_parameters_0(context, arguments);
        }

        public override object Handle_lambda_parameters_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_parameters_1(context, arguments);
        }

        public override object Handle_lambda_parameters_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_parameters_2(context, arguments);
        }

        public override object Handle_lambda_parameters_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_parameters_3(context, arguments);
        }

        public override object Handle_lambda_parameters_4(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_parameters_4(context, arguments);
        }

        public override object Handle_lambda_slash_no_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_slash_no_default_0(context, arguments);
        }

        public override object Handle_lambda_slash_no_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_slash_no_default_1(context, arguments);
        }

        public override object Handle_lambda_slash_with_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_slash_with_default_0(context, arguments);
        }

        public override object Handle_lambda_slash_with_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_slash_with_default_1(context, arguments);
        }

        public override object Handle_lambda_star_etc_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_star_etc_1(context, arguments);
        }

        public override object Handle_lambda_star_etc_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_star_etc_2(context, arguments);
        }

        public override object Handle_lambda_star_etc_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_star_etc_3(context, arguments);
        }

        public override object Handle_lambda_kwds_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_kwds_1(context, arguments);
        }

        public override object Handle_lambda_param_no_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_lambda_param_no_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_lambda_param_with_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_lambda_param_with_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_lambda_param_maybe_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_lambda_param_maybe_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_lambda_param_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildArgument(arguments, null);
        }

        public override object Handle_slash_no_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new SlashParameters(
                Sequence(arguments["a"]).OfType<AstNodes.Arg>().ToArray(),
                Array.Empty<AstNodes.ExprBase>());
        }

        public override object Handle_slash_no_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return new SlashParameters(
                Sequence(arguments["a"]).OfType<AstNodes.Arg>().ToArray(),
                Array.Empty<AstNodes.ExprBase>());
        }

        public override object Handle_slash_with_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSlashParameters(arguments);
        }

        public override object Handle_slash_with_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSlashParameters(arguments);
        }

        public override object Handle_star_etc_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildStarParameters(arguments);
        }

        public override object Handle_star_etc_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildStarParameters(arguments);
        }

        public override object Handle_star_etc_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildStarParameters(arguments);
        }

        public override object Handle_star_etc_4(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.Arg kwarg = Required(arguments, "a") as AstNodes.Arg
                ?? throw new InvalidOperationException("Keyword rest parameter had an unexpected representation.");
            return new StarParameters(
                null,
                Array.Empty<NameDefaultPair>(),
                kwarg);
        }

        public override object Handle_kwds_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_param_no_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return AddTypeComment(Required(arguments, "a"), arguments);
        }

        public override object Handle_param_no_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return AddTypeComment(Required(arguments, "a"), arguments);
        }

        public override object Handle_param_no_default_star_annotation_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return AddTypeComment(Required(arguments, "a"), arguments);
        }

        public override object Handle_param_no_default_star_annotation_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return AddTypeComment(Required(arguments, "a"), arguments);
        }

        public override object Handle_param_with_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_param_with_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_param_maybe_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_param_maybe_default_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildNameDefaultPair(arguments);
        }

        public override object Handle_param_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildArgument(arguments, null);
        }

        public override object Handle_param_star_annotation_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildArgument(arguments, null);
        }

        public override object Handle_simple_stmts_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new[] { Required(arguments, "a") };
        }

        public override object Handle_simple_stmts_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).ToArray();
        }

        public override object Handle_statement_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new[] { Required(arguments, "a") };
        }

        public override object Handle_single_compound_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new[] { Required(arguments, "a") };
        }

        public override object Handle_statement_newline_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_statement_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_statements_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return CollectStatements(arguments["a"]);
        }

        public override object Handle_file_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var body = new List<AstNodes.Stmt>();
            if (arguments.TryGetValue("a", out object? rawBody) && rawBody is not null) {
                body.AddRange(CollectStatements(rawBody));
            }
            return new AstNodes.Module(body, Array.Empty<AstNodes.TypeIgnoreBase>());
        }

        public override object Handle_eval_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new AstNodes.Expression(
                ToExpression(Required(arguments, "a")));
        }

        public override object Handle_interactive_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new AstNodes.Interactive(
                Sequence(Required(arguments, "a")).OfType<AstNodes.Stmt>().ToArray());
        }

        public override object Handle_func_type_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] argumentsList = Sequence(
                arguments.TryGetValue("a", out object? rawArguments) ? rawArguments : null)
                .Select(ToExpression)
                .ToArray();
            return new AstNodes.FunctionType(
                argumentsList,
                ToExpression(Required(arguments, "b")));
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

        public override object Handle_break_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Break(span.Line, span.Column, span.EndLine, span.EndColumn);
        }

        public override object Handle_continue_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Continue(span.Line, span.Column, span.EndLine, span.EndColumn);
        }

        public override object Handle_global_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Global(
                Sequence(arguments["a"]).Select(ToText).ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_nonlocal_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Nonlocal(
                Sequence(arguments["a"]).Select(ToText).ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_del_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Delete(
                Sequence(arguments["a"]).Select(value => SetContext(value, new AstNodes.Del())).ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_del_targets_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Sequence(arguments["a"]).Select(ToExpression).ToArray();
        }

        public override object Handle_del_target_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Attribute(
                value,
                ToText(Required(arguments, "b")),
                new AstNodes.Del(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_del_target_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase slice = ToExpression(Required(arguments, "b"));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Subscript(
                value,
                slice,
                new AstNodes.Del(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_del_t_atom_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return SetContext(Required(arguments, "a"), new AstNodes.Del());
        }

        public override object Handle_del_t_atom_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return SetContext(Required(arguments, "a"), new AstNodes.Del());
        }

        public override object Handle_del_t_atom_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] targets = arguments.TryGetValue("a", out object? rawTargets)
                && rawTargets is not null
                ? Sequence(rawTargets).Select(ToExpression).ToArray()
                : Array.Empty<AstNodes.ExprBase>();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Tuple(
                targets,
                new AstNodes.Del(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_del_t_atom_3(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] targets = Sequence(arguments["a"])
                .Select(ToExpression)
                .ToArray();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.List(
                targets,
                new AstNodes.Del(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_assert_stmt_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase test = ToExpression(Required(arguments, "a"));
            AstNodes.ExprBase? message = arguments.TryGetValue("b", out object? rawMessage)
                && rawMessage is not null
                ? ToExpression(rawMessage)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Assert(
                test,
                message,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_assert_stmt_1_2_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "z");
        }

        public override object Handle_yield_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "y"));
            AstSpan span = SpanOf(value);
            return new AstNodes.Expr(
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_yield_expr_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.YieldFrom(
                ToExpression(Required(arguments, "a")),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_yield_expr_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase? value = arguments.TryGetValue("a", out object? rawValue)
                && rawValue is not null
                ? ToExpression(rawValue)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Yield(
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_raise_stmt_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Raise(
                ToExpression(Required(arguments, "a")),
                ToExpression(Required(arguments, "b")),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_raise_stmt_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Raise(
                ToExpression(Required(arguments, "a")),
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_raise_stmt_3(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Raise(
                null,
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
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
            return Required(arguments, "a");
        }

        public override object Handle_star_annotation_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
        }

        public override object Handle_default_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "a");
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

        public override object Handle_fstring_middle_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildFStringMiddle(arguments);
        }

        public override object Handle_fstring_replacement_field_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "a"));
            bool hasDebugExpression = arguments.TryGetValue("debug_expr", out object? rawDebug)
                && rawDebug is not null;
            int conversion = -1;
            if (arguments.TryGetValue("conversion", out object? rawConversion)
                && rawConversion is FStringConversion fstringConversion) {
                conversion = fstringConversion.Value;
            }
            AstNodes.ExprBase? formatSpec = arguments.TryGetValue("format", out object? rawFormat)
                && rawFormat is not null
                ? ToExpression(rawFormat)
                : null;
            if (hasDebugExpression && conversion < 0 && formatSpec is null) {
                conversion = 'r';
            }
            AstSpan span = SpanOf(arguments);
            var formatted = new AstNodes.FormattedValue(
                value,
                conversion,
                formatSpec,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
            if (!hasDebugExpression) return formatted;

            string debugText = rawDebug is PegToken debugToken
                ? debugToken.SourceText
                : "=";
            var debugConstant = new AstNodes.Constant(
                debugText,
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
            return new AstNodes.JoinedStr(
                new AstNodes.ExprBase[] { debugConstant, formatted },
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_fstring_conversion_0(
            PegActionContext context,
            PegActionArguments arguments) {
            string conversion = ToText(Required(arguments, "conv"));
            if (conversion.Length != 1 || "sra".IndexOf(conversion[0]) < 0) {
                throw new InvalidOperationException($"Unsupported f-string conversion '{conversion}'.");
            }
            return new FStringConversion(conversion[0]);
        }

        public override object Handle_fstring_full_format_spec_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] values = FlattenJoinedValues(Sequence(
                arguments.TryGetValue("spec", out object? rawSpec) ? rawSpec : null)
                .Select(ToExpression));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.JoinedStr(
                values,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_fstring_format_spec_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildFStringMiddle(arguments);
        }

        public override object Handle_fstring_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] values = FlattenJoinedValues(Sequence(
                arguments.TryGetValue("b", out object? rawValues) ? rawValues : null)
                .Select(ToExpression));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.JoinedStr(
                values,
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
            if (arguments.TryGetValue("conversion", out object? rawConversion)
                && rawConversion is FStringConversion fstringConversion) {
                conversion = fstringConversion.Value;
            }
            AstNodes.ExprBase? formatSpec = arguments.TryGetValue("format", out object? rawFormat)
                && rawFormat is not null
                ? ToExpression(rawFormat)
                : null;
            bool hasDebugExpression = arguments.TryGetValue("debug_expr", out object? rawDebug)
                && rawDebug is not null;
            AstSpan span = SpanOf(arguments);
            string expression = GetInterpolationExpressionText(value, rawDebug);
            if (hasDebugExpression && conversion < 0 && formatSpec is null) {
                conversion = 'r';
            }
            var interpolation = new AstNodes.Interpolation(
                value,
                expression,
                conversion,
                formatSpec,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
            if (!hasDebugExpression) return interpolation;

            string debugText = rawDebug is PegToken debugToken
                ? debugToken.SourceText
                : expression + "=";
            if (!debugText.TrimEnd().EndsWith('=')) {
                debugText += "=";
            }
            return new AstNodes.TemplateStr(
                new AstNodes.ExprBase[] {
                    new AstNodes.Constant(
                        debugText,
                        null,
                        span.Line,
                        span.Column,
                        span.EndLine,
                        span.EndColumn),
                    interpolation
                },
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tstring_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] values = FlattenTemplateValues(Sequence(arguments["b"])
                .Select(ToExpression));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.TemplateStr(
                values,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tstring_format_spec_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildFStringMiddle(arguments);
        }

        public override object Handle_tstring_full_format_spec_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] values = FlattenTemplateValues(Sequence(
                arguments.TryGetValue("spec", out object? rawSpec) ? rawSpec : null)
                .Select(ToExpression));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.TemplateStr(
                values,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_tstring_format_spec_replacement_field_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Handle_tstring_replacement_field_0(context, arguments);
        }

        public override object Handle_strings_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] strings = Sequence(arguments["a"])
                .Select(ToExpression)
                .ToArray();
            return strings.Length == 1 ? strings[0] : strings;
        }

        public override object Handle_string_0(
            PegActionContext context,
            PegActionArguments arguments) {
            object value = Required(arguments, "s");
            if (value is not PegToken token) {
                value = Sequence(value).FirstOrDefault(item => item is PegToken)
                    ?? throw new InvalidOperationException("STRING action did not provide a token.");
                token = (PegToken)value;
            }

            AstSpan span = SpanOf(token);
            return new AstNodes.Constant(
                DecodeStringLiteral(token.Value),
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_strings_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase[] strings = Sequence(arguments["a"])
                .Select(ToExpression)
                .ToArray();
            if (strings.Length == 1) return strings[0];

            if (strings.All(item => item is AstNodes.Constant)) {
                object combined;
                if (strings.All(item => ((AstNodes.Constant)item).Value is string)) {
                    combined = string.Concat(
                        strings.Cast<AstNodes.Constant>()
                            .Select(item => (string)item.Value!));
                } else if (strings.All(item => ((AstNodes.Constant)item).Value is IronPython.Runtime.Bytes)) {
                    combined = new IronPython.Runtime.Bytes(
                        strings.Cast<AstNodes.Constant>()
                            .SelectMany(item => (IronPython.Runtime.Bytes)item.Value!));
                } else {
                    throw new InvalidOperationException(
                        "cannot mix bytes and nonbytes literals");
                }
                AstSpan constantSpan = SpanOf(strings);
                return new AstNodes.Constant(
                    combined,
                    null,
                    constantSpan.Line,
                    constantSpan.Column,
                    constantSpan.EndLine,
                    constantSpan.EndColumn);
            }

            var values = new List<AstNodes.ExprBase>();
            foreach (AstNodes.ExprBase item in strings) {
                if (item is AstNodes.JoinedStr joined) values.AddRange(joined.Values);
                else values.Add(item);
            }
            AstSpan span = SpanOf(strings);
            return new AstNodes.JoinedStr(
                values.ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_block_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return CollectStatements(arguments["a"]);
        }

        public override object Handle_if_stmt_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.Stmt continuation = Required(arguments, "c") as AstNodes.Stmt
                ?? throw new InvalidOperationException("If continuation had an unexpected representation.");
            AstSpan span = SpanOf(arguments);
            return new AstNodes.If(
                ToExpression(Required(arguments, "a")),
                BuildBlock(arguments["b"]),
                new[] { continuation },
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_if_stmt_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.If(
                ToExpression(Required(arguments, "a")),
                BuildBlock(arguments["b"]),
                BuildOptionalBlock(arguments, "c"),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_elif_stmt_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.Stmt continuation = Required(arguments, "c") as AstNodes.Stmt
                ?? throw new InvalidOperationException("Elif continuation had an unexpected representation.");
            AstSpan span = SpanOf(arguments);
            return new AstNodes.If(
                ToExpression(Required(arguments, "a")),
                BuildBlock(arguments["b"]),
                new[] { continuation },
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_elif_stmt_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.If(
                ToExpression(Required(arguments, "a")),
                BuildBlock(arguments["b"]),
                BuildOptionalBlock(arguments, "c"),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_else_block_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBlock(arguments["b"]);
        }

        public override object Handle_while_stmt_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.While(
                ToExpression(Required(arguments, "a")),
                BuildBlock(arguments["b"]),
                BuildOptionalBlock(arguments, "c"),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_for_stmt_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildFor(arguments, false);
        }

        public override object Handle_for_stmt_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildFor(arguments, true);
        }

        public override object Handle_with_stmt_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildWith(arguments, false);
        }

        public override object Handle_with_stmt_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildWith(arguments, false);
        }

        public override object Handle_with_stmt_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildWith(arguments, true);
        }

        public override object Handle_with_stmt_4(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildWith(arguments, true);
        }

        public override object Handle_with_item_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new AstNodes.Withitem(
                ToExpression(Required(arguments, "e")),
                SetContext(Required(arguments, "t"), new AstNodes.Store()));
        }

        public override object Handle_with_item_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return new AstNodes.Withitem(
                ToExpression(Required(arguments, "e")),
                null);
        }

        public override object Handle_try_stmt_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Try(
                BuildBlock(arguments["b"]),
                Array.Empty<AstNodes.ExceptHandlerBase>(),
                Array.Empty<AstNodes.Stmt>(),
                BuildBlock(arguments["f"]),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_try_stmt_2(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Try(
                BuildBlock(arguments["b"]),
                Sequence(arguments["ex"]).OfType<AstNodes.ExceptHandlerBase>().ToArray(),
                BuildOptionalBlock(arguments, "el"),
                BuildOptionalBlock(arguments, "f"),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_try_stmt_3(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.TryStar(
                BuildBlock(arguments["b"]),
                Sequence(arguments["ex"]).OfType<AstNodes.ExceptHandlerBase>().ToArray(),
                BuildOptionalBlock(arguments, "el"),
                BuildOptionalBlock(arguments, "f"),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_except_block_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildExceptHandler(arguments, null);
        }

        public override object Handle_except_block_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildExceptHandler(arguments, ToText(Required(arguments, "t")));
        }

        public override object Handle_except_block_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildExceptHandler(arguments, null);
        }

        public override object Handle_except_block_4(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildExceptHandler(arguments, null);
        }

        public override object Handle_except_star_block_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildExceptHandler(arguments, null);
        }

        public override object Handle_except_star_block_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildExceptHandler(arguments, ToText(Required(arguments, "t")));
        }

        public override object Handle_except_star_block_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildExceptHandler(arguments, null);
        }

        public override object Handle_finally_block_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildBlock(arguments["a"]);
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

        public override object Handle_literal_pattern_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSingletonPattern(arguments, null);
        }

        public override object Handle_literal_pattern_4(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSingletonPattern(arguments, true);
        }

        public override object Handle_literal_pattern_5(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildSingletonPattern(arguments, false);
        }

        public override object Handle_patterns_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchSequence(
                ToPatterns(arguments["patterns"]),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_as_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchAs(
                ToPattern(Required(arguments, "pattern")),
                ToText(Required(arguments, "target")),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_capture_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchAs(
                null,
                ToText(Required(arguments, "target")),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_pattern_capture_target_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "name");
        }

        public override object Handle_wildcard_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchAs(
                null,
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_value_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchValue(Required(arguments, "attr"));
        }

        public override object Handle_attr_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, "value"));
            string attribute = ToText(Required(arguments, "attr"));
            AstSpan span = SpanOf(value);
            return new AstNodes.Attribute(
                value,
                attribute,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_group_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "pattern");
        }

        public override object Handle_sequence_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchSequence(arguments, "patterns");
        }

        public override object Handle_sequence_pattern_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchSequence(arguments, "patterns");
        }

        public override object Handle_open_sequence_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            var patterns = new List<AstNodes.Pattern> {
                ToPattern(Required(arguments, "pattern"))
            };
            patterns.AddRange(ToPatterns(arguments.TryGetValue(
                "patterns",
                out object? rawPatterns) ? rawPatterns : null));
            return patterns.ToArray();
        }

        public override object Handle_maybe_sequence_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return ToPatterns(arguments["patterns"]);
        }

        public override object Handle_star_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchStar(
                ToText(Required(arguments, "target")),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_star_pattern_1(
            PegActionContext context,
            PegActionArguments arguments) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchStar(
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        public override object Handle_mapping_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchMapping(arguments, null, Array.Empty<MappingItem>());
        }

        public override object Handle_mapping_pattern_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchMapping(
                arguments,
                ToText(Required(arguments, "rest")),
                Array.Empty<MappingItem>());
        }

        public override object Handle_mapping_pattern_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchMapping(
                arguments,
                ToText(Required(arguments, "rest")),
                Sequence(arguments["items"]).OfType<MappingItem>().ToArray());
        }

        public override object Handle_mapping_pattern_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchMapping(
                arguments,
                null,
                Sequence(arguments["items"]).OfType<MappingItem>().ToArray());
        }

        public override object Handle_key_value_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new MappingItem(
                ToExpression(Required(arguments, "key")),
                ToPattern(Required(arguments, "pattern")));
        }

        public override object Handle_double_star_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return Required(arguments, "target");
        }

        public override object Handle_class_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchClass(
                arguments,
                Array.Empty<AstNodes.Pattern>(),
                Array.Empty<KeywordPattern>());
        }

        public override object Handle_class_pattern_1(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchClass(
                arguments,
                ToPatterns(arguments["patterns"]),
                Array.Empty<KeywordPattern>());
        }

        public override object Handle_class_pattern_2(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchClass(
                arguments,
                Array.Empty<AstNodes.Pattern>(),
                Sequence(arguments["keywords"]).OfType<KeywordPattern>().ToArray());
        }

        public override object Handle_class_pattern_3(
            PegActionContext context,
            PegActionArguments arguments) {
            return BuildMatchClass(
                arguments,
                ToPatterns(arguments["patterns"]),
                Sequence(arguments["keywords"]).OfType<KeywordPattern>().ToArray());
        }

        public override object Handle_positional_patterns_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return ToPatterns(arguments["args"]);
        }

        public override object Handle_keyword_pattern_0(
            PegActionContext context,
            PegActionArguments arguments) {
            return new KeywordPattern(
                ToText(Required(arguments, "arg")),
                ToPattern(Required(arguments, "value")));
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
            return arguments[name] ?? throw new InvalidOperationException(
                $"Missing PEG action field '{name}'.\n{Environment.StackTrace}");
        }

        private static IReadOnlyList<object?> Sequence(object? value) {
            if (value is null) return Array.Empty<object?>();
            if (value is IReadOnlyList<object?> list) return list;
            if (value is System.Collections.IEnumerable enumerable) {
                return enumerable.Cast<object?>().ToArray();
            }
            return new[] { value };
        }

        private static AstNodes.Stmt[] CollectStatements(object? value) {
            var statements = new List<AstNodes.Stmt>();
            AddStatements(value, statements);
            return statements.ToArray();
        }

        private static void AddStatements(
            object? value,
            List<AstNodes.Stmt> statements) {
            if (value is null) return;
            if (value is PegToken token
                && token.Kind is "NEWLINE" or "NL" or "INDENT" or "DEDENT" or "ENDMARKER") {
                return;
            }
            if (value is AstNodes.Stmt statement) {
                statements.Add(statement);
                return;
            }
            if (value is IEnumerable enumerable && value is not string) {
                bool found = false;
                foreach (object? item in enumerable) {
                    found = true;
                    AddStatements(item, statements);
                }
                if (found) return;
            }
            throw new InvalidOperationException(
                $"Statement value {Describe(value)} had no public AST lowering.");
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
            if (value is IEnumerable enumerable
                && value is not string
                && value is not PegActionArguments) {
                object? only = null;
                foreach (object? item in enumerable) {
                    if (item is null) continue;
                    if (only is not null) {
                        throw new InvalidOperationException(
                            $"PEG action sequence {Describe(value)} has multiple expressions.");
                    }
                    only = item;
                }
                if (only is not null) return ToExpression(only);
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
                $"PEG action value {Describe(value)} cannot be lowered to an expression.");
        }

        private static string Describe(object? value) {
            return value switch {
                null => "<null>",
                PegToken token => $"{token.Kind}({token.Value})",
                AstNodes.PythonAstNode node => node.NodeKind,
                PegActionArguments arguments => "{" + string.Join(
                    ", ",
                    arguments.Select((item, index) => $"{index}:{Describe(item)}")) + "}",
                IReadOnlyList<object?> sequence => "[" + string.Join(
                    ", ",
                    sequence.Select(Describe)) + "]",
                _ => value.GetType().FullName ?? value.GetType().Name
            };
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

        private static AstNodes.BoolOp BuildBoolean(
            PegActionArguments arguments,
            AstNodes.BoolOpKind @operator) {
            var values = new List<AstNodes.ExprBase> {
                ToExpression(Required(arguments, "a"))
            };
            values.AddRange(Sequence(arguments["b"]).Select(ToExpression));
            AstSpan span = SpanOf(values.ToArray());
            return new AstNodes.BoolOp(
                @operator,
                values.ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.ExprBase BuildComplexNumber(
            PegActionArguments arguments,
            AstNodes.OperatorKind @operator) {
            AstNodes.ExprBase real = ToExpression(Required(arguments, "real"));
            AstNodes.ExprBase imaginary = ToExpression(Required(arguments, "imag"));
            AstSpan span = SpanOf(real, imaginary);
            return new AstNodes.BinOp(
                real,
                @operator,
                imaginary,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static CompareEntry BuildCompareEntry(
            PegActionArguments arguments,
            AstNodes.CompareOpKind @operator) {
            return new CompareEntry(@operator, ToExpression(Required(arguments, "a")));
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

        private static AstNodes.UnaryOp BuildSignedNumber(
            PegActionArguments arguments,
            PegActionContext context,
            string fieldName) {
            AstNodes.ExprBase operand = BuildNumberConstant(Required(arguments, fieldName));
            AstNodes.UnaryOpKind @operator = context.ActionId.EndsWith(".2", StringComparison.Ordinal)
                ? new AstNodes.USub()
                : new AstNodes.UAdd();
            AstSpan span = SpanOf(arguments);
            return new AstNodes.UnaryOp(
                @operator,
                operand,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.Constant BuildNumberConstant(object? rawValue) {
            if (rawValue is AstNodes.Constant constant) {
                return constant;
            }
            if (rawValue is not PegToken token) {
                throw new InvalidOperationException(
                    "Numeric literal had an unexpected representation.");
            }
            AstSpan span = SpanOf(token);
            return new AstNodes.Constant(
                ParseNumber(token.Value),
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.ExprBase? GetOptionalExpression(
            PegActionArguments arguments,
            params string[] fieldNames) {
            foreach (string fieldName in fieldNames) {
                if (arguments.TryGetValue(fieldName, out object? value) && value is not null) {
                    return ToExpression(value);
                }
            }
            return null;
        }

        private static AstNodes.Starred BuildStarred(
            PegActionArguments arguments,
            string fieldName) {
            AstNodes.ExprBase value = ToExpression(Required(arguments, fieldName));
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Starred(
                value,
                new AstNodes.Load(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.Keyword BuildKeyword(string? name, object? rawValue) {
            AstNodes.ExprBase value = ToExpression(rawValue);
            AstSpan span = SpanOf(name, value);
            return new AstNodes.Keyword(
                name,
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.Constant BuildConstant(
            PegActionArguments arguments,
            object? value) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Constant(
                value,
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.Constant BuildFStringMiddle(PegActionArguments arguments) {
            object? rawValue = arguments.TryGetValue("t", out object? namedValue)
                ? namedValue
                : arguments.Count > 0 ? arguments[0] : null;
            string text = rawValue is PegToken token ? token.Value : ToText(rawValue);
            AstSpan span = SpanOf(rawValue);
            return new AstNodes.Constant(
                IronPython.Runtime.LiteralParser.ParseString(
                    text.Replace("{{", "{", StringComparison.Ordinal)
                        .Replace("}}", "}", StringComparison.Ordinal)
                        .AsSpan(),
                    isRaw: false),
                null,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.ExprBase[] FlattenJoinedValues(
            IEnumerable<AstNodes.ExprBase> values) {
            var flattened = new List<AstNodes.ExprBase>();
            foreach (AstNodes.ExprBase value in values) {
                if (value is AstNodes.JoinedStr joined) {
                    flattened.AddRange(joined.Values);
                } else {
                    flattened.Add(value);
                }
            }
            return flattened.ToArray();
        }

        private static AstNodes.ExprBase[] FlattenTemplateValues(
            IEnumerable<AstNodes.ExprBase> values) {
            var flattened = new List<AstNodes.ExprBase>();
            foreach (AstNodes.ExprBase value in values) {
                if (value is AstNodes.TemplateStr template) {
                    flattened.AddRange(template.Values);
                } else {
                    flattened.Add(value);
                }
            }
            return flattened.ToArray();
        }

        private static string GetInterpolationExpressionText(
            AstNodes.ExprBase value,
            object? rawDebug) {
            if (rawDebug is PegToken debugToken) {
                string source = debugToken.SourceText.TrimEnd();
                if (source.EndsWith('=')) {
                    return source.Substring(0, source.Length - 1).TrimEnd();
                }
            }
            return value switch {
                AstNodes.Name name => name.Id,
                AstNodes.Constant constant when constant.Value is not null =>
                    Convert.ToString(constant.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => string.Empty
            };
        }

        private static AstNodes.Comprehension BuildComprehension(
            PegActionArguments arguments,
            int isAsync) {
            return new AstNodes.Comprehension(
                SetContext(Required(arguments, "a"), new AstNodes.Store()),
                ToExpression(Required(arguments, "b")),
                Sequence(arguments.TryGetValue("c", out object? rawIfs) ? rawIfs : null)
                    .Select(ToExpression)
                    .ToArray(),
                isAsync);
        }

        private static AstNodes.Pattern ToPattern(object? value) {
            return value as AstNodes.Pattern
                ?? throw new InvalidOperationException(
                    $"Value {Describe(value)} cannot be converted to a match pattern.");
        }

        private static AstNodes.Pattern[] ToPatterns(object? value) {
            return Sequence(value).Select(ToPattern).ToArray();
        }

        private static bool ContainsStar(object? value) {
            if (value is PegToken token) return token.Value == "*";
            if (value is string text) return text == "*";
            if (value is IEnumerable enumerable && value is not string) {
                foreach (object? item in enumerable) {
                    if (ContainsStar(item)) return true;
                }
            }
            return false;
        }

        private static AstNodes.MatchValue BuildMatchValue(object value) {
            AstNodes.ExprBase expression = ToExpression(value);
            AstSpan span = SpanOf(value);
            return new AstNodes.MatchValue(
                expression,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.MatchSingleton BuildSingletonPattern(
            PegActionArguments arguments,
            object? value) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchSingleton(
                value,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.MatchSequence BuildMatchSequence(
            PegActionArguments arguments,
            string fieldName) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchSequence(
                ToPatterns(arguments.TryGetValue(fieldName, out object? rawPatterns)
                    ? rawPatterns
                    : null),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.MatchMapping BuildMatchMapping(
            PegActionArguments arguments,
            string? rest,
            IReadOnlyList<MappingItem> items) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchMapping(
                items.Select(item => item.Key).ToArray(),
                items.Select(item => item.Pattern).ToArray(),
                rest,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.MatchClass BuildMatchClass(
            PegActionArguments arguments,
            IReadOnlyList<AstNodes.Pattern> patterns,
            IReadOnlyList<KeywordPattern> keywords) {
            AstSpan span = SpanOf(arguments);
            return new AstNodes.MatchClass(
                ToExpression(Required(arguments, "cls")),
                patterns,
                keywords.Select(item => item.Name).ToArray(),
                keywords.Select(item => item.Pattern).ToArray(),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.Stmt[] BuildBlock(object? value) {
            return Sequence(value).OfType<AstNodes.Stmt>().ToArray();
        }

        private static AstNodes.Stmt[] BuildOptionalBlock(
            PegActionArguments arguments,
            string fieldName) {
            return arguments.TryGetValue(fieldName, out object? value)
                ? BuildBlock(value)
                : Array.Empty<AstNodes.Stmt>();
        }

        private static AstNodes.Stmt BuildFor(
            PegActionArguments arguments,
            bool isAsync) {
            string? typeComment = arguments.TryGetValue("tc", out object? rawTypeComment)
                && rawTypeComment is not null
                ? ToText(rawTypeComment)
                : null;
            AstSpan span = SpanOf(arguments);
            return isAsync
                ? new AstNodes.AsyncFor(
                    ToExpression(Required(arguments, "t")),
                    ToExpression(Required(arguments, "ex")),
                    BuildBlock(arguments["b"]),
                    BuildOptionalBlock(arguments, "el"),
                    typeComment,
                    span.Line,
                    span.Column,
                    span.EndLine,
                    span.EndColumn)
                : new AstNodes.For(
                    ToExpression(Required(arguments, "t")),
                    ToExpression(Required(arguments, "ex")),
                    BuildBlock(arguments["b"]),
                    BuildOptionalBlock(arguments, "el"),
                    typeComment,
                    span.Line,
                    span.Column,
                    span.EndLine,
                    span.EndColumn);
        }

        private static AstNodes.Stmt BuildWith(
            PegActionArguments arguments,
            bool isAsync) {
            string? typeComment = arguments.TryGetValue("tc", out object? rawTypeComment)
                && rawTypeComment is not null
                ? ToText(rawTypeComment)
                : null;
            AstSpan span = SpanOf(arguments);
            return isAsync
                ? new AstNodes.AsyncWith(
                    Sequence(arguments["a"]).OfType<AstNodes.Withitem>().ToArray(),
                    BuildBlock(arguments["b"]),
                    typeComment,
                    span.Line,
                    span.Column,
                    span.EndLine,
                    span.EndColumn)
                : new AstNodes.With(
                    Sequence(arguments["a"]).OfType<AstNodes.Withitem>().ToArray(),
                    BuildBlock(arguments["b"]),
                    typeComment,
                    span.Line,
                    span.Column,
                    span.EndLine,
                    span.EndColumn);
        }

        private static AstNodes.ExceptHandler BuildExceptHandler(
            PegActionArguments arguments,
            string? name) {
            AstNodes.ExprBase? type = arguments.TryGetValue("e", out object? rawType)
                && rawType is not null
                ? ToExpression(rawType)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.ExceptHandler(
                type,
                name,
                BuildBlock(arguments["b"]),
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static FunctionSpec BuildFunctionSpec(
            PegActionArguments arguments,
            bool isAsync) {
            AstNodes.Arguments parameters = arguments.TryGetValue("params", out object? rawParameters)
                && rawParameters is AstNodes.Arguments suppliedParameters
                ? suppliedParameters
                : EmptyArguments();
            AstNodes.ExprBase? returns = arguments.TryGetValue("a", out object? rawReturns)
                && rawReturns is not null
                ? ToExpression(rawReturns)
                : null;
            string? typeComment = arguments.TryGetValue("tc", out object? rawTypeComment)
                && rawTypeComment is not null
                ? ToText(rawTypeComment)
                : null;
            return new FunctionSpec(
                ToText(Required(arguments, "n")),
                parameters,
                Sequence(arguments["b"]).OfType<AstNodes.Stmt>().ToArray(),
                returns,
                typeComment,
                Sequence(
                    arguments.TryGetValue("t", out object? rawTypeParams) ? rawTypeParams : null)
                    .OfType<AstNodes.TypeParamBase>()
                    .ToArray(),
                isAsync,
                SpanOf(arguments));
        }

        private static AstNodes.Stmt BuildDecoratedFunction(
            object rawFunction,
            IReadOnlyList<AstNodes.ExprBase> decorators) {
            if (rawFunction is AstNodes.FunctionDef function) {
                return new AstNodes.FunctionDef(
                    function.Name,
                    function.Args,
                    function.Body,
                    decorators,
                    function.Returns,
                    function.TypeComment,
                    function.TypeParams,
                    function.LineNumber ?? 1,
                    function.ColumnOffset ?? 0,
                    function.EndLineNumber,
                    function.EndColumnOffset);
            }
            if (rawFunction is AstNodes.AsyncFunctionDef asyncFunction) {
                return new AstNodes.AsyncFunctionDef(
                    asyncFunction.Name,
                    asyncFunction.Args,
                    asyncFunction.Body,
                    decorators,
                    asyncFunction.Returns,
                    asyncFunction.TypeComment,
                    asyncFunction.TypeParams,
                    asyncFunction.LineNumber ?? 1,
                    asyncFunction.ColumnOffset ?? 0,
                    asyncFunction.EndLineNumber,
                    asyncFunction.EndColumnOffset);
            }
            throw new InvalidOperationException("Function definition data had an unexpected representation.");
        }

        private static AstNodes.Arg BuildArgument(
            PegActionArguments arguments,
            string? typeComment) {
            AstNodes.ExprBase? annotation = arguments.TryGetValue("b", out object? rawAnnotation)
                && rawAnnotation is not null
                ? ToExpression(rawAnnotation)
                : null;
            AstSpan span = SpanOf(arguments);
            return new AstNodes.Arg(
                ToText(Required(arguments, "a")),
                annotation,
                typeComment,
                span.Line,
                span.Column,
                span.EndLine,
                span.EndColumn);
        }

        private static AstNodes.Arg AddTypeComment(
            object rawArgument,
            PegActionArguments arguments) {
            AstNodes.Arg argument = rawArgument as AstNodes.Arg
                ?? throw new InvalidOperationException("Parameter had an unexpected representation.");
            string? typeComment = arguments.TryGetValue("tc", out object? rawTypeComment)
                && rawTypeComment is not null
                ? ToText(rawTypeComment)
                : null;
            if (typeComment is null) return argument;
            return new AstNodes.Arg(
                argument.ArgName,
                argument.Annotation,
                typeComment,
                argument.LineNumber ?? 1,
                argument.ColumnOffset ?? 0,
                argument.EndLineNumber,
                argument.EndColumnOffset);
        }

        private static NameDefaultPair BuildNameDefaultPair(PegActionArguments arguments) {
            AstNodes.Arg parameter = Required(arguments, "a") as AstNodes.Arg
                ?? throw new InvalidOperationException("Defaulted parameter had an unexpected representation.");
            parameter = (AstNodes.Arg)AddTypeComment(parameter, arguments);
            AstNodes.ExprBase? defaultValue = arguments.TryGetValue("c", out object? rawDefault)
                && rawDefault is not null
                ? ToExpression(rawDefault)
                : null;
            return new NameDefaultPair(parameter, defaultValue);
        }

        private static StarParameters BuildStarParameters(PegActionArguments arguments) {
            AstNodes.Arg? vararg = arguments.TryGetValue("a", out object? rawVararg)
                && rawVararg is AstNodes.Arg argument
                ? argument
                : null;
            NameDefaultPair[] keywordParameters = Sequence(
                arguments.TryGetValue("b", out object? rawKeywordParameters)
                    ? rawKeywordParameters
                    : null)
                .OfType<NameDefaultPair>()
                .ToArray();
            AstNodes.Arg? kwarg = arguments.TryGetValue("c", out object? rawKwarg)
                && rawKwarg is AstNodes.Arg keywordArgument
                ? keywordArgument
                : null;
            return new StarParameters(vararg, keywordParameters, kwarg);
        }

        private static SlashParameters BuildSlashParameters(PegActionArguments arguments) {
            var positional = new List<AstNodes.Arg>();
            positional.AddRange(Sequence(arguments["a"]).OfType<AstNodes.Arg>());
            NameDefaultPair[] defaulted = Sequence(arguments["b"])
                .OfType<NameDefaultPair>()
                .ToArray();
            positional.AddRange(defaulted.Select(item => item.Parameter));
            return new SlashParameters(
                positional.ToArray(),
                defaulted.Select(item => item.Default!).ToArray());
        }

        private static AstNodes.Arguments EmptyArguments() {
            return new AstNodes.Arguments(
                Array.Empty<AstNodes.Arg>(),
                Array.Empty<AstNodes.Arg>(),
                null,
                Array.Empty<AstNodes.Arg>(),
                Array.Empty<AstNodes.ExprBase?>(),
                null,
                Array.Empty<AstNodes.ExprBase>());
        }

        private static AstNodes.ExprBase SetContext(object? value, AstNodes.ExprContext context) {
            if (value is PegActionArguments) {
                value = ToExpression(value);
            } else if (value is not AstNodes.ExprBase
                && value is IEnumerable enumerable
                && value is not string) {
                object? only = null;
                foreach (object? item in enumerable) {
                    if (item is null) continue;
                    if (only is not null) {
                        throw new InvalidOperationException(
                            $"Target sequence {Describe(value)} has multiple expressions.");
                    }
                    only = item;
                }
                value = only;
            }
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
                AstNodes.Tuple tuple => new AstNodes.Tuple(
                    tuple.Elts.Select(item => SetContext(item, context)).ToArray(),
                    context,
                    tuple.LineNumber ?? span.Line,
                    tuple.ColumnOffset ?? span.Column,
                    tuple.EndLineNumber ?? span.EndLine,
                    tuple.EndColumnOffset ?? span.EndColumn),
                AstNodes.List list => new AstNodes.List(
                    list.Elts.Select(item => SetContext(item, context)).ToArray(),
                    context,
                    list.LineNumber ?? span.Line,
                    list.ColumnOffset ?? span.Column,
                    list.EndLineNumber ?? span.EndLine,
                    list.EndColumnOffset ?? span.EndColumn),
                AstNodes.Starred starred => new AstNodes.Starred(
                    SetContext(starred.Value, context),
                    context,
                    starred.LineNumber ?? span.Line,
                    starred.ColumnOffset ?? span.Column,
                    starred.EndLineNumber ?? span.EndLine,
                    starred.EndColumnOffset ?? span.EndColumn),
                AstNodes.Attribute attribute => new AstNodes.Attribute(
                    attribute.Value,
                    attribute.Attr,
                    context,
                    attribute.LineNumber ?? span.Line,
                    attribute.ColumnOffset ?? span.Column,
                    attribute.EndLineNumber ?? span.EndLine,
                    attribute.EndColumnOffset ?? span.EndColumn),
                AstNodes.Subscript subscript => new AstNodes.Subscript(
                    subscript.Value,
                    subscript.Slice,
                    context,
                    subscript.LineNumber ?? span.Line,
                    subscript.ColumnOffset ?? span.Column,
                    subscript.EndLineNumber ?? span.EndLine,
                    subscript.EndColumnOffset ?? span.EndColumn),
                _ => throw new InvalidOperationException(
                    $"Expression node '{value?.GetType().Name ?? "<null>"}' cannot be used as a target.")
            };
        }

        private static object ParseNumber(string text) {
            try {
                return ParseNumberWorker(text);
            } catch (FormatException) {
                string kind = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? "hexadecimal"
                    : text.StartsWith("0o", StringComparison.OrdinalIgnoreCase)
                        ? "octal"
                        : text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
                            ? "binary"
                            : "decimal";
                throw PythonOps.SyntaxError("invalid {0} literal", kind);
            }
        }

        private static object ParseNumberWorker(string text) {
            string normalized = text.Replace("_", string.Empty);
            if (normalized.EndsWith("j", StringComparison.OrdinalIgnoreCase)) {
                string imaginary = normalized.Substring(0, normalized.Length - 1);
                double value = imaginary.Length == 0
                    ? 1.0
                    : double.Parse(imaginary, NumberStyles.Float, CultureInfo.InvariantCulture);
                return new System.Numerics.Complex(0.0, value);
            }
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                return NormalizeInteger(ParseIntegerDigits(normalized.Substring(2), 16));
            }
            if (normalized.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) {
                return NormalizeInteger(ParseIntegerDigits(normalized.Substring(2), 8));
            }
            if (normalized.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
                return NormalizeInteger(ParseIntegerDigits(normalized.Substring(2), 2));
            }
            if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) {
                return integer;
            }
            if (BigInteger.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger bigInteger)) {
                return bigInteger;
            }
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)) {
                return real;
            }
            throw new FormatException($"Invalid numeric literal '{text}'.");
        }

        private static BigInteger ParseIntegerDigits(string digits, int radix) {
            BigInteger value = BigInteger.Zero;
            foreach (char digit in digits) {
                int number = digit switch {
                    >= '0' and <= '9' => digit - '0',
                    >= 'a' and <= 'f' => digit - 'a' + 10,
                    >= 'A' and <= 'F' => digit - 'A' + 10,
                    _ => throw new FormatException()
                };
                if (number >= radix) throw new FormatException();
                value = (value * radix) + number;
            }
            return value;
        }

        private static object NormalizeInteger(BigInteger value)
            => value >= int.MinValue && value <= int.MaxValue ? (int)value : value;

        private static object DecodeStringLiteral(string literal) {
            int quote = literal.IndexOfAny('\'', '"');
            if (quote < 0) return literal;

            string prefix = literal.Substring(0, quote).ToLowerInvariant();
            char quoteCharacter = literal[quote];
            int quoteLength = quote + 2 < literal.Length
                && literal[quote + 1] == quoteCharacter
                && literal[quote + 2] == quoteCharacter
                ? 3
                : 1;
            int end = literal.Length - quoteLength;
            if (end < quote + quoteLength) end = literal.Length;
            string content = literal.Substring(quote + quoteLength, end - quote - quoteLength);
            bool isRaw = prefix.Contains('r');
            if (prefix.Contains('b')) {
                return new IronPython.Runtime.Bytes(
                    IronPython.Runtime.LiteralParser.ParseBytes<char>(
                        content.AsSpan(),
                        isRaw,
                        isAscii: true,
                        normalizeLineEndings: true));
            }
            return IronPython.Runtime.LiteralParser.ParseString(content.AsSpan(), isRaw);
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
            if (value is CompareEntry comparison) {
                return TryGetSpan(comparison.Value);
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

        private sealed class KeyValueEntry {
            public KeyValueEntry(AstNodes.ExprBase? key, object value) {
                Key = key;
                Value = value;
            }

            public AstNodes.ExprBase? Key { get; }
            public object Value { get; }
        }

        private sealed class CallArguments {
            public static readonly CallArguments Empty = new CallArguments(
                Array.Empty<AstNodes.ExprBase>(),
                Array.Empty<AstNodes.Keyword>());

            public CallArguments(
                IReadOnlyList<AstNodes.ExprBase> positional,
                IReadOnlyList<AstNodes.Keyword> keywords) {
                Positional = positional;
                Keywords = keywords;
            }

            public IReadOnlyList<AstNodes.ExprBase> Positional { get; }
            public IReadOnlyList<AstNodes.Keyword> Keywords { get; }
        }

        private sealed class FStringConversion {
            public FStringConversion(char value) {
                Value = value;
            }

            public char Value { get; }
        }

        private sealed class CompareEntry {
            public CompareEntry(AstNodes.CompareOpKind @operator, AstNodes.ExprBase value) {
                Operator = @operator;
                Value = value;
            }

            public AstNodes.CompareOpKind Operator { get; }
            public AstNodes.ExprBase Value { get; }
        }

        private sealed class FunctionSpec {
            public FunctionSpec(
                string name,
                AstNodes.Arguments parameters,
                IReadOnlyList<AstNodes.Stmt> body,
                AstNodes.ExprBase? returns,
                string? typeComment,
                IReadOnlyList<AstNodes.TypeParamBase> typeParams,
                bool isAsync,
                AstSpan span) {
                Name = name;
                Parameters = parameters;
                Body = body;
                Returns = returns;
                TypeComment = typeComment;
                TypeParams = typeParams;
                IsAsync = isAsync;
                Span = span;
            }

            public string Name { get; }
            public AstNodes.Arguments Parameters { get; }
            public IReadOnlyList<AstNodes.Stmt> Body { get; }
            public AstNodes.ExprBase? Returns { get; }
            public string? TypeComment { get; }
            public IReadOnlyList<AstNodes.TypeParamBase> TypeParams { get; }
            public bool IsAsync { get; }
            public AstSpan Span { get; }

            public AstNodes.Stmt Build(IReadOnlyList<AstNodes.ExprBase> decorators) {
                return IsAsync
                    ? new AstNodes.AsyncFunctionDef(
                        Name,
                        Parameters,
                        Body,
                        decorators,
                        Returns,
                        TypeComment,
                        TypeParams,
                        Span.Line,
                        Span.Column,
                        Span.EndLine,
                        Span.EndColumn)
                    : new AstNodes.FunctionDef(
                        Name,
                        Parameters,
                        Body,
                        decorators,
                        Returns,
                        TypeComment,
                        TypeParams,
                        Span.Line,
                        Span.Column,
                        Span.EndLine,
                        Span.EndColumn);
            }
        }

        private sealed class ClassSpec {
            public ClassSpec(
                string name,
                CallArguments arguments,
                IReadOnlyList<AstNodes.Stmt> body,
                IReadOnlyList<AstNodes.TypeParamBase> typeParams,
                AstSpan span) {
                Name = name;
                Arguments = arguments;
                Body = body;
                TypeParams = typeParams;
                Span = span;
            }

            public string Name { get; }
            public CallArguments Arguments { get; }
            public IReadOnlyList<AstNodes.Stmt> Body { get; }
            public IReadOnlyList<AstNodes.TypeParamBase> TypeParams { get; }
            public AstSpan Span { get; }

            public AstNodes.ClassDef Build(IReadOnlyList<AstNodes.ExprBase> decorators) {
                return new AstNodes.ClassDef(
                    Name,
                    Arguments.Positional,
                    Arguments.Keywords,
                    Body,
                    decorators,
                    TypeParams,
                    Span.Line,
                    Span.Column,
                    Span.EndLine,
                    Span.EndColumn);
            }
        }

        private sealed class SlashParameters {
            public SlashParameters(
                IReadOnlyList<AstNodes.Arg> positionalOnly,
                IReadOnlyList<AstNodes.ExprBase> defaults) {
                PositionalOnly = positionalOnly;
                Defaults = defaults;
            }

            public IReadOnlyList<AstNodes.Arg> PositionalOnly { get; }
            public IReadOnlyList<AstNodes.ExprBase> Defaults { get; }
        }

        private sealed class StarParameters {
            public static readonly StarParameters Empty = new StarParameters(
                null,
                Array.Empty<NameDefaultPair>(),
                null);

            public StarParameters(
                AstNodes.Arg? vararg,
                IReadOnlyList<NameDefaultPair> keywordParameters,
                AstNodes.Arg? kwarg) {
                Vararg = vararg;
                KeywordParameters = keywordParameters;
                Kwarg = kwarg;
            }

            public AstNodes.Arg? Vararg { get; }
            public IReadOnlyList<NameDefaultPair> KeywordParameters { get; }
            public AstNodes.Arg? Kwarg { get; }
        }

        private sealed class NameDefaultPair {
            public NameDefaultPair(AstNodes.Arg parameter, AstNodes.ExprBase? defaultValue) {
                Parameter = parameter;
                Default = defaultValue;
            }

            public AstNodes.Arg Parameter { get; }
            public AstNodes.ExprBase? Default { get; }
        }

        private sealed class MappingItem {
            public MappingItem(AstNodes.ExprBase key, AstNodes.Pattern pattern) {
                Key = key;
                Pattern = pattern;
            }

            public AstNodes.ExprBase Key { get; }
            public AstNodes.Pattern Pattern { get; }
        }

        private sealed class KeywordPattern {
            public KeywordPattern(string name, AstNodes.Pattern pattern) {
                Name = name;
                Pattern = pattern;
            }

            public string Name { get; }
            public AstNodes.Pattern Pattern { get; }
        }
    }
}
