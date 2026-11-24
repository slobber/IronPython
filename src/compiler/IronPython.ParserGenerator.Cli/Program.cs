using System;
using System.IO;
using IronPython.ParserGenerator;

namespace IronPython.ParserGenerator.Cli {
    internal static class Program {
        private static int Main(string[] args) {
            if (args.Length < 2) {
                Console.Error.WriteLine(
                    "Usage: IronPython.ParserGenerator.Cli <python.gram|mini.grammar> <output-directory> [Python.asdl]");
                return 2;
            }

            string inputPath = Path.GetFullPath(args[0]);
            string outputDirectory = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(outputDirectory);
            string text = File.ReadAllText(inputPath);

            if (inputPath.EndsWith(".grammar", StringComparison.OrdinalIgnoreCase)) {
                GrammarDefinition mini = GrammarReader.Read(text);
                File.WriteAllText(
                    Path.Combine(outputDirectory, "MiniGrammarParser.g.cs"),
                    MiniGrammarEmitter.Emit(mini, "IronPython.Compiler.Generated", "MiniGrammarParser"));
            } else if (inputPath.EndsWith(".gram", StringComparison.OrdinalIgnoreCase)) {
                PegGrammar grammar = PegGrammarReader.Read(text);
                File.WriteAllText(
                    Path.Combine(outputDirectory, "PythonGrammarBaseline.g.cs"),
                    PegGrammarValidationEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarBaseline"));
                File.WriteAllText(
                    Path.Combine(outputDirectory, "PythonGrammarDescriptors.g.cs"),
                    PegDescriptorEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarDescriptors"));
                File.WriteAllText(
                    Path.Combine(outputDirectory, "PythonGrammarActionCatalog.g.cs"),
                    PegActionCatalogEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarActionCatalog"));
                File.WriteAllText(
                    Path.Combine(outputDirectory, "PythonGrammarActionHandler.g.cs"),
                    PegActionDispatcherEmitter.Emit(grammar, "IronPython.Compiler.Generated", "PythonGrammarActionHandler"));
                File.WriteAllText(
                    Path.Combine(outputDirectory, "PythonGeneratedParser.g.cs"),
                    PegParserFacadeEmitter.Emit(
                        "IronPython.Compiler.Generated",
                        "PythonGeneratedParser",
                        "PythonGrammarDescriptors"));
            } else if (inputPath.EndsWith(".asdl", StringComparison.OrdinalIgnoreCase)) {
                AsdlModule module = AsdlReader.Read(text);
                File.WriteAllText(
                    Path.Combine(outputDirectory, "PythonAst.g.cs"),
                    PythonAstEmitter.Emit(module, "IronPython.Compiler.Syntax.Ast"));
            } else {
                Console.Error.WriteLine($"Unsupported input extension: {Path.GetExtension(inputPath)}");
                return 2;
            }

            if (args.Length >= 3) {
                string asdlPath = Path.GetFullPath(args[2]);
                AsdlModule module = AsdlReader.Read(File.ReadAllText(asdlPath));
                File.WriteAllText(
                    Path.Combine(outputDirectory, "PythonAst.g.cs"),
                    PythonAstEmitter.Emit(module, "IronPython.Compiler.Syntax.Ast"));
            }

            return 0;
        }
    }
}
