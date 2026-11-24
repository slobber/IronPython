using System;
using System.Collections.Generic;

namespace IronPython.ParserGenerator {
    public enum GrammarItemKind {
        Token,
        Literal,
        EndOfFile
    }

    public sealed class GrammarItem {
        public GrammarItem(GrammarItemKind kind, string value) {
            Kind = kind;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public GrammarItemKind Kind { get; }
        public string Value { get; }
    }

    public sealed class GrammarRule {
        public GrammarRule(string name, IReadOnlyList<GrammarItem> items) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public string Name { get; }
        public IReadOnlyList<GrammarItem> Items { get; }
    }

    public sealed class GrammarDefinition {
        public GrammarDefinition(IReadOnlyList<GrammarRule> rules) {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public IReadOnlyList<GrammarRule> Rules { get; }
    }
}
