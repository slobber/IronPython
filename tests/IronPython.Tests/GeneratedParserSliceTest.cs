using IronPython.Compiler.Generated;
using IronPython.Compiler;
using IronPython.Compiler.PublicAst;
using IronPython.Compiler.Syntax;
using IronPython.Hosting;
using IronPython.ParserGenerator;
using IronPython.Runtime;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PublicAst = IronPython.Compiler.Syntax.Ast;

namespace IronPython.Tests {
    [TestFixture]
    [NonParallelizable]
    public sealed class GeneratedParserSliceTest {
        [Test]
        public void ParsesAssignmentFromGeneratedGrammar() {
            var result = MiniGrammarParser.ParseAssignment("answer = 42");

            Assert.That(result.Name, Is.EqualTo("answer"));
            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void ValidatesCpython315Grammar() {
            Assert.That(PythonGrammarBaseline.Validated, Is.True);
            Assert.That(PythonGrammarBaseline.RuleCount, Is.EqualTo(278));
            Assert.That(PythonGrammarBaseline.StartRules, Does.Contain("file"));
            Assert.That(PythonGrammarBaseline.StartRules, Does.Contain("interactive"));
            Assert.That(PythonGrammarBaseline.StartRules, Does.Contain("eval"));
            Assert.That(PythonGrammarBaseline.StartRules, Does.Contain("func_type"));
            Assert.That(PythonGrammarBaseline.InvalidRuleCount, Is.EqualTo(69));
            Assert.That(PythonGrammarBaseline.VersionCheckCount, Is.EqualTo(25));
            Assert.That(
                PythonGrammarBaseline.InvalidRules.All(
                    rule => rule.StartsWith("invalid_", System.StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                PythonGrammarDescriptors.Rules.Count(rule => rule.IsInvalid),
                Is.EqualTo(69));
        }

        [Test]
        public void EmitsDescriptorsForEveryGrammarRule() {
            Assert.That(PythonGrammarDescriptors.Rules, Has.Length.EqualTo(278));
            Assert.That(
                PythonGrammarDescriptors.Rules
                    .SelectMany(rule => rule.Alternatives)
                    .Select(alternative => alternative.ActionId)
                    .Distinct()
                    .Count(),
                Is.EqualTo(
                    PythonGrammarDescriptors.Rules
                        .SelectMany(rule => rule.Alternatives)
                        .Count()));
            Assert.That(PythonGrammarActionCatalog.ActionIds, Is.Not.Empty);
            Assert.That(
                PythonGrammarActionCatalog.ActionIds.Distinct().Count(),
                Is.EqualTo(PythonGrammarActionCatalog.ActionIds.Length));
        }

        [Test]
        public void MarksDirectLeftRecursiveRulesForMemoization() {
            PegRuleDescriptor tPrimary = PythonGrammarDescriptors.Rules.Single(rule => rule.Name == "t_primary");
            PegRuleDescriptor atom = PythonGrammarDescriptors.Rules.Single(rule => rule.Name == "atom");

            Assert.That(tPrimary.Memo, Is.True);
            Assert.That(atom.Memo, Is.False);
        }

        [Test]
        public void DispatchesEveryGeneratedActionToATypedMethod() {
            var handler = new PythonGrammarActionHandler();

            foreach (string actionId in PythonGrammarActionCatalog.ActionIds) {
                object value = handler.Invoke(
                    new PegActionContext(actionId, 0, 0),
                    new PegActionArguments(Enumerable.Empty<PegActionArgument>()));

                Assert.That(value, Is.InstanceOf<PegActionArguments>(), actionId);
            }
        }

        [Test]
        public void GeneratedActionCanBeOverriddenWithTypedDispatch() {
            var handler = new RecordingGeneratedActionHandler();
            object value = handler.Invoke(
                new PegActionContext("file.0", 0, 3),
                new PegActionArguments(new[] {
                    new PegActionArgument("a", "statements")
                }));

            Assert.That(value, Is.EqualTo("file.0"));
            Assert.That(handler.InvocationCount, Is.EqualTo(1));
        }

        [Test]
        public void ActionArgumentsPreserveGrammarFieldNames() {
            var arguments = new PegActionArguments(new[] {
                new PegActionArgument("left", 1),
                new PegActionArgument("right", 2),
                new PegActionArgument("left", 3)
            });

            Assert.That(arguments["left"], Is.EqualTo(1));
            Assert.That(arguments.GetValues("left"), Is.EqualTo(new object[] { 1, 3 }));
            Assert.That(arguments.TryGetValue("right", out object right), Is.True);
            Assert.That(right, Is.EqualTo(2));
        }

        [Test]
        public void PegRuntimeMatchesOrderedSequence() {
            var rules = new[] {
                new PegRuleDescriptor("start", false, new[] {
                    new PegAlternativeDescriptor("start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NAME"),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "="),
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "INTEGER")
                    }, false, true)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("NAME", "answer", 0, 6),
                new PegToken("=", "=", 7, 8),
                new PegToken("INTEGER", "42", 9, 11),
                new PegToken("ENDMARKER", "", 11, 11));

            var actions = new RecordingActionHandler();
            PegParseResult result = new PegParser(rules, tokens, actions).Parse("start");

            Assert.That(result.Success, Is.True);
            Assert.That(result.ActionId, Is.EqualTo("start.0"));
            Assert.That(tokens.Position, Is.EqualTo(3));
            Assert.That(actions.ActionIds, Is.EqualTo(new[] { "start.0" }));
        }

        [Test]
        public void ParsesMinimalPythonFileThroughFullGrammarDescriptors() {
            var tokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("NEWLINE", "\n", 1, 2),
                new PegToken("ENDMARKER", "", 2, 2));

            PegParseResult result = new PegParser(PythonGrammarDescriptors.Rules, tokens).Parse("file");

            Assert.That(result.Success, Is.True, result.ToString());
            Assert.That(result.Position, Is.EqualTo(3));
            Assert.That(tokens.Position, Is.EqualTo(3));
        }

