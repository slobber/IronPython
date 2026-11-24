using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IronPython.Compiler.Syntax {
    public readonly struct PegActionArgument {
        public PegActionArgument(string? fieldName, object? value) {
            FieldName = fieldName;
            Value = value;
        }

        public string? FieldName { get; }
        public object? Value { get; }
    }

    public sealed class PegActionArguments : IReadOnlyList<object?> {
        private readonly PegActionArgument[] _arguments;

        public PegActionArguments(
            IEnumerable<PegActionArgument> arguments,
            string? actionId = null) {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            _arguments = arguments.ToArray();
            ActionId = actionId;
        }

        public string? ActionId { get; }

        public int Count => _arguments.Length;

        public object? this[int index] => _arguments[index].Value;

        public object? this[string fieldName] {
            get {
                if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));
                for (int i = 0; i < _arguments.Length; i++) {
                    if (string.Equals(_arguments[i].FieldName, fieldName, StringComparison.Ordinal)) {
                        return _arguments[i].Value;
                    }
                }
                throw new KeyNotFoundException(
                    $"PEG action field was not captured: {fieldName}" +
                    (ActionId is null ? string.Empty : $" in {ActionId}"));
            }
        }

        public bool TryGetValue(string fieldName, out object? value) {
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));
            for (int i = 0; i < _arguments.Length; i++) {
                if (string.Equals(_arguments[i].FieldName, fieldName, StringComparison.Ordinal)) {
                    value = _arguments[i].Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public IReadOnlyList<object?> GetValues(string fieldName) {
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));
            var values = new List<object?>();
            for (int i = 0; i < _arguments.Length; i++) {
                if (string.Equals(_arguments[i].FieldName, fieldName, StringComparison.Ordinal)) {
                    values.Add(_arguments[i].Value);
                }
            }
            return values;
        }

        public IEnumerator<object?> GetEnumerator() {
            for (int i = 0; i < _arguments.Length; i++) {
                yield return _arguments[i].Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
