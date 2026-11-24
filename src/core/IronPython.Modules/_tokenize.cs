// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using IronPython.Compiler.Syntax;
using IronPython.Runtime;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

[assembly: PythonModule("_tokenize", typeof(IronPython.Modules.PythonTokenize))]
namespace IronPython.Modules {
    public static class PythonTokenize {
        public static PythonType TokenizerIter =>
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.TokenizerIter));
    }

    [PythonType("TokenizerIter")]
    public sealed class TokenizerIter {
        private readonly PythonToken[] _tokens;
        private readonly string[] _lines;
        private readonly bool _extraTokens;
        private int _position;

        private TokenizerIter(
            PythonToken[] tokens,
            string[] lines,
            bool extraTokens) {
            _tokens = tokens;
            _lines = lines;
            _extraTokens = extraTokens;
        }

        public static TokenizerIter __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] object readline) {
            return Create(context, cls, readline, false, "utf-8");
        }

        public static TokenizerIter __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] object readline,
            [ParamDictionary] IDictionary<object, object> keywordArguments) {
            bool extraTokens = keywordArguments.TryGetValue("extra_tokens", out object? rawExtra)
                && PythonOps.IsTrue(rawExtra);
            string encoding = keywordArguments.TryGetValue("encoding", out object? rawEncoding)
                ? rawEncoding as string ?? "utf-8"
                : "utf-8";
            return Create(context, cls, readline, extraTokens, encoding);
        }

        private static TokenizerIter Create(
            CodeContext context,
            PythonType cls,
            object readline,
            bool extraTokens,
            string encoding) {
            if (cls != PythonTokenize.TokenizerIter) {
                throw PythonOps.TypeError("TokenizerIter cannot be subclassed");
            }

            var source = new StringBuilder();
            while (true) {
                object? line = PythonCalls.Call(context, readline);
                if (line is null) break;
                string text = line switch {
                    string value => value,
                    Bytes bytes => Encoding.GetEncoding(encoding).GetString(bytes.ToArray()),
                    _ => throw PythonOps.TypeError(
                        "readline must return str or bytes")
                };
                if (text.Length == 0) break;
                source.Append(text);
            }

            string sourceText = source.ToString();
            var tokenizer = new PythonTokenizer(sourceText);
            tokenizer.Tokenize();
            return new TokenizerIter(
                new List<PythonToken>(tokenizer.Tokens).ToArray(),
                SplitLines(sourceText),
                extraTokens);
        }

        public TokenizerIter __iter__() => this;

        public object __next__() {
            while (_position < _tokens.Length) {
                PythonToken token = _tokens[_position++];
                if (!_extraTokens
                    && token.Kind == PythonTokenKind.NonLogicalNewLine) {
                    continue;
                }
                if (!_extraTokens
                    && token.Kind == PythonTokenKind.TypeComment) {
                    continue;
                }
                int tokenType = GetTokenType(token);
                string line = token.StartLine > 0 && token.StartLine <= _lines.Length
                    ? _lines[token.StartLine - 1]
                    : string.Empty;
                return PythonTuple.MakeTuple(
                    tokenType,
                    token.Text,
                    PythonTuple.MakeTuple(token.StartLine, token.StartColumn),
                    PythonTuple.MakeTuple(token.EndLine, token.EndColumn),
                    line);
            }
            throw PythonOps.StopIteration();
        }

        private static int GetTokenType(PythonToken token) {
            return token.Kind switch {
                PythonTokenKind.EndMarker => 0,
                PythonTokenKind.Name => 1,
                PythonTokenKind.Number => 2,
                PythonTokenKind.String => 3,
                PythonTokenKind.NewLine => 4,
                PythonTokenKind.Indent => 5,
                PythonTokenKind.Dedent => 6,
                PythonTokenKind.FStringStart => 59,
                PythonTokenKind.FStringMiddle => 60,
                PythonTokenKind.FStringEnd => 61,
                PythonTokenKind.TStringStart => 62,
                PythonTokenKind.TStringMiddle => 63,
                PythonTokenKind.TStringEnd => 64,
                PythonTokenKind.TypeComment => 65,
                PythonTokenKind.NonLogicalNewLine => 66,
                PythonTokenKind.Operator => 55,
                PythonTokenKind.Error => 56,
                _ => 56
            };
        }

        private static string[] SplitLines(string source) {
            if (source.Length == 0) return Array.Empty<string>();
            var lines = new List<string>();
            int start = 0;
            for (int i = 0; i < source.Length; i++) {
                if (source[i] != '\n' && source[i] != '\r') continue;
                int end = i + 1;
                if (source[i] == '\r'
                    && end < source.Length
                    && source[end] == '\n') {
                    end++;
                }
                lines.Add(source.Substring(start, end - start));
                start = end;
                i = end - 1;
            }
            if (start < source.Length) lines.Add(source.Substring(start));
            return lines.ToArray();
        }
    }
}
