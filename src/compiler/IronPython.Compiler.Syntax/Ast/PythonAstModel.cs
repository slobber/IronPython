using System;
using System.Collections.Generic;

namespace IronPython.Compiler.Syntax.Ast {
    public abstract class PythonAstNode {
        protected PythonAstNode(
            string kind,
            int? lineNumber,
            int? columnOffset,
            int? endLineNumber,
            int? endColumnOffset) {
            NodeKind = kind ?? throw new ArgumentNullException(nameof(kind));
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
            EndLineNumber = endLineNumber;
            EndColumnOffset = endColumnOffset;
        }

        public string NodeKind { get; }
        public int? LineNumber { get; }
        public int? ColumnOffset { get; }
        public int? EndLineNumber { get; }
        public int? EndColumnOffset { get; }

        public abstract IReadOnlyList<AstFieldValue> GetFields();
        public abstract void WalkChildren(PythonAstWalker walker);

        public PythonAstValidationResult Validate() {
            var validator = new PythonAstValidator();
            validator.Visit(this);
            return new PythonAstValidationResult(validator.Errors);
        }
    }

    public readonly struct AstFieldValue {
        public AstFieldValue(string name, object? value) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value;
        }

        public string Name { get; }
        public object? Value { get; }
    }

    public sealed class AstFieldMetadata {
        public AstFieldMetadata(
            string name,
            string clrType,
            bool optional,
            bool sequence,
            bool attribute) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
            Optional = optional;
            Sequence = sequence;
            Attribute = attribute;
        }

        public string Name { get; }
        public string ClrType { get; }
        public bool Optional { get; }
        public bool Sequence { get; }
        public bool Attribute { get; }
    }

    public sealed class AstTypeMetadata {
        public AstTypeMetadata(
            string name,
            string baseType,
            string clrType,
            IReadOnlyList<AstFieldMetadata> fields) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            BaseType = baseType ?? throw new ArgumentNullException(nameof(baseType));
            ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public string Name { get; }
        public string BaseType { get; }
        public string ClrType { get; }
        public IReadOnlyList<AstFieldMetadata> Fields { get; }
    }

    public class PythonAstWalker {
        public virtual void Visit(PythonAstNode? node) {
            if (node is null) return;
            VisitNode(node);
        }

        protected virtual void VisitNode(PythonAstNode node) {
            node.WalkChildren(this);
        }
    }

    public sealed class PythonAstValidationResult {
        public PythonAstValidationResult(IReadOnlyList<string> errors) {
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        public IReadOnlyList<string> Errors { get; }
        public bool IsValid => Errors.Count == 0;
    }

    internal sealed class PythonAstValidator : PythonAstWalker {
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Errors => _errors;

        protected override void VisitNode(PythonAstNode node) {
            if (node.LineNumber is int line && line < 1) {
                _errors.Add($"{node.NodeKind}: line number must be positive.");
            }
            if (node.ColumnOffset is int column && column < 0) {
                _errors.Add($"{node.NodeKind}: column offset must be non-negative.");
            }
            if (node.EndLineNumber is int endLine && node.LineNumber is int startLine && endLine < startLine) {
                _errors.Add($"{node.NodeKind}: end line precedes start line.");
            }
            base.VisitNode(node);
        }
    }
}
