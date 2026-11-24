using System;
using System.Collections.Generic;
using System.Linq;

namespace IronPython.Compiler.Syntax {
    public enum PegDescriptorKind {
        Name,
        String,
        Group,
        Optional,
        Repeat,
        RepeatOne,
        Gather,
        PositiveLookahead,
        NegativeLookahead,
        Cut,
        Forced,
        EndOfFile
    }

    public sealed class PegAlternativeDescriptor {
        public PegAlternativeDescriptor(
            string actionId,
            PegElementDescriptor[] elements,
            bool endsWithEof,
            bool hasAction = false,
            int minimumMinorVersion = 0) {
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
            EndsWithEof = endsWithEof;
            HasAction = hasAction;
            MinimumMinorVersion = minimumMinorVersion;
        }

        public string ActionId { get; }
        public PegElementDescriptor[] Elements { get; }
        public bool EndsWithEof { get; }
        public bool HasAction { get; }
        public int MinimumMinorVersion { get; }
    }

    public sealed class PegElementDescriptor {
        public PegElementDescriptor(
            PegDescriptorKind kind,
            string? value = null,
            string? fieldName = null,
            PegElementDescriptor? child = null,
            PegElementDescriptor? separator = null,
            PegAlternativeDescriptor[]? alternatives = null) {
            Kind = kind;
            Value = value;
            FieldName = fieldName;
            Child = child;
            Separator = separator;
            Alternatives = alternatives;
        }

        public PegDescriptorKind Kind { get; }
        public string? Value { get; }
        public string? FieldName { get; }
        public PegElementDescriptor? Child { get; }
        public PegElementDescriptor? Separator { get; }
        public PegAlternativeDescriptor[]? Alternatives { get; }
    }

    public sealed class PegRuleDescriptor {
        public PegRuleDescriptor(
            string name,
            bool memo,
            PegAlternativeDescriptor[] alternatives,
            bool indirectLeftRecursion = false,
            bool leftRecursive = false) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            IsInvalid = name.StartsWith("invalid_", StringComparison.Ordinal);
            Memo = memo;
            IndirectLeftRecursion = indirectLeftRecursion;
            LeftRecursive = leftRecursive;
            Alternatives = alternatives ?? throw new ArgumentNullException(nameof(alternatives));
        }

