using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IronPython.ParserGenerator {
    public static class PegGrammarReader {
        public static PegGrammar Read(string text) {
            if (text == null) throw new ArgumentNullException(nameof(text));

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var rules = new List<PegGrammarRule>();
            RuleHeader? currentHeader = null;
            var currentBody = new StringBuilder();

            foreach (string line in normalized.Split('\n')) {
                if (TryReadRuleHeader(line, out RuleHeader? header)) {
                    if (currentHeader is not null) {
                        rules.Add(ParseRule(currentHeader, currentBody.ToString()));
                    }

                    currentHeader = header;
                    currentBody.Clear();
                    int colon = FindHeaderColon(line);
                    if (colon >= 0 && colon + 1 < line.Length) {
                        currentBody.AppendLine(line.Substring(colon + 1));
                    }
                    continue;
                }

                if (currentHeader is not null) {
                    currentBody.AppendLine(line);
                }
            }

            if (currentHeader is not null) {
                rules.Add(ParseRule(currentHeader, currentBody.ToString()));
            }

            return new PegGrammar(MarkLeftRecursiveRules(rules));
        }

        private static IReadOnlyList<PegGrammarRule> MarkLeftRecursiveRules(IReadOnlyList<PegGrammarRule> rules) {
            var byName = new Dictionary<string, PegGrammarRule>(StringComparer.Ordinal);
            foreach (PegGrammarRule rule in rules) {
                byName.Add(rule.Name, rule);
            }

            HashSet<string> nullable = FindNullableRules(byName);
            var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (PegGrammarRule rule in rules) {
                var references = new HashSet<string>(StringComparer.Ordinal);
                AddLeftReferences(rule.Alternatives, byName, nullable, references);
                edges.Add(rule.Name, references);
            }

            var leftRecursive = new HashSet<string>(StringComparer.Ordinal);
            var indirectRecursive = new HashSet<string>(StringComparer.Ordinal);
            foreach (string ruleName in byName.Keys) {
                if (HasPathTo(ruleName, ruleName, edges, new HashSet<string>(StringComparer.Ordinal))) {
                    leftRecursive.Add(ruleName);
                }
                if (HasIndirectPathToSelf(ruleName, edges)) {
                    indirectRecursive.Add(ruleName);
                }
            }

            var result = new List<PegGrammarRule>(rules.Count);
            foreach (PegGrammarRule rule in rules) {
                result.Add(new PegGrammarRule(
                    rule.Name,
                    rule.ReturnType,
                    rule.Memo || leftRecursive.Contains(rule.Name),
                    rule.Alternatives,
                    indirectRecursive.Contains(rule.Name),
                    leftRecursive.Contains(rule.Name)));
            }
            return result;
        }

        private static HashSet<string> FindNullableRules(IReadOnlyDictionary<string, PegGrammarRule> byName) {
            var nullable = new HashSet<string>(StringComparer.Ordinal);
            bool changed;
            do {
                changed = false;
                foreach (PegGrammarRule rule in byName.Values) {
                    if (nullable.Contains(rule.Name)) continue;
                    foreach (PegAlternative alternative in rule.Alternatives) {
                        bool allNullable = true;
                        foreach (PegElement element in alternative.Elements) {
                            if (!IsNullable(element, byName, nullable)) {
                                allNullable = false;
                                break;
                            }
                        }
                        if (allNullable) {
                            nullable.Add(rule.Name);
                            changed = true;
                            break;
                        }
                    }
                }
            } while (changed);
            return nullable;
        }

        private static bool IsNullable(
            PegElement element,
            IReadOnlyDictionary<string, PegGrammarRule> byName,
            ISet<string> nullableRules) {
            switch (element.Kind) {
                case PegElementKind.Name:
                    return nullableRules.Contains(element.Value!);
                case PegElementKind.String:
                    return false;
                case PegElementKind.Group:
                    return element.Alternatives is not null
                        && element.Alternatives.Any(alternative => IsNullable(alternative, byName, nullableRules));
                case PegElementKind.Optional:
                case PegElementKind.Repeat:
                case PegElementKind.PositiveLookahead:
                case PegElementKind.NegativeLookahead:
                case PegElementKind.Cut:
                    return true;
                case PegElementKind.RepeatOne:
                    return IsNullable(element.Child!, byName, nullableRules);
                case PegElementKind.Gather:
                case PegElementKind.Forced:
                    return IsNullable(element.Child!, byName, nullableRules);
                default:
                    return false;
            }
        }

        private static bool IsNullable(
            PegAlternative alternative,
            IReadOnlyDictionary<string, PegGrammarRule> byName,
            ISet<string> nullableRules) {
            foreach (PegElement element in alternative.Elements) {
                if (!IsNullable(element, byName, nullableRules)) return false;
            }
            return true;
        }

        private static void AddLeftReferences(
            IReadOnlyList<PegAlternative> alternatives,
            IReadOnlyDictionary<string, PegGrammarRule> byName,
            ISet<string> nullableRules,
            ISet<string> references) {
            foreach (PegAlternative alternative in alternatives) {
                foreach (PegElement element in alternative.Elements) {
                    AddElementLeftReferences(element, byName, nullableRules, references);
                    if (!IsNullable(element, byName, nullableRules)) break;
                }
            }
        }

        private static void AddElementLeftReferences(
            PegElement element,
            IReadOnlyDictionary<string, PegGrammarRule> byName,
            ISet<string> nullableRules,
            ISet<string> references) {
            if (element.Kind == PegElementKind.Name) {
                if (byName.ContainsKey(element.Value!)) {
                    references.Add(element.Value!);
                }
                return;
            }
            if (element.Alternatives is not null) {
                AddLeftReferences(element.Alternatives, byName, nullableRules, references);
            }
            if (element.Child is not null) {
                AddElementLeftReferences(element.Child, byName, nullableRules, references);
            }
        }

        private static bool HasPathTo(
            string current,
            string target,
            IReadOnlyDictionary<string, HashSet<string>> edges,
            ISet<string> visited) {
            if (!edges.TryGetValue(current, out HashSet<string>? next)) return false;
            foreach (string candidate in next) {
                if (string.Equals(candidate, target, StringComparison.Ordinal)) return true;
                if (visited.Add(candidate)
                    && HasPathTo(candidate, target, edges, visited)) {
                    return true;
                }
            }
            return false;
        }

        private static bool HasIndirectPathToSelf(
            string ruleName,
            IReadOnlyDictionary<string, HashSet<string>> edges) {
            if (!edges.TryGetValue(ruleName, out HashSet<string>? direct)) return false;
            foreach (string intermediate in direct) {
                if (string.Equals(intermediate, ruleName, StringComparison.Ordinal)) continue;
                if (HasPathTo(
                    intermediate,
                    ruleName,
                    edges,
                    new HashSet<string>(StringComparer.Ordinal) { ruleName })) {
                    return true;
                }
            }
            return false;
        }

        private static PegGrammarRule ParseRule(RuleHeader header, string body) {
            var parser = new BodyParser(body);
            try {
                return new PegGrammarRule(header.Name, header.ReturnType, header.Memo, parser.Parse());
            } catch (FormatException exception) {
                throw new FormatException($"Rule '{header.Name}': {exception.Message}", exception);
            }
        }

        private static bool TryReadRuleHeader(string line, out RuleHeader? header) {
            header = null;
            if (line.Length == 0 || char.IsWhiteSpace(line[0])) return false;

            int i = 0;
            while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
            if (i == 0) return false;

            string name = line.Substring(0, i);
            string? returnType = null;
            bool memo = false;

            SkipSpaces(line, ref i);
            if (i < line.Length && line[i] == '[') {
                int end = FindMatching(line, i, '[', ']');
                if (end < 0) return false;
                returnType = line.Substring(i + 1, end - i - 1).Trim();
                i = end + 1;
                SkipSpaces(line, ref i);
            }

            if (i + 6 <= line.Length && line.Substring(i, 6) == "(memo)") {
                memo = true;
                i += 6;
                SkipSpaces(line, ref i);
            }

            if (i >= line.Length || line[i] != ':') return false;
            header = new RuleHeader(name, returnType, memo);
            return true;
        }

        private static int FindHeaderColon(string line) {
            bool inString = false;
            char quote = '\0';
            int bracketDepth = 0;
            for (int i = 0; i < line.Length; i++) {
                char c = line[i];
                if (inString) {
                    if (c == '\\') {
                        i++;
                    } else if (c == quote) {
                        inString = false;
                    }
                } else if (c == '\'' || c == '"') {
                    inString = true;
                    quote = c;
                } else if (c == '[') {
                    bracketDepth++;
                } else if (c == ']') {
                    bracketDepth--;
                } else if (c == ':' && bracketDepth == 0) {
                    return i;
                }
            }
            return -1;
        }

        private static int FindMatching(string text, int start, char open, char close) {
            int depth = 0;
            for (int i = start; i < text.Length; i++) {
                if (text[i] == open) depth++;
                else if (text[i] == close && --depth == 0) return i;
            }
            return -1;
        }

        private static void SkipSpaces(string text, ref int index) {
            while (index < text.Length && (text[index] == ' ' || text[index] == '\t')) index++;
        }

        private sealed class RuleHeader {
            public RuleHeader(string name, string? returnType, bool memo) {
                Name = name;
                ReturnType = returnType;
                Memo = memo;
            }

            public string Name { get; }
            public string? ReturnType { get; }
            public bool Memo { get; }
        }

        private enum TokenKind {
            Identifier,
            String,
            Action,
            Colon,
            Pipe,
            Dot,
            Star,
            Plus,
            Question,
            Amp,
            Bang,
            Tilde,
            Dollar,
            Equals,
            LeftParen,
            RightParen,
            LeftBracket,
            RightBracket,
            End
        }

        private readonly struct Token {
            public Token(TokenKind kind, string value) {
                Kind = kind;
                Value = value;
            }

            public TokenKind Kind { get; }
            public string Value { get; }
        }

        private sealed class BodyParser {
            private readonly List<Token> _tokens;
            private int _index;

            public BodyParser(string body) {
                _tokens = Lex(body);
            }

            public IReadOnlyList<PegAlternative> Parse() {
                return ParseAlternatives(TokenKind.End);
            }

            private IReadOnlyList<PegAlternative> ParseAlternatives(TokenKind terminator) {
                var alternatives = new List<PegAlternative>();
                if (Peek().Kind == TokenKind.Pipe) {
                    _index++;
                }
                while (true) {
                    alternatives.Add(ParseAlternative(terminator));
                    if (Peek().Kind == TokenKind.Pipe) {
                        _index++;
                        continue;
                    }
                    break;
                }
                return alternatives;
            }

            private PegAlternative ParseAlternative(TokenKind terminator) {
                var elements = new List<PegElement>();
                string? action = null;
                bool endsWithEof = false;

                while (true) {
                    TokenKind kind = Peek().Kind;
                    if (kind == TokenKind.Pipe || kind == terminator) break;
                    if (kind == TokenKind.Action) {
                        action = Next().Value;
                        break;
                    }
                    if (kind == TokenKind.Dollar) {
                        _index++;
                        endsWithEof = true;
                        continue;
                    }

                    elements.Add(ParseNamedItem());
                }

                return new PegAlternative(elements, action, endsWithEof);
            }

            private PegElement ParseNamedItem() {
                if (Peek().Kind == TokenKind.Identifier && IsNamedItem()) {
                    string fieldName = Next().Value;
                    string? annotation = null;
                    if (Peek().Kind == TokenKind.LeftBracket) {
                        annotation = ConsumeBalanced(TokenKind.LeftBracket, TokenKind.RightBracket);
                    }
                    Expect(TokenKind.Equals);
                    PegElement item = ParseItem();
                    return CloneWithName(item, fieldName, annotation);
                }

                if (Peek().Kind == TokenKind.Amp) {
                    _index++;
                    if (Peek().Kind == TokenKind.Amp) {
                        _index++;
                        return new PegElement(PegElementKind.Forced, child: ParseAtom());
                    }
                    return new PegElement(PegElementKind.PositiveLookahead, child: ParseAtom());
                }

                if (Peek().Kind == TokenKind.Bang) {
                    _index++;
                    return new PegElement(PegElementKind.NegativeLookahead, child: ParseAtom());
                }

                if (Peek().Kind == TokenKind.Tilde) {
                    _index++;
                    return new PegElement(PegElementKind.Cut, value: "~");
                }

                return ParseItem();
            }

            private PegElement ParseItem() {
                PegElement atom = ParseAtom();

                if (Peek().Kind == TokenKind.Question) {
                    _index++;
                    return new PegElement(PegElementKind.Optional, child: atom);
                }
                if (Peek().Kind == TokenKind.Star) {
                    _index++;
                    return new PegElement(PegElementKind.Repeat, child: atom);
                }
                if (Peek().Kind == TokenKind.Plus) {
                    _index++;
                    return new PegElement(PegElementKind.RepeatOne, child: atom);
                }
                if (Peek().Kind == TokenKind.Dot && IsAtomStart(Peek(1).Kind)) {
                    int gatherStart = _index;
                    _index++;
                    PegElement node = ParseAtom();
                    if (Peek().Kind == TokenKind.Plus) {
                        _index++;
                        return new PegElement(PegElementKind.Gather, child: node, separator: atom);
                    }
                    _index = gatherStart;
                }

                return atom;
            }

            private PegElement ParseAtom() {
                Token token = Next();
                switch (token.Kind) {
                    case TokenKind.Identifier:
                        return new PegElement(PegElementKind.Name, value: token.Value);
                    case TokenKind.String:
                        return new PegElement(PegElementKind.String, value: token.Value);
                    case TokenKind.LeftBracket:
                        IReadOnlyList<PegAlternative> optional = ParseAlternatives(TokenKind.RightBracket);
                        Expect(TokenKind.RightBracket);
                        return new PegElement(PegElementKind.Optional, alternatives: optional);
                    case TokenKind.LeftParen:
                        IReadOnlyList<PegAlternative> group = ParseAlternatives(TokenKind.RightParen);
                        Expect(TokenKind.RightParen);
                        return new PegElement(PegElementKind.Group, alternatives: group);
                    default:
                        throw new FormatException($"Unexpected grammar token '{token.Value}'.");
                }
            }

            private bool IsNamedItem() {
                int i = _index + 1;
                if (i < _tokens.Count && _tokens[i].Kind == TokenKind.LeftBracket) {
                    int depth = 0;
                    for (; i < _tokens.Count; i++) {
                        if (_tokens[i].Kind == TokenKind.LeftBracket) depth++;
                        else if (_tokens[i].Kind == TokenKind.RightBracket && --depth == 0) {
                            i++;
                            break;
                        }
                    }
                }
                return i < _tokens.Count && _tokens[i].Kind == TokenKind.Equals;
            }

            private string ConsumeBalanced(TokenKind open, TokenKind close) {
                Expect(open);
                var value = new StringBuilder();
                int depth = 1;
                while (depth > 0) {
                    Token token = Next();
                    if (token.Kind == TokenKind.End) throw new FormatException("Unterminated annotation.");
                    if (token.Kind == open) depth++;
                    else if (token.Kind == close) {
                        depth--;
                        if (depth == 0) break;
                    }
                    if (value.Length > 0) value.Append(' ');
                    value.Append(token.Value);
                }
                return value.ToString();
            }

            private static PegElement CloneWithName(PegElement element, string fieldName, string? annotation) {
                return new PegElement(
                    element.Kind,
                    element.Value,
                    fieldName,
                    annotation,
                    element.Alternatives,
                    element.Child,
                    element.Separator);
            }

            private static bool IsAtomStart(TokenKind kind) {
                return kind == TokenKind.Identifier || kind == TokenKind.String ||
                    kind == TokenKind.LeftParen || kind == TokenKind.LeftBracket;
            }

            private Token Peek(int offset = 0) {
                int index = _index + offset;
                return index < _tokens.Count ? _tokens[index] : new Token(TokenKind.End, "<end>");
            }

            private Token Next() {
                Token token = Peek();
                if (_index < _tokens.Count) _index++;
                return token;
            }

            private void Expect(TokenKind kind) {
                Token token = Next();
                if (token.Kind != kind) {
                    throw new FormatException($"Expected {kind}, got {token.Kind} '{token.Value}'.");
                }
            }
        }

        private static List<Token> Lex(string text) {
            var tokens = new List<Token>();
            int i = 0;
            while (i < text.Length) {
                char c = text[i];
                if (char.IsWhiteSpace(c)) {
                    i++;
                    continue;
                }
                if (c == '#') {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (c == '{') {
                    tokens.Add(new Token(TokenKind.Action, ReadAction(text, ref i)));
                    continue;
                }
                if (c == '\'' || c == '"') {
                    tokens.Add(new Token(TokenKind.String, ReadString(text, ref i)));
                    continue;
                }
                if (char.IsLetter(c) || c == '_') {
                    int start = i++;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    tokens.Add(new Token(TokenKind.Identifier, text.Substring(start, i - start)));
                    continue;
                }

                TokenKind kind = c switch {
                    ':' => TokenKind.Colon,
                    '|' => TokenKind.Pipe,
                    '.' => TokenKind.Dot,
                    '*' => TokenKind.Star,
                    '+' => TokenKind.Plus,
                    '?' => TokenKind.Question,
                    '&' => TokenKind.Amp,
                    '!' => TokenKind.Bang,
                    '~' => TokenKind.Tilde,
                    '$' => TokenKind.Dollar,
                    '=' => TokenKind.Equals,
                    '(' => TokenKind.LeftParen,
                    ')' => TokenKind.RightParen,
                    '[' => TokenKind.LeftBracket,
                    ']' => TokenKind.RightBracket,
                    _ => throw new FormatException($"Unexpected character '{c}' in grammar.")
                };
                tokens.Add(new Token(kind, c.ToString()));
                i++;
            }
            return tokens;
        }

        private static string ReadString(string text, ref int index) {
            char quote = text[index++];
            var value = new StringBuilder();
            while (index < text.Length) {
                char c = text[index++];
                if (c == quote) return value.ToString();
                if (c == '\\' && index < text.Length) {
                    value.Append(c);
                    value.Append(text[index++]);
                } else {
                    value.Append(c);
                }
            }
            throw new FormatException("Unterminated string in grammar.");
        }

        private static string ReadAction(string text, ref int index) {
            int start = index;
            int depth = 0;
            char quote = '\0';
            bool lineComment = false;
            bool blockComment = false;

            while (index < text.Length) {
                char c = text[index];
                char next = index + 1 < text.Length ? text[index + 1] : '\0';

                if (lineComment) {
                    if (c == '\n') lineComment = false;
                    index++;
                    continue;
                }
                if (blockComment) {
                    if (c == '*' && next == '/') {
                        blockComment = false;
                        index += 2;
                    } else {
                        index++;
                    }
                    continue;
                }
                if (quote != '\0') {
                    if (c == '\\') index += 2;
                    else {
                        if (c == quote) quote = '\0';
                        index++;
                    }
                    continue;
                }

                if (c == '/' && next == '/') {
                    lineComment = true;
                    index += 2;
                    continue;
                }
                if (c == '/' && next == '*') {
                    blockComment = true;
                    index += 2;
                    continue;
                }
                if (c == '\'' || c == '"') {
                    quote = c;
                    index++;
                    continue;
                }
                if (c == '{') {
                    depth++;
                } else if (c == '}' && --depth == 0) {
                    index++;
                    return text.Substring(start, index - start);
                }
                index++;
            }
            throw new FormatException("Unterminated action block in grammar.");
        }
    }
}
