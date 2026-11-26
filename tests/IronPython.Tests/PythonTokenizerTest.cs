using IronPython.Compiler.Syntax;
using NUnit.Framework;
using System;
using System.Linq;
using System.Text;
using Assert = NUnit.Framework.Assert;
using Is = NUnit.Framework.Is;

namespace IronPython.Tests {
    [TestFixture]
    public sealed class PythonTokenizerTest {
        [Test]
        public void EmitsIndentNewLineDedentAndEndMarker() {
            PythonTokenizer tokenizer = Tokenize("if x:\n    y = 1\n");

            Assert.That(
                tokenizer.Tokens.Where(token => token.Kind != PythonTokenKind.TypeComment)
                    .Select(token => token.Kind),
                Is.EqualTo(new[] {
                    PythonTokenKind.Name,
                    PythonTokenKind.Name,
                    PythonTokenKind.Operator,
                    PythonTokenKind.NewLine,
                    PythonTokenKind.Indent,
                    PythonTokenKind.Name,
                    PythonTokenKind.Operator,
                    PythonTokenKind.Number,
                    PythonTokenKind.NewLine,
                    PythonTokenKind.Dedent,
                    PythonTokenKind.EndMarker
                }));
        }

        [Test]
        public void MarksSoftKeywordsWithoutChangingTheirTokenKind() {
            PythonTokenizer tokenizer = Tokenize("match case type _ while\n");
            PythonToken[] words = tokenizer.Tokens
                .Where(token => token.Kind == PythonTokenKind.Name)
                .ToArray();

            Assert.That(words.Take(4).All(token => token.IsSoftKeyword), Is.True);
            Assert.That(words[4].IsSoftKeyword, Is.False);
        }

        [Test]
        public void EmitsFStringSegmentsAndNestedExpressionTokens() {
            PythonTokenizer tokenizer = Tokenize("f\"x={value}\"\n");

            Assert.That(
                tokenizer.Tokens.Take(6).Select(token => token.Kind),
                Is.EqualTo(new[] {
                    PythonTokenKind.FStringStart,
                    PythonTokenKind.FStringMiddle,
                    PythonTokenKind.Operator,
                    PythonTokenKind.Name,
                    PythonTokenKind.Operator,
                    PythonTokenKind.FStringEnd
                }));
            Assert.That(tokenizer.Tokens[1].Text, Is.EqualTo("x="));
            Assert.That(tokenizer.Tokens[2].Text, Is.EqualTo("{"));
            Assert.That(tokenizer.Tokens[3].Text, Is.EqualTo("value"));
            Assert.That(tokenizer.Tokens[4].Text, Is.EqualTo("}"));
        }

        [Test]
        public void EmitsTStringSegments() {
            PythonTokenizer tokenizer = Tokenize("t\"hello {name}\"\n");

            Assert.That(tokenizer.Tokens.Any(token => token.Kind == PythonTokenKind.TStringStart), Is.True);
            Assert.That(tokenizer.Tokens.Any(token => token.Kind == PythonTokenKind.TStringMiddle), Is.True);
            Assert.That(tokenizer.Tokens.Any(token => token.Kind == PythonTokenKind.TStringEnd), Is.True);
        }

        [Test]
        public void TokenizesNestedFStringExpressions() {
            PythonTokenizer tokenizer = Tokenize("f\"{prefix + f'{value}'}\"\n");
            int outerStart = IndexOfKind(tokenizer, PythonTokenKind.FStringStart, 0);
            int innerStart = IndexOfKind(tokenizer, PythonTokenKind.FStringStart, outerStart + 1);
            int innerEnd = IndexOfKind(tokenizer, PythonTokenKind.FStringEnd, innerStart + 1);
            int outerEnd = IndexOfKindFromEnd(tokenizer, PythonTokenKind.FStringEnd);

            Assert.That(innerStart, Is.GreaterThan(outerStart));
            Assert.That(innerEnd, Is.GreaterThan(innerStart));
            Assert.That(outerEnd, Is.GreaterThan(innerEnd));
        }

