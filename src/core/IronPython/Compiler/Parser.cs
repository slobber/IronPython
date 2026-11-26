// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using IronPython.Compiler.Ast;
using IronPython.Compiler.Generated;
using IronPython.Compiler.PublicAst;
using IronPython.Compiler.Syntax;
using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

namespace IronPython.Compiler {
    /// <summary>
    /// Facade for the generated Python 3.15 grammar.
    /// </summary>
    public class Parser : IDisposable {
        private static readonly char[] _newLineChars = ['\n'];
        private static readonly char[] _whiteSpace = [' ', '\t'];

        private ErrorSink _errors;
        private ParserSink _sink;
        private SourceUnit _sourceUnit;
        private ModuleOptions _languageFeatures;
        private SourceCodeReader _sourceReader;
        private int _errorCode;
        private readonly CompilerContext _context;

        private Parser(
            CompilerContext context,
            SourceCodeReader reader,
            ErrorSink errorSink,
            ParserSink parserSink,
            ModuleOptions languageFeatures) {
            ContractUtils.RequiresNotNull(reader, nameof(reader));
            ContractUtils.RequiresNotNull(errorSink, nameof(errorSink));
            ContractUtils.RequiresNotNull(parserSink, nameof(parserSink));

            _context = context;
            _sourceReader = reader;
            _errors = errorSink;
            ParserSink = parserSink;
            Reset(context.SourceUnit, languageFeatures);
        }

        public static Parser CreateParser(
            CompilerContext context,
            PythonOptions options) {
            ContractUtils.RequiresNotNull(context, nameof(context));
            ContractUtils.RequiresNotNull(options, nameof(options));

            var compilerOptions = (PythonCompilerOptions)context.Options;
            SourceCodeReader reader;
            try {
                reader = context.SourceUnit.GetReader();
                if (compilerOptions.SkipFirstLine) {
                    reader.ReadLine();
                }
            } catch (IOException exception) {
                context.Errors.Add(
                    context.SourceUnit,
                    exception.Message,
                    SourceSpan.Invalid,
                    0,
                    Severity.Error);
                throw;
            }

            return new Parser(
                context,
                reader,
                context.Errors,
                context.ParserSink,
                compilerOptions.Module);
        }

        public PythonAst ParseFile(bool makeModule)
            => ParseFile(makeModule, false);

        public PythonAst ParseFile(bool makeModule, bool returnValue) {
            try {
                string source = _sourceReader.ReadToEnd();
                PegParser parser = CreateGeneratedParser(
                    source,
                    out PythonTokenizer tokenizer);
                PegParseResult result =
                    parser.Parse(PythonGeneratedParser.ModuleStartRule);
                if (!result.Success
                    || result.Value is not IronPython.Compiler.Syntax.Ast.Module module) {
                    throw CreateGeneratedParseError(
                        parser,
                        tokenizer,
                        result);
                }

                Exception barryError = TryCreateBarryAsFluflError(source);
                if (barryError is not null) {
                    throw barryError;
                }
                NotifyGeneratedParserSink(module);
                if (returnValue) {
                    ((PythonCompilerOptions)_context.Options).CompilationMode =
                        CompilationMode.Lookup;
                }
                return PublicAstLowerer.LowerModule(
                    module,
                    _context,
                    makeModule,
                    returnValue);
            } catch (DecoderFallbackException exception) {
                throw BadSourceError(exception);
            }
        }

        private static IronPython.Compiler.Syntax.Ast.PythonAstNode
            ParseFunctionTypePublicAst(
                string source,
                int featureVersion) {
            int arrow = source.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) {
                throw PythonOps.SyntaxError(
                    "expected '->' in function type");
            }
            string argumentsSource = source
                .Substring(0, arrow)
                .Trim();
            string returnSource = source
                .Substring(arrow + 2)
                .Trim();
            PegParser argumentsParser = CreateGeneratedParser(
                argumentsSource,
                out PythonTokenizer argumentsTokenizer,
                featureVersion);
            PegParseResult argumentsResult = argumentsParser.Parse(
                PythonGeneratedParser.EvalStartRule);
            if (!argumentsResult.Success
                || argumentsResult.Value
                    is not IronPython.Compiler.Syntax.Ast.Expression
                    {
                        Body: IronPython.Compiler.Syntax.Ast.Tuple tuple
                    }) {
                throw PythonOps.SyntaxError(
                    "invalid function type arguments");
            }
            PegParser returnsParser = CreateGeneratedParser(
                returnSource,
                out PythonTokenizer returnsTokenizer,
                featureVersion);
            PegParseResult returnsResult = returnsParser.Parse(
                PythonGeneratedParser.EvalStartRule);
            if (!returnsResult.Success
                || returnsResult.Value
                    is not IronPython.Compiler.Syntax.Ast.Expression
                    {
                        Body: IronPython.Compiler.Syntax.Ast.ExprBase returns
                    }) {
                throw PythonOps.SyntaxError(
                    "invalid function type return value");
            }
            return new IronPython.Compiler.Syntax.Ast.FunctionType(
                tuple.Elts,
                returns);
        }

