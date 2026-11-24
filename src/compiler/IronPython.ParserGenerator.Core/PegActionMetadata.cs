using System;
using System.Collections.Generic;

namespace IronPython.ParserGenerator {
    public static class PegActionMetadata {
        public static int ExtractMinimumMinorVersion(string? action) {
            if (action is null || action.Length == 0) return 0;
            string value = action;

            int minimum = 0;
            int searchIndex = 0;
            while (searchIndex < value.Length) {
                int call = value.IndexOf("CHECK_VERSION", searchIndex, StringComparison.Ordinal);
                if (call < 0) break;

                int index = call + "CHECK_VERSION".Length;
                SkipSpace(value, ref index);
                if (index >= value.Length || value[index] != '(') {
                    searchIndex = index;
                    continue;
                }

                int depth = 0;
                int commaCount = 0;
                for (; index < value.Length; index++) {
                    char character = value[index];
                    if (character == '(') {
                        depth++;
                    } else if (character == ')') {
                        if (--depth == 0) {
                            searchIndex = index + 1;
                            break;
                        }
                    } else if (character == ',' && depth == 1) {
                        commaCount++;
                        if (commaCount == 1) {
                            int numberStart = index + 1;
                            SkipSpace(value, ref numberStart);
                            int numberEnd = numberStart;
                            while (numberEnd < value.Length && char.IsDigit(value[numberEnd])) numberEnd++;
                            if (numberEnd > numberStart
                                && int.TryParse(
                                    value.Substring(numberStart, numberEnd - numberStart),
                                    out int version)) {
                                minimum = Math.Max(minimum, version);
                            }
                            break;
                        }
                    }
                }
                if (searchIndex <= call) searchIndex = call + 1;
            }
            return minimum;
        }

        public static int CountVersionChecks(PegGrammar grammar) {
            if (grammar == null) throw new ArgumentNullException(nameof(grammar));
            int count = 0;
            foreach (PegGrammarRule rule in grammar.Rules) {
                foreach (PegAlternative alternative in rule.Alternatives) {
                    count += CountOccurrences(alternative.Action);
                }
            }
            return count;
        }

        private static int CountOccurrences(string? action) {
            if (action is null || action.Length == 0) return 0;
            string value = action;
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf("CHECK_VERSION", index, StringComparison.Ordinal)) >= 0) {
                count++;
                index += "CHECK_VERSION".Length;
            }
            return count;
        }

        private static void SkipSpace(string text, ref int index) {
            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        }
    }
}