        [Test]
        public void MatchesCpythonCodepointColumnsAndTracksUtf8ByteColumns() {
            PythonTokenizer tokenizer = Tokenize("é = 1\n");
            PythonToken name = tokenizer.Tokens[0];
            PythonToken op = tokenizer.Tokens[1];
            PythonToken number = tokenizer.Tokens[2];

            Assert.That(name.StartColumn, Is.EqualTo(0));
            Assert.That(name.EndColumn, Is.EqualTo(1));
            Assert.That(op.StartColumn, Is.EqualTo(2));
            Assert.That(number.StartColumn, Is.EqualTo(4));
            Assert.That(name.EndByteColumn, Is.EqualTo(2));
            Assert.That(op.StartByteColumn, Is.EqualTo(3));
            Assert.That(number.StartByteColumn, Is.EqualTo(5));
        }

        [Test]
        public void DecodesPythonEncodingCookies() {
            byte[] bytes = Encoding.ASCII.GetBytes("# coding: latin-1\nx = '");
            byte[] suffix = Encoding.ASCII.GetBytes("'\n");
            byte[] source = new byte[bytes.Length + 1 + suffix.Length];
            Buffer.BlockCopy(bytes, 0, source, 0, bytes.Length);
            source[bytes.Length] = 0xE9;
            Buffer.BlockCopy(suffix, 0, source, bytes.Length + 1, suffix.Length);

            PythonSourceText decoded = PythonSourceText.Decode(source);

            Assert.That(decoded.Encoding.CodePage, Is.EqualTo(28591));
            Assert.That(decoded.Text, Does.Contain("'é'"));
        }

        [Test]
        public void ReportsIncompleteInterpolatedInput() {
            PythonTokenizer tokenizer = Tokenize("f\"{value");

            Assert.That(tokenizer.NeedsMoreInput, Is.True);
        }

        [Test]
        public void RecoversFromInvalidCharactersWithoutStopping() {
            PythonTokenizer tokenizer = Tokenize("x ? y\n");

            Assert.That(tokenizer.Tokens.Any(token => token.Kind == PythonTokenKind.Error), Is.True);
            Assert.That(tokenizer.Tokens.Count(token => token.Kind == PythonTokenKind.Name), Is.EqualTo(2));
            Assert.That(
                tokenizer.Tokens[tokenizer.Tokens.Count - 1].Kind,
                Is.EqualTo(PythonTokenKind.EndMarker));
        }

        [TestCase("0xEC30A437.to_bytes")]
        [TestCase("0o755.bit_length")]
        [TestCase("0b1010.bit_length")]
        public void SplitsAttributeAccessAfterBasedIntegerLiteral(string source) {
            PythonTokenizer tokenizer = Tokenize(source);

            Assert.That(
                tokenizer.Tokens.Take(3).Select(token => (token.Kind, token.Text)),
                Is.EqualTo(new[] {
                    (PythonTokenKind.Number, source.Substring(0, source.IndexOf('.'))),
                    (PythonTokenKind.Operator, "."),
                    (PythonTokenKind.Name, source.Substring(source.IndexOf('.') + 1))
                }));
        }

        private static PythonTokenizer Tokenize(string source) {
            var tokenizer = new PythonTokenizer(source);
            tokenizer.Tokenize();
            return tokenizer;
        }

        private static int IndexOfKind(
            PythonTokenizer tokenizer,
            PythonTokenKind kind,
            int start) {
            for (int i = start; i < tokenizer.Tokens.Count; i++) {
                if (tokenizer.Tokens[i].Kind == kind) return i;
            }
            return -1;
        }

        private static int IndexOfKindFromEnd(PythonTokenizer tokenizer, PythonTokenKind kind) {
            for (int i = tokenizer.Tokens.Count - 1; i >= 0; i--) {
                if (tokenizer.Tokens[i].Kind == kind) return i;
            }
            return -1;
        }
    }
}