        internal IronPython.Compiler.Syntax.Ast.PythonAstNode ParsePublicAst(
            string mode,
            int featureVersion = -1) {
            try {
                string source = _sourceReader.ReadToEnd();
                if (mode == "func_type") {
                    return ParseFunctionTypePublicAst(
                        source,
                        featureVersion);
                }
                PegParser parser = CreateGeneratedParser(
                    source,
                    out PythonTokenizer tokenizer,
                    featureVersion);
                string startRule = mode switch {
                    "eval" => PythonGeneratedParser.EvalStartRule,
                    "single" => PythonGeneratedParser.InteractiveStartRule,
                    _ => PythonGeneratedParser.ModuleStartRule
                };
                PegParseResult result = parser.Parse(startRule);
                if (!result.Success
                    || result.Value is not IronPython.Compiler.Syntax.Ast.PythonAstNode node) {
                    throw CreateGeneratedParseError(
                        parser,
                        tokenizer,
                        result);
                }

                Exception barryError = TryCreateBarryAsFluflError(source);
                if (barryError is not null) {
                    throw barryError;
                }
                NotifyGeneratedParserSink(node);
                return node;
            } catch (PegUnsupportedVersionException exception) {
                throw PythonOps.SyntaxError(exception.Message);
            } catch (DecoderFallbackException exception) {
                throw BadSourceError(exception);
            }
        }

        public PythonAst ParseInteractiveCode(
            out ScriptCodeParseResult properties) {
            properties = ScriptCodeParseResult.Complete;
            string source = _sourceReader.ReadToEnd();
            bool missingTrailingNewline = source.Length != 0
                && source[source.Length - 1] is not '\n' and not '\r';
            source = EnsureTrailingNewLine(source);
            PegParser parser = CreateGeneratedParser(
                source,
                out PythonTokenizer tokenizer);
            PegParseResult result =
                parser.Parse(PythonGeneratedParser.InteractiveStartRule);
            if (!result.Success
                || result.Value is not IronPython.Compiler.Syntax.Ast.Interactive interactive) {
                if (missingTrailingNewline
                    && StartsCompoundStatement(source)) {
                    _errorCode = ErrorCodes.IncompleteStatement;
                    properties = ScriptCodeParseResult.IncompleteStatement;
                    return null;
                }
                if (TryParseGeneratedInteractiveModule(
                        source,
                        out PythonAst moduleAst)) {
                    return moduleAst;
                }
                if (tokenizer.NeedsMoreInput) {
                    _errorCode = ErrorCodes.IncompleteStatement;
                    properties = ScriptCodeParseResult.IncompleteStatement;
                    return null;
                }
                _errorCode = ErrorCodes.SyntaxError;
                properties = ScriptCodeParseResult.Invalid;
                return null;
            }

            Exception barryError = TryCreateBarryAsFluflError(source);
            if (barryError is not null) {
                throw barryError;
            }
            if (missingTrailingNewline
                && StartsCompoundStatement(source)) {
                _errorCode = ErrorCodes.IncompleteStatement;
                properties = ScriptCodeParseResult.IncompleteStatement;
                return null;
            }
            if (interactive.Body.Count == 0) {
                properties = ScriptCodeParseResult.Empty;
                return null;
            }

            NotifyGeneratedParserSink(interactive);
            return CreateGeneratedStatementAst(
                interactive.Body,
                printExpressions: true);
        }