        [Test]
        public void GeneratedFacadeSupportsModuleEvalAndInteractiveModes() {
            var moduleTokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("NEWLINE", "\n", 1, 2),
                new PegToken("ENDMARKER", "", 2, 2));
            var evalTokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("ENDMARKER", "", 1, 1));
            var interactiveTokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("NEWLINE", "\n", 1, 2),
                new PegToken("ENDMARKER", "", 2, 2));

            PegParseResult module = PythonGeneratedParser.ParseModule(moduleTokens);
            PegParseResult eval = PythonGeneratedParser.ParseEval(evalTokens);
            PegParseResult interactive = PythonGeneratedParser.ParseInteractive(interactiveTokens);

            Assert.That(module.Success, Is.True, module.ToString());
            Assert.That(eval.Success, Is.True, eval.ToString());
            Assert.That(interactive.Success, Is.True, interactive.ToString());
            Assert.That(PythonGeneratedParser.ModuleStartRule, Is.EqualTo("file"));
            Assert.That(PythonGeneratedParser.EvalStartRule, Is.EqualTo("eval"));
            Assert.That(PythonGeneratedParser.InteractiveStartRule, Is.EqualTo("interactive"));
        }

        [Test]
        public void ParsesAssignmentFromPython315SourceThroughGeneratedGrammar() {
            var tokenizer = new PythonTokenizer("x = 1 + 2\n");
            var actions = new PythonAstBuilder();
            var parser = new PegParser(PythonGrammarDescriptors.Rules, tokenizer, actions);

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(result.Success, Is.True, result.ToString());
            Assert.That(tokenizer.Tokens.Any(token => token.Kind == PythonTokenKind.EndMarker), Is.True);
            Assert.That(parser.LeftRecursionGrowthIterations, Is.LessThan(100));
            var module = result.Value as PublicAst.Module;
            Assert.That(module, Is.Not.Null);
            var assignment = module.Body.Single() as PublicAst.Assign;
            Assert.That(assignment, Is.Not.Null);
            var target = assignment.Targets.Single() as PublicAst.Name;
            Assert.That(target.Id, Is.EqualTo("x"));
            Assert.That(target.Ctx, Is.InstanceOf<PublicAst.Store>());
            var binOp = assignment.Value as PublicAst.BinOp;
            Assert.That(binOp, Is.Not.Null);
            Assert.That(binOp.Op, Is.InstanceOf<PublicAst.Add>());
            Assert.That(((PublicAst.Constant)binOp.Left).Value, Is.EqualTo(1));
            Assert.That(((PublicAst.Constant)binOp.Right).Value, Is.EqualTo(2));
            Assert.That(binOp.Left.LineNumber, Is.EqualTo(1));
            Assert.That(binOp.Left.ColumnOffset, Is.EqualTo(4));
            Assert.That(binOp.Left.EndColumnOffset, Is.EqualTo(5));
            Assert.That(binOp.Right.ColumnOffset, Is.EqualTo(8));
            Assert.That(binOp.Right.EndColumnOffset, Is.EqualTo(9));
            Assert.That(assignment.LineNumber, Is.EqualTo(1));
            Assert.That(assignment.ColumnOffset, Is.EqualTo(0));
            Assert.That(assignment.EndLineNumber, Is.EqualTo(1));
            Assert.That(assignment.EndColumnOffset, Is.EqualTo(9));

            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            PublicAstLowerer.ExecuteModule(module, pythonContext, out var globals);
            Assert.That(globals["x"], Is.EqualTo(3));
        }

        [Test]
        public void ExistingParserFacadeCanUseTheGeneratedParser() {
            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                "x = 1 + 2\n",
                "<facade-test>",
                SourceCodeKind.File);
            var compilerOptions = new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode);
            var sink = new RecordingParserSink();
            var compilerContext = new CompilerContext(
                sourceUnit,
                compilerOptions,
                ErrorSink.Default,
                sink);

            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                using Parser parser = Parser.CreateParser(compilerContext, new PythonOptions());
                var ast = parser.ParseFile(true);

                Assert.That(ast, Is.Not.Null);
                var suite = ast.Body as IronPython.Compiler.Ast.SuiteStatement;
                Assert.That(suite, Is.Not.Null);
                Assert.That(
                    suite.Statements.Single(),
                    Is.InstanceOf<IronPython.Compiler.Ast.AssignmentStatement>());
                Assert.That(sink.Names, Does.Contain("x"));
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void BuildsAndLowersImportFromTheGeneratedParser() {
            var tokenizer = new PythonTokenizer("import os\n");
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var module = (PublicAst.Module)result.Value;
            var import = module.Body.Single() as PublicAst.Import;
            Assert.That(import, Is.Not.Null);
            Assert.That(import.Names.Single().Name, Is.EqualTo("os"));

            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                string.Empty,
                "<import-lowering>",
                SourceCodeKind.File);
            var compilerContext = new CompilerContext(
                sourceUnit,
                new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode),
                ErrorSink.Default);
            var lowered = PublicAstLowerer.LowerModule(module, compilerContext);
            Assert.That(
                ((IronPython.Compiler.Ast.SuiteStatement)lowered.Body).Statements.Single(),
                Is.InstanceOf<IronPython.Compiler.Ast.ImportStatement>());
        }

        [Test]
        public void BuildsAndLowersFromImport() {
            var tokenizer = new PythonTokenizer("from os import path as p\n");
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var module = (PublicAst.Module)result.Value;
            var import = module.Body.Single() as PublicAst.ImportFrom;
            Assert.That(import, Is.Not.Null);
            Assert.That(import.Module, Is.EqualTo("os"));
            Assert.That(import.Names.Single().Name, Is.EqualTo("path"));
            Assert.That(import.Names.Single().Asname, Is.EqualTo("p"));
        }

        [Test]
        public void BuildsExpressionAndPassStatements() {
            var tokenizer = new PythonTokenizer("1 + 2\npass\n");
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var module = (PublicAst.Module)result.Value;
            Assert.That(module.Body[0], Is.InstanceOf<PublicAst.Expr>());
            Assert.That(module.Body[1], Is.InstanceOf<PublicAst.Pass>());
        }

        [Test]
        public void PreservesParenthesizedComparisonNesting() {
            const string source = "(value is sentinel) != (other is sentinel)\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var outer = (PublicAst.Compare)((PublicAst.Expr)((PublicAst.Module)result.Value).Body.Single()).Value;
            Assert.Multiple(() => {
                Assert.That(outer.Left, Is.InstanceOf<PublicAst.Compare>());
                Assert.That(outer.Ops, Has.Count.EqualTo(1));
                Assert.That(outer.Ops[0], Is.InstanceOf<PublicAst.NotEq>());
                Assert.That(outer.Comparators, Has.Count.EqualTo(1));
                Assert.That(outer.Comparators[0], Is.InstanceOf<PublicAst.Compare>());
            });
        }

        [Test]
        public void BuildsTypeAliasAndTypeParameters() {
            var tokenizer = new PythonTokenizer("type Pair[T] = int\n");
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var module = (PublicAst.Module)result.Value;
            var alias = module.Body.Single() as PublicAst.TypeAlias;
            Assert.That(alias, Is.Not.Null);
            Assert.That(((PublicAst.Name)alias.Name).Id, Is.EqualTo("Pair"));
            Assert.That(alias.TypeParams.Single(), Is.InstanceOf<PublicAst.TypeVar>());
            Assert.That(((PublicAst.Name)alias.Value).Id, Is.EqualTo("int"));
        }

        [Test]
        public void BuildsTemplateStringNodes() {
            var tokenizer = new PythonTokenizer("t\"hello {name}\"\n");
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var module = (PublicAst.Module)result.Value;
            var statement = (PublicAst.Expr)module.Body.Single();
            var template = statement.Value as PublicAst.TemplateStr;
            Assert.That(template, Is.Not.Null);
            Assert.That(template.Values[0], Is.InstanceOf<PublicAst.Constant>());
            Assert.That(template.Values[1], Is.InstanceOf<PublicAst.Interpolation>());
            Assert.That(
                ((PublicAst.Name)((PublicAst.Interpolation)template.Values[1]).Value).Id,
                Is.EqualTo("name"));
        }

        [Test]
        public void BuildsMatchStatementNodes() {
            var tokenizer = new PythonTokenizer(
                "match x:\n    case 1:\n        pass\n");
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var module = (PublicAst.Module)result.Value;
            var match = module.Body.Single() as PublicAst.Match;
            Assert.That(match, Is.Not.Null);
            Assert.That(((PublicAst.Name)match.Subject).Id, Is.EqualTo("x"));
            var matchValue = match.Cases.Single().Pattern as PublicAst.MatchValue;
            Assert.That(matchValue, Is.Not.Null);
            Assert.That(((PublicAst.Constant)matchValue.Value).Value, Is.EqualTo(1));
            Assert.That(match.Cases.Single().Body.Single(), Is.InstanceOf<PublicAst.Pass>());
        }

        [Test]
        public void BuildsPython315StdlibTraceback() {
            string stdlib = GetStdLibPath();
            string source = File.ReadAllText(Path.Combine(stdlib, "traceback.py"));
            string[] lines = File.ReadAllLines(Path.Combine(stdlib, "traceback.py"));
            string isolated = "class TracebackException:\n"
                + string.Join("\n", lines.Skip(1132).Take(107)) + "\n";
            var isolatedParser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(isolated),
                new PythonGrammarActionHandler());
            PegParseResult isolatedResult = isolatedParser.Parse(PythonGeneratedParser.ModuleStartRule);
            Assert.That(
                isolatedResult.Success,
                Is.True,
                isolatedParser.LastFailure != null
                    ? $"{isolatedParser.LastFailure.Message} Position={isolatedParser.LastFailure.Position}; " +
                      $"Rules={string.Join(" -> ", isolatedParser.LastFailure.RuleStack)}"
                    : isolatedResult.ToString());

            var syntaxParser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonGrammarActionHandler());
            PegParseResult syntax = syntaxParser.Parse(PythonGeneratedParser.ModuleStartRule);
            Assert.That(
                syntax.Success,
                Is.True,
                syntaxParser.LastFailure != null
                    ? $"{syntaxParser.LastFailure.Message} Position={syntaxParser.LastFailure.Position}; " +
                      $"Expected={string.Join(", ", syntaxParser.LastFailure.Expected)}; " +
                      $"Rules={string.Join(" -> ", syntaxParser.LastFailure.RuleStack)}"
                    : syntax.ToString());

            var tokenizer = new PythonTokenizer(source);
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null
                    ? $"{parser.LastFailure.Message} Position={parser.LastFailure.Position}; " +
                      $"Expected={string.Join(", ", parser.LastFailure.Expected)}; " +
                      $"Rules={string.Join(" -> ", parser.LastFailure.RuleStack)}"
                    : result.ToString());
            Assert.That(result.Value, Is.InstanceOf<PublicAst.Module>());
            var module = (PublicAst.Module)result.Value;
            Assert.That(module.Body, Is.Not.Empty);
            Assert.That(module.Body.OfType<PublicAst.FunctionDef>(), Is.Not.Empty);
            PublicAst.FunctionDef printException = module.Body
                .OfType<PublicAst.FunctionDef>()
                .Single(item => item.Name == "print_exception");
            Assert.Multiple(() => {
                Assert.That(printException.Args.Posonlyargs, Has.Count.EqualTo(1));
                Assert.That(printException.Args.Args, Has.Count.EqualTo(5));
                Assert.That(printException.Args.Defaults, Has.Count.EqualTo(5));
                Assert.That(printException.Args.Kwarg, Is.Not.Null);
            });
            PublicAst.ClassDef tracebackException = module.Body
                .OfType<PublicAst.ClassDef>()
                .Single(item => item.Name == "TracebackException");
            Assert.That(tracebackException.Body.OfType<PublicAst.FunctionDef>(), Is.Not.Empty);

            var engine = Python.CreateEngine();
            engine.SetSearchPaths(new[] { stdlib });
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                string.Empty,
                "<traceback-lowering>",
                SourceCodeKind.File);
            var compilerContext = new CompilerContext(
                sourceUnit,
                new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode),
                ErrorSink.Default);
            Assert.That(PublicAstLowerer.LowerModule(module, compilerContext), Is.Not.Null);
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                PublicAstLowerer.ExecuteModule(module, pythonContext, out var tracebackGlobals);
                Assert.That(tracebackGlobals.ContainsKey("TracebackException"), Is.True);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            SourceUnit tracebackSource = pythonContext.CreateFileUnit(
                Path.Combine(stdlib, "traceback.py"));
            var tracebackContext = new CompilerContext(
                tracebackSource,
                new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode),
                ErrorSink.Default);
            bool previousFacadeParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                using Parser facade = Parser.CreateParser(
                    tracebackContext,
                    new PythonOptions());
                Assert.That(facade.ParseFile(true), Is.Not.Null);
            } finally {
                Parser.UseGeneratedParser = previousFacadeParserMode;
            }
        }

        [Test]
        public void ExecutesFunctionsAndClassesFromGeneratedAst() {
            const string source =
                "class Adder:\n" +
                "    def add(self, value=1):\n" +
                "        return value + 1\n" +
                "adder = Adder()\n" +
                "result = adder.add(2)\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var module = (PublicAst.Module)parsed.Value;
            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            PublicAstLowerer.ExecuteModule(module, pythonContext, out var globals);
            Assert.That(globals["result"], Is.EqualTo(3));
        }

        [Test]
        public void GeneratedAstPreservesGlobalAndClosureLookupsInAsyncFunctions() {
            const string source =
                "module_value = 40\n" +
                "log = []\n" +
                "def make():\n" +
                "    local_value = 2\n" +
                "    async def inner():\n" +
                "        log.append('called')\n" +
                "        return module_value + local_value\n" +
                "    return inner\n" +
                "coroutine = make()()\n" +
                "try:\n" +
                "    coroutine.send(None)\n" +
                "except StopIteration as exc:\n" +
                "    result = exc.value\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                string.Empty,
                "<async-scope>",
                SourceCodeKind.AutoDetect);
            var compilerContext = new CompilerContext(
                sourceUnit,
                new PythonCompilerOptions(
                    ModuleOptions.ExecOrEvalCode | ModuleOptions.Initialize),
                ErrorSink.Default);
            var ast = PublicAstLowerer.LowerModule((PublicAst.Module)parsed.Value, compilerContext);
            ast.Bind();
            var globals = new PythonDictionary();
            globals["__builtins__"] = pythonContext.BuiltinModuleDict;
            globals["__name__"] = "<async-scope>";
            globals["__package__"] = null;
            var moduleContext = new ModuleContext(globals, pythonContext);
            ast.MakeLookupCode().ToScriptCode().Run(moduleContext.GlobalScope);
            Assert.That(globals["result"], Is.EqualTo(42));
            Assert.That(
                ((PythonList)globals["log"]).Cast<object>().ToArray(),
                Is.EqualTo(new object[] { "called" }));
        }

        [Test]
        public void GeneratedAstPreservesSourceLocations() {
            const string source =
                "value = 1\n" +
                "if value:\n" +
                "    value = 2\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var module = (PublicAst.Module)parsed.Value;
            var conditional = (PublicAst.If)module.Body[1];
            Assert.Multiple(() => {
                Assert.That(module.Body[0].LineNumber, Is.EqualTo(1));
                Assert.That(conditional.LineNumber, Is.EqualTo(2));
                Assert.That(conditional.Test.LineNumber, Is.EqualTo(2));
                Assert.That(((PublicAst.Assign)conditional.Body[0]).LineNumber, Is.EqualTo(3));
            });

            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                source,
                "<location-test>",
                SourceCodeKind.File);
            var lowered = PublicAstLowerer.LowerModule(
                module,
                new CompilerContext(
                    sourceUnit,
                    new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode),
                    ErrorSink.Default));
            lowered.Bind();
            var loweredConditional = (IronPython.Compiler.Ast.IfStatement)
                ((IronPython.Compiler.Ast.SuiteStatement)lowered.Body).Statements[1];
            Assert.Multiple(() => {
                Assert.That(loweredConditional.StartIndex, Is.EqualTo(10));
                Assert.That(lowered._lineLocations.Take(2), Is.EqualTo(new[] { 10, 20 }));
                Assert.That(loweredConditional.Start.Line, Is.EqualTo(2));
                Assert.That(loweredConditional.Tests[0].Start.Line, Is.EqualTo(2));
                Assert.That(
                    ((IronPython.Compiler.Ast.SuiteStatement)loweredConditional.Tests[0].Body)
                        .Statements[0]
                        .Start
                        .Line,
                    Is.EqualTo(3));
            });
        }

        [Test]
        public void GeneratedParserFacadePreservesAsyncFunctionScopes() {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "module_value = 40\n" +
                    "log = []\n" +
                    "async def inner():\n" +
                    "    log.append('called')\n" +
                    "    return module_value + 2\n" +
                    "coroutine = inner()\n" +
                    "try:\n" +
                    "    coroutine.send(None)\n" +
                    "except StopIteration as exc:\n" +
                    "    result = exc.value\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.That(scope.GetVariable<int>("result"), Is.EqualTo(42));
        }

        [Test]
        public void GeneratedParserFacadePreservesFunctionScopes() {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "module_value = 40\n" +
                    "def inner():\n" +
                    "    return module_value + 2\n" +
                    "result = inner()\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.That(scope.GetVariable<int>("result"), Is.EqualTo(42));
        }

        [Test]
        public void GeneratedParserFacadeAstRunsAgainstAnExternalModuleContext() {
            const string source =
                "module_value = 40\n" +
                "log = []\n" +
                "async def inner():\n" +
                "    log.append('called')\n" +
                "    return module_value + 2\n" +
                "coroutine = inner()\n" +
                "try:\n" +
                "    coroutine.send(None)\n" +
                "except StopIteration as exc:\n" +
                "    result = exc.value\n";
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                source,
                "<async-facade>",
                SourceCodeKind.AutoDetect);
            var compilerOptions = new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode) {
                CompilationMode = IronPython.Compiler.CompilationMode.Collectable
            };
            var compilerContext = new CompilerContext(
                sourceUnit,
                compilerOptions,
                ErrorSink.Default);
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            IronPython.Compiler.Ast.PythonAst ast;
            try {
                using Parser parser = Parser.CreateParser(compilerContext, new PythonOptions());
                ast = parser.ParseFile(true, true);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
            ast.Bind();

            var globals = new PythonDictionary();
            globals["__builtins__"] = pythonContext.BuiltinModuleDict;
            globals["__name__"] = "<async-facade>";
            globals["__package__"] = null;
            var moduleContext = new ModuleContext(globals, pythonContext);
            ast.ToScriptCode().Run(moduleContext.GlobalScope);

            Assert.That(globals["result"], Is.EqualTo(42));
        }

        [Test]
        public void ExecutesControlFlowFromGeneratedAst() {
            const string source =
                "values = []\n" +
                "for i in range(4):\n" +
                "    if i == 1:\n" +
                "        continue\n" +
                "    values.append(i)\n" +
                "try:\n" +
                "    raise ValueError('boom')\n" +
                "except ValueError as exc:\n" +
                "    caught = str(exc)\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            PublicAstLowerer.ExecuteModule(
                (PublicAst.Module)parsed.Value,
                pythonContext,
                out var globals);
            Assert.That(
                ((PythonList)globals["values"]).Cast<object>().ToArray(),
                Is.EqualTo(new object[] { 0, 2, 3 }));
            Assert.That(globals["caught"], Is.EqualTo("boom"));
        }

        [Test]
        public void ExecutesGeneratorLambdaAndInterpolatedFormatSpecs() {
            const string source =
                "generator_type = type((lambda: (yield))())\n" +
                "value = 255\n" +
                "width = 6\n" +
                "formatted = f'{value:x}:' f'{value:>{width}}'\n" +
                "debug_formatted = f'{value=}'\n" +
                "spaced_debug_formatted = f'{value = }'\n" +
                "joined = ','.join(str(item) for item in range(3))\n" +
                "pair = (lambda left, right=2, *, scale=3: (left + right) * scale)(1)\n" +
                "def collect(first, *items, required, **extras):\n" +
                "    return first, items, required, extras\n" +
                "collected = collect(1, 2, 3, required=4, extra=5)\n" +
                "expanded = [0, *[1, 2], 3]\n" +
                "expanded_dict = {**{'a': 1}, 'b': 2}\n" +
                "bytes_value = b'abc'\n" +
                "bytes_concat = b'a' b'\\x62' b'c'\n" +
                "escaped_string = 'a\\x62\\u0063'\n" +
                "def leaky():\n" +
                "    yield 1\n" +
                "    raise StopIteration('bad')\n" +
                "leaky_generator = leaky()\n" +
                "leaky_first = next(leaky_generator)\n" +
                "pep479_cause = None\n" +
                "try:\n" +
                "    next(leaky_generator)\n" +
                "except RuntimeError as exc:\n" +
                "    pep479_cause = type(exc.__cause__).__name__\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            PublicAstLowerer.ExecuteModule(
                (PublicAst.Module)parsed.Value,
                pythonContext,
                out var globals);
            Assert.That(globals["generator_type"].GetType().Name, Is.EqualTo("PythonType"));
            Assert.That(globals["formatted"], Is.EqualTo("ff:   255"));
            Assert.That(globals["debug_formatted"], Is.EqualTo("value=255"));
            Assert.That(globals["spaced_debug_formatted"], Is.EqualTo("value = 255"));
            Assert.That(globals["joined"], Is.EqualTo("0,1,2"));
            Assert.That(globals["pair"], Is.EqualTo(9));
            Assert.That(globals["collected"].ToString(), Is.EqualTo("(1, (2, 3), 4, {'extra': 5})"));
            Assert.That(
                ((PythonList)globals["expanded"]).Cast<object>().ToArray(),
                Is.EqualTo(new object[] { 0, 1, 2, 3 }));
            Assert.That(
                ((PythonDictionary)globals["expanded_dict"])["a"],
                Is.EqualTo(1));
            Assert.That(
                ((Bytes)globals["bytes_value"]).ToArray(),
                Is.EqualTo(new byte[] { (byte)'a', (byte)'b', (byte)'c' }));
            Assert.That(
                ((Bytes)globals["bytes_concat"]).ToArray(),
                Is.EqualTo(new byte[] { (byte)'a', (byte)'b', (byte)'c' }));
            Assert.That(globals["escaped_string"], Is.EqualTo("abc"));
            Assert.That(globals["leaky_first"], Is.EqualTo(1));
            Assert.That(globals["pep479_cause"], Is.EqualTo("StopIteration"));
        }

        [Test]
        public void BuildsParenthesizedAndStarImportTargets() {
            const string source =
                "from _py_warnings import (\n" +
                "    warn,\n" +
                "    _set_module,\n" +
                ")\n" +
                "from _locale import *\n" +
                "from os.path import curdir, pardir\n" +
                "from . import sibling\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var module = (PublicAst.Module)parsed.Value;
            var parenthesized = (PublicAst.ImportFrom)module.Body[0];
            var star = (PublicAst.ImportFrom)module.Body[1];
            var dotted = (PublicAst.ImportFrom)module.Body[2];
            var relative = (PublicAst.ImportFrom)module.Body[3];
            Assert.That(
                parenthesized.Names.Select(alias => alias.Name),
                Is.EqualTo(new[] { "warn", "_set_module" }));
            Assert.That(star.Names.Select(alias => alias.Name), Is.EqualTo(new[] { "*" }));
            Assert.That(dotted.Module, Is.EqualTo("os.path"));
            Assert.That(
                dotted.Names.Select(alias => alias.Name),
                Is.EqualTo(new[] { "curdir", "pardir" }));
            Assert.That(relative.Module, Is.Null);
            Assert.That(relative.Level, Is.EqualTo(1));
            Assert.That(relative.Names.Select(alias => alias.Name), Is.EqualTo(new[] { "sibling" }));
        }

        [Test]
        public void ExecutesAnnotationsWalrusConditionalAndStoreTargets() {
            const string source =
                "class Box:\n" +
                "    pass\n" +
                "box = Box()\n" +
                "box.value = 2\n" +
                "count: int = 4\n" +
                "label: str\n" +
                "target = []\n" +
                "target[0:0] = [count]\n" +
                "result = (found := box.value) if target and box else 0\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            PublicAstLowerer.ExecuteModule(
                (PublicAst.Module)parsed.Value,
                pythonContext,
                out var globals);
            Assert.That(globals["count"], Is.EqualTo(4));
            Assert.That(globals["found"], Is.EqualTo(2));
            Assert.That(globals["result"], Is.EqualTo(2));
            Assert.That(((PythonList)globals["target"]).Single(), Is.EqualTo(4));
        }

        [Test]
        public void CompilesEvalAndSingleModesWithGeneratedParser() {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute("assert eval('1 + 2 * 3') == 7");
                engine.Execute(
                    "evaluated_code = compile('40 + 2', '<eval>', 'eval')\n" +
                    "single_code = compile('single_value = 7', '<single>', 'single')\n" +
                    "single_namespace = {}\n" +
                    "exec(single_code, single_namespace)\n" +
                    "assert eval(evaluated_code) == 42\n" +
                    "assert single_namespace['single_value'] == 7\n");
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void RoundTripsPublicAstThroughUnderscoreAst() {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "_ast = __import__('_ast')\n" +
                    "tree = compile(\n" +
                    "    'value: int = 2\\n' +\n" +
                    "    'def render(item: int) -> str:\\n' +\n" +
                    "    '    return str(item)\\n',\n" +
                    "    '<roundtrip>', 'exec', flags=_ast.PyCF_ONLY_AST)\n" +
                    "assert type(tree).__name__ == 'Module'\n" +
                    "assert type(tree.body[0]).__name__ == 'AnnAssign'\n" +
                    "assert tree.body[0].target.id == 'value'\n" +
                    "assert tree.body[1].args.args[0].arg == 'item'\n" +
                    "code = compile(tree, '<roundtrip>', 'exec')\n" +
                    "namespace = {}\n" +
                    "exec(code, namespace)\n" +
                    "assert namespace['value'] == 2\n" +
                    "assert namespace['render'](3) == '3'\n");
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void BuildsCpython315TemplateStrings() {
            const string source =
                "name = 'world'\n" +
                "template = t'hello {name}'\n" +
                "formatted = t'a{3!r:>{5}}b'\n";
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());

            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var engine = Python.CreateEngine();
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            PublicAstLowerer.ExecuteModule(
                (PublicAst.Module)parsed.Value,
                pythonContext,
                out var globals);
            var template = (TemplateObject)globals["template"];
            var formatted = (TemplateObject)globals["formatted"];
            Assert.Multiple(() => {
                Assert.That(
                    template.strings.Cast<object>().ToArray(),
                    Is.EqualTo(new object[] { "hello ", "" }));
                Assert.That(template.interpolations.Count, Is.EqualTo(1));
                Assert.That(
                    template.interpolations[0].GetType().Name,
                    Is.EqualTo("InterpolationObject"));
                Assert.That(
                    ((InterpolationObject)template.interpolations[0]!).expression,
                    Is.EqualTo("name"));
                Assert.That(
                    ((InterpolationObject)formatted.interpolations[0]!).format_spec,
                    Is.EqualTo(">5"));
                Assert.That(
                    ((InterpolationObject)formatted.interpolations[0]!).conversion,
                    Is.EqualTo("r"));
            });
        }

        [Test]
        public void ImportsContextlibWithGeneratedParser() {
            string source = File.ReadAllText(
                Path.Combine(GetStdLibPath(), "contextlib.py"));
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(source),
                new PythonAstBuilder());
            PegParseResult parsed = parser.Parse(PythonGeneratedParser.ModuleStartRule);
            Assert.That(parsed.Success, Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : parsed.ToString());
            var engine = Python.CreateEngine();
            engine.SetSearchPaths(new[] {
                GetStdLibPath(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "lib")
            });
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                string.Empty,
                "<contextlib-lowering>",
                SourceCodeKind.File);
            Assert.That(
                PublicAstLowerer.LowerModule(
                    (PublicAst.Module)parsed.Value,
                    new CompilerContext(
                        sourceUnit,
                        new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode),
                        ErrorSink.Default)),
                Is.Not.Null);
            PublicAstLowerer.ExecuteModule(
                (PublicAst.Module)parsed.Value,
                pythonContext,
                out var globals);
            Assert.That(globals.ContainsKey("contextmanager"), Is.True);

            string abcSource = File.ReadAllText(
                Path.Combine(GetStdLibPath(), "abc.py"));
            var abcParser = new PegParser(
                PythonGrammarDescriptors.Rules,
                new PythonTokenizer(abcSource),
                new PythonAstBuilder());
            PegParseResult abcParsed = abcParser.Parse(PythonGeneratedParser.ModuleStartRule);
            Assert.That(abcParsed.Success, Is.True,
                abcParser.LastFailure != null ? abcParser.LastFailure.Message : abcParsed.ToString());
            PublicAstLowerer.ExecuteModule(
                (PublicAst.Module)abcParsed.Value,
                pythonContext,
                out _);

            var isolatedEngine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            isolatedEngine.SetSearchPaths(new[] { GetStdLibPath() });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                foreach (string module in new[] {
                    "abc", "os", "_collections_abc", "collections", "functools", "types", "warnings"
                }) {
                    try {
                        isolatedEngine.Execute($"import {module}");
                    } catch (System.Exception exception) {
                        string data = string.Join(
                            "; ",
                            exception.Data.Keys.Cast<object>()
                                .Select(key => key.ToString() == "PreviousStackTraces"
                                    ? $"PreviousStackTraces={string.Join(
                                        " | ",
                                        (exception.Data[key] as System.Collections.IEnumerable)
                                            ?.Cast<object>()
                                            .Select(item => item.ToString()) ?? System.Array.Empty<string>())}"
                                    : $"{key}={exception.Data[key]}"));
                        Assert.Fail(
                            $"Generated parser import failed for '{module}': {exception}; " +
                            $"inner={exception.InnerException}; data={data}");
                    }
                }
                isolatedEngine.Execute(
                    "import contextlib\n" +
                    "assert contextlib.__name__ == 'contextlib'\n");
                isolatedEngine.Execute(
                    "compiled = compile('value = __name__', '<memory>', 'exec', module='pkg.mod')\n" +
                    "namespace = {}\n" +
                    "exec(compiled, namespace)\n" +
                    "# module= is compile metadata; exec does not inject it into globals.\n" +
                    "assert namespace['value'] == 'builtins'\n");
                ScriptScope astScope = isolatedEngine.CreateScope();
                isolatedEngine.Execute(
                    "import ast\n" +
                    "tree = ast.parse('value = 1\\nif value:\\n    value = 2\\n', feature_version=6)\n" +
                    "target_name = tree.body[0].targets[0].id\n" +
                    "first_line = tree.body[0].lineno\n" +
                    "if_line = tree.body[1].lineno\n" +
                    "nested_line = tree.body[1].body[0].lineno\n",
                    astScope);
                Assert.Multiple(() => {
                    Assert.That(astScope.GetVariable<string>("target_name"), Is.EqualTo("value"));
                    Assert.That(astScope.GetVariable<int>("first_line"), Is.EqualTo(1));
                    Assert.That(astScope.GetVariable<int>("if_line"), Is.EqualTo(2));
                    Assert.That(astScope.GetVariable<int>("nested_line"), Is.EqualTo(3));
                });

                var warningsEngine = Python.CreateEngine(new Dictionary<string, object> {
                    ["NoSite"] = true
                });
                warningsEngine.SetSearchPaths(new[] { GetStdLibPath() });
                warningsEngine.Execute("import warnings");
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void ImportsTypingWithGeneratedParser() {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.SetSearchPaths(new[] { GetStdLibPath() });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                try {
                    engine.Execute(
                        "import typing\n" +
                        "assert typing.Any is not None\n" +
                        "assert typing.TypeVar is not None\n");
                } catch (System.Exception exception) {
                    string formatted = engine.GetService<Microsoft.Scripting.Hosting.ExceptionOperations>()
                        .FormatException(exception);
                    Assert.Fail($"Generated parser typing import failed:\n{formatted}");
                }
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void ImportsGbkViaGeneratedParser() {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.SetSearchPaths(new[] { GetStdLibPath() });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "import encodings.gbk\n" +
                    "encoded = '\\u4e2d\\u6587'.encode('gbk')\n" +
                    "assert encoded.decode('gbk') == '\\u4e2d\\u6587'\n");
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void BuildsMatrixMultiplicationWithMultiplicativePrecedence() {
            var tokenizer = new PythonTokenizer("x = 2 @ 3 * 4\n");
            var parser = new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder());

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
            var module = (PublicAst.Module)result.Value;
            var assignment = (PublicAst.Assign)module.Body.Single();
            var multiply = (PublicAst.BinOp)assignment.Value;
            Assert.That(multiply.Op, Is.InstanceOf<PublicAst.Mult>());
            Assert.That(multiply.Left, Is.InstanceOf<PublicAst.BinOp>());
            Assert.That(((PublicAst.BinOp)multiply.Left).Op, Is.InstanceOf<PublicAst.MatMult>());
        }

        [TestCase("match x:\n    case 1:\n        pass\n")]
        [TestCase("def f[T](x: T) -> T:\n    return x\n")]
        [TestCase("type Pair[T] = tuple[T, T]\n")]
        [TestCase("t\"hello {x}\"\n")]
        [TestCase("lazy import os\n")]
        [TestCase("try:\n    pass\nexcept A, B:\n    pass\n")]
        [TestCase("[*xs for *xs, in items]\n")]
        [TestCase("async def f(x):\n    await x\n")]
        [TestCase("async def f(xs):\n    async for x in xs:\n        pass\n")]
        [TestCase("async def f(ctx):\n    async with ctx:\n        pass\n")]
        [TestCase("async def f(xs):\n    return [x async for x in xs]\n")]
        [TestCase("async def f(xs):\n    return (x async for x in xs)\n")]
        [TestCase("async def f(obj):\n    value = await obj.call(  # type: ignore\n        1, timeout=None)\n")]
        [TestCase("if x:\n    if y:\n        z += f\". Did you mean: '{s}'?\"\n    else:\n        pass\n")]
        [TestCase("if a:\n    if b:\n        if c:\n            pass\n        else:\n            pass\nelif d:\n    pass\n")]
        [TestCase("if a:\n    pass\nelif a and b and \\\n        c:\n    pass\n")]
        [TestCase(
            "if a:\n" +
            "    pass\n" +
            "elif a and b and \\\n" +
            "        c is not None:\n" +
            "    wrong_name = getattr(value, \"name\", None)\n" +
            "    suggestion = compute(value, traceback, wrong_name)\n" +
            "    if suggestion:\n" +
            "        if suggestion.isascii():\n" +
            "            self._str += f\". Did you mean: '{suggestion}'?\"\n" +
            "        else:\n" +
            "            self._str += f\". Did you mean: '{suggestion}' ({suggestion!a})?\"\n" +
            "elif a and b:\n" +
            "    pass\n")]
        public void GeneratedGrammarAcceptsRepresentativePython315Syntax(string source) {
            var tokenizer = new PythonTokenizer(source);
            var actions = new PythonGrammarActionHandler();
            var parser = new PegParser(PythonGrammarDescriptors.Rules, tokenizer, actions);

            PegParseResult result = parser.Parse(PythonGeneratedParser.ModuleStartRule);

            Assert.That(
                result.Success,
                Is.True,
                parser.LastFailure != null ? parser.LastFailure.Message : result.ToString());
        }

        [Test]
        public void GrowsDirectLeftRecursiveRuleToTheLongestMatch() {
            var rules = new[] {
                new PegRuleDescriptor("expr", true, new[] {
                    new PegAlternativeDescriptor("expr.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "expr"),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "+"),
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NUM")
                    }, false, true),
                    new PegAlternativeDescriptor("expr.1", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NUM")
                    }, false, true)
                }, leftRecursive: true)
            };
            var tokens = new TestTokenSource(
                new PegToken("NUM", "1", 0, 1),
                new PegToken("+", "+", 1, 2),
                new PegToken("NUM", "2", 2, 3),
                new PegToken("+", "+", 3, 4),
                new PegToken("NUM", "3", 4, 5),
                new PegToken("ENDMARKER", "", 5, 5));
            var actions = new RecordingActionHandler();

            PegParseResult result = new PegParser(rules, tokens, actions).Parse("expr");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Position, Is.EqualTo(5));
            Assert.That(tokens.Position, Is.EqualTo(5));
            Assert.That(
                actions.ActionIds,
                Is.EqualTo(new[] { "expr.1", "expr.0", "expr.0" }));
        }

        [Test]
        public void GrowsIndirectLeftRecursiveRules() {
            var rules = new[] {
                new PegRuleDescriptor("start", true, new[] {
                    new PegAlternativeDescriptor("start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "expr")
                    }, false, true)
                }),
                new PegRuleDescriptor("expr", true, new[] {
                    new PegAlternativeDescriptor("expr.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "call")
                    }, false, true),
                    new PegAlternativeDescriptor("expr.1", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NUM")
                    }, false, true)
                }, indirectLeftRecursion: true, leftRecursive: true),
                new PegRuleDescriptor("call", true, new[] {
                    new PegAlternativeDescriptor("call.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "expr"),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "!")
                    }, false, true)
                }, indirectLeftRecursion: true, leftRecursive: true)
            };
            var tokens = new TestTokenSource(
                new PegToken("NUM", "1", 0, 1),
                new PegToken("!", "!", 1, 2),
                new PegToken("ENDMARKER", "", 2, 2));
            var actions = new RecordingActionHandler();

            PegParseResult result = new PegParser(rules, tokens, actions).Parse("start");

            Assert.That(result.Success, Is.True);
            Assert.That(result.Position, Is.EqualTo(2));
            Assert.That(tokens.Position, Is.EqualTo(2));
            Assert.That(
                actions.ActionIds,
                Is.EqualTo(new[] { "expr.1", "call.0", "expr.0", "start.0" }));
        }

        [Test]
        public void ReportsTheFarthestExpectedToken() {
            var rules = new[] {
                new PegRuleDescriptor("start", true, new[] {
                    new PegAlternativeDescriptor("start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NAME"),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "x"),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "z"),
                        new PegElementDescriptor(PegDescriptorKind.EndOfFile)
                    }, false),
                    new PegAlternativeDescriptor("start.1", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NAME"),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "q")
                    }, false)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("NAME", "a", 0, 1),
                new PegToken("NAME", "x", 1, 2),
                new PegToken("NAME", "q", 2, 3),
                new PegToken("ENDMARKER", "", 3, 3));
            var parser = new PegParser(rules, tokens);

            PegParseResult result = parser.Parse("start");

            Assert.That(result.Success, Is.False);
            Assert.That(parser.LastFailure, Is.Not.Null);
            Assert.That(parser.LastFailure.Position, Is.EqualTo(2));
            Assert.That(parser.LastFailure.Expected, Is.EqualTo(new[] { "z" }));
            Assert.That(parser.LastFailure.RuleStack, Does.Contain("start"));
        }

        [Test]
        public void CutPreventsFallbackToLaterAlternatives() {
            var rules = new[] {
                new PegRuleDescriptor("start", true, new[] {
                    new PegAlternativeDescriptor("start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.String, value: "a"),
                        new PegElementDescriptor(PegDescriptorKind.Cut),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "x")
                    }, false),
                    new PegAlternativeDescriptor("start.1", new[] {
                        new PegElementDescriptor(PegDescriptorKind.String, value: "a"),
                        new PegElementDescriptor(PegDescriptorKind.String, value: "y")
                    }, false)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("NAME", "a", 0, 1),
                new PegToken("NAME", "y", 1, 2),
                new PegToken("ENDMARKER", "", 2, 2));
            var parser = new PegParser(rules, tokens);

            PegParseResult result = parser.Parse("start");

            Assert.That(result.Success, Is.False);
            Assert.That(parser.LastFailure.Position, Is.EqualTo(1));
            Assert.That(parser.LastFailure.Expected, Is.EqualTo(new[] { "x" }));
        }

        [Test]
        public void InvalidRulesCanBeRecoveredThroughAHandler() {
            var rules = new[] {
                new PegRuleDescriptor("start", true, new[] {
                    new PegAlternativeDescriptor("start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "invalid_start")
                    }, false)
                }),
                new PegRuleDescriptor("invalid_start", true, new[] {
                    new PegAlternativeDescriptor("invalid_start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "BAD")
                    }, false)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("BAD", "bad", 0, 3),
                new PegToken("ENDMARKER", "", 3, 3));
            var recovery = new RecordingInvalidRuleHandler();
            var parser = new PegParser(rules, tokens, invalidRuleHandler: recovery);

            PegParseResult result = parser.Parse("start");

            Assert.That(result.Success, Is.False);
            Assert.That(recovery.Match, Is.Not.Null);
            Assert.That(recovery.Match.RuleName, Is.EqualTo("invalid_start"));
            Assert.That(parser.InvalidRuleMatches, Has.Count.EqualTo(1));
        }

        [Test]
        public void EnforcesTheConfiguredRuleDepthLimit() {
            var rules = new[] {
                new PegRuleDescriptor("deep", true, new[] {
                    new PegAlternativeDescriptor("deep.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.String, value: "x"),
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "deep")
                    }, false)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("NAME", "x", 1, 2),
                new PegToken("NAME", "x", 2, 3),
                new PegToken("NAME", "x", 3, 4),
                new PegToken("ENDMARKER", "", 4, 4));
            var parser = new PegParser(
                rules,
                tokens,
                options: new PegParserOptions { MaxRuleDepth = 3 });

            PegParseLimitException exception = Assert.Throws<PegParseLimitException>(
                () => parser.Parse("deep"));

            Assert.That(exception.Kind, Is.EqualTo(PegParseLimitKind.RuleDepth));
            Assert.That(exception.RuleStack, Does.Contain("deep"));
        }

        [Test]
        public void EnforcesTheConfiguredRuleInvocationLimit() {
            var rules = new[] {
                new PegRuleDescriptor("start", false, new[] {
                    new PegAlternativeDescriptor("start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "child")
                    }, false)
                }),
                new PegRuleDescriptor("child", false, new[] {
                    new PegAlternativeDescriptor("child.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NAME")
                    }, false)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("ENDMARKER", "", 1, 1));
            var parser = new PegParser(
                rules,
                tokens,
                options: new PegParserOptions { MaxRuleInvocations = 1 });

            PegParseLimitException exception = Assert.Throws<PegParseLimitException>(
                () => parser.Parse("start"));

            Assert.That(exception.Kind, Is.EqualTo(PegParseLimitKind.RuleInvocations));
            Assert.That(exception.RuleStack, Does.Contain("start"));
        }

        [Test]
        public void EnforcesTheConfiguredMemoLimit() {
            var rules = new[] {
                new PegRuleDescriptor("start", true, new[] {
                    new PegAlternativeDescriptor("start.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "child")
                    }, false)
                }),
                new PegRuleDescriptor("child", true, new[] {
                    new PegAlternativeDescriptor("child.0", new[] {
                        new PegElementDescriptor(PegDescriptorKind.Name, value: "NAME")
                    }, false)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("ENDMARKER", "", 1, 1));
            var parser = new PegParser(
                rules,
                tokens,
                options: new PegParserOptions { MaxMemoEntries = 1 });

            PegParseLimitException exception = Assert.Throws<PegParseLimitException>(
                () => parser.Parse("start"));

            Assert.That(exception.Kind, Is.EqualTo(PegParseLimitKind.MemoEntries));
        }

        [Test]
        public void RejectsAlternativesAboveTheConfiguredPythonVersion() {
            var rules = new[] {
                new PegRuleDescriptor("start", false, new[] {
                    new PegAlternativeDescriptor(
                        "start.0",
                        new[] { new PegElementDescriptor(PegDescriptorKind.Name, value: "NAME") },
                        false,
                        false,
                        minimumMinorVersion: 10)
                })
            };
            var tokens = new TestTokenSource(
                new PegToken("NAME", "x", 0, 1),
                new PegToken("ENDMARKER", "", 1, 1));
            var parser = new PegParser(
                rules,
                tokens,
                options: new PegParserOptions {
                    PythonMajorVersion = 3,
                    PythonMinorVersion = 9
                });

            PegUnsupportedVersionException exception = Assert.Throws<PegUnsupportedVersionException>(
                () => parser.Parse("start"));

            Assert.That(exception.ActionId, Is.EqualTo("start.0"));
            Assert.That(exception.RequiredMinorVersion, Is.EqualTo(10));
        }

        private sealed class TestTokenSource : IPegTokenSource {
            private readonly PegToken[] _tokens;

            public TestTokenSource(params PegToken[] tokens) {
                _tokens = tokens;
            }

            public int Position { get; set; }

            public PegToken Peek() => _tokens[Position];

            public PegToken Next() => _tokens[Position++];
        }

        private static string GetStdLibPath() {
            string configured = System.Environment.GetEnvironmentVariable(
                "CYBERSTONE_IRONPYTHON_STDLIB");
            if (!string.IsNullOrEmpty(configured)) {
                return Path.Combine(configured, "lib");
            }

            string assemblyDirectory = Path.GetDirectoryName(
                typeof(PythonContext).Assembly.Location);
            string candidate = Path.GetFullPath(
                Path.Combine(
                    assemblyDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "IronPython.StdLib",
                    "lib"));
            Assert.That(
                Directory.Exists(candidate),
                Is.True,
                $"Standard library not found at {candidate}.");
            return candidate;
        }

        private sealed class RecordingActionHandler : IPegActionHandler {
            public List<string> ActionIds { get; } = new List<string>();

            public object Invoke(PegActionContext context, PegActionArguments arguments) {
                ActionIds.Add(context.ActionId);
                return arguments;
            }
        }

        private sealed class RecordingGeneratedActionHandler : PythonGrammarActionHandler {
            public int InvocationCount { get; private set; }

            public override object Handle_file_0(PegActionContext context, PegActionArguments arguments) {
                InvocationCount++;
                return context.ActionId;
            }
        }

        private sealed class RecordingInvalidRuleHandler : IPegInvalidRuleHandler {
            public PegInvalidRuleMatch Match { get; private set; }

            public PegParseResult Recover(PegInvalidRuleMatch match, PegParseResult parsedValue) {
                Match = match;
                return PegParseResult.Fail(match.Start);
            }
        }

        private sealed class RecordingParserSink : ParserSink {
            public List<string> Names { get; } = new List<string>();

            public override void StartName(SourceSpan span, string name) {
                Names.Add(name);
            }
        }
    }
}
