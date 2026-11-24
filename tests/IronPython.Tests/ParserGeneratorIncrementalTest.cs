extern alias ParserGeneratorSource;
using ParserGeneratorSource::IronPython.ParserGenerator.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Assert = NUnit.Framework.Assert;
using Is = NUnit.Framework.Is;

namespace IronPython.Tests {
    [TestFixture]
    public sealed class ParserGeneratorIncrementalTest {
        [Test]
        public void ReplacingGrammarAdditionalTextInvalidatesGeneratedDescriptors() {
            const string firstGrammar = @"
start:
    | NAME
";
            const string secondGrammar = @"
start:
    | NAME
    | NUMBER
";
            var grammarText = new MutableAdditionalText("python.gram", firstGrammar);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "GeneratorIncrementalTest",
                references: new[] {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
                },
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new PythonGrammarGenerator().AsSourceGenerator() },
                additionalTexts: new[] { grammarText },
                driverOptions: new GeneratorDriverOptions(
                    IncrementalGeneratorOutputKind.None,
                    trackIncrementalGeneratorSteps: true));

            driver = driver.RunGenerators(compilation);
            string firstOutput = GetDescriptorsSource(driver.GetRunResult());
            var secondText = new MutableAdditionalText("python.gram", secondGrammar);
            driver = driver.ReplaceAdditionalText(grammarText, secondText);
            driver = driver.RunGenerators(compilation);
            string secondOutput = GetDescriptorsSource(driver.GetRunResult());

            Assert.That(secondOutput, Is.Not.EqualTo(firstOutput));
            Assert.That(secondOutput, Does.Contain("start.1"));
        }

        private static string GetDescriptorsSource(GeneratorDriverRunResult result) {
            GeneratedSourceResult source = result.Results
                .SelectMany(run => run.GeneratedSources)
                .Single(item => item.HintName == "PythonGrammarDescriptors.g.cs");
            return source.SourceText.ToString();
        }

        private sealed class MutableAdditionalText : AdditionalText {
            public MutableAdditionalText(string path, string text) {
                Path = path;
                Text = text;
            }

            public override string Path { get; }
            public string Text { get; set; }

            public override SourceText GetText(CancellationToken cancellationToken = default) {
                return SourceText.From(Text);
            }
        }
    }
}