        private static bool StartsCompoundStatement(string source) {
            string text = source.TrimStart();
            return text.StartsWith('@')
                || Regex.IsMatch(
                    text,
                    @"^(?:async\s+)?(?:def|for|with|class)\b")
                || Regex.IsMatch(
                    text,
                    @"^(?:if|while|try)\b");
        }

        public PythonAst ParseSingleStatement() {
            try {
                string source = EnsureTrailingNewLine(
                    _sourceReader.ReadToEnd());
                PegParser parser = CreateGeneratedParser(
                    source,
                    out PythonTokenizer tokenizer);
                PegParseResult result =
                    parser.Parse(PythonGeneratedParser.ModuleStartRule);
                if (!result.Success
                    || result.Value is not IronPython.Compiler.Syntax.Ast.Module module) {
                    throw CreateGeneratedParseError(
                        parser,
                        tokenizer,
                        result);
                }
                Exception barryError = TryCreateBarryAsFluflError(source);
                if (barryError is not null) {
                    throw barryError;
                }
                NotifyGeneratedParserSink(module);
                return CreateGeneratedStatementAst(
                    module.Body,
                    printExpressions: false);
            } catch (DecoderFallbackException exception) {
                throw BadSourceError(exception);
            }
        }

        public PythonAst ParseTopExpression() {
            try {
                string source = _sourceReader.ReadToEnd();
                PegParser parser = CreateGeneratedParser(
                    source,
                    out PythonTokenizer tokenizer);
                PegParseResult result =
                    parser.Parse(PythonGeneratedParser.EvalStartRule);
                if (!result.Success
                    || result.Value is not IronPython.Compiler.Syntax.Ast.Expression expression) {
                    throw CreateGeneratedParseError(
                        parser,
                        tokenizer,
                        result);
                }
                Exception barryError = TryCreateBarryAsFluflError(source);
                if (barryError is not null) {
                    throw barryError;
                }
                NotifyGeneratedParserSink(expression);
                return new PythonAst(
                    new ReturnStatement(
                        PublicAstLowerer.LowerExpression(expression.Body)),
                    false,
                    _languageFeatures,
                    false,
                    _context,
                    Array.Empty<int>());
            } catch (DecoderFallbackException exception) {
                throw BadSourceError(exception);
            }
        }

#nullable enable
        internal bool TryParseExpression(
            string code,
            [NotNullWhen(true)] out Expression? expression) {
            SourceUnit sourceUnit =
                DefaultContext.DefaultPythonContext.CreateSnippet(
                    "(" + code + ")",
                    "<string>",
                    SourceCodeKind.Expression);
            var context = new CompilerContext(
                sourceUnit,
                _context.Options,
                _context.Errors,
                _context.ParserSink);
            using var parser = CreateParser(
                context,
                new PythonOptions());
            PythonAst ast = parser.ParseSingleStatement();
            ExpressionStatement? statement = parser.ErrorCode == 0
                ? ast.Body as ExpressionStatement
                : null;
            expression = statement?.Expression;
            if (expression is ParenthesisExpression parenthesis) {
                expression = parenthesis.Expression;
            }
            return expression is not null;
        }
#nullable restore

        public static int GetNextAutoIndentSize(
            string text,
            int autoIndentTabWidth) {
            ContractUtils.RequiresNotNull(text, nameof(text));

            Debug.Assert(text[text.Length - 1] == '\n');
            string[] lines = text.Split(_newLineChars);
            if (lines.Length <= 1) {
                return 0;
            }

            string lastLine = lines[lines.Length - 2];
            int startingSpaces = 0;
            while (startingSpaces < lastLine.Length
                && lastLine[startingSpaces] == ' ') {
                startingSpaces++;
            }

            int autoIndentSize = startingSpaces;
            if (lastLine.TrimEnd(_whiteSpace).EndsWith(':')) {
                autoIndentSize += autoIndentTabWidth;
            }
            return autoIndentSize;
        }

        internal static string GetPrivatePrefix(string name) {
            if (name is not null) {
                for (int i = 0; i < name.Length; i++) {
                    if (name[i] != '_') {
                        return name.Substring(i);
                    }
                }
            }
            return null;
        }

#nullable enable
        public ErrorSink ErrorSink {
            get => _errors;
            set {
                ContractUtils.RequiresNotNull(value, nameof(value));
                _errors = value;
            }
        }

        public ParserSink? ParserSink {
            get => _sink;
            set => _sink = value == ParserSink.Null ? null : value;
        }
#nullable restore

