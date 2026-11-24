#nullable enable
using System;
using System.Linq;
using IronPython.Runtime;
using IronPython.Runtime.Operations;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using CompilerAst = IronPython.Compiler.Ast;
using AstNodes = IronPython.Compiler.Syntax.Ast;

namespace IronPython.Compiler.PublicAst {
    public static class PublicAstLowerer {
        private static int _matchTemp;
        [ThreadStatic]
        private static SourceLocationMap? _sourceLocations;
        [ThreadStatic]
        private static string? _className;

        public static CompilerAst.PythonAst LowerModule(
            AstNodes.Module module,
            CompilerContext context) {
            return LowerModule(module, context, true, false);
        }

        public static CompilerAst.PythonAst LowerModule(
            AstNodes.Module module,
            CompilerContext context,
            bool makeModule) {
            return LowerModule(module, context, makeModule, false);
        }

        public static CompilerAst.PythonAst LowerModule(
            AstNodes.Module module,
            CompilerContext context,
            bool makeModule,
            bool returnValue) {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (module.Body.OfType<AstNodes.ImportFrom>().Any(importFrom =>
                    string.Equals(importFrom.Module, "__future__", StringComparison.Ordinal)
                    && importFrom.Names.Any(alias => string.Equals(alias.Name, "annotations", StringComparison.Ordinal)))) {
                ((PythonCompilerOptions)context.Options).FutureAnnotations = true;
            }

            var locations = SourceLocationMap.Create(context.SourceUnit.GetCode());
            _sourceLocations = locations;
            try {
                CompilerAst.Statement[] statements = module.Body
                    .Select(LowerStatement)
                    .ToArray();
                if (returnValue && statements.Length > 0
                    && statements[statements.Length - 1] is CompilerAst.ExpressionStatement expression) {
                    statements[statements.Length - 1] = new CompilerAst.ReturnStatement(
                        expression.Expression) {
                        IndexSpan = expression.Expression.IndexSpan
                    };
                }
                ModuleOptions languageFeatures =
                    ((PythonCompilerOptions)context.Options).Module;
                var ast = new CompilerAst.PythonAst(
                    makeModule,
                    languageFeatures,
                    false,
                    context);
                ast.ParsingFinished(
                    locations.LineStarts,
                    new CompilerAst.SuiteStatement(statements),
                    languageFeatures);
                ast.SkipNamedExpressionCheck = true;
                ast.SkipStarredExpressionCheck = true;
                return ast;
            } finally {
                _sourceLocations = null;
            }
        }

        public static CompilerAst.Statement LowerStatement(AstNodes.Stmt statement) {
            CompilerAst.Statement lowered = statement switch {
                AstNodes.Assign assign => new CompilerAst.AssignmentStatement(
                    assign.Targets.Select(LowerExpression).ToArray(),
                    LowerExpression(assign.Value)),
                AstNodes.TypeAlias typeAlias => LowerTypeAlias(typeAlias),
                AstNodes.AugAssign assign => new CompilerAst.AugmentedAssignStatement(
                    LowerOperator(assign.Op),
                    LowerExpression(assign.Target),
                    LowerExpression(assign.Value)),
                AstNodes.AnnAssign assign => new CompilerAst.AnnotatedAssignStatement(
                    LowerExpression(assign.Target),
                    LowerExpression(assign.Annotation),
                    assign.Value is null ? null : LowerExpression(assign.Value),
                    assign.Simple != 0),
                AstNodes.Import import => new CompilerAst.ImportStatement(
                    import.Names.Select(alias => new CompilerAst.ModuleName(alias.Name.Split('.'))).ToArray(),
                    import.Names.Select(alias => alias.Asname).ToArray(),
                    import.IsLazy == 1),
                AstNodes.ImportFrom importFrom => LowerImportFrom(importFrom),
                AstNodes.Expr expression => new CompilerAst.ExpressionStatement(
                    LowerExpression(expression.Value)),
                AstNodes.Pass => new CompilerAst.EmptyStatement(),
                AstNodes.Return @return => new CompilerAst.ReturnStatement(
                    @return.Value is null ? null : LowerExpression(@return.Value)),
                AstNodes.Raise raise => new CompilerAst.RaiseStatement(
                    raise.Exc is null ? null : LowerExpression(raise.Exc),
                    raise.Cause is null ? null : LowerExpression(raise.Cause)),
                AstNodes.Assert assert => new CompilerAst.AssertStatement(
                    LowerExpression(assert.Test),
                    assert.Msg is null ? null! : LowerExpression(assert.Msg)),
                AstNodes.Global global => new CompilerAst.GlobalStatement(global.Names.Select(MangleIdentifier).ToArray()),
                AstNodes.Nonlocal nonlocal => new CompilerAst.NonlocalStatement(nonlocal.Names.Select(MangleIdentifier).ToArray()),
                AstNodes.Delete delete => new CompilerAst.DelStatement(
                    delete.Targets.Select(LowerExpression).ToArray()),
                AstNodes.Break => new CompilerAst.BreakStatement(),
                AstNodes.Continue => new CompilerAst.ContinueStatement(),
                AstNodes.If @if => LowerIf(@if),
                AstNodes.While @while => new CompilerAst.WhileStatement(
                    LowerExpression(@while.Test),
                    LowerSuite(@while.Body),
                    LowerOptionalSuite(@while.Orelse)),
                AstNodes.For @for => new CompilerAst.ForStatement(
                    LowerExpression(@for.Target),
                    LowerExpression(@for.Iter),
                    LowerSuite(@for.Body),
                    LowerOptionalSuite(@for.Orelse)),
                AstNodes.AsyncFor @for => new CompilerAst.AsyncForStatement(
                    LowerExpression(@for.Target),
                    LowerExpression(@for.Iter),
                    LowerSuite(@for.Body),
                    @for.Orelse.Count == 0 ? null : LowerSuite(@for.Orelse)),
                AstNodes.With @with => LowerWith(@with.Items, @with.Body, false),
                AstNodes.AsyncWith @with => LowerWith(@with.Items, @with.Body, true),
                AstNodes.Match match => LowerMatch(match),
                AstNodes.Try @try => LowerTry(
                    @try.Body,
                    @try.Handlers,
                    @try.Orelse,
                    @try.Finalbody),
                AstNodes.TryStar @try => LowerTry(
                    @try.Body,
                    @try.Handlers,
                    @try.Orelse,
                    @try.Finalbody),
                AstNodes.FunctionDef function => LowerFunction(function),
                AstNodes.AsyncFunctionDef function => LowerFunction(function),
                AstNodes.ClassDef @class => LowerClass(@class),
                _ => throw Unsupported(statement)
            };
            SetLocation(lowered, statement);
            int headerIndex = Math.Clamp(
                GetStartOffset(statement),
                lowered.StartIndex,
                lowered.EndIndex);
            switch (lowered) {
                case CompilerAst.WhileStatement @while:
                    @while.HeaderIndex = headerIndex;
                    break;
                case CompilerAst.ForStatement @for:
                    @for.HeaderIndex = headerIndex;
                    break;
                case CompilerAst.AsyncForStatement @for:
                    @for.HeaderIndex = headerIndex;
                    break;
                case CompilerAst.WithStatement @with:
                    @with.HeaderIndex = headerIndex;
                    break;
                case CompilerAst.AsyncWithStatement @with:
                    @with.HeaderIndex = headerIndex;
                    break;
                case CompilerAst.TryStatement @try:
                    @try.HeaderIndex = headerIndex;
                    break;
            }
            return lowered;
        }

