// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using IronPython.Runtime;
using IronPython.Runtime.Operations;

[assembly: PythonModule("math.integer", typeof(IronPython.Modules.PythonMathInteger))]
namespace IronPython.Modules {
    public static class PythonMathInteger {
        public const string __doc__ = "Integer-only mathematical functions.";

        public static object factorial(object? value)
            => PythonMath.factorial(value);

        public static object gcd(object? left, object? right)
            => PythonMath.gcd(left, right);

        public static object lcm(object? left, object? right) {
            BigInteger x = ToBigInteger(left);
            BigInteger y = ToBigInteger(right);
            if (x.IsZero || y.IsZero) {
                return 0;
            }
            return ToPython(
                BigInteger.Abs(
                    x / BigInteger.GreatestCommonDivisor(x, y) * y));
        }

        public static object lcm(
            [NotNone] params object?[] values) {
            object result = 1;
            foreach (object? value in values.Length == 0 ? new object?[] { 1 } : values) {
                result = lcm(result, value);
            }
            return result;
        }

        public static object isqrt(object? value) {
            BigInteger number = ToBigInteger(value);
            if (number.Sign < 0) {
                throw PythonOps.ValueError(
                    "isqrt() argument must be nonnegative");
            }
            if (number.IsZero) {
                return 0;
            }

            BigInteger current = BigInteger.One << ((GetBitLength(number) + 1) / 2);
            while (true) {
                BigInteger next = (current + number / current) >> 1;
                if (next >= current) {
                    return ToPython(current);
                }
                current = next;
            }
        }

        public static object perm(object? nValue, object? kValue = null) {
            BigInteger n = ToBigInteger(nValue);
            BigInteger k = kValue is null ? n : ToBigInteger(kValue);
            if (n.Sign < 0 || k.Sign < 0) {
                throw PythonOps.ValueError(
                    "perm() argument must be nonnegative");
            }
            if (k > n) {
                return 0;
            }

            BigInteger result = BigInteger.One;
            for (BigInteger value = n - k + 1; value <= n; value++) {
                result *= value;
            }
            return ToPython(result);
        }

        public static object comb(object? nValue, object? kValue) {
            BigInteger n = ToBigInteger(nValue);
            BigInteger k = ToBigInteger(kValue);
            if (n.Sign < 0 || k.Sign < 0) {
                throw PythonOps.ValueError(
                    "comb() argument must be nonnegative");
            }
            if (k > n) {
                return 0;
            }
            k = BigInteger.Min(k, n - k);

            BigInteger result = BigInteger.One;
            for (BigInteger index = BigInteger.One; index <= k; index++) {
                result = result * (n - k + index) / index;
            }
            return ToPython(result);
        }

        private static BigInteger ToBigInteger(object? value) {
            return PythonOps.Index(value) switch {
                int integer => integer,
                BigInteger integer => integer,
                var other => Converter.ConvertToBigInteger(other)
            };
        }

        private static object ToPython(BigInteger value)
            => value >= int.MinValue && value <= int.MaxValue
                ? (int)value
                : value;

        private static int GetBitLength(BigInteger value) {
            byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (bytes.Length == 0) {
                return 0;
            }
            int leading = System.Numerics.BitOperations.LeadingZeroCount(
                (uint)bytes[0]) - 24;
            return (bytes.Length - 1) * 8 + (8 - leading);
        }
    }
}