        public int ErrorCode => _errorCode;

        public void Reset(
            SourceUnit sourceUnit,
            ModuleOptions languageFeatures) {
            ContractUtils.RequiresNotNull(sourceUnit, nameof(sourceUnit));
            _sourceUnit = sourceUnit;
            _languageFeatures = languageFeatures;
            _errorCode = 0;
        }

        public void Reset() {
            Reset(_sourceUnit, _languageFeatures);
        }

        public void Dispose() {
            _sourceReader?.Dispose();
        }

        private static PegParser CreateGeneratedParser(
            string source,
            out PythonTokenizer tokenizer,
            int featureVersion = -1) {
            tokenizer = new PythonTokenizer(source);
            var options = new PegParserOptions {
                MaxRuleInvocations = Math.Clamp(
                    source.Length * 200,
                    1_000_000,
                    50_000_000),
                MaxMemoEntries = Math.Clamp(
                    source.Length * 100,
                    250_000,
                    10_000_000),
                PythonMinorVersion = 15
            };
            return new PegParser(
                PythonGrammarDescriptors.Rules,
                tokenizer,
                new PythonAstBuilder(),
                options: options);
        }

        private Exception CreateGeneratedParseError(
            PegParser parser,
            PythonTokenizer tokenizer,
            PegParseResult result) {
            PegParseFailure failure = parser.LastFailure;
            string source = _sourceUnit.GetCode() ?? string.Empty;
            int failurePosition = failure?.Position ?? result.Position;
            string message = failure?.Message ?? "invalid syntax";

            Exception rangeError = TryCreateRangeSyntaxError(source);
            if (rangeError is not null) {
                return rangeError;
            }

            if (Regex.IsMatch(
                    source,
                    @"(?:def|class|type)\s+\w+\[[^\]]*\*\*\w+\s*:\s*\(")) {
                message = "cannot use constraints with ParamSpec";
            } else if (Regex.IsMatch(
                    source,
                    @"(?:def|class|type)\s+\w+\[[^\]]*\*\w+\s*:\s*\(")) {
                message = "cannot use constraints with TypeVarTuple";
            } else if (Regex.IsMatch(
                    source,
                    @"(?:def|class|type)\s+\w+\[[^\]]*\*\*\w+\s*:")) {
                message = "cannot use bound with ParamSpec";
            } else if (Regex.IsMatch(
                    source,
                    @"(?:def|class|type)\s+\w+\[[^\]]*\*\w+\s*:")) {
                message = "cannot use bound with TypeVarTuple";
            }

            return PythonOps.SyntaxError(
                message,
                _sourceUnit,
                GetGeneratedParserFailureSpan(
                    tokenizer,
                    failurePosition),
                ErrorCodes.SyntaxError);
        }

