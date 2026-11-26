using IronPython.Compiler;
using IronPython.Compiler.Ast;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace IronPython.Tests {
    [TestFixture]
    [NonParallelizable]
    public sealed class Cpython315RuntimeSurfaceTest {
        [Test]
        public void FrozendictHasCpython315ValueSemantics() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            engine.Execute(
                "fd = frozendict({'a': 1}, b=2)\n" +
                "fd_same = frozendict(fd)\n" +
                "fd_equal = fd == frozendict({'b': 2, 'a': 1})\n" +
                "fd_dict_equal = fd == {'a': 1, 'b': 2}\n" +
                "fd_hash_equal = hash(fd) == hash(frozendict({'a': 1, 'b': 2}))\n" +
                "fd_copy_same = fd.copy() is fd\n" +
                "try:\n" +
                "    fd['a'] = 3\n" +
                "except TypeError as exc:\n" +
                "    fd_immutable_error = str(exc)\n",
                scope);

            var frozen = scope.GetVariable<FrozenPythonDictionary>("fd");
            Assert.Multiple(() => {
                Assert.That(frozen.Count, Is.EqualTo(2));
                Assert.That(scope.GetVariable<bool>("fd_same"), Is.True);
                Assert.That(scope.GetVariable<bool>("fd_equal"), Is.True);
                Assert.That(scope.GetVariable<bool>("fd_dict_equal"), Is.True);
                Assert.That(scope.GetVariable<bool>("fd_hash_equal"), Is.True);
                Assert.That(scope.GetVariable<bool>("fd_copy_same"), Is.True);
                Assert.That(
                    scope.GetVariable<string>("fd_immutable_error"),
                    Is.EqualTo("'frozendict' object does not support item assignment"));
            });
        }

        [Test]
        public void SentinelHasCpython315IdentityAndMetadata() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            scope.SetVariable("__name__", "sentinel_test");
            engine.Execute(
                "__name__ = 'sentinel_test'\n" +
                "missing = sentinel('MISSING')\n" +
                "token = sentinel('TOKEN', repr='custom-token')\n" +
                "sentinel_copy_same = missing.__copy__() is missing\n" +
                "sentinel_deepcopy_same = missing.__deepcopy__({}) is missing\n" +
                "assert repr(missing) == 'MISSING'\n" +
                "assert repr(token) == 'custom-token'\n",
                scope);

            var missing = scope.GetVariable<SentinelObject>("missing");
            Assert.Multiple(() => {
                Assert.That(missing.__name__, Is.EqualTo("MISSING"));
                Assert.That(missing.__module__, Is.EqualTo("sentinel_test"));
                Assert.That(scope.GetVariable<bool>("sentinel_copy_same"), Is.True);
                Assert.That(scope.GetVariable<bool>("sentinel_deepcopy_same"), Is.True);
            });
        }

        [Test]
        public void ClassLocalsAreLiveWhileFunctionLocalsRemainSnapshots() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            engine.Execute(
                "closure_value = 42\n" +
                "class ClassNamespace:\n" +
                "    locals()['closure_value'] = 43\n" +
                "    from_closure = closure_value\n" +
                "class_locals_value = ClassNamespace.from_closure\n" +
                "\n" +
                "def function_locals():\n" +
                "    local_value = 1\n" +
                "    snapshot = locals()\n" +
                "    local_value = 2\n" +
                "    snapshot['local_value'] = 3\n" +
                "    return local_value, snapshot['local_value']\n" +
                "\n" +
                "function_local, snapshot_local = function_locals()\n",
                scope);

            Assert.Multiple(() => {
                Assert.That(scope.GetVariable<int>("class_locals_value"), Is.EqualTo(43));
                Assert.That(scope.GetVariable<int>("function_local"), Is.EqualTo(2));
                Assert.That(scope.GetVariable<int>("snapshot_local"), Is.EqualTo(3));
            });
        }

        [Test]
        public void ContextVarsSupportDefaultsTokensAndContextIsolation() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import _contextvars\n" +
                "var = _contextvars.ContextVar('var', default=1)\n" +
                "initial = var.get()\n" +
                "token = var.set(2)\n" +
                "after_set = var.get()\n" +
                "var.reset(token)\n" +
                "after_reset = var.get()\n" +
                "snapshot_token = var.set(3)\n" +
                "context = _contextvars.copy_context()\n" +
                "var.set(4)\n" +
                "snapshot_value = context[var]\n" +
                "inside = context.run(var.get)\n" +
                "after_run = var.get()\n" +
                "var.reset(snapshot_token)\n" +
                "missing_error = None\n" +
                "missing = _contextvars.ContextVar('missing')\n" +
                "try:\n" +
                "    missing.get()\n" +
                "except LookupError as exc:\n" +
                "    missing_error = type(exc).__name__\n" +
                "assert initial == 1\n" +
                "assert after_set == 2\n" +
                "assert after_reset == 1\n" +
                "assert snapshot_value == 3\n" +
                "assert inside == 3\n" +
                "assert after_run == 4\n" +
                "assert missing_error == 'LookupError'\n");
        }

        [Test]
        public void ContextVarsFlowAcrossDotNetTaskContinuations() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import clr\n" +
                "from System.Threading.Tasks import Task\n" +
                "from _contextvars import ContextVar\n" +
                "current = ContextVar('current', default='missing')\n" +
                "async def flow():\n" +
                "    current.set('parent')\n" +
                "    await Task.Delay(1)\n" +
                "    after_delay = current.get()\n" +
                "    async def child():\n" +
                "        return current.get()\n" +
                "    child_value = await child()\n" +
                "    return after_delay, child_value\n" +
                "assert flow().AsTask().Result == ('parent', 'parent')\n");
        }

        [Test]
        public void WindowsSubprocessConstantsAreComplete() {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                Assert.Ignore("Windows-only runtime surface");
            }

            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import _winapi\n" +
                "assert _winapi.STARTF_FORCEONFEEDBACK == 0x2\n" +
                "assert _winapi.STARTF_FORCEOFFFEEDBACK == 0x80\n" +
                "import sys\n" +
                "assert sys.exception() is None\n" +
                "try:\n" +
                "    raise LookupError('current')\n" +
                "except LookupError:\n" +
                "    current_exception = sys.exception()\n" +
                "assert isinstance(current_exception, LookupError)\n" +
                "import io\n" +
                "stream = io.TextIOWrapper(io.BytesIO(b'locale'), encoding='locale')\n" +
                "assert stream.read() == 'locale'\n");
        }

        [Test]
        public void ClassIterationAndTypeKeywordsUseTheMetaclassProtocol() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "class IterMeta(type):\n" +
                "    def __iter__(cls):\n" +
                "        return iter((1, 2, 3))\n" +
                "class Iterable(metaclass=IterMeta):\n" +
                "    def __iter__(self):\n" +
                "        return iter(())\n" +
                "assert list(Iterable) == [1, 2, 3]\n" +
                "class KeywordMeta(type):\n" +
                "    def __new__(mcls, name, bases, namespace, **keywords):\n" +
                "        namespace['keyword_value'] = keywords['marker']\n" +
                "        return super().__new__(mcls, name, bases, namespace)\n" +
                "class KeywordClass(metaclass=KeywordMeta, marker=42):\n" +
                "    pass\n" +
                "assert KeywordClass.keyword_value == 42\n");
        }

        [Test]
        public void ClrArrayAliasSupportsPythonTypeArguments() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import System\n" +
                "array_type = System.Array[System.Int32]\n" +
                "assert 'Array[Int32]' in repr(array_type)\n");
        }

        [Test]
        public void GenericAliasesCanBeUsedAsClassBases() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "mapping_alias = dict[str, str]\n" +
                "class DerivedMapping(mapping_alias):\n" +
                "    pass\n" +
                "assert issubclass(DerivedMapping, dict)\n");
        }

        [Test]
        public void CompileAcceptsFeatureVersionKeyword() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "code = compile('value = 2', '<feature>', 'exec', _feature_version=6)\n" +
                "namespace = {}\n" +
                "exec(code, namespace)\n" +
                "assert namespace['value'] == 2\n");
        }

        [Test]
        public void FunctionAnnotationsAreLazyAndSupportFormats() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "class LazyAnnotation:\n" +
                    "    def method(self, value: int) -> LazyAnnotation:\n" +
                    "        return self\n" +
                    "annotation_function = LazyAnnotation.__dict__['method'].__annotate__\n" +
                    "annotation_values = LazyAnnotation.__dict__['method'].__annotations__\n" +
                    "annotation_strings = annotation_function(4)\n" +
                    "assert annotation_values == {'value': int, 'return': LazyAnnotation}\n" +
                    "assert annotation_strings == {'value': 'int', 'return': 'LazyAnnotation'}\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void InteractiveCompilationPreservesAnnotationSourceText() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "import annotationlib\n" +
                    "source = 'def annotated(value: int) -> list[str]:\\n    return []\\n'\n" +
                    "exec(compile(source, '<interactive>', 'single'), globals())\n" +
                    "strings = annotated.__annotate__(annotationlib.Format.STRING)\n" +
                    "assert strings == {'value': 'int', 'return': 'list[str]'}, repr(strings)\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void DocstringsUseCompilerWhitespaceNormalization() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "\"\"\"\n" +
                    "  module\n" +
                    "\"\"\"\n" +
                    "def function():\n" +
                    "    \"\"\"\n" +
                    "      first\n" +
                    "        second\n" +
                    "    \"\"\"\n" +
                    "class Type:\n" +
                    "    '''  alpha\n" +
                    "      beta\n" +
                    "    '''\n" +
                    "assert __doc__ == '\\nmodule\\n', repr(__doc__)\n" +
                    "assert function.__doc__ == '\\nfirst\\n  second\\n', repr(function.__doc__)\n" +
                    "assert Type.__doc__ == 'alpha\\nbeta\\n', repr(Type.__doc__)\n");
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void ModuleAndClassVariableAnnotationsAreRecorded() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "module_value: int\n" +
                    "class AnnotatedValues:\n" +
                    "    value: int\n" +
                    "    named: str = 'value'\n",
                    scope);
                engine.Execute(
                    "assert __annotations__ == {'module_value': int}, repr(__annotations__)\n" +
                    "assert AnnotatedValues.__annotations__ == {'value': int, 'named': str}, repr(AnnotatedValues.__annotations__)\n" +
                    "assert AnnotatedValues.named == 'value', repr(AnnotatedValues.named)\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void FutureAnnotationsStoreSourceTextWithoutResolution() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "from __future__ import annotations\n" +
                    "module_value: UndefinedName\n" +
                    "class FutureAnnotations:\n" +
                    "    class_value: UndefinedName\n" +
                    "    def method(self, value: UndefinedName) -> UndefinedName:\n" +
                    "        return value\n",
                    scope);
                engine.Execute(
                    "assert __annotations__ == {'module_value': 'UndefinedName'}\n" +
                    "assert FutureAnnotations.__annotations__ == {'class_value': 'UndefinedName'}\n" +
                    "assert FutureAnnotations.method.__annotations__ == {\n" +
                    "    'value': 'UndefinedName', 'return': 'UndefinedName'}\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PositionalOnlyParametersRejectKeywords(bool useGeneratedParser) {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = useGeneratedParser;
            try {
                engine.Execute(
                    "def positional_only(value, /, keyword):\n" +
                    "    return value, keyword\n" +
                    "positional_result = positional_only(1, keyword=2)\n" +
                    "assert positional_result == (1, 2)\n" +
                    "assert positional_only.__code__.co_posonlyargcount == 1\n" +
                    "try:\n" +
                    "    positional_only(value=1, keyword=2)\n" +
                    "except TypeError as exc:\n" +
                    "    positional_error = str(exc)\n" +
                    "assert \"unexpected keyword argument 'value'\" in positional_error\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void AstConstructorsValidateRequiredAndUnknownFields() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import ast\n" +
                "constant = ast.Constant(value=1)\n" +
                "assert constant.value == 1\n" +
                "try:\n" +
                "    ast.Constant(value=1, unknown=2)\n" +
                "except TypeError as exc:\n" +
                "    unknown_error = str(exc)\n" +
                "try:\n" +
                "    ast.Constant()\n" +
                "except TypeError as exc:\n" +
                "    missing_error = str(exc)\n" +
                "assert 'unknown' in unknown_error, unknown_error\n" +
                "assert 'value' in missing_error, missing_error\n");
        }

        [Test]
        public void AstDumpUsesCpython315ConstantAndSourcePositionShape() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "import ast\n" +
                    "dumped = ast.dump(ast.parse('print(1 + 2)\\n'), include_attributes=True)\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.That(
                scope.GetVariable<string>("dumped"),
                Is.EqualTo(
                    "Module(body=[Expr(value=Call(func=Name(id='print', lineno=1, " +
                    "col_offset=0, end_lineno=1, end_col_offset=5), args=[BinOp(" +
                    "left=Constant(value=1, lineno=1, col_offset=6, end_lineno=1, " +
                    "end_col_offset=7), op=Add(), right=Constant(value=2, lineno=1, " +
                    "col_offset=10, end_lineno=1, end_col_offset=11), lineno=1, " +
                    "col_offset=6, end_lineno=1, end_col_offset=11)], lineno=1, " +
                    "col_offset=0, end_lineno=1, end_col_offset=12), lineno=1, " +
                    "col_offset=0, end_lineno=1, end_col_offset=12)])"));
        }

        [Test]
        public void TypeAliasSupportsSubscriptionAndRuntimeArguments() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "type Pair[T] = tuple[T, T]\n" +
                    "pair_alias = Pair[int]\n" +
                    "pair_args = pair_alias.__args__\n" +
                    "pair_args_text = repr(pair_args)\n" +
                    "pair_origin = pair_alias.__origin__\n" +
                    "pair_text = str(pair_alias)\n" +
                    "pair_qualname = Pair.__qualname__\n" +
                    "pair_module = Pair.__module__\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.Multiple(() => {
                Assert.That(
                    scope.GetVariable<string>("pair_args_text"),
                    Is.EqualTo("(<class 'int'>,)"));
                Assert.That(
                    scope.GetVariable<TypeAliasObject>("pair_origin").__name__,
                    Is.EqualTo("Pair"));
                Assert.That(scope.GetVariable<string>("pair_text"), Is.EqualTo("Pair[int]"));
                Assert.That(scope.GetVariable<string>("pair_qualname"), Is.EqualTo("Pair"));
                Assert.That(scope.GetVariable<object>("pair_module"), Is.Not.Null);
            });
        }

        [Test]
        public void FunctionalTypeAliasExposesCpythonMetadata() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "from typing import TypeAliasType, TypeVar\n" +
                    "T = TypeVar('T')\n" +
                    "Alias = TypeAliasType('Alias', list[T], type_params=(T,), " +
                    "qualname='Owner.Alias')\n" +
                    "alias_qualname = Alias.__qualname__\n" +
                    "alias_parameters = Alias.__parameters__\n" +
                    "alias_args = Alias[int].__args__\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.Multiple(() => {
                Assert.That(scope.GetVariable<string>("alias_qualname"), Is.EqualTo("Owner.Alias"));
                Assert.That(
                    ((PythonTuple)scope.GetVariable("alias_parameters")).Count,
                    Is.EqualTo(1));
                Assert.That(scope.GetVariable<PythonTuple>("alias_args"), Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void GeneratedParserKeepsFunctionGlobalWritesInTheCallingScope() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "py_func_called = False\n" +
                    "def py_func():\n" +
                    "    global py_func_called\n" +
                    "    py_func_called = True\n",
                    scope);
                PythonFunction function = scope.GetVariable<PythonFunction>("py_func");
                engine.Operations.Invoke(function);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            bool autoDetectResult = scope.GetVariable<bool>("py_func_called");

            ScriptScope statementScope = engine.CreateScope();
            Parser.UseGeneratedParser = true;
            try {
                engine.CreateScriptSourceFromString(
                    "py_func_called = False\n" +
                    "def py_func():\n" +
                    "    global py_func_called\n" +
                    "    py_func_called = True\n",
                    SourceCodeKind.Statements).Execute(statementScope);
                engine.Operations.Invoke(
                    statementScope.GetVariable<PythonFunction>("py_func"));
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
            Assert.Multiple(() => {
                Assert.That(autoDetectResult, Is.True);
                Assert.That(statementScope.GetVariable<bool>("py_func_called"), Is.True);
            });
        }

        [Test]
        public void GeneratedParserBindsGlobalDeclarationsAsGlobals() {
            const string source =
                "py_func_called = False\n" +
                "def py_func():\n" +
                "    global py_func_called\n" +
                "    py_func_called = True\n";
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            var pythonContext = (PythonContext)HostingHelpers.GetLanguageContext(engine);
            SourceUnit sourceUnit = pythonContext.CreateSnippet(
                source,
                "<global-binding>",
                SourceCodeKind.AutoDetect);
            var compilerContext = new CompilerContext(
                sourceUnit,
                new PythonCompilerOptions(ModuleOptions.ExecOrEvalCode),
                ErrorSink.Default);
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                using Parser parser = Parser.CreateParser(compilerContext, new PythonOptions());
                PythonAst ast = parser.ParseFile(true, true);
                ast.Bind();
                Assert.That(
                    ast.CompilationMode.GetType().Name,
                    Is.EqualTo("LookupCompilationMode"));
                var suite = (SuiteStatement)ast.Body;
                FunctionDefinition function = suite.Statements.OfType<FunctionDefinition>().Single();
                Assert.That(
                    function.Variables!["py_func_called"].Kind,
                    Is.EqualTo(VariableKind.Global));
                var functionSuite = (SuiteStatement)function.Body;
                var assignment = (AssignmentStatement)functionSuite.Statements[1];
                Assert.That(
                    ((NameExpression)assignment.Left[0]).Reference.PythonVariable!.Kind,
                    Is.EqualTo(VariableKind.Global));

                var globals = new PythonDictionary {
                    ["__builtins__"] = pythonContext.BuiltinModuleDict,
                    ["__name__"] = "<global-binding>",
                    ["__package__"] = null
                };
                var moduleContext = new ModuleContext(globals, pythonContext);
                ast.ToScriptCode().Run(moduleContext.GlobalScope);
                var functionValue = (PythonFunction)globals["py_func"];
                engine.Operations.Invoke(functionValue);
                Assert.That(globals["py_func_called"], Is.True);

                var lookupGlobals = new PythonDictionary {
                    ["__builtins__"] = pythonContext.BuiltinModuleDict,
                    ["__name__"] = "<lookup-global-binding>",
                    ["__package__"] = null
                };
                var lookupContext = new ModuleContext(lookupGlobals, pythonContext);
                ast.MakeLookupCode().ToScriptCode().Run(lookupContext.GlobalScope);
                var lookupFunction = (PythonFunction)lookupGlobals["py_func"];
                engine.Operations.Invoke(lookupFunction);
                Assert.That(lookupGlobals["py_func_called"], Is.True);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void ParenthesizedComparisonsRemainIndependentOperands() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "left = (1 is 1)\n" +
                    "right = (2 is 2)\n" +
                    "result = left != right\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.That(scope.GetVariable<bool>("result"), Is.False);
        }

        [Test]
        public void FromImportUsesModuleGetattr() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "from typing import Pattern, Match\n" +
                    "result = Pattern is not None and Match is not None\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.That(scope.GetVariable<bool>("result"), Is.True);
        }

        [Test]
        public void PrefixMatchAndTypedDictClassSyntaxAreAvailable() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "import re\n" +
                    "from typing import TypedDict\n" +
                    "class Movie(TypedDict):\n" +
                    "    title: str\n" +
                    "match_prefix = re.compile('ab').prefixmatch('abc').group()\n" +
                    "typed_dict_name = Movie.__name__\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }

            Assert.Multiple(() => {
                Assert.That(scope.GetVariable<string>("match_prefix"), Is.EqualTo("ab"));
                Assert.That(scope.GetVariable<string>("typed_dict_name"), Is.EqualTo("Movie"));
            });
        }

        [Test]
        public void LazyImportsResolveOnAccessAndCall() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "lazy import math as lazy_math\n" +
                    "lazy from math import sqrt as lazy_sqrt\n" +
                    "import types\n" +
                    "assert type(lazy_math) is types.LazyImportType\n" +
                    "assert type(lazy_sqrt) is types.LazyImportType\n" +
                    "assert lazy_math.sqrt(16.0) == 4.0\n" +
                    "assert lazy_sqrt(25.0) == 5.0\n");
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void TimeModuleProvidesNanosecondClocks() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import time\n" +
                "assert isinstance(time.time_ns(), int)\n" +
                "assert isinstance(time.monotonic_ns(), int)\n" +
                "assert isinstance(time.perf_counter_ns(), int)\n" +
                "assert isinstance(time.process_time_ns(), int)\n" +
                "assert isinstance(time.thread_time_ns(), int)\n" +
                "assert time.get_clock_info('time_ns').adjustable\n" +
                "assert not time.get_clock_info('perf_counter_ns').adjustable\n");
        }

        [Test]
        public void SysModuleExposesPlatformAndJitMetadata() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import sys\n" +
                "assert sys.platlibdir == 'lib'\n" +
                "assert sys._jit.is_available() is False\n" +
                "assert sys._jit.is_enabled() is False\n" +
                "assert sys._jit.is_active() is False\n");
        }

        [Test]
        public void CodeReplaceAndSysconfigSurfaceAreAvailable() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "import _sysconfig\n" +
                "assert _sysconfig.config_vars()['VERSION'] == '3.15'\n" +
                "def generator():\n" +
                "    yield 1\n" +
                "replacement = generator.__code__.replace(\n" +
                "    co_flags=generator.__code__.co_flags | 0x100)\n" +
                "assert replacement.co_flags & 0x100\n");
        }

        [Test]
        public void ExceptionGroupsSupportSplitAndSubgroup() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "group = ExceptionGroup('errors', [ValueError('v'), TypeError('t')])\n" +
                "assert isinstance(group, BaseExceptionGroup)\n" +
                "assert group.message == 'errors'\n" +
                "assert len(group.exceptions) == 2\n" +
                "matching, rest = group.split(lambda exc: isinstance(exc, ValueError))\n" +
                "assert len(matching.exceptions) == 1\n" +
                "assert len(rest.exceptions) == 1\n" +
                "subgroup = group.subgroup(lambda exc: isinstance(exc, TypeError))\n" +
                "assert len(subgroup.exceptions) == 1\n");
        }

        [Test]
        public void ExceptStarSplitsGroupsAndCombinesUnhandledErrors() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                    "seen = []\n" +
                    "try:\n" +
                    "    raise ExceptionGroup('errors', [ValueError('v'), TypeError('t')])\n" +
                    "except* ValueError as group:\n" +
                    "    seen.append(('value', group.message, [type(item).__name__ for item in group.exceptions]))\n" +
                    "except* TypeError as group:\n" +
                    "    seen.append(('type', group.message, [type(item).__name__ for item in group.exceptions]))\n" +
                    "assert seen == [('value', 'errors', ['ValueError']), ('type', 'errors', ['TypeError'])]\n" +
                    "unhandled = None\n" +
                    "try:\n" +
                    "    try:\n" +
                    "        raise ExceptionGroup('errors', [ValueError('v'), TypeError('t')])\n" +
                    "    except* ValueError:\n" +
                    "        pass\n" +
                    "except BaseExceptionGroup as group:\n" +
                    "    unhandled = (group.message, [type(item).__name__ for item in group.exceptions])\n" +
                    "assert unhandled == ('errors', ['TypeError'])\n" +
                    "import clr\n" +
                    "from System.Threading.Tasks import Task\n" +
                    "def run_coro(coro):\n" +
                    "    value = None\n" +
                    "    while True:\n" +
                    "        try:\n" +
                    "            task = coro.send(value)\n" +
                    "            task.Wait()\n" +
                    "            value = None\n" +
                    "        except StopIteration as exc:\n" +
                    "            return exc.value\n" +
                    "async def async_handler():\n" +
                    "    result = []\n" +
                    "    try:\n" +
                    "        raise ExceptionGroup('async', [ValueError('v')])\n" +
                    "    except* ValueError as group:\n" +
                    "        await Task.FromResult(None)\n" +
                    "        result.append(group.message)\n" +
                    "    return result\n" +
                    "assert run_coro(async_handler()) == ['async']\n" +
                    "invalid_except_star = 0\n" +
                    "for source in [\n" +
                    "    'def f():\\n try: pass\\n except* ValueError: return 1\\n',\n" +
                    "    'while True:\\n try: pass\\n except* ValueError: break\\n',\n" +
                    "    'while True:\\n try: pass\\n except* ValueError: continue\\n']:\n" +
                    "    try:\n" +
                    "        compile(source, '<except-star>', 'exec')\n" +
                    "    except SyntaxError:\n" +
                    "        invalid_except_star += 1\n" +
                    "assert invalid_except_star == 3\n" +
                    "combined = None\n" +
                    "try:\n" +
                    "    try:\n" +
                    "        raise ExceptionGroup('errors', [ValueError('v'), TypeError('t')])\n" +
                    "    except* ValueError:\n" +
                    "        raise KeyError('handler')\n" +
                    "except BaseExceptionGroup as group:\n" +
                    "    combined = [type(item).__name__ for item in group.exceptions]\n" +
                    "assert combined == ['KeyError', 'ExceptionGroup']\n",
                    scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }

        [Test]
        public void ItertoolsBatchedSupportsStrictMode() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "from itertools import batched\n" +
                "assert list(batched('ABCDE', 2)) == [('A', 'B'), ('C', 'D'), ('E',)]\n" +
                "try:\n" +
                "    list(batched('ABCDE', 2, strict=True))\n" +
                "except ValueError as exc:\n" +
                "    batched_error = str(exc)\n" +
                "assert 'incomplete batch' in batched_error\n");
        }

        [Test]
        public void PurePython315StandardLibrarySmokeTests() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            engine.Execute(
                "from graphlib import TopologicalSorter\n" +
                "graph = {'deploy': {'build'}, 'build': {'prepare'}, 'prepare': set()}\n" +
                "assert list(TopologicalSorter(graph).static_order()) == ['prepare', 'build', 'deploy']\n" +
                "import tomllib\n" +
                "document = tomllib.loads('title = \"Iron\"\\n[build]\\nvalue = 42\\n')\n" +
                "assert document == {'title': 'Iron', 'build': {'value': 42}}\n");
        }

        [Test]
        public void DictionaryPreservesInsertionOrderAcrossStorageMutations() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            ScriptScope scope = engine.CreateScope();
            engine.Execute(
                "values = {}\n" +
                "for index in range(40):\n" +
                "    values[str(index)] = index\n" +
                "original = [str(index) for index in range(40)]\n" +
                "initial_order_ok = list(values) == original\n" +
                "values['5'] = 500\n" +
                "replacement_order_ok = list(values) == original\n" +
                "del values['10']\n" +
                "values['10'] = 1000\n" +
                "reinserted = original[:10] + original[11:] + ['10']\n" +
                "reinsert_order_ok = list(values) == reinserted\n" +
                "copied = dict(values)\n" +
                "copy_order_ok = list(copied) == reinserted\n" +
                "merged = {'before': 0} | values | {'after': 41}\n" +
                "merge_order_ok = list(merged) == ['before'] + reinserted + ['after']\n" +
                "in_place = {'before': 0}\n" +
                "in_place |= values\n" +
                "in_place_order_ok = list(in_place) == ['before'] + reinserted\n",
                scope);

            Assert.Multiple(() => {
                Assert.That(scope.GetVariable<bool>("initial_order_ok"), Is.True);
                Assert.That(scope.GetVariable<bool>("replacement_order_ok"), Is.True);
                Assert.That(scope.GetVariable<bool>("reinsert_order_ok"), Is.True);
                Assert.That(scope.GetVariable<bool>("copy_order_ok"), Is.True);
                Assert.That(scope.GetVariable<bool>("merge_order_ok"), Is.True);
                Assert.That(scope.GetVariable<bool>("in_place_order_ok"), Is.True);
            });
        }

        [Test]
        public void ModernSyntaxFeaturesExecuteTogether() {
            ScriptEngine engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(
                "class Point:\n" +
                "    __match_args__ = ('x', 'y')\n" +
                "    def __init__(self, x, y):\n" +
                "        self.x = x\n" +
                "        self.y = y\n" +
                "point = Point(1, 2)\n" +
                "match point:\n" +
                "    case Point(x=left, y=right) if left < right:\n" +
                "        matched = (left, right)\n" +
                "    case _:\n" +
                "        matched = None\n" +
                "match [1, 2, 3, 4]:\n" +
                "    case [first, *middle, last]:\n" +
                "        sequence_match = (first, middle, last)\n" +
                "match {'a': 1, 'b': 2}:\n" +
                "    case {'a': 1, **rest}:\n" +
                "        mapping_rest = rest\n" +
                "class PatternValues:\n" +
                "    RED = 1\n" +
                "match 1:\n" +
                "    case PatternValues.RED:\n" +
                "        dotted_pattern = True\n" +
                "    case _:\n" +
                "        dotted_pattern = False\n" +
                "merged = {'a': 1} | {'b': 2}\n" +
                "updated = {'a': 1}\n" +
                "updated |= {'b': 2}\n" +
                "unpacked_comprehension = [*items for items in ([1, 2], [3])]\n" +
                "dict_unpacked_comprehension = {**item for item in ({'a': 1}, {'b': 2})}\n" +
                "zipped = list(zip([1], [2], strict=True))\n" +
                "zip_error = None\n" +
                "try:\n" +
                "    list(zip([1], [2, 3], strict=True))\n" +
                "except ValueError as exc:\n" +
                "    zip_error = type(exc).__name__\n" +
                "class Manager:\n" +
                "    def __enter__(self):\n" +
                "        return self\n" +
                "    def __exit__(self, *exc_info):\n" +
                "        return False\n" +
                "with (Manager(), Manager()):\n" +
                "    parenthesized_with = True\n" +
                "for first, *remaining in [(1, 2, 3)]:\n" +
                "    starred_for = (first, remaining)\n" +
                "try:\n" +
                "    raise ValueError('caught')\n" +
                "except ValueError, TypeError:\n" +
                "    bracketless_except = True\n" +
                "note_error = ValueError('note')\n" +
                "note_error.add_note('detail')\n" +
                "class HookBase:\n" +
                "    subclass_name = None\n" +
                "    def __init_subclass__(cls):\n" +
                "        HookBase.subclass_name = cls.__name__\n" +
                "class HookChild(HookBase):\n" +
                "    pass\n" +
                "class CooperativeHook:\n" +
                "    calls = []\n" +
                "    def __init_subclass__(cls, **kwargs):\n" +
                "        CooperativeHook.calls.append(cls.__name__)\n" +
                "        super().__init_subclass__(**kwargs)\n" +
                "class CooperativeChild(CooperativeHook):\n" +
                "    pass\n" +
                "class KeywordHook:\n" +
                "    keyword = None\n" +
                "    def __init_subclass__(cls, marker=None):\n" +
                "        KeywordHook.keyword = marker\n" +
                "class KeywordChild(KeywordHook, marker=42):\n" +
                "    pass\n" +
                "class InitSubclassMeta(type):\n" +
                "    pass\n" +
                "class MetaclassHook:\n" +
                "    called = False\n" +
                "    def __init_subclass__(cls, *args, **kwargs):\n" +
                "        MetaclassHook.called = True\n" +
                "        super().__init_subclass__(*args, **kwargs)\n" +
                "class MetaclassHookChild(MetaclassHook, metaclass=InitSubclassMeta):\n" +
                "    pass\n" +
                "from abc import ABCMeta\n" +
                "class ClassMethodHook(metaclass=ABCMeta):\n" +
                "    subclass_name = None\n" +
                "    @classmethod\n" +
                "    def __init_subclass__(cls, **kwargs):\n" +
                "        ClassMethodHook.subclass_name = cls.__name__\n" +
                "        super().__init_subclass__(**kwargs)\n" +
                "class ClassMethodHookChild(ClassMethodHook):\n" +
                "    pass\n" +
                "init_subclass_chain = []\n" +
                "class ChainHookFirst:\n" +
                "    def __init_subclass__(cls, **kwargs):\n" +
                "        init_subclass_chain.append('first:' + cls.__name__)\n" +
                "        super().__init_subclass__(**kwargs)\n" +
                "class ChainHookSecond:\n" +
                "    def __init_subclass__(cls, **kwargs):\n" +
                "        init_subclass_chain.append('second:' + cls.__name__)\n" +
                "        super().__init_subclass__(**kwargs)\n" +
                "class ChainHookChild(ChainHookFirst, ChainHookSecond):\n" +
                "    pass\n" +
                "def make_nested_class():\n" +
                "    class NestedOuter:\n" +
                "        class NestedInner:\n" +
                "            pass\n" +
                "    return NestedOuter\n" +
                "NestedOuter = make_nested_class()\n" +
                "class NamedDescriptor:\n" +
                "    def __set_name__(self, owner, name):\n" +
                "        self.owner_name = owner.__name__\n" +
                "        self.attribute_name = name\n" +
                "class Described:\n" +
                "    value = NamedDescriptor()\n" +
                "def identity_decorator(function):\n" +
                "    return function\n" +
                "@identity_decorator\n" +
                "def decorated():\n" +
                "    return 3\n" +
                "def continue_in_finally():\n" +
                "    for item in range(3):\n" +
                "        try:\n" +
                "            continue\n" +
                "        finally:\n" +
                "            pass\n" +
                "    return item\n" +
                "assert matched == (1, 2), 'matched'\n" +
                "assert sequence_match == (1, [2, 3], 4), 'sequence'\n" +
                "assert mapping_rest == {'b': 2}, 'mapping rest'\n" +
                "assert dotted_pattern, 'dotted match value'\n" +
                "assert merged == {'a': 1, 'b': 2}, 'merged'\n" +
                "assert updated == {'a': 1, 'b': 2}, 'updated'\n" +
                "assert list(updated) == ['a', 'b'], repr(list(updated))\n" +
                "assert unpacked_comprehension == [1, 2, 3], 'list unpack comprehension'\n" +
                "assert dict_unpacked_comprehension == {'a': 1, 'b': 2}, 'dict unpack comprehension'\n" +
                "assert zipped == [(1, 2)], 'zip'\n" +
                "assert zip_error == 'ValueError', 'zip strict'\n" +
                "assert parenthesized_with, 'parenthesized with'\n" +
                "assert starred_for == (1, [2, 3]), 'starred for'\n" +
                "assert bracketless_except, 'bracketless except'\n" +
                "assert note_error.__notes__ == ['detail'], 'notes'\n" +
                "assert HookBase.subclass_name == 'HookChild', 'init subclass'\n" +
                "assert CooperativeHook.calls == ['CooperativeChild'], 'cooperative init subclass'\n" +
                "assert KeywordHook.keyword == 42, 'init subclass keywords'\n" +
                "assert MetaclassHook.called, 'metaclass init subclass'\n" +
                "assert ClassMethodHook.subclass_name == 'ClassMethodHookChild', 'classmethod init subclass'\n" +
                "assert init_subclass_chain == ['first:ChainHookChild', 'second:ChainHookChild'], 'init subclass chain'\n" +
                "assert NestedOuter.NestedInner.__qualname__.endswith('.NestedOuter.NestedInner'), 'nested class qualname'\n" +
                "assert Described.value.owner_name == 'Described', 'set name owner'\n" +
                "assert Described.value.attribute_name == 'value', 'set name attribute'\n" +
                "assert decorated() == 3, 'decorator'\n" +
                "assert continue_in_finally() == 2, 'continue finally'\n");
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
        }
    }
}