        private static CompilerAst.FromImportStatement LowerImportFrom(
            AstNodes.ImportFrom importFrom) {
            string[] names = importFrom.Names.Select(alias => alias.Name).ToArray();
            if (names.Length == 1 && names[0] == "*") {
                names = (string[])CompilerAst.FromImportStatement.Star;
            }
            return new CompilerAst.FromImportStatement(
                importFrom.Level > 0
                    ? new CompilerAst.RelativeModuleName(
                        (importFrom.Module ?? string.Empty).Split(
                            new[] { '.' },
                            StringSplitOptions.RemoveEmptyEntries),
                        importFrom.Level ?? 0)
                    : new CompilerAst.ModuleName(
                        (importFrom.Module ?? string.Empty).Split(
                            new[] { '.' },
                            StringSplitOptions.RemoveEmptyEntries)),
                names,
                importFrom.Names.Select(alias => alias.Asname).ToArray(),
                string.Equals(importFrom.Module, "__future__", StringComparison.Ordinal),
                importFrom.IsLazy == 1);
        }

        private static CompilerAst.Statement LowerTypeAlias(AstNodes.TypeAlias typeAlias) {
            if (LowerExpression(typeAlias.Name) is not CompilerAst.NameExpression name) {
                throw new InvalidOperationException("TypeAlias name must be a simple name.");
            }
            var thunk = new CompilerAst.LambdaExpression(
                new CompilerAst.FunctionDefinition(
                    null,
                    Array.Empty<CompilerAst.Parameter>(),
                    new CompilerAst.ReturnStatement(LowerExpression(typeAlias.Value))) {
                    GeneratorStop = true
                });
            var typeParameters = PythonTuple.MakeTuple(
                typeAlias.TypeParams.Select(typeParameter => {
                    object? value = typeParameter.GetFields()
                        .FirstOrDefault(field => field.Name is "name" or "Name")
                        .Value;
                    return (object?)(value is string text
                        ? text
                        : value?.ToString() ?? string.Empty);
                }).ToArray());
            return new CompilerAst.AssignmentStatement(
                new CompilerAst.Expression[] { name },
                Call(
                    "__build_type_alias__",
                    Constant(name.Name),
                    thunk,
                    Constant(typeParameters)));
        }

