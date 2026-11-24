#nullable enable
using IronPython.Compiler;
using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace IronPython.Tests {
    [TestFixture]
    [NonParallelizable]
    public sealed class StructuralPatternMatchingTest {
        [Test]
        public void NonSequenceValuesDoNotRaiseDuringSequenceMatching() {
            string result = ExecuteGenerated(
                "def classify(value):\n" +
                "    match value:\n" +
                "        case [item]:\n" +
                "            return ('sequence', item)\n" +
                "        case _:\n" +
                "            return ('other', value)\n" +
                "result = repr((classify(7), classify('x'), classify((8,))))\n");

            Assert.That(
                result,
                Is.EqualTo("(('other', 7), ('other', 'x'), ('sequence', 8))"));
        }

        [Test]
        public void SequenceAndOrPatternsCaptureTheMatchedValue() {
            string result = ExecuteGenerated(
                "def capture(value):\n" +
                "    match value:\n" +
                "        case [item] | (item,):\n" +
                "            return item\n" +
                "    return None\n" +
                "result = repr((capture([7]), capture((8,)), capture(9)))\n");

            Assert.That(result, Is.EqualTo("(7, 8, None)"));
        }

        [Test]
        public void MappingPatternsRequireAMappingAndSupportRestCapture() {
            string result = ExecuteGenerated(
                "class Impostor:\n" +
                "    __getitem__ = lambda self, key: 1\n" +
                "    keys = lambda self: ['x']\n" +
                "def capture(value):\n" +
                "    match value:\n" +
                "        case {'x': item, **rest}:\n" +
                "            return item, rest\n" +
                "    return None\n" +
                "result = repr((capture({'x': 1, 'y': 2}), capture(Impostor())))\n");

            Assert.That(result, Is.EqualTo("((1, {'y': 2}), None)"));
        }

        [Test]
        public void ClassPatternsUsePositionalAndKeywordAttributes() {
            string result = ExecuteGenerated(
                "class Point:\n" +
                "    __match_args__ = ('x',)\n" +
                "    def __init__(self, x, y):\n" +
                "        self.x = x\n" +
                "        self.y = y\n" +
                "match Point(3, 4):\n" +
                "    case Point(x, y=value):\n" +
                "        result = repr((x, value))\n" +
                "    case _:\n" +
                "        result = 'None'\n");

            Assert.That(result, Is.EqualTo("(3, 4)"));
        }

        [Test]
        public void RegisteredSequenceAndMappingAbcsAreSupported() {
            string result = ExecuteGenerated(
                "import collections.abc\n" +
                "class CustomSequence(collections.abc.Sequence):\n" +
                "    def __len__(self):\n" +
                "        return 2\n" +
                "    def __getitem__(self, index):\n" +
                "        return (5, 6)[index]\n" +
                "class CustomMapping(collections.abc.Mapping):\n" +
                "    def __len__(self):\n" +
                "        return 2\n" +
                "    def __iter__(self):\n" +
                "        return iter(('x', 'y'))\n" +
                "    def __getitem__(self, key):\n" +
                "        return {'x': 7, 'y': 8}[key]\n" +
                "def capture(value):\n" +
                "    match value:\n" +
                "        case [left, right]:\n" +
                "            return ('sequence', left, right)\n" +
                "        case {'x': item, **rest}:\n" +
                "            return ('mapping', item, rest)\n" +
                "        case _:\n" +
                "            return None\n" +
                "result = repr((capture(CustomSequence()), capture(CustomMapping())))\n");

            Assert.That(
                result,
                Is.EqualTo("(('sequence', 5, 6), ('mapping', 7, {'y': 8}))"));
        }

        [TestCase("match [1, 2]:\n    case [x, x]:\n        pass\n", "multiple assignments")]
        [TestCase("match [1]:\n    case [x] | [y]:\n        pass\n", "different names")]
        [TestCase("match {'x': 1}:\n    case {'x': x, 'x': y}:\n        pass\n", "duplicate key")]
        public void RejectsInvalidPatternBindings(string source, string message) {
            Assert.That(
                () => ExecuteGenerated(source),
                Throws.TypeOf<SyntaxErrorException>()
                    .With.Property("Message").Contains(message));
        }

        private static string ExecuteGenerated(string source) {
            var engine = Python.CreateEngine(new Dictionary<string, object> {
                ["NoSite"] = true
            });
            string? configured = Environment.GetEnvironmentVariable(
                "CYBERSTONE_IRONPYTHON_STDLIB");
            string stdlib = !string.IsNullOrEmpty(configured)
                ? Path.Combine(configured, "lib")
                : Path.GetFullPath(Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "IronPython.StdLib",
                    "lib"));
            if (Directory.Exists(stdlib)) {
                engine.SetSearchPaths(new[] { stdlib });
            }

            ScriptScope scope = engine.CreateScope();
            bool previousParserMode = Parser.UseGeneratedParser;
            Parser.UseGeneratedParser = true;
            try {
                engine.Execute(source, scope);
            } finally {
                Parser.UseGeneratedParser = previousParserMode;
            }
            return scope.GetVariable<string>("result")
                ?? throw new InvalidOperationException("Pattern matching test did not set result.");
        }
    }
}
