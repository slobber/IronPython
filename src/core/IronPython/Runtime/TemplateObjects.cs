// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    [PythonType("Interpolation")]
    public sealed class InterpolationObject : ICodeFormattable {
        public static readonly PythonTuple __match_args__ = PythonTuple.MakeTuple(
            "value",
            "expression",
            "conversion",
            "format_spec");

        public InterpolationObject(
            object? value,
            [NotNone] string expression,
            string? conversion,
            [NotNone] string formatSpec) {
            Value = value;
            Expression = expression;
            Conversion = conversion;
            FormatSpec = formatSpec;
        }

        public static InterpolationObject __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            object? value) {
            return new InterpolationObject(value, string.Empty, null, string.Empty);
        }

        public static InterpolationObject __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            object? value,
            [NotNone] string expression) {
            return new InterpolationObject(value, expression, null, string.Empty);
        }

        public static InterpolationObject __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            object? value,
            [NotNone] string expression,
            object? conversion) {
            return new InterpolationObject(
                value,
                expression,
                ConversionName(conversion),
                string.Empty);
        }

        public static InterpolationObject __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            object? value,
            [NotNone] string expression,
            object? conversion,
            [NotNone] string formatSpec) {
            return new InterpolationObject(
                value,
                expression,
                ConversionName(conversion),
                formatSpec);
        }

        private static string? ConversionName(object? conversion) {
            string? name = conversion switch {
                null => null,
                string value => value,
                _ => throw PythonOps.TypeError(
                    "Interpolation() argument 'conversion' must be str, not {0}",
                    DynamicHelpers.GetPythonType(conversion).Name)
            };
            if (name is not null
                && (name.Length != 1 || name[0] is not ('a' or 'r' or 's'))) {
                throw PythonOps.ValueError(
                    "Interpolation() argument 'conversion' must be one of 's', 'a' or 'r'");
            }
            return name;
        }

        public object? value => Value;

        public string expression => Expression;

        public string? conversion => Conversion;

        public string format_spec => FormatSpec;

        internal object? Value { get; }
        internal string Expression { get; }
        internal string? Conversion { get; }
        internal string FormatSpec { get; }

        public string __repr__(CodeContext context) {
            return $"Interpolation({PythonOps.Repr(context, Value)}, " +
                $"{PythonOps.Repr(context, Expression)}, " +
                $"{PythonOps.Repr(context, Conversion)}, " +
                $"{PythonOps.Repr(context, FormatSpec)})";
        }

        public object __reduce__() {
            return PythonTuple.MakeTuple(
                DynamicHelpers.GetPythonType(this),
                PythonTuple.MakeTuple(
                    Value,
                    Expression,
                    Conversion,
                    FormatSpec));
        }
    }

    [PythonType("Template")]
    public sealed class TemplateObject :
        ICodeFormattable,
        IStructuralEquatable,
        IEnumerable<object> {
        private readonly object[] _parts;
        private readonly PythonTuple _strings;
        private readonly PythonTuple _interpolations;
        private readonly PythonTuple _values;

        public TemplateObject([NotNone] params object[] parts) {
            var strings = new List<string> { string.Empty };
            var interpolations = new List<InterpolationObject>();
            foreach (object? part in parts ?? Array.Empty<object?>()) {
                if (part is string text) {
                    strings[strings.Count - 1] += text;
                } else if (part is InterpolationObject interpolation) {
                    interpolations.Add(interpolation);
                    strings.Add(string.Empty);
                } else {
                    throw PythonOps.TypeError(
                        "Template() arguments must be str or Interpolation objects");
                }
            }
            _parts = parts ?? Array.Empty<object>();
            _strings = PythonTuple.MakeTuple(strings.Cast<object>().ToArray());
            _interpolations = PythonTuple.MakeTuple(interpolations.Cast<object>().ToArray());
            _values = PythonTuple.MakeTuple(
                interpolations.Select(item => item.Value).ToArray());
        }

        public static TemplateObject __new__(
            CodeContext context,
            [NotNone] PythonType cls,
            [NotNone] params object[] parts) {
            return new TemplateObject(parts);
        }

        public PythonTuple strings => _strings;

        public PythonTuple interpolations => _interpolations;

        public PythonTuple values => _values;

        public TemplateIterator __iter__() {
            var values = new List<object>();
            for (int i = 0; i < _interpolations.Count; i++) {
                string text = (string)_strings[i]!;
                if (text.Length != 0) values.Add(text);
                values.Add(_interpolations[i]!);
            }
            string tail = (string)_strings[_strings.Count - 1]!;
            if (tail.Length != 0) {
                values.Add(tail);
            }
            return new TemplateIterator(values);
        }

        IEnumerator<object> IEnumerable<object>.GetEnumerator()
            => __iter__();

        IEnumerator IEnumerable.GetEnumerator()
            => __iter__();

        public int __hash__(CodeContext context) {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        public object __eq__(CodeContext context, [NotNone] object other) {
            if (other is not TemplateObject template) {
                return NotImplementedType.Value;
            }
            if (_parts.Length != template._parts.Length) return false;
            for (int i = 0; i < _parts.Length; i++) {
                if (!PythonOps.IsOrEqualsRetBool(_parts[i], template._parts[i])) {
                    return false;
                }
            }
            return true;
        }

        public object __ne__(CodeContext context, [NotNone] object other) {
            object result = __eq__(context, other);
            return ReferenceEquals(result, NotImplementedType.Value) ? result : !(bool)result;
        }

        public TemplateObject __add__(CodeContext context, [NotNone] object other) {
            if (other is not TemplateObject template) {
                throw PythonOps.TypeError(
                    "can only concatenate string.templatelib.Template (not \"{0}\") to string.templatelib.Template",
                    DynamicHelpers.GetPythonType(other).Name);
            }
            return new TemplateObject(_parts.Concat(template._parts).ToArray());
        }

        public object __radd__(CodeContext context, [NotNone] object other) {
            string name = DynamicHelpers.GetPythonType(other).Name;
            throw PythonOps.TypeError(
                "can only concatenate {0} (not \"string.templatelib.Template\") to {0}",
                name);
        }

        public string __repr__(CodeContext context) {
            var result = new StringBuilder("Template(strings=");
            result.Append(PythonOps.Repr(context, _strings));
            result.Append(", interpolations=");
            result.Append(PythonOps.Repr(context, _interpolations));
            result.Append(')');
            return result.ToString();
        }

        public object __reduce__() {
            return PythonTuple.MakeTuple(
                DynamicHelpers.GetPythonType(this),
                PythonTuple.MakeTuple(_parts));
        }

        bool IStructuralEquatable.Equals(object? other, System.Collections.IEqualityComparer comparer) {
            if (other is not TemplateObject template) return false;
            return _parts.Length == template._parts.Length
                && _parts.Zip(template._parts, comparer.Equals).All(value => value);
        }

        int IStructuralEquatable.GetHashCode(System.Collections.IEqualityComparer comparer) {
            int hash = 17;
            foreach (object part in _parts) {
                hash = unchecked(hash * 31 + (part is null ? 0 : comparer.GetHashCode(part)));
            }
            return hash;
        }

        [PythonType("template_iterator")]
        public sealed class TemplateIterator :
            IEnumerator<object>,
            IEnumerable<object> {
            private readonly IReadOnlyList<object> _values;
            private int _index = -1;

            internal TemplateIterator(IReadOnlyList<object> values) {
                _values = values;
            }

            public object Current => _values[_index];

            object IEnumerator.Current => Current;

            public bool MoveNext() {
                if (_index + 1 >= _values.Count) {
                    return false;
                }
                _index++;
                return true;
            }

            public object __next__() {
                if (!MoveNext()) {
                    throw PythonOps.StopIteration();
                }
                return Current;
            }

            public TemplateIterator __iter__() => this;

            public IEnumerator<object> GetEnumerator() => this;

            IEnumerator IEnumerable.GetEnumerator() => this;

            public void Reset() {
                _index = -1;
            }

            public void Dispose() {
            }
        }
    }
}
