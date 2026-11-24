using IronPython.Compiler.Syntax;
using NUnit.Framework;
using System;
using System.Linq;
using Assert = NUnit.Framework.Assert;
using Is = NUnit.Framework.Is;

namespace IronPython.Tests {
    [TestFixture]
    public sealed class Cpython315TokenizerDifferentialTest {
        [Test]
        public void MatchesCpython315ForIndentation() {
            AssertTokens(
                "if x:\n    y = 1\n",
                ("NAME", "if", 1, 0, 1, 2),
                ("NAME", "x", 1, 3, 1, 4),
                ("OP", ":", 1, 4, 1, 5),
                ("NEWLINE", "\n", 1, 5, 1, 6),
                ("INDENT", "    ", 2, 0, 2, 4),
                ("NAME", "y", 2, 4, 2, 5),
                ("OP", "=", 2, 6, 2, 7),
                ("NUMBER", "1", 2, 8, 2, 9),
                ("NEWLINE", "\n", 2, 9, 2, 10),
                ("DEDENT", "", 3, 0, 3, 0),
                ("ENDMARKER", "", 3, 0, 3, 0));
        }

        [Test]
        public void MatchesCpython315ForFStrings() {
            AssertTokens(
                "f\"x={value}\"\n",
                ("FSTRING_START", "f\"", 1, 0, 1, 2),
                ("FSTRING_MIDDLE", "x=", 1, 2, 1, 4),
                ("OP", "{", 1, 4, 1, 5),
                ("NAME", "value", 1, 5, 1, 10),
                ("OP", "}", 1, 10, 1, 11),
                ("FSTRING_END", "\"", 1, 11, 1, 12),
                ("NEWLINE", "\n", 1, 12, 1, 13),
                ("ENDMARKER", "", 2, 0, 2, 0));
        }

        [Test]
        public void MatchesCpython315ForNestedFStrings() {
            AssertTokens(
                "f\"{prefix + f'{value}'}\"\n",
                ("FSTRING_START", "f\"", 1, 0, 1, 2),
                ("OP", "{", 1, 2, 1, 3),
                ("NAME", "prefix", 1, 3, 1, 9),
                ("OP", "+", 1, 10, 1, 11),
                ("FSTRING_START", "f'", 1, 12, 1, 14),
                ("OP", "{", 1, 14, 1, 15),
                ("NAME", "value", 1, 15, 1, 20),
                ("OP", "}", 1, 20, 1, 21),
                ("FSTRING_END", "'", 1, 21, 1, 22),
                ("OP", "}", 1, 22, 1, 23),
                ("FSTRING_END", "\"", 1, 23, 1, 24),
                ("NEWLINE", "\n", 1, 24, 1, 25),
                ("ENDMARKER", "", 2, 0, 2, 0));
        }

        [Test]
        public void MatchesCpython315ForTStringsAndUnicodeColumns() {
            AssertTokens(
                "t\"hello {name}\"\n",
                ("TSTRING_START", "t\"", 1, 0, 1, 2),
                ("TSTRING_MIDDLE", "hello ", 1, 2, 1, 8),
                ("OP", "{", 1, 8, 1, 9),
                ("NAME", "name", 1, 9, 1, 13),
                ("OP", "}", 1, 13, 1, 14),
                ("TSTRING_END", "\"", 1, 14, 1, 15),
                ("NEWLINE", "\n", 1, 15, 1, 16),
                ("ENDMARKER", "", 2, 0, 2, 0));

            AssertTokens(
                "é = 1\n",
                ("NAME", "é", 1, 0, 1, 1),
                ("OP", "=", 1, 2, 1, 3),
                ("NUMBER", "1", 1, 4, 1, 5),
                ("NEWLINE", "\n", 1, 5, 1, 6),
                ("ENDMARKER", "", 2, 0, 2, 0));
        }

        private static void AssertTokens(
            string source,
            params (string Kind, string Text, int StartLine, int StartColumn, int EndLine, int EndColumn)[] expected) {
            var tokenizer = new PythonTokenizer(source);
            tokenizer.Tokenize();

            Assert.That(tokenizer.Tokens.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++) {
                PythonToken actual = tokenizer.Tokens[i];
                Assert.Multiple(() => {
                    Assert.That(actual.GrammarKind, Is.EqualTo(expected[i].Kind), $"kind at token {i}");
                    Assert.That(actual.Text, Is.EqualTo(expected[i].Text), $"text at token {i}");
                    Assert.That(
                        new[] { actual.StartLine, actual.StartColumn, actual.EndLine, actual.EndColumn },
                        Is.EqualTo(new[] {
                            expected[i].StartLine,
                            expected[i].StartColumn,
                            expected[i].EndLine,
                            expected[i].EndColumn
                        }),
                        $"span at token {i}");
                });
            }
        }
    }
}