        public string Name { get; }
        public bool IsInvalid { get; }
        public bool Memo { get; }
        public bool IndirectLeftRecursion { get; }
        public bool LeftRecursive { get; }
        public PegAlternativeDescriptor[] Alternatives { get; }
    }

    public readonly struct PegToken {
        public PegToken(
            string kind,
            string value,
            int start,
            int end,
            int startLine = 1,
            int startColumn = 0,
            int endLine = 1,
            int endColumn = 0,
            int startByteColumn = 0,
            int endByteColumn = 0) {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Start = start;
            End = end;
            StartLine = startLine;
            StartColumn = startColumn;
            EndLine = endLine;
            EndColumn = endColumn;
            StartByteColumn = startByteColumn;
            EndByteColumn = endByteColumn;
        }

        public string Kind { get; }
        public string Value { get; }
        public int Start { get; }
        public int End { get; }
        public int StartLine { get; }
        public int StartColumn { get; }
        public int EndLine { get; }
        public int EndColumn { get; }
        public int StartByteColumn { get; }
        public int EndByteColumn { get; }
        public bool IsEnd => Kind == "ENDMARKER";
    }

    public interface IPegTokenSource {
        int Position { get; set; }
        PegToken Peek();
        PegToken Next();
    }

    public sealed class PegParseResult {
        public PegParseResult(bool success, int position, object? value = null, string? actionId = null) {
            Success = success;
            Position = position;
            Value = value;
            ActionId = actionId;
        }

        public bool Success { get; }
        public int Position { get; }
        public object? Value { get; }
        public string? ActionId { get; }

        public static PegParseResult Fail(int position) => new PegParseResult(false, position);
    }

    public readonly struct PegActionContext {
        public PegActionContext(string actionId, int start, int end) {
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            Start = start;
            End = end;
        }

        public string ActionId { get; }
        public int Start { get; }
        public int End { get; }
    }

    public interface IPegActionHandler {
        object? Invoke(PegActionContext context, PegActionArguments arguments);
    }

    public sealed class PegActionTraceHandler : IPegActionHandler {
        private readonly List<PegActionContext> _invocations = new List<PegActionContext>();

        public IReadOnlyList<PegActionContext> Invocations => _invocations;

        public object? Invoke(PegActionContext context, PegActionArguments arguments) {
            _invocations.Add(context);
            return arguments;
        }
    }

    public sealed class PegInvalidRuleMatch {
        public PegInvalidRuleMatch(string ruleName, int start, int end) {
            RuleName = ruleName ?? throw new ArgumentNullException(nameof(ruleName));
            Start = start;
            End = end;
        }

        public string RuleName { get; }
        public int Start { get; }
        public int End { get; }
    }

    public interface IPegInvalidRuleHandler {
        PegParseResult Recover(PegInvalidRuleMatch match, PegParseResult parsedValue);
    }

    public sealed class PegParseFailure {
        public PegParseFailure(int position, IEnumerable<string> expected, IReadOnlyList<string> ruleStack) {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            Position = position;
            Expected = expected.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            RuleStack = ruleStack ?? throw new ArgumentNullException(nameof(ruleStack));
        }

        public int Position { get; }
        public IReadOnlyList<string> Expected { get; }
        public IReadOnlyList<string> RuleStack { get; }

        public string Message =>
            Expected.Count == 0
                ? $"Invalid syntax at offset {Position}."
                : $"Invalid syntax at offset {Position}; expected {string.Join(" or ", Expected)}.";
    }

    public sealed class PegParser {
        private readonly Dictionary<string, PegRuleDescriptor> _rules = new Dictionary<string, PegRuleDescriptor>(StringComparer.Ordinal);
        private readonly Dictionary<(string Rule, int Position), PegMemoEntry> _memo = new Dictionary<(string Rule, int Position), PegMemoEntry>();
        private readonly IPegTokenSource _tokens;
        private readonly IPegActionHandler? _actions;
        private readonly IPegInvalidRuleHandler? _invalidRuleHandler;
        private readonly PegParserOptions _options;
        private readonly List<string> _ruleStack = new List<string>();
        private readonly List<PegMemoEntry> _activeMemoEntries = new List<PegMemoEntry>();
        private readonly Stack<PegAlternativeFrame> _alternativeFrames = new Stack<PegAlternativeFrame>();
        private readonly List<PegInvalidRuleMatch> _invalidRuleMatches = new List<PegInvalidRuleMatch>();
        private readonly SortedSet<string> _expected = new SortedSet<string>(StringComparer.Ordinal);
        private int _furthestFailurePosition = -1;
        private string[] _furthestFailureRuleStack = Array.Empty<string>();
        private int _depth;
        private int _ruleInvocations;

        public PegParser(
            PegRuleDescriptor[] rules,
            IPegTokenSource tokens,
            IPegActionHandler? actions = null,
            IPegInvalidRuleHandler? invalidRuleHandler = null,
            PegParserOptions? options = null) {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _actions = actions;
            _invalidRuleHandler = invalidRuleHandler;
            _options = options ?? new PegParserOptions();
            foreach (PegRuleDescriptor rule in rules) {
                _rules.Add(rule.Name, rule);
            }
        }

        public PegParseFailure? LastFailure { get; private set; }
        public IReadOnlyList<PegInvalidRuleMatch> InvalidRuleMatches => _invalidRuleMatches;
        public int LeftRecursionGrowthIterations { get; private set; }

        public PegParseResult Parse(string startRule) {
            _memo.Clear();
            _activeMemoEntries.Clear();
            _alternativeFrames.Clear();
            _invalidRuleMatches.Clear();
            _expected.Clear();
            _furthestFailurePosition = -1;
            _furthestFailureRuleStack = Array.Empty<string>();
            _ruleInvocations = 0;
            LeftRecursionGrowthIterations = 0;
            LastFailure = null;

            PegParseResult result = MatchRule(startRule);
            if (!result.Success) {
                LastFailure = new PegParseFailure(
                    _furthestFailurePosition >= 0 ? _furthestFailurePosition : result.Position,
                    _expected,
                    _furthestFailureRuleStack);
            }
            return result;
        }

        private PegParseResult MatchRule(string name) {
            _ruleInvocations++;
            if (_ruleInvocations > _options.MaxRuleInvocations) {
                throw new PegParseLimitException(
                    PegParseLimitKind.RuleInvocations,
                    _options.MaxRuleInvocations,
                    _ruleStack.ToArray());
            }
            if (_depth >= _options.MaxRuleDepth) {
                throw new PegParseLimitException(
                    PegParseLimitKind.RuleDepth,
                    _options.MaxRuleDepth,
                    _ruleStack.ToArray());
            }

            _depth++;
            _ruleStack.Add(name);
            try {
                return MatchRuleCore(name);
            } finally {
                _ruleStack.RemoveAt(_ruleStack.Count - 1);
                _depth--;
            }
        }

        private PegParseResult MatchRuleCore(string name) {
            int position = _tokens.Position;
            var memoKey = (name, position);
            _rules.TryGetValue(name, out PegRuleDescriptor? rule);
            bool memoize = rule is not null && rule.Memo;
            if (memoize && _memo.TryGetValue(memoKey, out PegMemoEntry? memoized)) {
                PegParseResult current = memoized.InProgress ? memoized.Seed : memoized.Result;
                _tokens.Position = current.Position;
                return current;
            }

            PegMemoEntry? memoEntry = null;
            if (memoize) {
                memoEntry = new PegMemoEntry(new PegParseResult(false, position)) { InProgress = true };
                if (_memo.Count >= _options.MaxMemoEntries) {
                    throw new PegParseLimitException(
                        PegParseLimitKind.MemoEntries,
                        _options.MaxMemoEntries,
                        _ruleStack.ToArray());
                }
                _memo.Add(memoKey, memoEntry);
                _activeMemoEntries.Add(memoEntry);
            }

            PegParseResult result;
            if (rule == null) {
                PegToken token = _tokens.Peek();
                if (token.Kind == name) {
                    _tokens.Next();
                    result = new PegParseResult(true, _tokens.Position, token);
                } else {
                    ReportExpected(position, name);
                    result = PegParseResult.Fail(position);
                }
            } else if (rule.Name == "ENDMARKER" || name == "ENDMARKER") {
                result = _tokens.Peek().IsEnd
                    ? new PegParseResult(true, position, _tokens.Next())
                    : FailAt(position, "end of input");
            } else {
                result = MatchAlternatives(rule.Alternatives, position);
                if (memoEntry is not null && rule.LeftRecursive) {
                    bool seedActionCompleted = false;
                    while (result.Success && result.Position > memoEntry.Seed.Position) {
                        LeftRecursionGrowthIterations++;
                        memoEntry.Seed = CompleteAction(result, position);
                        seedActionCompleted = true;
                        if (rule.IndirectLeftRecursion) {
                            InvalidateDependencies(memoEntry);
                        }
                        _tokens.Position = position;
                        result = MatchAlternatives(rule.Alternatives, position);
                    }
                    result = memoEntry.Seed;
                    memoEntry.Result = result;
                    memoEntry.InProgress = false;
                    if (seedActionCompleted) {
                        result = memoEntry.Seed;
                    } else {
                        result = CompleteAction(result, position);
                    }
                } else {
                    result = CompleteAction(result, position);
                }
                if (memoEntry is not null && !rule.LeftRecursive) {
                    memoEntry.Result = result;
                    memoEntry.InProgress = false;
                }
                if (rule.IsInvalid && result.Success) {
                    var match = new PegInvalidRuleMatch(rule.Name, position, result.Position);
                    _invalidRuleMatches.Add(match);
                    if (_invalidRuleHandler is not null) {
                        result = _invalidRuleHandler.Recover(match, result);
                    } else {
                        result = PegParseResult.Fail(position);
                    }
                }
            }

            if (memoEntry is not null && !memoEntry.InProgress) {
                memoEntry.Result = result;
                RecordMemoDependency(memoKey, memoEntry);
                _activeMemoEntries.Remove(memoEntry);
            }
            if (result.Success) {
                _tokens.Position = result.Position;
            }
            return result;
        }

        private void InvalidateDependencies(PegMemoEntry owner) {
            foreach ((string Rule, int Position) key in owner.Dependencies) {
                if (key.Position == owner.StartPosition
                    && _memo.TryGetValue(key, out PegMemoEntry? dependency)
                    && !dependency.InProgress) {
                    _memo.Remove(key);
                }
            }
            owner.Dependencies.Clear();
        }

        private void RecordMemoDependency((string Rule, int Position) key, PegMemoEntry dependency) {
            for (int i = 0; i < _activeMemoEntries.Count; i++) {
                PegMemoEntry active = _activeMemoEntries[i];
                if (!ReferenceEquals(active, dependency)) {
                    active.Dependencies.Add(key);
                }
            }
        }

        private PegParseResult FailAt(int position, string expectation) {
            ReportExpected(position, expectation);
            return PegParseResult.Fail(position);
        }

        private void ReportExpected(int position, string expectation) {
            if (position > _furthestFailurePosition) {
                _furthestFailurePosition = position;
                _furthestFailureRuleStack = _ruleStack.ToArray();
                _expected.Clear();
            }
            if (position == _furthestFailurePosition) {
                _expected.Add(expectation);
            }
        }

        private PegParseResult MatchAlternatives(PegAlternativeDescriptor[] alternatives, int fallbackPosition) {
            foreach (PegAlternativeDescriptor alternative in alternatives) {
                EnforceVersion(alternative);
                _tokens.Position = fallbackPosition;
                var frame = new PegAlternativeFrame();
                _alternativeFrames.Push(frame);
                bool success = true;
                var arguments = new List<PegActionArgument>();
                try {
                    foreach (PegElementDescriptor element in alternative.Elements) {
                        PegParseResult current = MatchElement(element);
                        if (!current.Success) {
                            success = false;
                            break;
                        }
                        arguments.Add(new PegActionArgument(element.FieldName, current.Value));
                    }

                    if (success && alternative.EndsWithEof && !_tokens.Peek().IsEnd) {
                        ReportExpected(_tokens.Position, "end of input");
                        success = false;
                    }
                } finally {
                    _alternativeFrames.Pop();
                }
                if (success) {
                    object? value = alternative.HasAction
                        ? new PegActionArguments(arguments)
                        : ImplicitAlternativeValue(arguments);
                    return new PegParseResult(
                        true,
                        _tokens.Position,
                        value,
                        alternative.HasAction ? alternative.ActionId : null);
                }
                if (frame.Committed) {
                    break;
                }
            }
            _tokens.Position = fallbackPosition;
            return PegParseResult.Fail(fallbackPosition);
        }

        private static object? ImplicitAlternativeValue(IReadOnlyList<PegActionArgument> arguments) {
            for (int i = arguments.Count - 1; i >= 0; i--) {
                if (arguments[i].Value is not null) {
                    return arguments[i].Value;
                }
            }
            return null;
        }

        private void EnforceVersion(PegAlternativeDescriptor alternative) {
            bool supported = _options.PythonMajorVersion > 3
                || (_options.PythonMajorVersion == 3
                    && _options.PythonMinorVersion >= alternative.MinimumMinorVersion);
            if (!supported) {
                throw new PegUnsupportedVersionException(
                    alternative.ActionId,
                    alternative.MinimumMinorVersion,
                    _options.PythonMajorVersion,
                    _options.PythonMinorVersion);
            }
        }

        private PegParseResult MatchElement(PegElementDescriptor element) {
            int position = _tokens.Position;
            switch (element.Kind) {
                case PegDescriptorKind.Name:
                    return MatchRule(element.Value!);
                case PegDescriptorKind.String: {
                    PegToken token = _tokens.Peek();
                    if (token.Value == element.Value) {
                        _tokens.Next();
                        return new PegParseResult(true, _tokens.Position, token);
                    }
                    return FailAt(position, element.Value!);
                }
                case PegDescriptorKind.EndOfFile:
                    return _tokens.Peek().IsEnd
                        ? new PegParseResult(true, position, _tokens.Next())
                        : FailAt(position, "end of input");
                case PegDescriptorKind.Group:
                    return CompleteAction(MatchAlternatives(element.Alternatives!, position), position);
                case PegDescriptorKind.Optional: {
                    PegParseResult result = element.Alternatives is not null
                        ? CompleteAction(MatchAlternatives(element.Alternatives, position), position)
                        : MatchElement(element.Child!);
                    return result.Success ? result : new PegParseResult(true, position, null);
                }
                case PegDescriptorKind.Repeat:
                    return MatchRepeat(element.Child!, position, minimum: 0);
                case PegDescriptorKind.RepeatOne:
                    return MatchRepeat(element.Child!, position, minimum: 1);
                case PegDescriptorKind.Gather:
                    return MatchGather(element.Separator!, element.Child!, position);
                case PegDescriptorKind.PositiveLookahead: {
                    PegParseResult result = MatchElement(element.Child!);
                    _tokens.Position = position;
                    return result.Success ? new PegParseResult(true, position, result.Value) : PegParseResult.Fail(position);
                }
                case PegDescriptorKind.NegativeLookahead: {
                    PegParseResult result = MatchElement(element.Child!);
                    _tokens.Position = position;
                    return result.Success ? PegParseResult.Fail(position) : new PegParseResult(true, position, null);
                }
                case PegDescriptorKind.Cut:
                    if (_alternativeFrames.Count > 0) {
                        _alternativeFrames.Peek().Committed = true;
                    }
                    return new PegParseResult(true, position, null);
                case PegDescriptorKind.Forced:
                    return MatchElement(element.Child!);
                default:
                    return PegParseResult.Fail(position);
            }
        }

        private PegParseResult MatchRepeat(PegElementDescriptor child, int fallbackPosition, int minimum) {
            int count = 0;
            var values = new List<object?>();
            while (true) {
                int before = _tokens.Position;
                PegParseResult result = MatchElement(child);
                if (!result.Success) {
                    _tokens.Position = before;
                    break;
                }
                values.Add(result.Value);
                count++;
                if (_tokens.Position == before) break;
            }

            if (count < minimum) {
                _tokens.Position = fallbackPosition;
                return PegParseResult.Fail(fallbackPosition);
            }
            return new PegParseResult(true, _tokens.Position, values);
        }

        private PegParseResult CompleteAction(PegParseResult result, int start) {
            if (!result.Success || result.ActionId is null || _actions is null) {
                return result;
            }

            object? value = _actions.Invoke(
                new PegActionContext(result.ActionId, start, result.Position),
                result.Value as PegActionArguments
                    ?? new PegActionArguments(new[] { new PegActionArgument(null, result.Value) }));
            return new PegParseResult(true, result.Position, value, result.ActionId);
        }

        private PegParseResult MatchGather(PegElementDescriptor separator, PegElementDescriptor child, int fallbackPosition) {
            PegParseResult first = MatchElement(child);
            if (!first.Success) return PegParseResult.Fail(fallbackPosition);

            var values = new List<object?> { first.Value };
            while (true) {
                int beforeSeparator = _tokens.Position;
                PegParseResult separated = MatchElement(separator);
                if (!separated.Success) {
                    _tokens.Position = beforeSeparator;
                    break;
                }
                PegParseResult item = MatchElement(child);
                if (!item.Success) {
                    _tokens.Position = beforeSeparator;
                    break;
                }
                values.Add(item.Value);
            }

            return new PegParseResult(true, _tokens.Position, values);
        }

        private sealed class PegAlternativeFrame {
            public bool Committed { get; set; }
        }

        private sealed class PegMemoEntry {
            public PegMemoEntry(PegParseResult seed) {
                Seed = seed;
                Result = seed;
                StartPosition = seed.Position;
            }

            public HashSet<(string Rule, int Position)> Dependencies { get; } = new HashSet<(string Rule, int Position)>();
            public bool InProgress { get; set; }
            public int StartPosition { get; }
            public PegParseResult Seed { get; set; }
            public PegParseResult Result { get; set; }
        }
    }
}