        public static CompilerAst.Expression LowerExpression(AstNodes.ExprBase expression) {
            CompilerAst.Expression lowered = expression switch {
                AstNodes.Name name => new CompilerAst.NameExpression(MangleIdentifier(name.Id)),
                AstNodes.Constant constant => new CompilerAst.ConstantExpression(constant.Value),
                AstNodes.List list => new CompilerAst.ListExpression(
                    list.Elts.Select(LowerExpression).ToArray()),
                AstNodes.Tuple tuple => new CompilerAst.TupleExpression(
                    false,
                    tuple.Elts.Select(LowerExpression).ToArray()),
                AstNodes.Starred starred => new CompilerAst.StarredExpression(
                    LowerExpression(starred.Value)),
                AstNodes.Set set => new CompilerAst.SetExpression(
                    set.Elts.Select(LowerExpression).ToArray()),
                AstNodes.Dict dictionary => new CompilerAst.DictionaryExpression(
                    dictionary.Keys.Select((key, index) => new CompilerAst.SliceExpression(
                        key is null ? null : LowerExpression(key),
                        LowerExpression(dictionary.Values[index]),
                        null)).ToArray()),
                AstNodes.Attribute attribute => new CompilerAst.MemberExpression(
                    LowerExpression(attribute.Value),
                    MangleIdentifier(attribute.Attr)),
                AstNodes.Subscript subscript => new CompilerAst.IndexExpression(
                    LowerExpression(subscript.Value),
                    LowerExpression(subscript.Slice)),
                AstNodes.Slice slice => new CompilerAst.SliceExpression(
                    slice.Lower is null ? null : LowerExpression(slice.Lower),
                    slice.Upper is null ? null : LowerExpression(slice.Upper),
                    slice.Step is null ? null : LowerExpression(slice.Step)),
                AstNodes.Call call => new CompilerAst.CallExpression(
                    LowerExpression(call.Func),
                    call.Args.Select(LowerExpression).ToArray(),
                    call.Keywords.Select(keyword => new CompilerAst.Keyword(
                        keyword.ArgName,
                        LowerExpression(keyword.Value))).ToArray()),
                AstNodes.TemplateStr template => LowerTemplateString(template),
                AstNodes.JoinedStr joined => LowerJoinedString(joined),
                AstNodes.FormattedValue formatted => new CompilerAst.FormattedValueExpression(
                    LowerExpression(formatted.Value),
                    formatted.Conversion < 0 ? null : (char?)formatted.Conversion,
                    formatted.FormatSpec is AstNodes.JoinedStr formatSpec
                        ? LowerJoinedString(formatSpec)
                        : formatted.FormatSpec is null
                            ? null
                            : new CompilerAst.JoinedStringExpression(
                                new[] { LowerExpression(formatted.FormatSpec) })),
                AstNodes.Yield yield => new CompilerAst.YieldExpression(
                    yield.Value is null ? null : LowerExpression(yield.Value)),
                AstNodes.YieldFrom yieldFrom => new CompilerAst.YieldFromExpression(
                    LowerExpression(yieldFrom.Value)),
                AstNodes.Await await => new CompilerAst.AwaitExpression(
                    LowerExpression(await.Value)),
                AstNodes.ListComp comprehension => comprehension.Elt is AstNodes.Starred starred
                    ? LowerStarredComprehension(starred.Value, comprehension.Generators, "list")
                    : new CompilerAst.ListComprehension(
                        LowerExpression(comprehension.Elt),
                        LowerComprehensionIterators(comprehension.Generators)),
                AstNodes.SetComp comprehension => comprehension.Elt is AstNodes.Starred starred
                    ? LowerStarredComprehension(starred.Value, comprehension.Generators, "set")
                    : new CompilerAst.SetComprehension(
                        LowerExpression(comprehension.Elt),
                        LowerComprehensionIterators(comprehension.Generators)),
                AstNodes.DictComp comprehension => comprehension.Value is null
                    ? LowerDictionaryUnpackingComprehension(
                        comprehension,
                        comprehension.Key)
                    : new CompilerAst.DictionaryComprehension(
                        LowerExpression(comprehension.Key),
                        LowerExpression(comprehension.Value),
                        LowerComprehensionIterators(comprehension.Generators)),
                AstNodes.GeneratorExp generator => LowerGeneratorExpression(generator),
                AstNodes.Lambda lambda => LowerLambda(lambda),
                AstNodes.NamedExpr named => new CompilerAst.NamedExpression(
                    LowerExpression(named.Target) as CompilerAst.NameExpression
                        ?? throw new InvalidOperationException(
                            "Walrus assignment target must be a name."),
                    LowerExpression(named.Value)),
                AstNodes.IfExp conditional => new CompilerAst.ConditionalExpression(
                    LowerExpression(conditional.Test),
                    LowerExpression(conditional.Body),
                    LowerExpression(conditional.Orelse)),
                AstNodes.BoolOp boolean => LowerBoolean(boolean),
                AstNodes.Compare comparison => LowerComparison(comparison),
                AstNodes.BinOp binOp => new CompilerAst.BinaryExpression(
                    LowerOperator(binOp.Op),
                    LowerExpression(binOp.Left),
                    LowerExpression(binOp.Right)),
                AstNodes.UnaryOp unary => new CompilerAst.UnaryExpression(
                    LowerUnaryOperator(unary.Op),
                    LowerExpression(unary.Operand)),
                _ => throw Unsupported(expression)
            };
            SetLocation(lowered, expression);
            return lowered;
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
            globals["__builtins__"] = context.BuiltinModuleDict;
            globals["__name__"] = "<public-ast>";
            globals["__package__"] = null;
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

        private static CompilerAst.JoinedStringExpression LowerJoinedString(
            AstNodes.JoinedStr joined) {
            return new CompilerAst.JoinedStringExpression(
                joined.Values.Select(LowerExpression).ToArray());
        }

        private static CompilerAst.TemplateStringExpression LowerTemplateString(
            AstNodes.TemplateStr template) {
            var strings = new System.Collections.Generic.List<string> { string.Empty };
            var interpolations = new System.Collections.Generic.List<CompilerAst.TemplateInterpolation>();
            foreach (AstNodes.ExprBase value in template.Values) {
                if (value is AstNodes.Constant constant && constant.Value is string text) {
                    strings[strings.Count - 1] += text;
                    continue;
                }
                if (value is AstNodes.Interpolation interpolation) {
                    interpolations.Add(new CompilerAst.TemplateInterpolation(
                        LowerExpression(interpolation.Value),
                        interpolation.Str as string ?? string.Empty,
                        interpolation.Conversion < 0
                            ? null
                            : ((char)interpolation.Conversion).ToString(),
                        interpolation.FormatSpec is null
                            ? null
                            : LowerTemplateFormatSpec(interpolation.FormatSpec)));
                    strings.Add(string.Empty);
                    continue;
                }
                throw new NotSupportedException(
                    $"Template string part '{value.NodeKind}' is not supported.");
            }
            return new CompilerAst.TemplateStringExpression(
                strings.ToArray(),
                interpolations.ToArray());
        }

        private static CompilerAst.Expression LowerTemplateFormatSpec(
            AstNodes.ExprBase formatSpec) {
            if (formatSpec is AstNodes.Constant constant) {
                return new CompilerAst.ConstantExpression(constant.Value);
            }
            if (formatSpec is AstNodes.JoinedStr joined) {
                return LowerJoinedString(joined);
            }
            if (formatSpec is AstNodes.TemplateStr template) {
                return new CompilerAst.JoinedStringExpression(
                    template.Values.Select(value => value is AstNodes.Interpolation interpolation
                        ? (CompilerAst.Expression)new CompilerAst.FormattedValueExpression(
                            LowerExpression(interpolation.Value),
                            interpolation.Conversion < 0
                                ? null
                                : (char?)interpolation.Conversion,
                            interpolation.FormatSpec is null
                                ? null
                                : LowerTemplateFormatSpecExpression(interpolation.FormatSpec))
                        : LowerExpression(value)).ToArray());
            }
            return LowerExpression(formatSpec);
        }

        private static CompilerAst.Expression LowerDictionaryUnpackingComprehension(
            AstNodes.DictComp comprehension,
            AstNodes.ExprBase packedExpression) {
            if (comprehension.Generators.Count == 0) {
                throw new InvalidOperationException(
                    "Dictionary unpacking comprehension is incomplete.");
            }
            if (comprehension.Generators.Any(item => item.IsAsync != 0)) {
                throw new NotSupportedException(
                    "Async dictionary unpacking comprehensions require async lowering.");
            }

            AstNodes.Comprehension first = comprehension.Generators[0];
            var root = new CompilerAst.ForStatement(
                LowerExpression(first.Target),
                new CompilerAst.NameExpression("__dict_unpack_iterable"),
                null!,
                null!);
            CompilerAst.Statement current = root;

            for (int i = 0; i < comprehension.Generators.Count; i++) {
                AstNodes.Comprehension generator = comprehension.Generators[i];
                if (i > 0) {
                    var loop = new CompilerAst.ForStatement(
                        LowerExpression(generator.Target),
                        LowerExpression(generator.Iter),
                        null!,
                        null!);
                    SetGeneratorBody(current, loop);
                    current = loop;
                }
                foreach (AstNodes.ExprBase condition in generator.Ifs) {
                    var test = new CompilerAst.IfStatement(
                        new[] {
                            new CompilerAst.IfStatementTest(
                                LowerExpression(condition),
                                null!)
                        },
                        null!);
                    SetGeneratorBody(current, test);
                    current = test;
                }
            }

            var packed = new CompilerAst.NameExpression("__dict_unpack_value");
            var key = new CompilerAst.NameExpression("__dict_unpack_key");
            var value = new CompilerAst.NameExpression("__dict_unpack_item");
            var yieldPair = new CompilerAst.ExpressionStatement(
                new CompilerAst.YieldExpression(
                    new CompilerAst.TupleExpression(false, new[] { key, value })));
            var unpackItems = new CompilerAst.ForStatement(
                new CompilerAst.TupleExpression(false, new[] {
                    (CompilerAst.Expression)key,
                    value
                }),
                new CompilerAst.CallExpression(
                    new CompilerAst.MemberExpression(packed, "items"),
                    Array.Empty<CompilerAst.Expression>(),
                    null),
                yieldPair,
                null!);
            SetGeneratorBody(
                current,
                new CompilerAst.SuiteStatement(
                    new CompilerAst.Statement[] {
                        new CompilerAst.AssignmentStatement(
                            new CompilerAst.Expression[] { packed },
                            LowerExpression(packedExpression)),
                        unpackItems
                    }));

            var parameter = new CompilerAst.Parameter("__dict_unpack_iterable");
            var function = new CompilerAst.FunctionDefinition(
                "<dict unpack genexpr>",
                new[] { parameter },
                root) {
                IsGenerator = true,
                IsGeneratorExpression = true,
                GeneratorStop = true
            };
            var generatorExpression = new CompilerAst.GeneratorExpression(
                function,
                LowerExpression(first.Iter));
            return new CompilerAst.CallExpression(
                new CompilerAst.NameExpression("dict"),
                new CompilerAst.Expression[] { generatorExpression },
                null);
        }

        private static CompilerAst.JoinedStringExpression LowerTemplateFormatSpecExpression(
            AstNodes.ExprBase formatSpec) {
            CompilerAst.Expression lowered = LowerTemplateFormatSpec(formatSpec);
            return lowered as CompilerAst.JoinedStringExpression
                ?? new CompilerAst.JoinedStringExpression(new[] { lowered });
        }

        private static CompilerAst.IfStatement LowerIf(AstNodes.If @if) {
            var tests = new System.Collections.Generic.List<CompilerAst.IfStatementTest>();
            AstNodes.If current = @if;
            while (true) {
                tests.Add(new CompilerAst.IfStatementTest(
                    LowerExpression(current.Test),
                    LowerSuite(current.Body)) {
                    HeaderIndex = GetStartOffset(current)
                });
                SetLocation(tests[tests.Count - 1], current);
                if (current.Orelse.Count == 1 && current.Orelse[0] is AstNodes.If continuation) {
                    current = continuation;
                    continue;
                }
                return new CompilerAst.IfStatement(
                    tests.ToArray(),
                    LowerOptionalSuite(current.Orelse));
            }
        }

        private static CompilerAst.Statement LowerMatch(AstNodes.Match match) {
            var statements = new System.Collections.Generic.List<CompilerAst.Statement>();
            CompilerAst.Expression subject = NewMatchTemp(
                LowerExpression(match.Subject),
                statements);
            var tests = new System.Collections.Generic.List<CompilerAst.IfStatementTest>();
            foreach (AstNodes.MatchCase matchCase in match.Cases) {
                ValidatePatternBindings(matchCase.Pattern);
                PatternResult pattern = LowerPattern(matchCase.Pattern, subject);
                CompilerAst.Expression condition = pattern.Condition;
                if (matchCase.Guard is not null) {
                    condition = And(condition, LowerExpression(matchCase.Guard));
                }
                var body = new System.Collections.Generic.List<CompilerAst.Statement>();
                body.AddRange(pattern.Assignments);
                body.AddRange(matchCase.Body.Select(LowerStatement));
                tests.Add(new CompilerAst.IfStatementTest(
                    condition,
                    new CompilerAst.SuiteStatement(body.ToArray())));
            }
            statements.Add(new CompilerAst.IfStatement(
                tests.ToArray(),
                new CompilerAst.EmptyStatement()));
            return new CompilerAst.SuiteStatement(statements.ToArray());
        }

        private static void ValidatePatternBindings(AstNodes.Pattern pattern) {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            CollectPatternBindings(pattern, seen);
        }

        private static void CollectPatternBindings(
            AstNodes.Pattern pattern,
            System.Collections.Generic.HashSet<string> seen) {
            switch (pattern) {
                case AstNodes.MatchAs asPattern:
                    AddPatternBinding(asPattern.Name, seen);
                    if (asPattern.Pattern != null) {
                        CollectPatternBindings(asPattern.Pattern, seen);
                    }
                    break;
                case AstNodes.MatchStar star:
                    AddPatternBinding(star.Name, seen);
                    break;
                case AstNodes.MatchSequence sequence:
                    foreach (AstNodes.Pattern item in sequence.Patterns) {
                        CollectPatternBindings(item, seen);
                    }
                    break;
                case AstNodes.MatchMapping mapping:
                    AddPatternBinding(mapping.Rest, seen);
                    var literalKeys = new System.Collections.Generic.HashSet<object?>();
                    foreach (AstNodes.ExprBase key in mapping.Keys) {
                        if (key is AstNodes.Constant constant
                            && !literalKeys.Add(constant.Value)) {
                            throw PythonOps.SyntaxError(
                                "mapping pattern checks duplicate key ({0})",
                                constant.Value?.ToString() ?? "None");
                        }
                    }
                    foreach (AstNodes.Pattern item in mapping.Patterns) {
                        CollectPatternBindings(item, seen);
                    }
                    break;
                case AstNodes.MatchClass classPattern:
                    foreach (AstNodes.Pattern item in classPattern.Patterns) {
                        CollectPatternBindings(item, seen);
                    }
                    foreach (AstNodes.Pattern item in classPattern.KwdPatterns) {
                        CollectPatternBindings(item, seen);
                    }
                    break;
                case AstNodes.MatchOr orPattern:
                    System.Collections.Generic.HashSet<string>? alternativeBindings = null;
                    foreach (AstNodes.Pattern alternative in orPattern.Patterns) {
                        var bindings = new System.Collections.Generic.HashSet<string>(
                            StringComparer.Ordinal);
                        CollectPatternBindings(alternative, bindings);
                        if (alternativeBindings == null) {
                            alternativeBindings = bindings;
                        } else if (!alternativeBindings.SetEquals(bindings)) {
                            throw PythonOps.SyntaxError(
                                "alternative patterns bind different names");
                        }
                    }
                    if (alternativeBindings != null) {
                        foreach (string name in alternativeBindings) {
                            AddPatternBinding(name, seen);
                        }
                    }
                    break;
            }
        }

        private static void AddPatternBinding(
            string? name,
            System.Collections.Generic.HashSet<string> seen) {
            if (string.IsNullOrEmpty(name) || string.Equals(name, "_", StringComparison.Ordinal)) {
                return;
            }
            if (!seen.Add(name)) {
                throw PythonOps.SyntaxError(
                    "multiple assignments to name '{0}' in pattern",
                    name);
            }
        }

        private static PatternResult LowerPattern(
            AstNodes.Pattern pattern,
            CompilerAst.Expression subject) {
            switch (pattern) {
                case AstNodes.MatchAs matchAs: {
                    PatternResult nested = matchAs.Pattern is null
                        ? PatternResult.Always
                        : LowerPattern(matchAs.Pattern, subject);
                    var assignments = new System.Collections.Generic.List<CompilerAst.Statement>(
                        nested.Assignments);
                    if (matchAs.Name is not null) {
                        nested = new PatternResult(
                            And(
                                nested.Condition,
                                new CompilerAst.NamedExpression(
                                    new CompilerAst.NameExpression(matchAs.Name),
                                    subject)),
                            assignments);
                    }
                    return new PatternResult(nested.Condition, assignments);
                }
                case AstNodes.MatchSingleton singleton:
                    return new PatternResult(
                        new CompilerAst.BinaryExpression(
                            PythonOperator.Is,
                            subject,
                            new CompilerAst.ConstantExpression(singleton.Value)),
                        Array.Empty<CompilerAst.Statement>());
                case AstNodes.MatchValue value:
                    return new PatternResult(
                        new CompilerAst.BinaryExpression(
                            PythonOperator.Equal,
                            subject,
                            LowerExpression(value.Value)),
                        Array.Empty<CompilerAst.Statement>());
                case AstNodes.MatchClass matchClass: {
                    var assignments = new System.Collections.Generic.List<CompilerAst.Statement>();
                    CompilerAst.Expression matchType = NewMatchTemp(
                        LowerExpression(matchClass.Cls),
                        assignments);
                    CompilerAst.Expression condition = new CompilerAst.CallExpression(
                        new CompilerAst.NameExpression("isinstance"),
                        new CompilerAst.Expression[] {
                            subject,
                            matchType
                        },
                        null);
                    if (matchClass.Patterns.Count != 0) {
                        CompilerAst.Expression matchArgs = NewMatchTemp(
                            Call("getattr", matchType, Constant("__match_args__")),
                            assignments);
                        condition = And(
                            condition,
                            new CompilerAst.BinaryExpression(
                                PythonOperator.GreaterThanOrEqual,
                                Call("len", matchArgs),
                                new CompilerAst.ConstantExpression(matchClass.Patterns.Count)));
                        for (int i = 0; i < matchClass.Patterns.Count; i++) {
                            CompilerAst.Expression attributeName = NewMatchTemp(
                                new CompilerAst.IndexExpression(
                                    matchArgs,
                                    new CompilerAst.ConstantExpression(i)),
                                assignments);
                            CompilerAst.Expression attribute = NewMatchTemp(
                                Call("getattr", subject, attributeName),
                                assignments);
                            PatternResult nested = LowerPattern(
                                matchClass.Patterns[i],
                                attribute);
                            assignments.AddRange(nested.Assignments);
                            condition = And(condition, nested.Condition);
                        }
                    }
                    for (int i = 0; i < matchClass.KwdPatterns.Count; i++) {
                        CompilerAst.Expression attribute = NewMatchTemp(
                            new CompilerAst.MemberExpression(
                                subject,
                                matchClass.KwdAttrs[i]),
                            assignments);
                        PatternResult nested = LowerPattern(
                            matchClass.KwdPatterns[i],
                            attribute);
                        assignments.AddRange(nested.Assignments);
                        condition = And(condition, nested.Condition);
                    }
                    return new PatternResult(condition, assignments);
                }
                case AstNodes.MatchSequence sequence:
                    return LowerSequencePattern(sequence, subject);
                case AstNodes.MatchMapping mapping:
                    return LowerMappingPattern(mapping, subject);
                case AstNodes.MatchOr alternate: {
                    CompilerAst.Expression condition = Constant(false);
                    foreach (AstNodes.Pattern alternative in alternate.Patterns) {
                        PatternResult nested = LowerPattern(alternative, subject);
                        if (nested.Assignments.Count != 0) {
                            throw new NotSupportedException(
                                "Captures in or-patterns require branch-local assignment lowering.");
                        }
                        condition = Or(condition, nested.Condition);
                    }
                    return new PatternResult(condition, Array.Empty<CompilerAst.Statement>());
                }
                default:
                    throw new NotSupportedException(
                        $"Match pattern '{pattern.NodeKind}' has no compiler lowering.");
            }
        }

        private static PatternResult LowerSequencePattern(
            AstNodes.MatchSequence sequence,
            CompilerAst.Expression subject) {
            var assignments = new System.Collections.Generic.List<CompilerAst.Statement>();
            CompilerAst.Expression length = NewMatchTemp(
                Call("len", subject),
                assignments);
            bool hasStar = sequence.Patterns.Any(item => item is AstNodes.MatchStar);
            if (!hasStar) {
                CompilerAst.Expression condition = And(
                    IsPatternSequence(subject),
                    new CompilerAst.BinaryExpression(
                        PythonOperator.Equal,
                        length,
                        new CompilerAst.ConstantExpression(sequence.Patterns.Count)));
                for (int i = 0; i < sequence.Patterns.Count; i++) {
                    CompilerAst.Expression element = NewMatchTemp(
                        new CompilerAst.IndexExpression(
                            subject,
                            new CompilerAst.ConstantExpression(i)),
                        assignments);
                    PatternResult nested = LowerPattern(sequence.Patterns[i], element);
                    assignments.AddRange(nested.Assignments);
                    condition = And(condition, nested.Condition);
                }
                return new PatternResult(condition, assignments);
            }

            int starIndex = sequence.Patterns
                .Select((pattern, index) => (pattern, index))
                .First(item => item.pattern is AstNodes.MatchStar)
                .index;
            int suffixCount = sequence.Patterns.Count - starIndex - 1;
            CompilerAst.Expression conditionWithStar = And(
                IsPatternSequence(subject),
                new CompilerAst.BinaryExpression(
                    PythonOperator.GreaterThanOrEqual,
                    length,
                    new CompilerAst.ConstantExpression(sequence.Patterns.Count - 1)));
            for (int i = 0; i < sequence.Patterns.Count; i++) {
                if (i == starIndex) continue;
                CompilerAst.Expression element = NewMatchTemp(
                    new CompilerAst.IndexExpression(
                        subject,
                        i < starIndex
                            ? (CompilerAst.Expression)new CompilerAst.ConstantExpression(i)
                            : new CompilerAst.BinaryExpression(
                                PythonOperator.Subtract,
                                length,
                                new CompilerAst.ConstantExpression(suffixCount - (i - starIndex - 1)))),
                    assignments);
                PatternResult nested = LowerPattern(sequence.Patterns[i], element);
                assignments.AddRange(nested.Assignments);
                conditionWithStar = And(conditionWithStar, nested.Condition);
            }
            if (sequence.Patterns[starIndex] is AstNodes.MatchStar { Name: not null } star) {
                assignments.Add(new CompilerAst.AssignmentStatement(
                    new CompilerAst.Expression[] { new CompilerAst.NameExpression(star.Name) },
                    new CompilerAst.IndexExpression(
                        subject,
                        new CompilerAst.SliceExpression(
                            new CompilerAst.ConstantExpression(starIndex),
                            new CompilerAst.BinaryExpression(
                                PythonOperator.Subtract,
                                length,
                                new CompilerAst.ConstantExpression(suffixCount)),
                            null))));
            }
            return new PatternResult(conditionWithStar, assignments);
        }

        private static PatternResult LowerMappingPattern(
            AstNodes.MatchMapping mapping,
            CompilerAst.Expression subject) {
            var assignments = new System.Collections.Generic.List<CompilerAst.Statement>();
            CompilerAst.Expression condition = IsPatternMapping(subject);
            for (int i = 0; i < mapping.Patterns.Count; i++) {
                CompilerAst.Expression key = LowerExpression(mapping.Keys[i]);
                condition = And(
                    condition,
                    new CompilerAst.BinaryExpression(
                        PythonOperator.In,
                        key,
                        subject));
                CompilerAst.Expression value = NewMatchTemp(
                    new CompilerAst.IndexExpression(
                        subject,
                        key),
                    assignments);
                PatternResult nested = LowerPattern(mapping.Patterns[i], value);
                assignments.AddRange(nested.Assignments);
                condition = And(condition, nested.Condition);
            }
            if (mapping.Rest is not null) {
                var rest = new CompilerAst.NameExpression(mapping.Rest);
                assignments.Add(new CompilerAst.AssignmentStatement(
                    new CompilerAst.Expression[] { rest },
                    Call("dict", subject)));
                foreach (AstNodes.ExprBase key in mapping.Keys) {
                    assignments.Add(new CompilerAst.DelStatement(
                        new CompilerAst.Expression[] {
                            new CompilerAst.IndexExpression(rest, LowerExpression(key))
                        }));
                }
            }
            return new PatternResult(condition, assignments);
        }

        private static CompilerAst.Expression IsPatternSequence(
            CompilerAst.Expression subject) {
            CompilerAst.Expression textTypes = new CompilerAst.TupleExpression(
                false,
                new CompilerAst.Expression[] {
                    new CompilerAst.NameExpression("str"),
                    new CompilerAst.NameExpression("bytes"),
                    new CompilerAst.NameExpression("bytearray")
                });
            return And(
                IsAbcInstance(subject, "Sequence"),
                new CompilerAst.UnaryExpression(
                    PythonOperator.Not,
                    Call("isinstance", subject, textTypes)));
        }

        private static CompilerAst.Expression IsPatternMapping(
            CompilerAst.Expression subject) {
            return IsAbcInstance(subject, "Mapping");
        }

        private static CompilerAst.Expression IsAbcInstance(
            CompilerAst.Expression subject,
            string abcType) {
            CompilerAst.Expression collections = Call(
                "__import__",
                Constant("collections.abc"));
            CompilerAst.Expression type = new CompilerAst.MemberExpression(
                new CompilerAst.MemberExpression(collections, "abc"),
                abcType);
            return Call("isinstance", subject, type);
        }

        private static CompilerAst.Expression NewMatchTemp(
            CompilerAst.Expression value,
            System.Collections.Generic.IList<CompilerAst.Statement> statements) {
            string name = "__match_tmp_" + System.Threading.Interlocked.Increment(ref _matchTemp);
            var target = new CompilerAst.NameExpression(name) {
                IndexSpan = value.IndexSpan
            };
            return new CompilerAst.NamedExpression(target, value) {
                IndexSpan = value.IndexSpan
            };
        }

        private static CompilerAst.Expression Call(
            string name,
            params CompilerAst.Expression[] arguments) {
            return new CompilerAst.CallExpression(
                new CompilerAst.NameExpression(name),
                arguments,
                null);
        }

        private static CompilerAst.Expression Constant(object? value) {
            return new CompilerAst.ConstantExpression(value);
        }

        private static CompilerAst.Expression And(
            CompilerAst.Expression left,
            CompilerAst.Expression right) {
            return left is CompilerAst.ConstantExpression { Value: true }
                ? right
                : new CompilerAst.AndExpression(left, right);
        }

        private static CompilerAst.Expression Or(
            CompilerAst.Expression left,
            CompilerAst.Expression right) {
            return left is CompilerAst.ConstantExpression { Value: false }
                ? right
                : new CompilerAst.OrExpression(left, right);
        }

        private static CompilerAst.Statement LowerWith(
            System.Collections.Generic.IReadOnlyList<AstNodes.Withitem> items,
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> body,
            bool isAsync) {
            CompilerAst.Statement result = LowerSuite(body);
            for (int i = items.Count - 1; i >= 0; i--) {
                AstNodes.Withitem item = items[i];
                result = isAsync
                    ? new CompilerAst.AsyncWithStatement(
                        LowerExpression(item.ContextExpr),
                        item.OptionalVars is null ? null : LowerExpression(item.OptionalVars),
                        result)
                    : new CompilerAst.WithStatement(
                        LowerExpression(item.ContextExpr),
                        item.OptionalVars is null ? null! : LowerExpression(item.OptionalVars),
                        result);
            }
            return result;
        }

        private static CompilerAst.TryStatement LowerTry(
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> body,
            System.Collections.Generic.IReadOnlyList<AstNodes.ExceptHandlerBase> handlers,
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> orelse,
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> finalbody) {
            return new CompilerAst.TryStatement(
                LowerSuite(body),
                handlers.Select(handler => {
                    AstNodes.ExceptHandler typedHandler = (AstNodes.ExceptHandler)handler;
                    var loweredHandler = new CompilerAst.TryStatementHandler(
                        typedHandler.Type is null ? null! : LowerExpression(typedHandler.Type),
                        typedHandler.Name is null
                            ? null!
                            : new CompilerAst.NameExpression(typedHandler.Name),
                        LowerSuite(typedHandler.Body)) {
                        HeaderIndex = GetStartOffset(typedHandler)
                    };
                    SetLocation(loweredHandler, typedHandler);
                    return loweredHandler;
                }).ToArray(),
                orelse.Count == 0 ? null : LowerSuite(orelse),
                finalbody.Count == 0 ? null : LowerSuite(finalbody));
        }

        private static CompilerAst.FunctionDefinition LowerFunction(
            AstNodes.FunctionDef function) {
            return LowerFunction(
                function.Name,
                function.Args,
                function.Body,
                function.DecoratorList,
                function.Returns,
                false,
                function);
        }

        private static CompilerAst.FunctionDefinition LowerFunction(
            AstNodes.AsyncFunctionDef function) {
            return LowerFunction(
                function.Name,
                function.Args,
                function.Body,
                function.DecoratorList,
                function.Returns,
                true,
                function);
        }

        private static CompilerAst.FunctionDefinition LowerFunction(
            string name,
            AstNodes.Arguments arguments,
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> body,
            System.Collections.Generic.IReadOnlyList<AstNodes.ExprBase> decorators,
            AstNodes.ExprBase? returns,
            bool isAsync,
            AstNodes.PythonAstNode source) {
            string loweredName = MangleIdentifier(name);
            var function = new CompilerAst.FunctionDefinition(
                loweredName,
                LowerParameters(arguments, name),
                LowerSuite(body),
                isAsync) {
                HeaderIndex = GetStartOffset(source)
            };
            function.IsGenerator = ContainsYield(body);
            function.GeneratorStop = function.IsGenerator;
            function.Decorators = decorators.Select(LowerExpression).ToArray();
            function.ReturnAnnotation = returns is null
                ? null!
                : LowerExpression(returns);
            return function;
        }

        private static CompilerAst.Parameter[] LowerParameters(
            AstNodes.Arguments arguments,
            string? functionName) {
            var parameters = new System.Collections.Generic.List<CompilerAst.Parameter>();
            AstNodes.Arg[] positionalOnly = arguments.Posonlyargs.ToArray();
            AstNodes.Arg[] positional = positionalOnly
                .Concat(arguments.Args)
                .ToArray();
            parameters.AddRange(positionalOnly.Select(argument => LowerParameter(
                argument,
                CompilerAst.ParameterKind.PositionalOnly)));
            parameters.AddRange(arguments.Args.Select(argument => LowerParameter(argument)));
            int firstDefault = positional.Length - arguments.Defaults.Count;
            if (firstDefault < 0) {
                throw new InvalidOperationException(
                    $"Function '{functionName}' has {arguments.Defaults.Count} defaults for " +
                    $"{positional.Length} positional parameters.");
            }
            for (int i = 0; i < arguments.Defaults.Count; i++) {
                parameters[firstDefault + i].DefaultValue =
                    LowerExpression(arguments.Defaults[i]);
            }
            for (int i = 0; i < arguments.Kwonlyargs.Count; i++) {
                CompilerAst.Parameter parameter = LowerParameter(
                    arguments.Kwonlyargs[i],
                    CompilerAst.ParameterKind.KeywordOnly);
                if (i < arguments.KwDefaults.Count && arguments.KwDefaults[i] is not null) {
                    parameter.DefaultValue = LowerExpression(arguments.KwDefaults[i]!);
                }
                parameters.Add(parameter);
            }
            if (arguments.Vararg is not null) {
                parameters.Add(LowerParameter(
                    arguments.Vararg,
                    CompilerAst.ParameterKind.List));
            }
            if (arguments.Kwarg is not null) {
                parameters.Add(LowerParameter(
                    arguments.Kwarg,
                    CompilerAst.ParameterKind.Dictionary));
            }
            return parameters.ToArray();
        }

        private static CompilerAst.LambdaExpression LowerLambda(AstNodes.Lambda lambda) {
            CompilerAst.Expression body = LowerExpression(lambda.Body);
            var function = new CompilerAst.FunctionDefinition(
                null,
                LowerParameters(lambda.Args, null),
                new CompilerAst.ReturnStatement(body));
            function.IsGenerator = ContainsYield(lambda.Body);
            function.GeneratorStop = function.IsGenerator;
            return new CompilerAst.LambdaExpression(function);
        }

        private static CompilerAst.Parameter LowerParameter(
            AstNodes.Arg argument,
            CompilerAst.ParameterKind kind = CompilerAst.ParameterKind.Normal) {
            return new CompilerAst.Parameter(argument.ArgName, kind) {
                Annotation = argument.Annotation is null
                    ? null!
                    : LowerExpression(argument.Annotation)
            };
        }

        private static CompilerAst.ClassDefinition LowerClass(AstNodes.ClassDef @class) {
            CompilerAst.Expression[] bases = @class.Bases.Select(LowerExpression).ToArray();
            CompilerAst.Keyword[] keywords = @class.Keywords.Select(keyword => new CompilerAst.Keyword(
                keyword.ArgName,
                LowerExpression(keyword.Value))).ToArray();
            CompilerAst.Expression[] decorators = @class.DecoratorList.Select(LowerExpression).ToArray();

            string? previousClass = _className;
            _className = @class.Name;
            CompilerAst.SuiteStatement body;
            try {
                body = LowerSuite(@class.Body);
            } finally {
                _className = previousClass;
            }

            var definition = new CompilerAst.ClassDefinition(
                MangleIdentifier(@class.Name),
                bases,
                keywords,
                body) {
                HeaderIndex = GetStartOffset(@class)
            };
            definition.Decorators = decorators;
            return definition;
        }

        private static string MangleIdentifier(string name) {
            if (_className is null ||
                name.Length < 2 ||
                name[0] != '_' ||
                name[1] != '_' ||
                name.EndsWith("__", StringComparison.Ordinal)) {
                return name;
            }

            string className = _className.TrimStart('_');
            return className.Length == 0 ? name : "_" + className + name;
        }

        private static CompilerAst.SuiteStatement LowerSuite(
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> statements) {
            return new CompilerAst.SuiteStatement(
                statements.Select(LowerStatement).ToArray());
        }

        private static CompilerAst.Statement LowerOptionalSuite(
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> statements) {
            return statements.Count == 0
                ? new CompilerAst.EmptyStatement()
                : LowerSuite(statements);
        }

        private static bool ContainsYield(
            System.Collections.Generic.IReadOnlyList<AstNodes.Stmt> body) {
            var finder = new YieldFinder();
            foreach (AstNodes.Stmt statement in body) finder.Visit(statement);
            return finder.Found;
        }

        private static bool ContainsYield(AstNodes.ExprBase expression) {
            var finder = new YieldFinder();
            finder.Visit(expression);
            return finder.Found;
        }

        private sealed class YieldFinder : AstNodes.PythonAstWalker {
            public bool Found { get; private set; }

            protected override void VisitNode(AstNodes.PythonAstNode node) {
                if (node is AstNodes.Yield or AstNodes.YieldFrom) {
                    Found = true;
                    return;
                }
                if (node is AstNodes.FunctionDef
                    or AstNodes.AsyncFunctionDef
                    or AstNodes.ClassDef
                    or AstNodes.Lambda) {
                    return;
                }
                base.VisitNode(node);
            }
        }

        private static CompilerAst.Expression LowerBoolean(AstNodes.BoolOp boolean) {
            CompilerAst.Expression result = LowerExpression(boolean.Values[0]);
            for (int i = 1; i < boolean.Values.Count; i++) {
                CompilerAst.Expression right = LowerExpression(boolean.Values[i]);
                result = boolean.Op switch {
                    AstNodes.And => new CompilerAst.AndExpression(result, right),
                    AstNodes.Or => new CompilerAst.OrExpression(result, right),
                    _ => throw Unsupported(boolean.Op)
                };
            }
            return result;
        }

        private static CompilerAst.Expression LowerComparison(AstNodes.Compare comparison) {
            CompilerAst.Expression result = LowerComparisonOperand(
                comparison.Comparators[comparison.Comparators.Count - 1]);
            for (int i = comparison.Ops.Count - 1; i >= 0; i--) {
                CompilerAst.Expression left = i == 0
                    ? LowerComparisonOperand(comparison.Left)
                    : LowerComparisonOperand(comparison.Comparators[i - 1]);
                result = new CompilerAst.BinaryExpression(
                    LowerCompareOperator(comparison.Ops[i]),
                    left,
                    result);
            }
            return result;
        }

        private static CompilerAst.Expression LowerComparisonOperand(
            AstNodes.ExprBase expression) {
            CompilerAst.Expression lowered = LowerExpression(expression);
            return expression is AstNodes.Compare
                ? new CompilerAst.ParenthesisExpression(lowered)
                : lowered;
        }

        private static CompilerAst.ComprehensionIterator[] LowerComprehensionIterators(
            System.Collections.Generic.IReadOnlyList<AstNodes.Comprehension> generators) {
            var iterators = new System.Collections.Generic.List<CompilerAst.ComprehensionIterator>();
            foreach (AstNodes.Comprehension generator in generators) {
                if (generator.IsAsync != 0) {
                    throw new NotSupportedException(
                        "Async comprehensions require async comprehension lowering.");
                }
                iterators.Add(new CompilerAst.ComprehensionFor(
                    LowerExpression(generator.Target),
                    LowerExpression(generator.Iter)));
                iterators.AddRange(generator.Ifs.Select(
                    condition => (CompilerAst.ComprehensionIterator)new CompilerAst.ComprehensionIf(
                        LowerExpression(condition))));
            }
            return iterators.ToArray();
        }

        private static CompilerAst.Expression LowerStarredComprehension(
            AstNodes.ExprBase element,
            System.Collections.Generic.IReadOnlyList<AstNodes.Comprehension> generators,
            string factoryName) {
            if (generators.Count == 0) {
                throw new InvalidOperationException("Comprehension has no generators.");
            }
            if (generators.Any(item => item.IsAsync != 0)) {
                throw new NotSupportedException(
                    "Async starred comprehensions require async lowering.");
            }

            AstNodes.Comprehension first = generators[0];
            var root = new CompilerAst.ForStatement(
                LowerExpression(first.Target),
                new CompilerAst.NameExpression("__starred_comp_iterable"),
                null!,
                null!);
            CompilerAst.Statement current = root;
            for (int i = 0; i < generators.Count; i++) {
                AstNodes.Comprehension generator = generators[i];
                if (i > 0) {
                    var loop = new CompilerAst.ForStatement(
                        LowerExpression(generator.Target),
                        LowerExpression(generator.Iter),
                        null!,
                        null!);
                    SetGeneratorBody(current, loop);
                    current = loop;
                }
                foreach (AstNodes.ExprBase condition in generator.Ifs) {
                    var test = new CompilerAst.IfStatement(
                        new[] {
                            new CompilerAst.IfStatementTest(
                                LowerExpression(condition),
                                null!)
                        },
                        null!);
                    SetGeneratorBody(current, test);
                    current = test;
                }
            }

            var item = new CompilerAst.NameExpression("__starred_comp_item");
            SetGeneratorBody(
                current,
                new CompilerAst.ForStatement(
                    item,
                    LowerExpression(element),
                    new CompilerAst.ExpressionStatement(
                        new CompilerAst.YieldExpression(item)),
                    null!));

            var parameter = new CompilerAst.Parameter("__starred_comp_iterable");
            var function = new CompilerAst.FunctionDefinition(
                "<starred comp genexpr>",
                new[] { parameter },
                root) {
                IsGenerator = true,
                IsGeneratorExpression = true,
                GeneratorStop = true
            };
            return new CompilerAst.CallExpression(
                new CompilerAst.NameExpression(factoryName),
                new CompilerAst.Expression[] {
                    new CompilerAst.GeneratorExpression(
                        function,
                        LowerExpression(first.Iter))
                },
                null);
        }

        private static CompilerAst.GeneratorExpression LowerGeneratorExpression(
            AstNodes.GeneratorExp generator) {
            if (generator.Generators.Count == 0) {
                throw new InvalidOperationException("Generator expression has no generators.");
            }
            if (generator.Generators.Any(item => item.IsAsync != 0)) {
                throw new NotSupportedException(
                    "Async generator expressions require async comprehension lowering.");
            }

            AstNodes.Comprehension first = generator.Generators[0];
            var root = new CompilerAst.ForStatement(
                LowerExpression(first.Target),
                new CompilerAst.NameExpression("__gen_$_parm__"),
                null!,
                null!);
            CompilerAst.Statement current = root;

            void AddIterators(AstNodes.Comprehension comprehension, bool firstGenerator) {
                if (!firstGenerator) {
                    var loop = new CompilerAst.ForStatement(
                        LowerExpression(comprehension.Target),
                        LowerExpression(comprehension.Iter),
                        null!,
                        null!);
                    SetGeneratorBody(current, loop);
                    current = loop;
                }
                foreach (AstNodes.ExprBase condition in comprehension.Ifs) {
                    var test = new CompilerAst.IfStatement(
                        new[] {
                            new CompilerAst.IfStatementTest(
                                LowerExpression(condition),
                                null!)
                        },
                        null!);
                    SetGeneratorBody(current, test);
                    current = test;
                }
            }

            AddIterators(first, true);
            for (int i = 1; i < generator.Generators.Count; i++) {
                AddIterators(generator.Generators[i], false);
            }
            SetGeneratorBody(
                current,
                new CompilerAst.ExpressionStatement(
                    new CompilerAst.YieldExpression(
                        LowerExpression(generator.Elt))));

            var parameter = new CompilerAst.Parameter("__gen_$_parm__");
            var function = new CompilerAst.FunctionDefinition(
                "<genexpr>",
                new[] { parameter },
                root);
            function.IsGenerator = true;
            function.IsGeneratorExpression = true;
            function.GeneratorStop = true;
            function.HeaderIndex = root.StartIndex;
            return new CompilerAst.GeneratorExpression(
                function,
                LowerExpression(first.Iter));
        }

        private static void SetGeneratorBody(
            CompilerAst.Statement current,
            CompilerAst.Statement body) {
            switch (current) {
                case CompilerAst.ForStatement loop:
                    loop.Body = body;
                    break;
                case CompilerAst.IfStatement conditional:
                    conditional.Tests[0].Body = body;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected generator-expression statement '{current.GetType().Name}'.");
            }
        }

        private static PythonOperator LowerUnaryOperator(AstNodes.UnaryOpKind @operator) {
            return @operator switch {
                AstNodes.UAdd => PythonOperator.Pos,
                AstNodes.USub => PythonOperator.Negate,
                AstNodes.Invert => PythonOperator.Invert,
                AstNodes.Not => PythonOperator.Not,
                _ => throw Unsupported(@operator)
            };
        }

        private static PythonOperator LowerCompareOperator(AstNodes.CompareOpKind @operator) {
            return @operator switch {
                AstNodes.Eq => PythonOperator.Equal,
                AstNodes.NotEq => PythonOperator.NotEqual,
                AstNodes.Lt => PythonOperator.LessThan,
                AstNodes.LtE => PythonOperator.LessThanOrEqual,
                AstNodes.Gt => PythonOperator.GreaterThan,
                AstNodes.GtE => PythonOperator.GreaterThanOrEqual,
                AstNodes.Is => PythonOperator.Is,
                AstNodes.IsNot => PythonOperator.IsNot,
                AstNodes.In => PythonOperator.In,
                AstNodes.NotIn => PythonOperator.NotIn,
                _ => throw Unsupported(@operator)
            };
        }

        private static void SetLocation(
            CompilerAst.Node lowered,
            AstNodes.PythonAstNode source) {
            SourceLocationMap? locations = _sourceLocations;
            if (locations is null) return;

            int start = locations.ToOffset(source.LineNumber, source.ColumnOffset);
            int end = locations.ToOffset(
                source.EndLineNumber ?? source.LineNumber,
                source.EndColumnOffset ?? source.ColumnOffset);
            if (end < start) end = start;
            lowered.IndexSpan = new Microsoft.Scripting.IndexSpan(start, end - start);
        }

        private static int GetStartOffset(AstNodes.PythonAstNode source) {
            return _sourceLocations?.ToOffset(source.LineNumber, source.ColumnOffset) ?? 0;
        }

        private static NotSupportedException Unsupported(AstNodes.PythonAstNode node) {
            return new NotSupportedException(
                $"Public AST node '{node.NodeKind}' has no compiler-AST lowering yet.");
        }

        private sealed class SourceLocationMap {
            private readonly int[] _lineStarts;

            private SourceLocationMap(int[] lineStarts) {
                _lineStarts = lineStarts;
                LineStarts = lineStarts.Skip(1).ToArray();
            }

            public int[] LineStarts { get; }

            public static SourceLocationMap Create(string? source) {
                source ??= string.Empty;
                var starts = new System.Collections.Generic.List<int> { 0 };
                for (int i = 0; i < source.Length; i++) {
                    if (source[i] == '\n') starts.Add(i + 1);
                }
                return new SourceLocationMap(starts.ToArray());
            }

            public int ToOffset(int? lineNumber, int? columnOffset) {
                int line = Math.Clamp((lineNumber ?? 1) - 1, 0, _lineStarts.Length - 1);
                return _lineStarts[line] + Math.Max(columnOffset ?? 0, 0);
            }
        }

        private sealed class PatternResult {
            public static readonly PatternResult Always = new PatternResult(
                Constant(true),
                Array.Empty<CompilerAst.Statement>());

            public PatternResult(
                CompilerAst.Expression condition,
                System.Collections.Generic.IReadOnlyList<CompilerAst.Statement> assignments) {
                Condition = condition;
                Assignments = assignments;
            }

            public CompilerAst.Expression Condition { get; }
            public System.Collections.Generic.IReadOnlyList<CompilerAst.Statement> Assignments { get; }
        }
    }
}
