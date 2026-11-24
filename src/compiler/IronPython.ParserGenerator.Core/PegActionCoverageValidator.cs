using System;
using System.Collections.Generic;
using System.Linq;

namespace IronPython.ParserGenerator {
    public enum PegActionIssueKind {
        Missing,
        Duplicate,
        Stale
    }

    public sealed class PegActionMapping {
        public PegActionMapping(string actionId, string handlerName) {
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            HandlerName = handlerName ?? throw new ArgumentNullException(nameof(handlerName));
        }

        public string ActionId { get; }
        public string HandlerName { get; }
    }

    public sealed class PegActionIssue {
        public PegActionIssue(PegActionIssueKind kind, string actionId, string message) {
            Kind = kind;
            ActionId = actionId ?? throw new ArgumentNullException(nameof(actionId));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public PegActionIssueKind Kind { get; }
        public string ActionId { get; }
        public string Message { get; }
    }

    public static class PegActionCoverageValidator {
        public static IReadOnlyList<PegActionIssue> Validate(
            PegGrammar grammar,
            IReadOnlyList<PegActionMapping> mappings) {
            if (grammar == null) throw new ArgumentNullException(nameof(grammar));
            if (mappings == null) throw new ArgumentNullException(nameof(mappings));

            var required = new List<string>();
            foreach (PegGrammarRule rule in grammar.Rules) {
                for (int i = 0; i < rule.Alternatives.Count; i++) {
                    CollectAlternative(rule.Alternatives[i], rule.Name + "." + i, required);
                }
            }

            var requiredSet = new HashSet<string>(required, StringComparer.Ordinal);
            var mapped = new HashSet<string>(StringComparer.Ordinal);
            var issues = new List<PegActionIssue>();
            foreach (PegActionMapping mapping in mappings) {
                if (!requiredSet.Contains(mapping.ActionId)) {
                    issues.Add(new PegActionIssue(
                        PegActionIssueKind.Stale,
                        mapping.ActionId,
                        $"Action mapping targets an unknown grammar alternative: {mapping.ActionId}."));
                }
                if (!mapped.Add(mapping.ActionId)) {
                    issues.Add(new PegActionIssue(
                        PegActionIssueKind.Duplicate,
                        mapping.ActionId,
                        $"Action '{mapping.ActionId}' has more than one C# handler."));
                }
            }

            foreach (string actionId in requiredSet) {
                if (!mapped.Contains(actionId)) {
                    issues.Add(new PegActionIssue(
                        PegActionIssueKind.Missing,
                        actionId,
                        $"Action '{actionId}' has no C# handler."));
                }
            }

            return issues
                .OrderBy(issue => issue.ActionId, StringComparer.Ordinal)
                .ThenBy(issue => issue.Kind)
                .ToArray();
        }

        private static void CollectAlternative(PegAlternative alternative, string path, List<string> actionIds) {
            if (alternative.Action is not null) {
                actionIds.Add(path);
            }
            for (int i = 0; i < alternative.Elements.Count; i++) {
                CollectElement(alternative.Elements[i], path + "." + i, actionIds);
            }
        }

        private static void CollectElement(PegElement element, string path, List<string> actionIds) {
            if (element.Child is not null) CollectElement(element.Child, path + ".child", actionIds);
            if (element.Separator is not null) CollectElement(element.Separator, path + ".separator", actionIds);
            if (element.Alternatives is not null) {
                for (int i = 0; i < element.Alternatives.Count; i++) {
                    CollectAlternative(element.Alternatives[i], path + "." + i, actionIds);
                }
            }
        }
    }
}