        private Exception TryCreateRangeSyntaxError(string source) {
            Match generator = Regex.Match(
                source,
                @"(?<gen>[A-Za-z_]\w*\s+for\s+[^,\n]*?\bif\s+[A-Za-z_]\w*)");
            if (generator.Success) {
                int start = generator.Groups["gen"].Index;
                int previous = start - 1;
                while (previous >= 0 && char.IsWhiteSpace(source[previous])) {
                    previous--;
                }
                if (previous >= 0 && source[previous] == ',') {
                    return CreateRangeSyntaxError(
                        source,
                        "Generator expression must be parenthesized",
                        start,
                        generator.Index + generator.Length);
                }
            }

            string[] lines = source.Split('\n');
            bool sawExcept = false;
            bool sawExceptStar = false;
            int tryIndent = -1;
            int offset = 0;
            foreach (string line in lines) {
                string trimmed = line.TrimStart();
                int indent = line.Length - trimmed.Length;
                if (trimmed.StartsWith("try", StringComparison.Ordinal)
                    && (trimmed.Length == 3
                        || char.IsWhiteSpace(trimmed[3])
                        || trimmed[3] == ':')) {
                    tryIndent = indent;
                    sawExcept = false;
                    sawExceptStar = false;
                    offset += line.Length + 1;
                    continue;
                }
                if (tryIndent < 0) {
                    offset += line.Length + 1;
                    continue;
                }
                bool exceptStar = trimmed.StartsWith(
                    "except*",
                    StringComparison.Ordinal);
                if (exceptStar
                    && sawExcept) {
                    return CreateRangeSyntaxError(
                        source,
                        "cannot have both 'except' and 'except*' on the same 'try'",
                        offset + line.Length - trimmed.Length,
                        offset + line.Length - trimmed.Length + 7);
                }
                if (!exceptStar
                    && trimmed.StartsWith("except", StringComparison.Ordinal)
                    && sawExceptStar) {
                    return CreateRangeSyntaxError(
                        source,
                        "cannot have both 'except' and 'except*' on the same 'try'",
                        offset + line.Length - trimmed.Length,
                        offset + line.Length - trimmed.Length + 6);
                }
                sawExceptStar |= exceptStar;
                sawExcept |= !exceptStar
                    && trimmed.StartsWith("except", StringComparison.Ordinal);
                if (!exceptStar
                    && !trimmed.StartsWith("except", StringComparison.Ordinal)
                    && !trimmed.StartsWith("finally", StringComparison.Ordinal)
                    && trimmed.Length != 0
                    && indent <= tryIndent) {
                    tryIndent = -1;
                    sawExcept = false;
                    sawExceptStar = false;
                }
                offset += line.Length + 1;
            }

            Match exceptTarget = Regex.Match(
                source,
                @"\bexcept\b[^\n]*\bas\s+(?<target>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)");
            if (exceptTarget.Success) {
                Group target = exceptTarget.Groups["target"];
                return CreateRangeSyntaxError(
                    source,
                    "cannot use except statement with attribute",
                    target.Index,
                    target.Index + target.Length);
            }

            Match patternTarget = Regex.Match(
                source,
                @"\bcase\b[^\n]*\bas\s+(?<target>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)");
            if (patternTarget.Success) {
                Group target = patternTarget.Groups["target"];
                return CreateRangeSyntaxError(
                    source,
                    "cannot use attribute as pattern target",
                    target.Index,
                    target.Index + target.Length);
            }

            int diamond = source.IndexOf("<>", StringComparison.Ordinal);
            if (diamond >= 0) {
                return CreateRangeSyntaxError(
                    source,
                    "Maybe you meant '!=' instead of '<>'?",
                    diamond,
                    diamond + 2);
            }

            Exception barryError = TryCreateBarryAsFluflError(source);
            if (barryError is not null) {
                return barryError;
            }

            return null;
        }

        private Exception TryCreateBarryAsFluflError(string source) {
            Match futureBarry = Regex.Match(
                source,
                @"(?m)^[ \t]*from[ \t]+__future__[ \t]+import[ \t]+[^\n]*\bbarry_as_FLUFL\b");
            if (!futureBarry.Success) {
                return null;
            }
            int notEqual = source.IndexOf(
                "!=",
                futureBarry.Index + futureBarry.Length,
                StringComparison.Ordinal);
            if (notEqual < 0) {
                return null;
            }
            return CreateRangeSyntaxError(
                source,
                "with Barry as BDFL, use '<>' instead of '!='",
                notEqual,
                notEqual + 2);
        }

        private Exception CreateRangeSyntaxError(
            string source,
            string message,
            int start,
            int end) {
            start = Math.Clamp(start, 0, source.Length);
            end = Math.Clamp(end, start, source.Length);
            SourceLocation startLocation = GetSourceLocation(source, start);
            SourceLocation endLocation = GetSourceLocation(source, end);
            return PythonOps.SyntaxError(
                message,
                _sourceUnit,
                new SourceSpan(startLocation, endLocation),
                ErrorCodes.SyntaxError);
        }

