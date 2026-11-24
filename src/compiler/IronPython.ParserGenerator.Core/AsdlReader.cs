using System;
using System.Collections.Generic;

namespace IronPython.ParserGenerator {
    public static class AsdlReader {
        public static AsdlModule Read(string text) {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return new Parser(text).Parse();
        }

        private sealed class Parser {
            private readonly List<AsdlToken> _tokens;
            private int _index;

            public Parser(string text) {
                _tokens = Lex(text);
            }

            public AsdlModule Parse() {
                ExpectIdentifier("module");
                string name = ReadIdentifier();
                Expect("{");
                var types = new List<AsdlTypeDefinition>();
                while (!Match("}")) {
                    if (Peek().Kind == AsdlTokenKind.End) {
                        throw Error("Unexpected end of ASDL while reading a type definition.");
                    }
                    types.Add(ParseTypeDefinition());
                }
                ExpectEnd();
                return new AsdlModule(name, types);
            }

            private AsdlTypeDefinition ParseTypeDefinition() {
                string name = ReadIdentifier();
                Expect("=");

                bool product = Match("(");
                if (product) {
                    IReadOnlyList<AsdlField> fields = ParseFieldsAfterOpenParenthesis();
                    IReadOnlyList<AsdlField> productAttributes = ParseAttributes();
                    return new AsdlTypeDefinition(
                        name,
                        true,
                        fields,
                        Array.Empty<AsdlConstructor>(),
                        productAttributes);
                }

                var constructors = new List<AsdlConstructor>();
                while (true) {
                    string constructorName = ReadIdentifier();
                    IReadOnlyList<AsdlField> fields;
                    if (Match("(")) {
                        fields = ParseFieldsAfterOpenParenthesis();
                    } else {
                        fields = Array.Empty<AsdlField>();
                    }
                    constructors.Add(new AsdlConstructor(constructorName, fields));
                    if (!Match("|")) break;
                }

                IReadOnlyList<AsdlField> attributes = ParseAttributes();
                return new AsdlTypeDefinition(
                    name,
                    false,
                    Array.Empty<AsdlField>(),
                    constructors,
                    attributes);
            }

            private IReadOnlyList<AsdlField> ParseAttributes() {
                if (!MatchIdentifier("attributes")) return Array.Empty<AsdlField>();
                Expect("(");
                return ParseFieldsAfterOpenParenthesis();
            }

            private IReadOnlyList<AsdlField> ParseFieldsAfterOpenParenthesis() {
                var fields = new List<AsdlField>();
                while (!Match(")")) {
                    string typeName = ReadIdentifier();
                    bool optional = Match("?");
                    bool sequence = Match("*");
                    string fieldName = ReadIdentifier();
                    fields.Add(new AsdlField(typeName, fieldName, optional, sequence));
                    if (Match(",")) continue;
                    Expect(")");
                    break;
                }
                return fields;
            }

            private AsdlToken Peek() => _tokens[_index];

            private bool Match(string value) {
                if (!string.Equals(Peek().Value, value, StringComparison.Ordinal)) return false;
                _index++;
                return true;
            }

            private bool MatchIdentifier(string value) {
                if (Peek().Kind != AsdlTokenKind.Identifier
                    || !string.Equals(Peek().Value, value, StringComparison.Ordinal)) {
                    return false;
                }
                _index++;
                return true;
            }

            private void Expect(string value) {
                if (!Match(value)) {
                    throw Error($"Expected '{value}', found '{Peek().Value}'.");
                }
            }

            private void ExpectIdentifier(string value) {
                if (!MatchIdentifier(value)) {
                    throw Error($"Expected '{value}', found '{Peek().Value}'.");
                }
            }

            private string ReadIdentifier() {
                AsdlToken token = Peek();
                if (token.Kind != AsdlTokenKind.Identifier) {
                    throw Error($"Expected an identifier, found '{token.Value}'.");
                }
                _index++;
                return token.Value;
            }

            private void ExpectEnd() {
                if (Peek().Kind != AsdlTokenKind.End) {
                    throw Error($"Unexpected token '{Peek().Value}'.");
                }
            }

            private FormatException Error(string message) {
                return new FormatException($"{message} Token index: {_index}.");
            }
        }

        private enum AsdlTokenKind {
            Identifier,
            Punctuation,
            End
        }

        private readonly struct AsdlToken {
            public AsdlToken(AsdlTokenKind kind, string value) {
                Kind = kind;
                Value = value;
            }

            public AsdlTokenKind Kind { get; }
            public string Value { get; }
        }

        private static List<AsdlToken> Lex(string text) {
            var tokens = new List<AsdlToken>();
            for (int index = 0; index < text.Length;) {
                char character = text[index];
                if (char.IsWhiteSpace(character)) {
                    index++;
                    continue;
                }
                if (character == '-' && index + 1 < text.Length && text[index + 1] == '-') {
                    index += 2;
                    while (index < text.Length && text[index] != '\n') index++;
                    continue;
                }
                if (char.IsLetter(character) || character == '_') {
                    int start = index++;
                    while (index < text.Length
                        && (char.IsLetterOrDigit(text[index]) || text[index] == '_')) {
                        index++;
                    }
                    tokens.Add(new AsdlToken(AsdlTokenKind.Identifier, text.Substring(start, index - start)));
                    continue;
                }
                if (character == '{' || character == '}' || character == '(' || character == ')'
                    || character == '=' || character == ',' || character == '|'
                    || character == '*' || character == '?') {
                    tokens.Add(new AsdlToken(AsdlTokenKind.Punctuation, character.ToString()));
                    index++;
                    continue;
                }
                throw new FormatException($"Unexpected ASDL character '{character}' at offset {index}.");
            }
            tokens.Add(new AsdlToken(AsdlTokenKind.End, "<end>"));
            return tokens;
        }
    }
}
