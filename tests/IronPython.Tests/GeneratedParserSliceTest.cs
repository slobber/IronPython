using IronPython.Compiler.Generated;
using IronPython.Compiler;
using IronPython.Compiler.PublicAst;
using IronPython.Compiler.Syntax;
using IronPython.Hosting;
using IronPython.ParserGenerator;
using IronPython.Runtime;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using PublicAst = IronPython.Compiler.Syntax.Ast;

namespace IronPython.Tests {
    [TestFixture]
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
                Parser.UseGeneratedParser = false;
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
