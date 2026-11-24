using System;
using System.Collections.Generic;

namespace IronPython.Compiler.Syntax {
    public enum PegParseLimitKind {
        RuleDepth,
        RuleInvocations,
        MemoEntries
    }

    public sealed class PegParserOptions {
        private int _maxRuleDepth = 512;
        private int _maxRuleInvocations = 1_000_000;
        private int _maxMemoEntries = 250_000;
        private int _pythonMajorVersion = 3;
        private int _pythonMinorVersion = 15;

        public int PythonMajorVersion {
            get => _pythonMajorVersion;
            set {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _pythonMajorVersion = value;
            }
        }

        public int PythonMinorVersion {
            get => _pythonMinorVersion;
            set {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _pythonMinorVersion = value;
            }
        }

        public int MaxRuleDepth {
            get => _maxRuleDepth;
            set {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                _maxRuleDepth = value;
            }
        }

        public int MaxRuleInvocations {
            get => _maxRuleInvocations;
            set {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                _maxRuleInvocations = value;
            }
        }

        public int MaxMemoEntries {
            get => _maxMemoEntries;
            set {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                _maxMemoEntries = value;
            }
        }
    }

    public sealed class PegParseLimitException : InvalidOperationException {
        public PegParseLimitException(
            PegParseLimitKind kind,
            int limit,
            IReadOnlyList<string> ruleStack)
            : base(BuildMessage(kind, limit, ruleStack)) {
            Kind = kind;
            Limit = limit;
            RuleStack = ruleStack ?? throw new ArgumentNullException(nameof(ruleStack));
        }

        public PegParseLimitKind Kind { get; }
        public int Limit { get; }
        public IReadOnlyList<string> RuleStack { get; }

        private static string BuildMessage(
            PegParseLimitKind kind,
            int limit,
            IReadOnlyList<string> ruleStack) {
            string path = ruleStack == null || ruleStack.Count == 0
                ? "<none>"
                : string.Join(" -> ", ruleStack);
            return $"PEG parse limit exceeded ({kind}: {limit}); rule stack: {path}";
        }
    }

    public sealed class PegUnsupportedVersionException : InvalidOperationException {
        public PegUnsupportedVersionException(
            string actionId,
            int requiredMinorVersion,
            int actualMajorVersion,
            int actualMinorVersion)
            : base(
                $"PEG action '{actionId}' requires Python 3.{requiredMinorVersion} or later; "
                + $"parser is configured for {actualMajorVersion}.{actualMinorVersion}.") {
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            RequiredMinorVersion = requiredMinorVersion;
            ActualMajorVersion = actualMajorVersion;
            ActualMinorVersion = actualMinorVersion;
        }

        public string ActionId { get; }
        public int RequiredMinorVersion { get; }
        public int ActualMajorVersion { get; }
        public int ActualMinorVersion { get; }
    }
}
