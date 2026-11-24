using IronPython.ParserGenerator;
using NUnit.Framework;
using System;
using System.Linq;
using Assert = NUnit.Framework.Assert;
using Is = NUnit.Framework.Is;

namespace IronPython.Tests {
    [TestFixture]
    public sealed class ParserGeneratorValidationTest {
        private const string ActionGrammar = @"
start[object]:
    | a=NAME { a }
    | a=NUMBER { a }
";

        [Test]
        public void ValidatesMissingDuplicateAndStaleActions() {
            PegGrammar grammar = PegGrammarReader.Read(ActionGrammar);

            Assert.That(
                PegActionCoverageValidator.Validate(grammar, Array.Empty<PegActionMapping>())
                    .Count(issue => issue.Kind == PegActionIssueKind.Missing),
                Is.EqualTo(2));

            var duplicate = new[] {
                new PegActionMapping("start.0", "HandleStart0"),
                new PegActionMapping("start.0", "HandleStart0Again"),
                new PegActionMapping("start.1", "HandleStart1"),
                new PegActionMapping("stale.2", "HandleStale")
            };
            PegActionIssue[] issues = PegActionCoverageValidator.Validate(grammar, duplicate).ToArray();

            Assert.That(issues.Any(issue => issue.Kind == PegActionIssueKind.Duplicate), Is.True);
            Assert.That(issues.Any(issue => issue.Kind == PegActionIssueKind.Stale), Is.True);
        }

        [Test]
        public void AcceptsCompleteActionCoverage() {
            PegGrammar grammar = PegGrammarReader.Read(ActionGrammar);
            var mappings = new[] {
                new PegActionMapping("start.0", "HandleStart0"),
                new PegActionMapping("start.1", "HandleStart1")
            };

            Assert.That(PegActionCoverageValidator.Validate(grammar, mappings), Is.Empty);
        }

        [Test]
        public void RejectsInvalidGrammarAndEmitterFailures() {
            Assert.Throws<FormatException>(
                () => PegGrammarReader.Read("broken: 'unterminated"));
            Assert.Throws<ArgumentNullException>(
                () => PegDescriptorEmitter.Emit(null!, "Example", "Descriptors"));
        }

        [Test]
        public void EmittersAreDeterministicForUnchangedInputs() {
            PegGrammar grammar = PegGrammarReader.Read(ActionGrammar);

            string firstDescriptors = PegDescriptorEmitter.Emit(
                grammar,
                "Example",
                "Descriptors");
            string secondDescriptors = PegDescriptorEmitter.Emit(
                grammar,
                "Example",
                "Descriptors");
            string firstActions = PegActionDispatcherEmitter.Emit(
                grammar,
                "Example",
                "Actions");
            string secondActions = PegActionDispatcherEmitter.Emit(
                grammar,
                "Example",
                "Actions");

            Assert.That(secondDescriptors, Is.EqualTo(firstDescriptors));
            Assert.That(secondActions, Is.EqualTo(firstActions));
        }
    }
}
