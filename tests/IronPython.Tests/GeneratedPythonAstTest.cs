using IronPython.Compiler.Syntax.Ast;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Assert = NUnit.Framework.Assert;
using Is = NUnit.Framework.Is;

namespace IronPython.Tests {
    [TestFixture]
    public sealed class GeneratedPythonAstTest {
        [Test]
        public void GeneratesTheCompleteCpython315AstSchema() {
            Assert.That(PythonAstSchema.Types.Keys, Is.SupersetOf(new[] {
                "Module",
                "FunctionDef",
                "Import",
                "ImportFrom",
                "Interpolation",
                "TemplateStr",
                "MatchClass",
                "TypeVar",
                "ParamSpec",
                "TypeVarTuple"
            }));

            AstTypeMetadata functionDef = PythonAstSchema.Types["FunctionDef"];
            Assert.That(functionDef.BaseType, Is.EqualTo("stmt"));
            Assert.That(functionDef.Fields.Select(field => field.Name), Is.EqualTo(new[] {
                "name",
                "args",
                "body",
                "decorator_list",
                "returns",
                "type_comment",
                "type_params",
                "lineno",
                "col_offset",
                "end_lineno",
                "end_col_offset"
            }));

            AstTypeMetadata import = PythonAstSchema.Types["Import"];
            Assert.That(import.Fields.Single(field => field.Name == "is_lazy").Optional, Is.True);
            Assert.That(import.Fields.Single(field => field.Name == "names").Sequence, Is.True);

            AstTypeMetadata matchValue = PythonAstSchema.Types["MatchValue"];
            Assert.That(matchValue.Fields.Single(field => field.Name == "end_lineno").Optional, Is.False);
            Assert.That(matchValue.Fields.Single(field => field.Name == "end_col_offset").Optional, Is.False);
        }

        [Test]
        public void GeneratedConstructorsPreserveFieldsAndSourcePositions() {
            var load = new Load();
            var name = new Name("x", load, 1, 0, 1, 1);
            var statement = new Expr(name, 1, 0, 1, 1);
            var module = new Module(new Stmt[] { statement }, Array.Empty<TypeIgnoreBase>());

            Assert.That(module.NodeKind, Is.EqualTo("Module"));
            Assert.That(statement.LineNumber, Is.EqualTo(1));
            Assert.That(statement.ColumnOffset, Is.EqualTo(0));
            Assert.That(statement.EndLineNumber, Is.EqualTo(1));
            Assert.That(statement.EndColumnOffset, Is.EqualTo(1));
            Assert.That(name.GetFields().Select(field => field.Name), Is.EqualTo(new[] {
                "id",
                "ctx",
                "lineno",
                "col_offset",
                "end_lineno",
                "end_col_offset"
            }));

            var matchValue = new MatchValue(
                new Constant(1, null, 2, 4, 2, 5),
                2,
                4,
                2,
                5);
            Assert.That(matchValue.EndLineNumber, Is.EqualTo(2));
            Assert.That(matchValue.EndColumnOffset, Is.EqualTo(5));
        }

        [Test]
        public void GeneratedWalkerVisitsAllChildNodes() {
            var name = new Name("x", new Load(), 1, 0, 1, 1);
            var statement = new Expr(name, 1, 0, 1, 1);
            var module = new Module(new Stmt[] { statement }, Array.Empty<TypeIgnoreBase>());
            var walker = new RecordingAstWalker();

            walker.Visit(module);

            Assert.That(walker.Kinds, Is.EqualTo(new[] { "Module", "Expr", "Name", "Load" }));
            Assert.That(module.Validate().IsValid, Is.True);
        }

        [Test]
        public void GeneratedValidationRejectsInvalidSourcePositions() {
            var name = new Name("x", new Load(), 0, -1, 0, 0);

            PythonAstValidationResult result = name.Validate();

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
        }

        [Test]
        public void PublicAstRoundTripsThroughTheFieldDictionary() {
            var original = new Name("x", new Load(), 1, 2, 1, 3);

            IReadOnlyDictionary<string, object> fields = PythonAstInterop.ToDictionary(original);
            var roundTripped = (Name)PythonAstInterop.FromDictionary("Name", fields);

            Assert.That(roundTripped.Id, Is.EqualTo("x"));
            Assert.That(roundTripped.Ctx, Is.InstanceOf<Load>());
            Assert.That(roundTripped.LineNumber, Is.EqualTo(1));
            Assert.That(roundTripped.ColumnOffset, Is.EqualTo(2));
        }

        [Test]
        public void PublicAstFactoryRejectsUnknownAndMissingFields() {
            var fields = new Dictionary<string, object> {
                ["id"] = "x",
                ["ctx"] = new Load(),
                ["lineno"] = 1,
                ["col_offset"] = 0,
                ["end_lineno"] = 1,
                ["end_col_offset"] = 1
            };
            fields["unknown"] = 42;

            Assert.Throws<PythonAstConstructionException>(
                () => PythonAstInterop.FromDictionary("Name", fields));

            fields.Remove("unknown");
            fields.Remove("id");
            Assert.Throws<PythonAstConstructionException>(
                () => PythonAstInterop.FromDictionary("Name", fields));
        }

        private sealed class RecordingAstWalker : PythonAstWalker {
            public List<string> Kinds { get; } = new List<string>();

            public override void Visit(PythonAstNode node) {
                Kinds.Add(node.NodeKind);
                base.Visit(node);
            }
        }
    }
}