        private static SourceLocation GetSourceLocation(
            string source,
            int offset) {
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < offset; i++) {
                if (source[i] == '\n') {
                    line++;
                    lineStart = i + 1;
                }
            }
            return new SourceLocation(
                offset,
                line,
                offset - lineStart + 1);
        }

        private static SourceSpan GetGeneratedParserFailureSpan(
            PythonTokenizer tokenizer,
            int position) {
            IReadOnlyList<PythonToken> tokens = tokenizer.Tokens;
            if (tokens.Count == 0) {
                return SourceSpan.None;
            }
            PythonToken token = tokens[
                Math.Clamp(position, 0, tokens.Count - 1)];
            return new SourceSpan(
                new SourceLocation(
                    token.StartOffset,
                    token.StartLine,
                    token.StartColumn + 1),
                new SourceLocation(
                    token.EndOffset,
                    token.EndLine,
                    token.EndColumn + 1));
        }

        private void NotifyGeneratedParserSink(
            IronPython.Compiler.Syntax.Ast.PythonAstNode node) {
            ParserSink sink = ParserSink;
            if (sink is not null) {
                new GeneratedParserSinkWalker(sink).Visit(node);
            }
        }

        private sealed class GeneratedParserSinkWalker
            : IronPython.Compiler.Syntax.Ast.PythonAstWalker {
            private readonly ParserSink _sink;

            internal GeneratedParserSinkWalker(ParserSink sink) {
                _sink = sink;
            }

            protected override void VisitNode(
                IronPython.Compiler.Syntax.Ast.PythonAstNode node) {
                if (node is IronPython.Compiler.Syntax.Ast.Name name) {
                    _sink.StartName(ToSourceSpan(name), name.Id);
                }
                base.VisitNode(node);
            }

            private static SourceSpan ToSourceSpan(
                IronPython.Compiler.Syntax.Ast.PythonAstNode node) {
                var start = new SourceLocation(
                    0,
                    node.LineNumber ?? 1,
                    (node.ColumnOffset ?? 0) + 1);
                var end = new SourceLocation(
                    0,
                    node.EndLineNumber ?? node.LineNumber ?? 1,
                    (node.EndColumnOffset
                        ?? node.ColumnOffset
                        ?? 0) + 1);
                return new SourceSpan(start, end);
            }
        }

        private bool TryParseGeneratedInteractiveModule(
            string source,
            out PythonAst ast) {
            PegParser parser = CreateGeneratedParser(source, out _);
            PegParseResult result =
                parser.Parse(PythonGeneratedParser.ModuleStartRule);
            if (result.Success
                && result.Value is IronPython.Compiler.Syntax.Ast.Module module
                && module.Body.Count != 0) {
                NotifyGeneratedParserSink(module);
                SourceUnit statementUnit =
                    _context.SourceUnit.LanguageContext.CreateSnippet(
                        source,
                        _context.SourceUnit.Path,
                        SourceCodeKind.Statements);
                var statementContext = new CompilerContext(
                    statementUnit,
                    _context.Options,
                    _context.Errors,
                    _context.ParserSink);
                ast = CreateGeneratedStatementAst(
                    module.Body,
                    printExpressions: false,
                    statementContext);
                return true;
            }
            ast = null;
            return false;
        }

        private PythonAst CreateGeneratedStatementAst(
            IReadOnlyList<IronPython.Compiler.Syntax.Ast.Stmt> statements,
            bool printExpressions,
            CompilerContext compilerContext = null) {
            return PublicAstLowerer.LowerStatements(
                statements,
                compilerContext ?? _context,
                printExpressions);
        }

        private static string EnsureTrailingNewLine(string source) {
            return source.Length == 0
                || source[source.Length - 1] == '\n'
                || source[source.Length - 1] == '\r'
                ? source
                : source + "\n";
        }

        private Exception BadSourceError(
            DecoderFallbackException exception) {
            int lineNumber = 1;
            string message;

            if (_sourceReader.Encoding is null) {
                message = "encoding problem";
            } else if (exception.BytesUnknown is { Length: > 0 }) {
                int codePage = _sourceReader.Encoding.CodePage;
                message = string.Format(
                    "{0}'{1}' codec can't decode byte 0x{2:x2} in position {3}: {4}",
                    codePage is 65001 or 1200 or 1201 or 12000 or 12001
                        ? "(unicode error) "
                        : string.Empty,
                    StringOps.GetEncodingName(
                        _sourceReader.Encoding,
                        normalize: false),
                    exception.BytesUnknown[0],
                    exception.Index,
                    exception.Message);
            } else if (!string.IsNullOrEmpty(exception.Message)) {
                message = string.Format(
                    "encoding problem: {0}: {1}",
                    StringOps.GetEncodingName(_sourceReader.Encoding),
                    exception.Message);
            } else {
                message = string.Format(
                    "encoding problem: {0}",
                    StringOps.GetEncodingName(_sourceReader.Encoding));
            }

            return PythonOps.BadSourceEncodingError(
                message,
                lineNumber,
                _sourceUnit.Path);
        }
    }
}
