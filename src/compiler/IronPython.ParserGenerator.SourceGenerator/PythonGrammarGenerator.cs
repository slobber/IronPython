using System;
using System.IO;
using System.Linq;
using System.Text;
using IronPython.ParserGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace IronPython.ParserGenerator.SourceGenerator {
    [Generator(LanguageNames.CSharp)]
    public sealed class PythonGrammarGenerator : IIncrementalGenerator {
        private static readonly DiagnosticDescriptor GenerationError = new DiagnosticDescriptor(
            id: "IPY315001",
            title: "Python grammar generation failed",
            messageFormat: "{0}",
            category: "IronPython.ParserGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public void Initialize(IncrementalGeneratorInitializationContext context) {
            var grammars = context.AdditionalTextsProvider
                .Where(static file =>
                    file.Path.EndsWith(".grammar", StringComparison.OrdinalIgnoreCase) ||
                    file.Path.EndsWith(".gram", StringComparison.OrdinalIgnoreCase) ||
                    file.Path.EndsWith(".asdl", StringComparison.OrdinalIgnoreCase))
                .Select(static (file, cancellationToken) => new GrammarInput(file.Path, file.GetText(cancellationToken)?.ToString() ?? string.Empty));

            context.RegisterSourceOutput(grammars, static (production, input) => {
                try {
                    if (input.Path.EndsWith(".grammar", StringComparison.OrdinalIgnoreCase)) {
                        var grammar = GrammarReader.Read(input.Text);
                        var source = MiniGrammarEmitter.Emit(grammar, "IronPython.Compiler.Generated", "MiniGrammarParser");
                        production.AddSource("MiniGrammarParser.g.cs", SourceText.From(source, Encoding.UTF8));
                    } else if (input.Path.EndsWith(".gram", StringComparison.OrdinalIgnoreCase)) {
                        var grammar = PegGrammarReader.Read(input.Text);
                        var source = PegGrammarValidationEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarBaseline");
                        production.AddSource("PythonGrammarBaseline.g.cs", SourceText.From(source, Encoding.UTF8));
                        var descriptors = PegDescriptorEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarDescriptors");
                        production.AddSource("PythonGrammarDescriptors.g.cs", SourceText.From(descriptors, Encoding.UTF8));
                        var actions = PegActionCatalogEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarActionCatalog");
                        production.AddSource("PythonGrammarActionCatalog.g.cs", SourceText.From(actions, Encoding.UTF8));
                        var dispatcher = PegActionDispatcherEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarActionHandler");
                        production.AddSource("PythonGrammarActionHandler.g.cs", SourceText.From(dispatcher, Encoding.UTF8));
                        var facade = PegParserFacadeEmitter.Emit(
                            "IronPython.Compiler.Generated",
                            "PythonGeneratedParser",
                            "PythonGrammarDescriptors");
                        production.AddSource("PythonGeneratedParser.g.cs", SourceText.From(facade, Encoding.UTF8));
                    } else {
                        var module = AsdlReader.Read(input.Text);
                        var ast = PythonAstEmitter.Emit(module, "IronPython.Compiler.Syntax.Ast");
                        production.AddSource("PythonAst.g.cs", SourceText.From(ast, Encoding.UTF8));
                    }
                } catch (Exception exception) {
                    production.ReportDiagnostic(Diagnostic.Create(GenerationError, Location.None, $"{input.Path}: {exception.Message}"));
                }
            });
        }

        private readonly struct GrammarInput {
            public GrammarInput(string path, string text) {
                Path = path;
                Text = text;
            }

            public string Path { get; }
            public string Text { get; }
        }
    }
}
