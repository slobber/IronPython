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
            out PythonTokenizer tokenizer) {
            tokenizer = new PythonTokenizer(source);
            var options = new PegParserOptions {
                MaxRuleInvocations = Math.Clamp(
                    source.Length * 200,
                    1_000_000,
                    50_000_000),
                MaxMemoEntries = Math.Clamp(
                    source.Length * 100,
                    250_000,
                    10_000_000)
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
