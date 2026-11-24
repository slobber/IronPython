using System;
using System.Collections.Generic;

namespace IronPython.ParserGenerator {
    public enum PegElementKind {
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
        Forced
    }

    public sealed class PegElement {
        public PegElement(
            PegElementKind kind,
            string? value = null,
            string? fieldName = null,
            string? annotation = null,
            IReadOnlyList<PegAlternative>? alternatives = null,
            PegElement? child = null,
            PegElement? separator = null) {
            Kind = kind;
            Value = value;
            FieldName = fieldName;
            Annotation = annotation;
            Alternatives = alternatives;
            Child = child;
            Separator = separator;
        }

        public PegElementKind Kind { get; }
        public string? Value { get; }
        public string? FieldName { get; }
        public string? Annotation { get; }
        public IReadOnlyList<PegAlternative>? Alternatives { get; }
        public PegElement? Child { get; }
        public PegElement? Separator { get; }
    }

    public sealed class PegAlternative {
        public PegAlternative(IReadOnlyList<PegElement> elements, string? action, bool endsWithEof) {
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
            Action = action;
            EndsWithEof = endsWithEof;
        }

        public IReadOnlyList<PegElement> Elements { get; }
        public string? Action { get; }
        public bool EndsWithEof { get; }
    }

    public sealed class PegGrammarRule {
        public PegGrammarRule(
            string name,
            string? returnType,
            bool memo,
            IReadOnlyList<PegAlternative> alternatives,
            bool indirectLeftRecursion = false,
            bool leftRecursive = false) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ReturnType = returnType;
            Memo = memo;
            Alternatives = alternatives ?? throw new ArgumentNullException(nameof(alternatives));
            IndirectLeftRecursion = indirectLeftRecursion;
            LeftRecursive = leftRecursive;
        }

        public string Name { get; }
        public string? ReturnType { get; }
        public bool Memo { get; }
        public bool IndirectLeftRecursion { get; }
        public bool LeftRecursive { get; }
        public IReadOnlyList<PegAlternative> Alternatives { get; }
    }

    public sealed class PegGrammar {
        public PegGrammar(IReadOnlyList<PegGrammarRule> rules) {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public IReadOnlyList<PegGrammarRule> Rules { get; }
    }
}
