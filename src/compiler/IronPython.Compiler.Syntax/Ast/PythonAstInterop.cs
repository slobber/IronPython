using System;
using System.Collections.Generic;
using System.Linq;

namespace IronPython.Compiler.Syntax.Ast {
    public static class PythonAstFactorySupport {
        public static T Required<T>(IReadOnlyDictionary<string, object?> fields, string name) {
            if (!fields.TryGetValue(name, out object? value)) {
                throw new PythonAstConstructionException($"Missing required AST field '{name}'.");
            }
            return ConvertValue<T>(name, value);
        }

        public static T? Optional<T>(IReadOnlyDictionary<string, object?> fields, string name) {
            if (!fields.TryGetValue(name, out object? value) || value is null) return default;
            return ConvertValue<T>(name, value);
        }

        public static IReadOnlyList<T> List<T>(IReadOnlyDictionary<string, object?> fields, string name) {
            if (!fields.TryGetValue(name, out object? value) || value is null) {
                throw new PythonAstConstructionException($"Missing required AST sequence field '{name}'.");
            }
            if (value is IReadOnlyList<T> typed) return typed;
            if (value is IEnumerable<object?> values) {
                return values.Select(item => ConvertValue<T>(name, item)).ToArray();
            }
            throw new PythonAstConstructionException(
                $"AST field '{name}' must be a sequence, but was {value.GetType().FullName}.");
        }

        private static T ConvertValue<T>(string name, object? value) {
            if (value is T typed) return typed;
            if (value is null) {
                if (default(T) is null) return default!;
                throw new PythonAstConstructionException(
                    $"AST field '{name}' cannot be null.");
            }
            throw new PythonAstConstructionException(
                $"AST field '{name}' has incompatible type {value.GetType().FullName}; expected {typeof(T).FullName}.");
        }
    }

    public sealed class PythonAstConstructionException : ArgumentException {
        public PythonAstConstructionException(string message) : base(message) { }
    }
}
