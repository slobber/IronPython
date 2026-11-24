using System;
using System.Collections.Generic;
using System.Linq;

namespace IronPython.ParserGenerator {
    public static class GrammarReader {
        public static GrammarDefinition Read(string text) {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var rules = new List<GrammarRule>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n')) {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') {
                    continue;
                }

                if (line[line.Length - 1] != ';') {
                    throw new FormatException($"Grammar rule must end with ';': {line}");
                }

                var body = line.Substring(0, line.Length - 1);
                var colon = body.IndexOf(':');
                if (colon <= 0) {
                    throw new FormatException($"Grammar rule must contain a name and ':': {line}");
                }

                var name = body.Substring(0, colon).Trim();
                if (!names.Add(name)) {
                    throw new FormatException($"Duplicate grammar rule '{name}'.");
                }

                var itemText = body.Substring(colon + 1).Trim();
                if (itemText.Length == 0) {
                    throw new FormatException($"Grammar rule '{name}' has no items.");
                }

                rules.Add(new GrammarRule(name, ReadItems(itemText)));
            }

            if (rules.Count == 0) {
                throw new FormatException("Grammar contains no rules.");
            }

            return new GrammarDefinition(rules);
        }

        private static IReadOnlyList<GrammarItem> ReadItems(string text) {
            var items = new List<GrammarItem>();
            foreach (var rawItem in text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                var item = rawItem.Trim();
                if (item.Length >= 2 && item[0] == '"' && item[item.Length - 1] == '"') {
                    items.Add(new GrammarItem(GrammarItemKind.Literal, item.Substring(1, item.Length - 2)));
                } else if (item == "EOF") {
                    items.Add(new GrammarItem(GrammarItemKind.EndOfFile, item));
                } else if (item.All(c => char.IsLetterOrDigit(c) || c == '_')) {
                    items.Add(new GrammarItem(GrammarItemKind.Token, item));
                } else {
                    throw new FormatException($"Unsupported grammar item '{item}'.");
                }
            }

            return items;
        }
    }
}
