using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace IronPython.Compiler.Syntax {
    public enum PythonTokenKind {
        Name,
        Number,
        String,
        FStringStart,
        FStringMiddle,
        FStringEnd,
        TStringStart,
        TStringMiddle,
        TStringEnd,
        Operator,
        NewLine,
        NonLogicalNewLine,
        Indent,
        Dedent,
        TypeComment,
        Error,
        EndMarker
    }

    public sealed class PythonToken {
        public PythonToken(
            PythonTokenKind kind,
            string text,
            string literalValue,
            int startOffset,
            int endOffset,
            int startLine,
            int startColumn,
            int endLine,
            int endColumn,
            bool isSoftKeyword = false,
            int? startByteColumn = null,
            int? endByteColumn = null,
            bool isKeyword = false,
            string? sourceText = null) {
            Kind = kind;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            LiteralValue = literalValue ?? throw new ArgumentNullException(nameof(literalValue));
            StartOffset = startOffset;
            EndOffset = endOffset;
            StartLine = startLine;
            StartColumn = startColumn;
            StartByteColumn = startByteColumn ?? startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
            EndByteColumn = endByteColumn ?? endColumn;
            IsSoftKeyword = isSoftKeyword;
            IsKeyword = isKeyword;
            SourceText = sourceText ?? Text;
        }

        public PythonTokenKind Kind { get; }
        public string Text { get; }
        public string SourceText { get; }
        public string LiteralValue { get; }
        public int StartOffset { get; }
        public int EndOffset { get; }
        public int StartLine { get; }
        public int StartColumn { get; }
        public int StartByteColumn { get; }
        public int EndLine { get; }
        public int EndColumn { get; }
        public int EndByteColumn { get; }
        public bool IsSoftKeyword { get; }
        public bool IsKeyword { get; }

        public string GrammarKind => Kind switch {
            PythonTokenKind.Name => "NAME",
            PythonTokenKind.Number => "NUMBER",
            PythonTokenKind.String => "STRING",
            PythonTokenKind.FStringStart => "FSTRING_START",
            PythonTokenKind.FStringMiddle => "FSTRING_MIDDLE",
            PythonTokenKind.FStringEnd => "FSTRING_END",
            PythonTokenKind.TStringStart => "TSTRING_START",
            PythonTokenKind.TStringMiddle => "TSTRING_MIDDLE",
            PythonTokenKind.TStringEnd => "TSTRING_END",
            PythonTokenKind.Operator => "OP",
            PythonTokenKind.NewLine => "NEWLINE",
            PythonTokenKind.NonLogicalNewLine => "NL",
            PythonTokenKind.Indent => "INDENT",
            PythonTokenKind.Dedent => "DEDENT",
            PythonTokenKind.TypeComment => "TYPE_COMMENT",
            PythonTokenKind.Error => "ERRORTOKEN",
            PythonTokenKind.EndMarker => "ENDMARKER",
            _ => Kind.ToString().ToUpperInvariant()
        };

        public PegToken ToPegToken() {
            string kind = IsKeyword ? "KEYWORD" : GrammarKind;
            return new PegToken(
                kind,
                LiteralValue,
                StartOffset,
                EndOffset,
                StartLine,
                StartColumn,
                EndLine,
                EndColumn,
                StartByteColumn,
                EndByteColumn,
                Text,
                SourceText);
        }
    }

    public sealed class PythonSourceText {
        private PythonSourceText(string text, Encoding encoding) {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        }

        public string Text { get; }
        public Encoding Encoding { get; }

        public static PythonSourceText FromString(string text, Encoding? encoding = null) {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return new PythonSourceText(text, encoding ?? new UTF8Encoding(false, true));
        }

        public static PythonSourceText Decode(byte[] bytes, Encoding? defaultEncoding = null) {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
                return new PythonSourceText(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), Encoding.UTF8);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) {
                return new PythonSourceText(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) {
                return new PythonSourceText(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);
            }

            Encoding encoding = ResolveEncoding(ReadEncodingCookie(bytes), defaultEncoding);
            return new PythonSourceText(encoding.GetString(bytes), encoding);
        }

        private static string? ReadEncodingCookie(byte[] bytes) {
            int lineCount = 0;
            for (int start = 0; start < bytes.Length && lineCount < 2;) {
                int end = start;
                while (end < bytes.Length && bytes[end] != (byte)'\n' && bytes[end] != (byte)'\r') end++;
                string line = Encoding.ASCII.GetString(bytes, start, end - start);
                int marker = line.IndexOf("coding", StringComparison.Ordinal);
                if (marker >= 0) {
                    int separator = marker + 6;
                    while (separator < line.Length && char.IsWhiteSpace(line[separator])) separator++;
                    if (separator < line.Length && (line[separator] == ':' || line[separator] == '=')) {
                        separator++;
                        while (separator < line.Length && char.IsWhiteSpace(line[separator])) separator++;
                        int nameStart = separator;
                        while (separator < line.Length
                            && (char.IsLetterOrDigit(line[separator])
                                || line[separator] == '-'
                                || line[separator] == '_'
                                || line[separator] == '.')) {
                            separator++;
                        }
                        if (separator > nameStart) {
                            return line.Substring(nameStart, separator - nameStart);
                        }
                    }
                }

                if (end >= bytes.Length) break;
                if (bytes[end] == (byte)'\r' && end + 1 < bytes.Length && bytes[end + 1] == (byte)'\n') end++;
                start = end + 1;
                lineCount++;
            }
            return null;
        }

        private static Encoding ResolveEncoding(string? name, Encoding? fallback) {
            if (name == null) return fallback ?? new UTF8Encoding(false, true);
            string normalized = name.Replace('_', '-').ToLowerInvariant();
            switch (normalized) {
                case "utf-8":
                case "utf8":
                    return new UTF8Encoding(false, true);
                case "ascii":
                case "us-ascii":
                    return Encoding.ASCII;
                case "latin-1":
                case "iso-8859-1":
                    return Encoding.Latin1;
                case "utf-16":
                case "utf-16-le":
                    return Encoding.Unicode;
                case "utf-16-be":
                    return Encoding.BigEndianUnicode;
                default:
                    throw new PythonTokenizationException($"Unknown source encoding '{name}'.");
            }
        }
    }

    public sealed class PythonTokenizer : IPegTokenSource {
        private static readonly HashSet<string> SoftKeywords =
            new HashSet<string>(StringComparer.Ordinal) { "match", "case", "type", "_" };

        private static readonly HashSet<string> HardKeywords =
            new HashSet<string>(StringComparer.Ordinal) {
                "False", "None", "True", "and", "as", "assert", "async", "await",
                "break", "class", "continue", "def", "del", "elif", "else", "except",
                "finally", "for", "from", "global", "if", "import", "in", "is",
                "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try",
                "while", "with", "yield"
            };

        private static readonly string[] Operators = {
            "**=", "//=", "<<=", ">>=", "...", ":=", "->", "**", "//", "<<", ">>",
            "<=", ">=", "==", "!=", "!", "<>", "+=", "-=", "*=", "/=", "%=", "@=", "&=",
            "|=", "^=", "(", ")", "[", "]", "{", "}", ":", ",", ";", ".", "+", "-",
            "*", "/", "%", "@", "&", "|", "^", "~", "<", ">", "="
        };

        private readonly string _source;
        private readonly List<PythonToken> _tokens = new List<PythonToken>();
        private readonly List<int> _lineStartOffsets = new List<int> { 0 };
        private readonly Stack<int> _indentation = new Stack<int>();
        private readonly Stack<char> _brackets = new Stack<char>();
        private int _index;
        private int _line = 1;
        private int _column;
        private int _tokenIndex;
        private bool _atLineStart = true;
        private bool _lineHasContent;
        private bool _reportedFinalNewLine;
        private bool _ended;

        public PythonTokenizer(PythonSourceText source) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _source = source.Text;
            _indentation.Push(0);
        }

        public PythonTokenizer(string source) : this(PythonSourceText.FromString(source)) { }

        public int Position {
            get => _tokenIndex;
            set {
                if (value < 0 || value > _tokens.Count) {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                _tokenIndex = value;
            }
        }

        public bool NeedsMoreInput { get; private set; }
        public IReadOnlyList<PythonToken> Tokens => _tokens;

        public PegToken Peek() {
            EnsureToken(_tokenIndex);
            SkipNonLogicalNewLines();
            return _tokens[Math.Min(_tokenIndex, _tokens.Count - 1)].ToPegToken();
        }

        public PegToken Next() {
            EnsureToken(_tokenIndex);
            SkipNonLogicalNewLines();
            PythonToken token = _tokens[Math.Min(_tokenIndex, _tokens.Count - 1)];
            if (token.Kind != PythonTokenKind.EndMarker) _tokenIndex++;
            return token.ToPegToken();
        }

        private void SkipNonLogicalNewLines() {
            while (_tokens[_tokenIndex].Kind == PythonTokenKind.NonLogicalNewLine) {
                _tokenIndex++;
                EnsureToken(_tokenIndex);
            }
        }

        public PythonToken NextToken() {
            EnsureToken(_tokenIndex);
            return _tokens[_tokenIndex++];
        }

        public IReadOnlyList<PythonToken> Tokenize() {
            while (!_ended) EnsureToken(_tokens.Count);
            return _tokens;
        }

        private void EnsureToken(int index) {
            while (_tokens.Count <= index && !_ended) {
                LexNext();
            }
            if (index >= _tokens.Count) {
                PythonToken end = EndMarker(_index, _line, _column);
                _tokens.Add(end);
                _ended = true;
            }
        }

        private void LexNext() {
            if (_atLineStart && _brackets.Count == 0) {
                if (ProcessLineStart()) return;
            }
            SkipHorizontalWhitespace();

            if (_index >= _source.Length) {
                FinishInput();
                return;
            }

            char current = _source[_index];
            if (current == '#') {
                LexComment();
                return;
            }
            if (IsNewLine(current)) {
                LexNewLine();
                return;
            }
            if (current == '\\' && IsNewLine(Peek(1))) {
                AdvanceChar();
                ConsumeNewLine();
                return;
            }
            if (TryLexInterpolatedString()) return;
            if (TryLexString()) return;
            if (IsIdentifierStart(current)) {
                LexIdentifier();
                return;
            }
            if (char.IsDigit(current) || (current == '.' && Peek(1) >= '0' && Peek(1) <= '9')) {
                LexNumber();
                return;
            }
            if (LexOperator()) return;

            int start = _index;
            int startLine = _line;
            int startColumn = _column;
            AdvanceChar();
            AddToken(
                PythonTokenKind.Error,
                start,
                startLine,
                startColumn,
                "invalid character");
        }

        private bool ProcessLineStart() {
            int start = _index;
            int indentation = 0;
            while (_index < _source.Length && (_source[_index] == ' ' || _source[_index] == '\t' || _source[_index] == '\f')) {
                if (_source[_index] == ' ') indentation++;
                else if (_source[_index] == '\t') indentation = (indentation / 8 + 1) * 8;
                else indentation = 0;
                AdvanceChar();
            }

            if (_index >= _source.Length) return false;
            if (_source[_index] == '#') {
                while (_index < _source.Length && !IsNewLine(_source[_index])) AdvanceChar();
                if (_index < _source.Length) {
                    int line = _line;
                    int column = _column;
                    int tokenStart = _index;
                    ConsumeNewLine();
                    _tokens.Add(new PythonToken(
                        PythonTokenKind.NonLogicalNewLine,
                        _source.Substring(tokenStart, _index - tokenStart),
                        "\n",
                        tokenStart,
                        _index,
                        line,
                        column,
                        line,
                        column + CountCodePoints(
                            _source.Substring(tokenStart, _index - tokenStart))));
                }
                _atLineStart = true;
                return true;
            }

            _atLineStart = false;
            if (_source[_index] == '\n' || _source[_index] == '\r') {
                _index = start;
                _column = 0;
                return false;
            }

            int previous = _indentation.Peek();
            if (indentation > previous) {
                _indentation.Push(indentation);
                _tokens.Add(new PythonToken(
                    PythonTokenKind.Indent,
                    _source.Substring(start, _index - start),
                    string.Empty,
                    start,
                    _index,
                    _line,
                    0,
                    _line,
                    _column));
            } else if (indentation < previous) {
                while (_indentation.Count > 1 && indentation < _indentation.Peek()) {
                    _indentation.Pop();
                    _tokens.Add(new PythonToken(
                        PythonTokenKind.Dedent,
                        string.Empty,
                        string.Empty,
                        _index,
                        _index,
                        _line,
                        _column,
                        _line,
                        _column));
                }
                if (indentation != _indentation.Peek()) {
                    _tokens.Add(new PythonToken(
                        PythonTokenKind.Error,
                        string.Empty,
                        "unindent does not match any outer indentation level",
                        _index,
                        _index,
                        _line,
                        _column,
                        _line,
                        _column));
                }
            }
            return false;
        }

        private void FinishInput() {
            if (!_reportedFinalNewLine
                && _lineHasContent) {
                _reportedFinalNewLine = true;
                _tokens.Add(new PythonToken(
                    PythonTokenKind.NewLine,
                    string.Empty,
                    "\n",
                    _index,
                    _index,
                    _line,
                    _column,
                    _line,
                    _column));
            }
            while (_indentation.Count > 1) {
                _indentation.Pop();
                _tokens.Add(new PythonToken(
                    PythonTokenKind.Dedent,
                    string.Empty,
                    string.Empty,
                    _index,
                    _index,
                    _line,
                    _column,
                    _line,
                    _column));
            }
            _tokens.Add(EndMarker(_index, _line, _column));
            _ended = true;
        }

        private void LexIdentifier() {
            int start = _index;
            int line = _line;
            int column = _column;
            AdvanceChar();
            while (_index < _source.Length && IsIdentifierPart(_source[_index])) AdvanceChar();
            string value = _source.Substring(start, _index - start);
            AddToken(
                PythonTokenKind.Name,
                start,
                line,
                column,
                value,
                SoftKeywords.Contains(value),
                HardKeywords.Contains(value));
        }

        private void LexNumber() {
            int start = _index;
            int line = _line;
            int column = _column;
            AdvanceChar();
            while (_index < _source.Length) {
                char current = _source[_index];
                char previous = _source[_index - 1];
                if (char.IsLetterOrDigit(current) || current == '_') {
                    AdvanceChar();
                    continue;
                }
                if (current == '.' && !IsExponent(previous)) {
                    AdvanceChar();
                    continue;
                }
                if ((current == '+' || current == '-') && IsExponent(previous)) {
                    AdvanceChar();
                    continue;
                }
                break;
            }
            AddToken(PythonTokenKind.Number, start, line, column, _source.Substring(start, _index - start));
        }

        private bool LexOperator() {
            foreach (string candidate in Operators) {
                if (!StartsWith(candidate, _index)) continue;
                int start = _index;
                int line = _line;
                int column = _column;
                for (int i = 0; i < candidate.Length; i++) AdvanceChar();
                if (candidate.Length == 1 && "([{".Contains(candidate[0])) {
                    _brackets.Push(candidate[0]);
                } else if (candidate.Length == 1 && ")]}".Contains(candidate[0])) {
                    if (_brackets.Count > 0) _brackets.Pop();
                }
                AddToken(PythonTokenKind.Operator, start, line, column, candidate);
                return true;
            }
            return false;
        }

        private void LexComment() {
            int start = _index;
            int line = _line;
            int column = _column;
            while (_index < _source.Length && !IsNewLine(_source[_index])) AdvanceChar();
            string text = _source.Substring(start, _index - start);
            if (_brackets.Count == 0
                && text.StartsWith("# type:", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("# type: ignore", StringComparison.OrdinalIgnoreCase)) {
                AddToken(PythonTokenKind.TypeComment, start, line, column, text);
            }
        }

        private void LexNewLine() {
            int start = _index;
            int line = _line;
            int column = _column;
            ConsumeNewLine();
            string newlineText = _source.Substring(start, _index - start);
            int textColumns = CountCodePoints(newlineText);
            int startByteColumn = GetByteColumn(start);
            _tokens.Add(new PythonToken(
                _brackets.Count == 0 && _lineHasContent
                    ? PythonTokenKind.NewLine
                    : PythonTokenKind.NonLogicalNewLine,
                newlineText,
                "\n",
                start,
                _index,
                line,
                column,
                line,
                column + textColumns,
                startByteColumn: startByteColumn,
                endByteColumn: startByteColumn + Encoding.UTF8.GetByteCount(newlineText)));
            _lineHasContent = false;
            _atLineStart = _brackets.Count == 0;
        }

        private bool TryLexInterpolatedString() {
            int prefixLength = ReadStringPrefixLength(_index);
            if (prefixLength < 0) return false;
            string prefix = _source.Substring(_index, prefixLength);
            bool interpolated = prefix.Contains('f') || prefix.Contains('F')
                || prefix.Contains('t') || prefix.Contains('T');
            if (!interpolated) return false;
            ScanInterpolatedString(prefix);
            return true;
        }

        private bool TryLexString() {
            int prefixLength = ReadStringPrefixLength(_index);
            if (prefixLength < 0 && !IsQuote(_source[_index])) return false;
            int start = _index;
            int line = _line;
            int column = _column;
            if (prefixLength > 0) {
                for (int i = 0; i < prefixLength; i++) AdvanceChar();
            }
            if (!ScanOrdinaryString()) return false;
            AddToken(PythonTokenKind.String, start, line, column, _source.Substring(start, _index - start));
            return true;
        }

        private void ScanInterpolatedString(string prefix) {
            int start = _index;
            int line = _line;
            int column = _column;
            for (int i = 0; i < prefix.Length; i++) AdvanceChar();
            char quote = _source[_index];
            bool triple = _index + 2 < _source.Length
                && _source[_index + 1] == quote
                && _source[_index + 2] == quote;
            int quoteLength = triple ? 3 : 1;
            for (int i = 0; i < quoteLength; i++) AdvanceChar();
            bool template = prefix.Contains('t') || prefix.Contains('T');
            AddToken(
                template ? PythonTokenKind.TStringStart : PythonTokenKind.FStringStart,
                start,
                line,
                column,
                _source.Substring(start, _index - start));

            int middleStart = _index;
            int middleLine = _line;
            int middleColumn = _column;
            while (_index < _source.Length) {
                if (IsStringTerminator(quote, triple)) {
                    FlushInterpolatedMiddle(template, middleStart, middleLine, middleColumn);
                    int endStart = _index;
                    int endLine = _line;
                    int endColumn = _column;
                    for (int i = 0; i < quoteLength; i++) AdvanceChar();
                    AddToken(
                        template ? PythonTokenKind.TStringEnd : PythonTokenKind.FStringEnd,
                        endStart,
                        endLine,
                        endColumn,
                        _source.Substring(endStart, _index - endStart));
                    return;
                }

                char current = _source[_index];
                if (current == '\\') {
                    AdvanceChar();
                    if (_index < _source.Length) AdvanceChar();
                    continue;
                }
                if (current == '{' && Peek(1) == '{') {
                    AdvanceChar();
                    AdvanceChar();
                    continue;
                }
                if (current == '}' && Peek(1) == '}') {
                    AdvanceChar();
                    AdvanceChar();
                    continue;
                }
                if (current == '{') {
                    FlushInterpolatedMiddle(template, middleStart, middleLine, middleColumn);
                    int braceStart = _index;
                    int braceLine = _line;
                    int braceColumn = _column;
                    AdvanceChar();
                    AddToken(PythonTokenKind.Operator, braceStart, braceLine, braceColumn, "{");
                    ScanInterpolationExpression(template);
                    middleStart = _index;
                    middleLine = _line;
                    middleColumn = _column;
                    continue;
                }
                AdvanceChar();
            }

            NeedsMoreInput = true;
            FlushInterpolatedMiddle(template, middleStart, middleLine, middleColumn);
            AddToken(
                PythonTokenKind.Error,
                _index,
                _line,
                _column,
                "unterminated interpolated string");
        }

        private void ScanInterpolationExpression(bool template) {
            int expressionStart = _index;
            int depth = 1;
            while (_index < _source.Length) {
                char current = _source[_index];
                if (current == '}' && depth == 1) {
                    int closingStart = _index;
                    int line = _line;
                    int column = _column;
                    AdvanceChar();
                    AddToken(PythonTokenKind.Operator, closingStart, line, column, "}");
                    return;
                }
                if (current == ':' && depth == 1) {
                    int colonStart = _index;
                    int line = _line;
                    int column = _column;
                    AdvanceChar();
                    AddToken(PythonTokenKind.Operator, colonStart, line, column, ":");
                    ScanInterpolatedFormatSpec(template);
                    return;
                }
                if (current == '(' || current == '[' || current == '{') {
                    depth++;
                } else if (current == ')' || current == ']' || current == '}') {
                    depth--;
                }

                if (current == '#') {
                    LexComment();
                    continue;
                }
                if (current == ' ' || current == '\t' || current == '\f') {
                    AdvanceChar();
                    continue;
                }
                if (IsNewLine(current)) {
                    int newlineStart = _index;
                    int newlineLine = _line;
                    int newlineColumn = _column;
                    ConsumeNewLine();
                    string newlineText = _source.Substring(newlineStart, _index - newlineStart);
                    int newlineColumns = CountCodePoints(newlineText);
                    int newlineByteColumn = GetByteColumn(newlineStart);
                    _tokens.Add(new PythonToken(
                        PythonTokenKind.NonLogicalNewLine,
                        newlineText,
                        "\n",
                        newlineStart,
                        _index,
                        newlineLine,
                        newlineColumn,
                        newlineLine,
                        newlineColumn + newlineColumns,
                        startByteColumn: newlineByteColumn,
                        endByteColumn: newlineByteColumn + Encoding.UTF8.GetByteCount(newlineText)));
                    continue;
                }
                if (TryLexInterpolatedString()) continue;
                if (TryLexString()) continue;
                if (IsIdentifierStart(current)) {
                    LexIdentifier();
                    continue;
                }
                if (char.IsDigit(current)) {
                    LexNumber();
                    continue;
                }
                if (current == '=' && Peek(1) != '=' && depth == 1) {
                    int debugEnd = _index + 1;
                    while (debugEnd < _source.Length
                        && (_source[debugEnd] == ' ' || _source[debugEnd] == '\t'
                            || _source[debugEnd] == '\f')) {
                        debugEnd++;
                    }
                    AddToken(
                        PythonTokenKind.Operator,
                        _index,
                        _line,
                        _column,
                        "=",
                        textOverride: _source.Substring(
                            expressionStart,
                            debugEnd - expressionStart));
                    AdvanceChar();
                    continue;
                }
                if (LexOperator()) continue;

                int start = _index;
                int startLine = _line;
                int startColumn = _column;
                AdvanceChar();
                AddToken(PythonTokenKind.Error, start, startLine, startColumn, "invalid interpolation character");
            }
            NeedsMoreInput = true;
        }

        private void ScanInterpolatedFormatSpec(bool template) {
            int middleStart = _index;
            int middleLine = _line;
            int middleColumn = _column;
            while (_index < _source.Length) {
                char current = _source[_index];
                if (current == '}') {
                    FlushInterpolatedMiddle(
                        template,
                        middleStart,
                        middleLine,
                        middleColumn,
                        includeEmpty: true);
                    int closingStart = _index;
                    int line = _line;
                    int column = _column;
                    AdvanceChar();
                    AddToken(PythonTokenKind.Operator, closingStart, line, column, "}");
                    return;
                }
                if (current == '{') {
                    FlushInterpolatedMiddle(template, middleStart, middleLine, middleColumn);
                    int braceStart = _index;
                    int braceLine = _line;
                    int braceColumn = _column;
                    AdvanceChar();
                    AddToken(PythonTokenKind.Operator, braceStart, braceLine, braceColumn, "{");
                    ScanInterpolationExpression(template);
                    middleStart = _index;
                    middleLine = _line;
                    middleColumn = _column;
                    continue;
                }
                AdvanceChar();
            }

            NeedsMoreInput = true;
            FlushInterpolatedMiddle(template, middleStart, middleLine, middleColumn);
        }

        private void FlushInterpolatedMiddle(
            bool template,
            int start,
            int line,
            int column,
            bool includeEmpty = false) {
            if (_index < start || (_index == start && !includeEmpty)) return;
            AddToken(
                template ? PythonTokenKind.TStringMiddle : PythonTokenKind.FStringMiddle,
                start,
                line,
                column,
                _source.Substring(start, _index - start));
        }

        private bool ScanOrdinaryString() {
            char quote = _source[_index];
            bool triple = _index + 2 < _source.Length
                && _source[_index + 1] == quote
                && _source[_index + 2] == quote;
            int quoteLength = triple ? 3 : 1;
            for (int i = 0; i < quoteLength; i++) AdvanceChar();
            while (_index < _source.Length) {
                if (IsStringTerminator(quote, triple)) {
                    for (int i = 0; i < quoteLength; i++) AdvanceChar();
                    return true;
                }
                if (_source[_index] == '\\') {
                    AdvanceChar();
                    if (_index < _source.Length) AdvanceChar();
                    continue;
                }
                if (!triple && IsNewLine(_source[_index])) {
                    NeedsMoreInput = true;
                    return true;
                }
                AdvanceChar();
            }
            NeedsMoreInput = true;
            return true;
        }

        private int ReadStringPrefixLength(int start) {
            int index = start;
            while (index < _source.Length && index - start < 3 && IsStringPrefixChar(_source[index])) index++;
            return index < _source.Length && IsQuote(_source[index]) ? index - start : -1;
        }

        private void AddToken(
            PythonTokenKind kind,
            int start,
            int line,
            int column,
            string literalValue,
            bool soft = false,
            bool keyword = false,
            string? textOverride = null) {
            (int actualStartLine, int actualStartColumn) = GetLineColumn(start);
            (int actualEndLine, int actualEndColumn) = GetLineColumn(_index);
            int actualStartByteColumn = GetByteColumn(start);
            int actualEndByteColumn = GetByteColumn(_index);
            _tokens.Add(new PythonToken(
                kind,
                _source.Substring(start, _index - start),
                literalValue,
                start,
                _index,
                actualStartLine,
                actualStartColumn,
                actualEndLine,
                actualEndColumn,
                soft,
                actualStartByteColumn,
                actualEndByteColumn,
                keyword,
                textOverride));
            _lineHasContent = true;
        }

        private PythonToken EndMarker(int offset, int line, int column) {
            (int actualLine, int actualColumn) = GetLineColumn(offset);
            int byteColumn = GetByteColumn(offset);
            return new PythonToken(
                PythonTokenKind.EndMarker,
                string.Empty,
                string.Empty,
                offset,
                offset,
                actualLine,
                actualColumn,
                actualLine,
                actualColumn,
                startByteColumn: byteColumn,
                endByteColumn: byteColumn);
        }

        private (int Line, int Column) GetLineColumn(int offset) {
            int low = 0;
            int high = _lineStartOffsets.Count - 1;
            while (low <= high) {
                int middle = low + ((high - low) / 2);
                if (_lineStartOffsets[middle] <= offset) {
                    low = middle + 1;
                } else {
                    high = middle - 1;
                }
            }

            int lineIndex = Math.Max(0, high);
            int lineStart = _lineStartOffsets[lineIndex];
            int column = 0;
            ReadOnlySpan<char> text = _source.AsSpan(lineStart, offset - lineStart);
            foreach (Rune rune in text.EnumerateRunes()) {
                column++;
            }
            return (lineIndex + 1, column);
        }

        private int GetByteColumn(int offset) {
            int lineIndex = FindLineIndex(offset);
            int lineStart = _lineStartOffsets[lineIndex];
            return Encoding.UTF8.GetByteCount(_source.AsSpan(lineStart, offset - lineStart));
        }

        private int FindLineIndex(int offset) {
            int low = 0;
            int high = _lineStartOffsets.Count - 1;
            while (low <= high) {
                int middle = low + ((high - low) / 2);
                if (_lineStartOffsets[middle] <= offset) {
                    low = middle + 1;
                } else {
                    high = middle - 1;
                }
            }
            return Math.Max(0, high);
        }

        private static int CountCodePoints(string text) {
            int count = 0;
            foreach (Rune rune in text.EnumerateRunes()) count++;
            return count;
        }

        private void SkipHorizontalWhitespace() {
            while (_index < _source.Length
                && (_source[_index] == ' ' || _source[_index] == '\t' || _source[_index] == '\f')) {
                AdvanceChar();
            }
        }

        private void AdvanceChar() {
            if (_index >= _source.Length) return;
            char character = _source[_index++];
            _column++;
            if (character == '\n') {
                _line++;
                _column = 0;
                _lineStartOffsets.Add(_index);
            }
        }

        private void ConsumeNewLine() {
            if (_index < _source.Length && _source[_index] == '\r') {
                _index++;
                if (_index < _source.Length && _source[_index] == '\n') _index++;
                _line++;
                _column = 0;
                _lineStartOffsets.Add(_index);
            } else if (_index < _source.Length && _source[_index] == '\n') {
                _index++;
                _line++;
                _column = 0;
                _lineStartOffsets.Add(_index);
            }
        }

        private char Peek(int offset) {
            int index = _index + offset;
            return index >= 0 && index < _source.Length ? _source[index] : '\0';
        }

        private bool StartsWith(string value, int index) {
            return index + value.Length <= _source.Length
                && string.CompareOrdinal(_source, index, value, 0, value.Length) == 0;
        }

        private static bool IsIdentifierStart(char character) {
            return character == '_' || char.IsLetter(character) || character > 127 && CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;
        }

        private static bool IsIdentifierPart(char character) {
            if (IsIdentifierStart(character) || char.IsDigit(character)) return true;
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            return category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.Format;
        }

        private static bool IsStringPrefixChar(char character) {
            return "rRbBuUfFtT".Contains(character);
        }

        private static bool IsQuote(char character) => character == '\'' || character == '"';

        private bool IsStringTerminator(char quote, bool triple) {
            if (_source[_index] != quote) return false;
            return !triple
                || (_index + 2 < _source.Length
                    && _source[_index + 1] == quote
                    && _source[_index + 2] == quote);
        }

        private static bool IsNewLine(char character) => character == '\n' || character == '\r';

        private static bool IsExponent(char character) => character == 'e' || character == 'E';
    }

    public sealed class PythonTokenizationException : FormatException {
        public PythonTokenizationException(string message) : base(message) { }
    }
}
