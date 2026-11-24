using IronPython.Compiler;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting.Hosting;
using NUnit.Framework;
using System.Collections.Generic;

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
        public void WindowsSubprocessConstantsAreComplete() {
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
                    "pair_text = str(pair_alias)\n",
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
