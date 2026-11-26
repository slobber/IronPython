// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

using IronPython.Runtime;
using IronPython.Runtime.Binding;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Utils;

[assembly: PythonModule("math", typeof(IronPython.Modules.PythonMath))]
namespace IronPython.Modules {
    public static partial class PythonMath {
        public const string __doc__ = "Provides common mathematical functions.";
        public static readonly PythonList __path__ = new();

#if NET
        public const double tau = Math.Tau;
#else
        public const double tau = 2 * Math.PI;
#endif
        public const double pi = Math.PI;
        public const double e = Math.E;

        private const double degreesToRadians = Math.PI / 180.0;
        private const double radiansToDegrees = 180.0 / Math.PI;

        public static double degrees(double radians) {
            return Check(radians, radians * radiansToDegrees);
        }

        public static double ulp(double value) {
            if (double.IsNaN(value)) return double.NaN;
            if (double.IsInfinity(value)) return double.PositiveInfinity;

            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            int exponent = (int)(((ulong)bits >> 52) & 0x7ff);
            if (exponent == 0) {
                return BitConverter.Int64BitsToDouble(1);
            }
            return Math.ScaleB(1.0, exponent - 1023 - 52);
        }

        public static double radians(double degrees) {
            return Check(degrees, degrees * degreesToRadians);
        }

        public static double cbrt(double value) {
            return Check(value, Math.Cbrt(value));
        }

        public static double exp2(double value) {
            return Check(value, Math.Pow(2.0, value));
        }

        public static bool isnormal(object? value) {
            double number = Converter.ConvertToDouble(value);
            double minimumNormal =
                BitConverter.Int64BitsToDouble(0x0010000000000000L);
            return double.IsFinite(number)
                && number != 0.0
                && Math.Abs(number) >= minimumNormal;
        }

        public static bool issubnormal(object? value) {
            double number = Converter.ConvertToDouble(value);
            double minimumNormal =
                BitConverter.Int64BitsToDouble(0x0010000000000000L);
            return double.IsFinite(number)
                && number != 0.0
                && Math.Abs(number) < minimumNormal;
        }

        public static double fmod(double v, double w) {
            return Check(v, w, v % w);
        }

        private static double sum(List<double> partials) {
            // sum the partials the same was as CPython does
            var n = partials.Count;
            var hi = 0.0;

            if (n == 0) return hi;

            var lo = 0.0;

            // sum exact
            while (n > 0) {
                var x = hi;
                var y = partials[--n];
                hi = x + y;
                lo = y - (hi - x);
                if (lo != 0.0)
                    break;
            }

            if (n == 0) return hi;

            // half-even rounding
            if (lo < 0.0 && partials[n - 1] < 0.0 || lo > 0.0 && partials[n - 1] > 0.0) {
                var y = lo * 2.0;
                var x = hi + y;
                var yr = x - hi;
                if (y == yr)
                    hi = x;
            }
            return hi;
        }

        public static double fsum([NotNone] IEnumerable e) {
            // msum from https://code.activestate.com/recipes/393090/
            var partials = new List<double>();
            bool sawPositiveInfinity = false;
            bool sawNegativeInfinity = false;
            foreach (var v in e.Cast<object>().Select(o => Converter.ConvertToDouble(o))) {
                sawPositiveInfinity |= double.IsPositiveInfinity(v);
                sawNegativeInfinity |= double.IsNegativeInfinity(v);
                var x = v;
                var i = 0;
                for (var j = 0; j < partials.Count; j++) {
                    var y = partials[j];
                    if (Math.Abs(x) < Math.Abs(y)) {
                        var t = x;
                        x = y;
                        y = t;
                    }
                    var hi = x + y;
                    var lo = y - (hi - x);
                    if (lo != 0) {
                        partials[i++] = lo;
                    }
                    x = hi;
                }
                partials.RemoveRange(i, partials.Count - i);
                partials.Add(x);
            }

            double result = sum(partials);
            if (double.IsNaN(result)) {
                if (sawPositiveInfinity && sawNegativeInfinity) {
                    throw PythonOps.ValueError("math domain error");
                }
                return double.NaN;
            }
            if (double.IsInfinity(result)) {
                if (sawPositiveInfinity || sawNegativeInfinity) {
                    return result;
                }
                throw PythonOps.OverflowError("math range error");
            }
            return result;
        }

        public static PythonTuple frexp(double v) {
            if (double.IsInfinity(v) || double.IsNaN(v) || v == 0) {
                return PythonTuple.MakeTuple(v, 0);
            }

            DecomposeDouble(v, out double mantissa, out int exponent);
            return PythonTuple.MakeTuple(mantissa, exponent);
        }

        public static PythonTuple modf(double v) {
            if (double.IsInfinity(v)) {
                return PythonTuple.MakeTuple(0.0, v);
            }
            double w = v % 1.0;
            v -= w;
            return PythonTuple.MakeTuple(w, v);
        }

        public static double ldexp(double v, BigInteger w) {
            if (v == 0.0 || double.IsNaN(v) || double.IsInfinity(v)) {
                return v;
            }
            if (w > int.MaxValue) {
                return Check(v, v > 0 ? double.PositiveInfinity : double.NegativeInfinity);
            }
            if (w < int.MinValue) {
                return v > 0 ? 0.0 : -0.0;
            }
            return Check(v, Math.ScaleB(v, (int)w));
        }

        [PythonHidden]
        public static double hypot(double v, double w) {
            if (double.IsInfinity(v) || double.IsInfinity(w)) {
                return double.PositiveInfinity;
            }
            return Check(v, w, MathUtils.Hypot(v, w));
        }

        public static double pow(double v, double exp) {
            if (v == 1.0 || exp == 0.0) {
                return 1.0;
            } else if (double.IsNaN(v) || double.IsNaN(exp)) {
                return double.NaN;
            } else if (v == 0.0) {
                if (double.IsPositiveInfinity(exp)) return 0.0;
                if (double.IsNegativeInfinity(exp)) {
                    return double.PositiveInfinity;
                }
                if (exp > 0.0) {
                    return BitConverter.DoubleToInt64Bits(v) < 0
                        && exp % 2.0 == 1.0
                        ? -0.0
                        : 0.0;
                }
                throw PythonOps.ValueError("math domain error");
            } else if (double.IsPositiveInfinity(exp)) {
                if (v > 1.0 || v < -1.0) {
                    return double.PositiveInfinity;
                } else if (v == -1.0) {
                    return 1.0;
                } else {
                    return 0.0;
                }
            } else if (double.IsNegativeInfinity(exp)) {
                if (v > 1.0 || v < -1.0) {
                    return 0.0;
                } else if (v == -1.0) {
                    return 1.0;
                } else {
                    return double.PositiveInfinity;
                }
            }
            return Check(v, exp, Math.Pow(v, exp));
        }

        [PythonHidden]
        public static double log(double v0) {
            if (v0 <= 0.0) {
                throw PythonOps.ValueError(
                    "expected a positive input, got {0}",
                    FloatRepr(v0));
            }
            return Check(v0, Math.Log(v0));
        }

        [PythonHidden]
        public static double log(double v0, double v1) {
            if (v0 <= 0.0 || v1 <= 0.0) {
                throw PythonOps.ValueError(
                    "expected a positive input, got {0}",
                    FloatRepr(v0 <= 0.0 ? v0 : v1));
            } else if (v1 == 1.0) {
                throw PythonOps.ZeroDivisionError("float division");
            } else if (v1 == Double.PositiveInfinity) {
                return 0.0;
            }
            return Check(Math.Log(v0, v1));
        }

        public static double log(BigInteger value) {
            if (value.Sign <= 0) {
                throw PythonOps.ValueError("expected a positive input");
            }
            return value.Log();
        }

        public static double log(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            if (value is int or BigInteger) {
                return log(Converter.ConvertToBigInteger(value));
            }
            double val;
            if (!TryConvertToDoubleViaFloat(value, out val)) {
                return log(Converter.ConvertToBigInteger(
                    PythonOps.Index(value)));
            }
            return log(val);
        }

        public static double log(BigInteger value, double newBase) {
            if (newBase <= 0.0) {
                throw PythonOps.ValueError(
                    "expected a positive input, got {0}",
                    FloatRepr(newBase));
            }
            if (value <= 0) {
                throw PythonOps.ValueError("expected a positive input");
            } else if (newBase == 1.0) {
                throw PythonOps.ZeroDivisionError("float division");
            } else if (newBase == Double.PositiveInfinity) {
                return 0.0;
            }
            double result = value.Log(newBase);
            if (double.IsFinite(newBase)
                && newBase > 1.0
                && newBase <= long.MaxValue
                && Math.Truncate(newBase) == newBase) {
                result = SnapExactIntegerLog(
                    value,
                    new BigInteger((long)newBase),
                    result);
            }
            return Check(result);
        }

        public static double log(BigInteger value, BigInteger newBase) {
            if (newBase.Sign <= 0) {
                throw PythonOps.ValueError("expected a positive input");
            }
            if (value.Sign <= 0) {
                throw PythonOps.ValueError("expected a positive input");
            }
            if (newBase == BigInteger.One) {
                throw PythonOps.ZeroDivisionError("float division");
            }
            return SnapExactIntegerLog(
                value,
                newBase,
                BigInteger.Log(value) / BigInteger.Log(newBase));
        }

        private static double SnapExactIntegerLog(
            BigInteger value,
            BigInteger newBase,
            double approximation) {
            double rounded = Math.Round(approximation);
            if (rounded >= 0
                && rounded <= int.MaxValue
                && Math.Abs(approximation - rounded)
                    <= 1e-9 * Math.Max(1.0, Math.Abs(approximation))
                && BigInteger.Pow(newBase, (int)rounded) == value) {
                return rounded;
            }
            return approximation;
        }

        public static double log(object? value, double newBase) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            if (value is int or BigInteger) {
                return log(Converter.ConvertToBigInteger(value), newBase);
            }
            double val;
            if (!TryConvertToDoubleViaFloat(value, out val)) {
                return log(
                    Converter.ConvertToBigInteger(PythonOps.Index(value)),
                    Converter.ConvertToBigInteger(newBase));
            }
            return log(val, newBase);
        }

        public static double log(object? value, object? newBase) {
            if (TryConvertToDoubleViaFloat(value, out double val)
                && TryConvertToDoubleViaFloat(newBase, out double baseValue)) {
                return log(val, baseValue);
            }
            return log(
                Converter.ConvertToBigInteger(PythonOps.Index(value)),
                Converter.ConvertToBigInteger(PythonOps.Index(newBase)));
        }

        public static double log2(double x) {
            if (x <= 0) {
                throw PythonOps.ValueError(
                    "expected a positive input, got {0}",
                    FloatRepr(x));
            }
            if (double.IsPositiveInfinity(x) || double.IsNaN(x)) return x;

#if NETCOREAPP
            return Math.Log2(x);
#else
            DecomposeDouble(x, out double mantissa, out int exponent);

            if (x >= 1)
                return Math.Log(mantissa * 2, 2) + (exponent - 1); // similar to CPython for precision
            else
                return Math.Log(mantissa, 2) + exponent;
#endif
        }

        public static double log2(BigInteger x) {
            if (x <= 0) throw PythonOps.ValueError("expected a positive input");

            // cast to double if we can
            var d = (double)x;
            if (!double.IsPositiveInfinity(d)) return log2(d);

            // bring to into double range and try again
            var y = BigInteger.Log(x, 2);
            var z = (int)Math.Ceiling(y) - 1023;
            x >>= z;

            Debug.Assert(!double.IsPositiveInfinity((double)x));

            return log2((double)x) + z;
        }

        public static double log2(object? value) {
            if (value is int or BigInteger) {
                return log2(Converter.ConvertToBigInteger(value));
            }
            if (TryConvertToDoubleViaFloat(value, out double number)) {
                return log2(number);
            }
            return log2(Converter.ConvertToBigInteger(
                PythonOps.Index(value)));
        }

        [PythonHidden]
        public static double log10(double v0) {
            if (v0 <= 0.0) {
                throw PythonOps.ValueError(
                    "expected a positive input, got {0}",
                    FloatRepr(v0));
            }
            return Check(v0, Math.Log10(v0));
        }

        public static double log10(BigInteger value) {
            if (value.Sign <= 0) {
                throw PythonOps.ValueError("expected a positive input");
            }
            return SnapExactIntegerLog(
                value,
                new BigInteger(10),
                BigInteger.Log(value, 10.0));
        }

        public static double log10(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            if (value is int or BigInteger) {
                return log10(Converter.ConvertToBigInteger(value));
            }
            double val;
            if (!TryConvertToDoubleViaFloat(value, out val)) {
                return log10(Converter.ConvertToBigInteger(
                    PythonOps.Index(value)));
            }
            return log10(val);
        }

        private static bool TryConvertToDoubleViaFloat(
            object? value,
            out double result) {
            try {
                result = Converter.ConvertToDouble(PythonCalls.Call(
                    DefaultContext.Default,
                    TypeCache.Double,
                    value));
                return true;
            } catch (Exception) {
                result = default;
                return false;
            }
        }

        public static double log1p(double v0) {
            // Calculate log(1.0 + v0) using William Kahan's algorithm for numerical precision

            if (double.IsPositiveInfinity(v0)) {
                return double.PositiveInfinity;
            }

            double v1 = v0 + 1.0;

            // Linear approximation for very small v0
            if (v1 == 1.0) {
                return v0;
            }

            // Apply correction factor
            return log(v1) * (v0 / (v1 - 1.0));
        }

        public static double log1p(BigInteger value) {
            return log(value + BigInteger.One);
        }

        public static double log1p(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            double val;
            if (Converter.TryConvertToDouble(value, out val)) {
                return log1p(val);
            } else {
                return log1p(Converter.ConvertToBigInteger(value));
            }
        }

        public static double expm1(double v0) {
            return Check(v0, Math.Tanh(v0 / 2.0) * (Math.Exp(v0) + 1.0));
        }

        public static double asinh(double x) {
            if (x == 0.0 || double.IsInfinity(x)) {
                return x;
            }

#if NETCOREAPP
            return Math.Asinh(x);
#else
            // rewrote ln(x + sqrt(x**2 + 1)) for precision
            if (Math.Abs(x) > 1.0) {
                return Math.Sign(x) * (Math.Log(Math.Abs(x)) + Math.Log(1.0 + MathUtils.Hypot(1.0, 1.0 / x)));
            } else {
                var x2 = x * x;
                return log1p(x + x2 / (Math.Sqrt(x2 + 1) + 1));
            }
#endif
        }

        public static double asinh(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            double val;
            if (Converter.TryConvertToDouble(value, out val)) {
                return asinh(val);
            } else {
                return asinh(Converter.ConvertToBigInteger(value));
            }
        }

        public static double acosh(double x) {
            if (x < 1.0) {
                throw PythonOps.ValueError("math domain error");
            } else if (double.IsPositiveInfinity(x)) {
                return double.PositiveInfinity;
            }

#if NETCOREAPP
            return Math.Acosh(x);
#else
            if (x < 2) {
                var c = x - 1;
                return log1p(c + Math.Sqrt(c * c + 2 * c));
            } else {
                // rewrote ln(x + sqrt(x**2 - 1)) for precision
                double c = Math.Sqrt(x + 1.0);
                return Math.Log(c) + Math.Log(x / c + Math.Sqrt(x - 1.0));
            }
#endif
        }

        public static double acosh(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            double val;
            if (Converter.TryConvertToDouble(value, out val)) {
                return acosh(val);
            } else {
                return acosh(Converter.ConvertToBigInteger(value));
            }
        }

        public static double atanh(double x) {
            if (x >= 1.0 || x <= -1.0) {
                throw PythonOps.ValueError(
                    "expected a number between -1 and 1, got {0}",
                    FloatRepr(x));
            } else if (x == 0.0) {
                // preserve +/-0.0
                return x;
            }

#if NETCOREAPP
            return Math.Atanh(x);
#else
            return (log1p(x) - log1p(-x)) * 0.5;
#endif

        }

        public static double atanh(BigInteger value) {
            if (value == 0) {
                return 0;
            } else {
                throw PythonOps.ValueError("math domain error");
            }
        }

        public static double atanh(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            double val;
            if (Converter.TryConvertToDouble(value, out val)) {
                return atanh(val);
            } else {
                return atanh(Converter.ConvertToBigInteger(value));
            }
        }

        public static double atan2(double v0, double v1) {
            if (double.IsNaN(v0) || double.IsNaN(v1)) {
                return double.NaN;
            } else if (double.IsInfinity(v0)) {
                if (double.IsPositiveInfinity(v1)) {
                    return pi * 0.25 * Math.Sign(v0);
                } else if (double.IsNegativeInfinity(v1)) {
                    return pi * 0.75 * Math.Sign(v0);
                } else {
                    return pi * 0.5 * Math.Sign(v0);
                }
            } else if (double.IsInfinity(v1)) {
                return v1 > 0.0 ? 0.0 : pi * DoubleOps.Sign(v0);
            }
            return Math.Atan2(v0, v1);
        }

        public static object ceil(CodeContext context, object? x) {
            object val;
            if (PythonTypeOps.TryInvokeUnaryOperator(context, x, "__ceil__", out val)) {
                return val;
            }
            if (x is int or BigInteger) return x;
            if (Converter.TryConvertToDouble(x, out double number)) {
                return ceil(number);
            }

            throw PythonOps.TypeError("a float is required");
        }

        [PythonHidden]
        public static object ceil(double v0) {
            if (double.IsInfinity(v0)) throw PythonOps.OverflowError("cannot convert float infinity to integer");
            if (double.IsNaN(v0)) throw PythonOps.ValueError("cannot convert float NaN to integer");

            var res = Math.Ceiling(v0);
            if (res < int.MinValue || res > int.MaxValue) {
                return (BigInteger)res;
            }
            return (int)res;
        }

        /// <summary>
        /// Error function on real values
        /// </summary>
        public static double erf(double v0) {
            return MathUtils.Erf(v0);
        }

        /// <summary>
        /// Complementary error function on real values: erfc(x) =  1 - erf(x)
        /// </summary>
        public static double erfc(double v0) {
            return MathUtils.ErfComplement(v0);
        }

        public static object factorial(double v0) {
            throw PythonOps.TypeError(
                "factorial() only accepts integral values");
        }

        public static object factorial(BigInteger value) {
            if (value < 0) {
                throw PythonOps.ValueError("factorial() not defined for negative values");
            }
            if (value > SysModule.maxsize) {
                throw PythonOps.OverflowError("factorial() argument should not exceed {0}", SysModule.maxsize);
            }

            BigInteger val = 1;
            for (BigInteger mul = value; mul > BigInteger.One; mul -= BigInteger.One) {
                val *= mul;
            }

            if (val > int.MaxValue) {
                return val;
            }
            return (int)val;
        }

        public static object factorial(object? value) {
            if (value is double or float) {
                throw PythonOps.TypeError(
                    "factorial() only accepts integral values");
            }
            try {
                return PythonOps.Index(value) switch {
                    int integer => factorial(new BigInteger(integer)),
                    BigInteger integer => factorial(integer),
                    var indexed => factorial(Converter.ConvertToBigInteger(indexed))
                };
            } catch (TypeErrorException) {
                throw PythonOps.TypeError(
                    "factorial() only accepts integral values");
            }
        }

        public static object lcm(
            [NotNone] params object?[] values) {
            object result = 1;
            foreach (object? value in values.Length == 0 ? new object?[] { 1 } : values) {
                result = PythonMathInteger.lcm(result, value);
            }
            return result;
        }

        public static object isqrt(object? value)
            => PythonMathInteger.isqrt(value);

        public static object perm(object? value, object? kValue = null)
            => PythonMathInteger.perm(value, kValue);

        public static object comb(object? value, object? kValue)
            => PythonMathInteger.comb(value, kValue);

        public static object floor(CodeContext context, object? x) {
            object val;
            if (PythonTypeOps.TryInvokeUnaryOperator(context, x, "__floor__", out val)) {
                return val;
            }
            if (x is int or BigInteger) return x;
            if (Converter.TryConvertToDouble(x, out double number)) {
                return floor(number);
            }

            throw PythonOps.TypeError("a float is required");
        }

        [PythonHidden]
        public static object floor(double v0) {
            if (double.IsInfinity(v0)) throw PythonOps.OverflowError("cannot convert float infinity to integer");
            if (double.IsNaN(v0)) throw PythonOps.ValueError("cannot convert float NaN to integer");

            var res = Math.Floor(v0);
            if (res < int.MinValue || res > int.MaxValue) {
                return (BigInteger)res;
            }
            return (int)res;
        }

        /// <summary>
        /// Gamma function on real values
        /// </summary>
        public static double gamma(double v0) {
            if (v0 <= 0.0 && Math.Floor(v0) == v0) {
                string valueText = v0 >= long.MinValue && v0 <= long.MaxValue
                    ? ((long)v0).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                    : FloatRepr(v0);
                throw PythonOps.ValueError(
                    "expected a noninteger or positive integer, got {0}",
                    valueText);
            }
            return Check(v0, MathUtils.Gamma(v0));
        }

        /// <summary>
        /// Natural log of absolute value of Gamma function
        /// </summary>
        public static double lgamma(double v0) {
            return Check(v0, MathUtils.LogGamma(v0));
        }

        public static object? trunc(CodeContext/*!*/ context, object? value) {
            object? func;
            if (PythonOps.TryGetBoundAttr(value, "__trunc__", out func)) {
                return PythonCalls.Call(context, func);
            } else {
                throw PythonOps.TypeError("type {0} doesn't define __trunc__ method", PythonOps.GetPythonTypeName(value));
            }
        }

        public static bool isfinite(double x) {
            return !double.IsInfinity(x) && !double.IsNaN(x);
        }

        public static bool isinf(double v0) {
            return double.IsInfinity(v0);
        }

        public static bool isinf(BigInteger value) {
            return false;
        }

        public static bool isinf(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            double val;
            if (Converter.TryConvertToDouble(value, out val)) {
                return isinf(val);
            }
            return false;
        }

        public static bool isnan(double v0) {
            return double.IsNaN(v0);
        }

        public static bool isnan(BigInteger value) {
            return false;
        }

        public static bool isnan(object? value) {
            // CPython tries float first, then double, so we need
            // an explicit overload which properly matches the order here
            double val;
            if (Converter.TryConvertToDouble(value, out val)) {
                return isnan(val);
            }
            return false;
        }

        public static double copysign(double x, double y) {
            return DoubleOps.CopySign(x, y);
        }

        public static double copysign(object? x, object? y) {
            double val, sign;
            if (!Converter.TryConvertToDouble(x, out val) ||
                !Converter.TryConvertToDouble(y, out sign)) {
                throw PythonOps.TypeError("TypeError: a float is required");
            }
            return DoubleOps.CopySign(val, sign);
        }

        public static double fma(double x, double y, double z) {
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) {
                return double.NaN;
            }
            if ((x == 0.0 && double.IsInfinity(y))
                || (y == 0.0 && double.IsInfinity(x))) {
                throw PythonOps.ValueError("math domain error");
            }
            double product = x * y;
            if (double.IsInfinity(product) && double.IsInfinity(z)
                && (double.IsInfinity(x) || double.IsInfinity(y))
                && Math.Sign(product) != Math.Sign(z)) {
                throw PythonOps.ValueError("math domain error");
            }
            double result = Math.FusedMultiplyAdd(x, y, z);
            if (double.IsInfinity(result)
                && double.IsFinite(x)
                && double.IsFinite(y)
                && double.IsFinite(z)) {
                throw PythonOps.OverflowError("math range error");
            }
            return result;
        }

        public static double fmax(double x, double y) {
            if (double.IsNaN(x)) return y;
            if (double.IsNaN(y)) return x;
            if (x == y
                && BitConverter.DoubleToInt64Bits(x) < 0
                && BitConverter.DoubleToInt64Bits(y) > 0) {
                return x;
            }
            return Math.Max(x, y);
        }

        public static double fmin(double x, double y) {
            if (double.IsNaN(x)) return y;
            if (double.IsNaN(y)) return x;
            if (x == y
                && BitConverter.DoubleToInt64Bits(x) > 0
                && BitConverter.DoubleToInt64Bits(y) < 0) {
                return x;
            }
            return Math.Min(x, y);
        }

        public static double remainder(double x, double y) {
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            if (double.IsInfinity(x) || y == 0.0) {
                throw PythonOps.ValueError("math domain error");
            }
            if (double.IsInfinity(y)) return x;
            return Math.IEEERemainder(x, y);
        }

        public static double nextafter(double x, double y, int steps = 1) {
            if (steps < 0) {
                throw PythonOps.ValueError("steps must be non-negative");
            }
            for (int i = 0; i < steps; i++) {
                x = NextAfter(x, y);
            }
            return x;
        }

        private static double NextAfter(double x, double y) {
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
            if (x == y
                || BitConverter.DoubleToInt64Bits(x)
                    == BitConverter.DoubleToInt64Bits(y)) {
                return y;
            }
            if (double.IsInfinity(x)) {
                return x > 0 ? double.MaxValue : -double.MaxValue;
            }
            if (x == 0.0) {
                return y > 0
                    ? BitConverter.Int64BitsToDouble(1)
                    : BitConverter.Int64BitsToDouble(
                        unchecked((long)0x8000000000000001));
            }

            long bits = BitConverter.DoubleToInt64Bits(x);
            bool moveTowardGreater = y > x;
            if (x < 0) moveTowardGreater = !moveTowardGreater;
            bits += moveTowardGreater ? 1 : -1;
            return BitConverter.Int64BitsToDouble(bits);
        }

        public static bool signbit(double value)
            => BitConverter.DoubleToInt64Bits(value) < 0;

        public static bool signbit(BigInteger value) => value.Sign < 0;

        public static bool signbit(object? value) {
            if (value is string or char) {
                throw PythonOps.TypeError("must be real number, not {0}",
                    PythonOps.GetPythonTypeName(value));
            }
            return signbit(Converter.ConvertToDouble(value));
        }

        public static double hypot(
            CodeContext context,
            [NotNone] params object[] values) {
            if (values.Length == 0) {
                return 0.0;
            }
            var coordinates = new double[values.Length];
            double maximum = 0.0;
            bool foundNaN = false;
            for (int i = 0; i < values.Length; i++) {
                double coordinate = Math.Abs(
                    Converter.ConvertToDouble(values[i]));
                coordinates[i] = coordinate;
                foundNaN |= double.IsNaN(coordinate);
                if (coordinate > maximum) maximum = coordinate;
            }
            if (double.IsInfinity(maximum)) return maximum;
            if (foundNaN) return double.NaN;
            if (maximum == 0.0 || coordinates.Length <= 1) return maximum;

            int maximumExponent = Math.ILogB(maximum) + 1;
            if (maximumExponent < -1023) {
                for (int i = 0; i < coordinates.Length; i++) {
                    coordinates[i] /= double.Epsilon;
                }
                return double.Epsilon
                    * VectorNorm(coordinates, maximum / double.Epsilon);
            }
            return VectorNorm(coordinates, maximum);
        }

        private static double VectorNorm(
            IReadOnlyList<double> coordinates,
            double maximum) {
            int maximumExponent = Math.ILogB(maximum) + 1;
            double scale = Math.ScaleB(1.0, -maximumExponent);
            double compensatedSum = 1.0;
            double fraction1 = 0.0;
            double fraction2 = 0.0;
            foreach (double value in coordinates) {
                double x = value * scale;
                (double hi, double lo) product = TwoProduct(x, x);
                (double hi, double lo) sum =
                    FastTwoSum(compensatedSum, product.hi);
                compensatedSum = sum.hi;
                fraction1 += product.lo;
                fraction2 += sum.lo;
            }

            double result = Math.Sqrt(
                compensatedSum - 1.0 + (fraction1 + fraction2));
            (double hi, double lo) correction =
                TwoProduct(-result, result);
            (double hi, double lo) correctionSum =
                FastTwoSum(compensatedSum, correction.hi);
            compensatedSum = correctionSum.hi;
            fraction1 += correction.lo;
            fraction2 += correctionSum.lo;
            double residual =
                compensatedSum - 1.0 + (fraction1 + fraction2);
            result += residual / (2.0 * result);
            return Math.ScaleB(result, maximumExponent);
        }

        private static (double hi, double lo) TwoProduct(
            double left,
            double right) {
            double product = left * right;
            return (
                product,
                Math.FusedMultiplyAdd(left, right, -product));
        }

        private static (double hi, double lo) FastTwoSum(
            double left,
            double right) {
            double sum = left + right;
            return (sum, right - (sum - left));
        }

        private static string FloatRepr(double value)
            => PythonOps.Repr(DefaultContext.Default, value);

        public static double dist(
            CodeContext context,
            [NotNone] params object[] coordinates) {
            if (coordinates.Length != 2) {
                throw PythonOps.TypeError(
                    "dist() takes exactly 2 arguments");
            }
            if (TryEnumerate(context, coordinates[0], out var left)
                && TryEnumerate(context, coordinates[1], out var right)) {
                if (left.Count != right.Count) {
                    throw PythonOps.ValueError(
                        "both points must have the same number of dimensions");
                }
                var differences = new List<object>(left.Count);
                for (int i = 0; i < left.Count; i++) {
                    differences.Add(
                        Converter.ConvertToDouble(left[i])
                        - Converter.ConvertToDouble(right[i]));
                }
                return hypot(context, differences.ToArray());
            }
            throw PythonOps.TypeError("dist() arguments must be iterables");
        }

        private static bool TryEnumerate(
            CodeContext context,
            object value,
            out List<object> result) {
            result = new List<object>();
            if (value is string or char
                || Converter.TryConvertToDouble(value, out _)
                || value is IList) {
                if (value is IList list) {
                    foreach (object item in list) result.Add(item);
                    return true;
                }
                if (value is string or char
                    || Converter.TryConvertToDouble(value, out _)) {
                    return false;
                }
            }
            try {
                IEnumerator enumerator = PythonOps.GetEnumerator(context, value);
                while (enumerator.MoveNext()) result.Add(enumerator.Current);
                return true;
            } catch (TypeErrorException) {
                return false;
            }
        }

        public static object prod(
            CodeContext context,
            [NotNone] IEnumerable values)
            => ProdCore(context, values, 1);

        public static object prod(
            CodeContext context,
            [NotNone] IEnumerable values,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            object result = 1;
            foreach (KeyValuePair<object, object> pair in kwargs) {
                if (pair.Key is not string name || name != "start") {
                    throw PythonOps.TypeError(
                        "prod() got an unexpected keyword argument '{0}'",
                        pair.Key);
                }
                result = pair.Value;
            }
            return ProdCore(context, values, result);
        }

        private static object ProdCore(
            CodeContext context,
            IEnumerable values,
            object result) {
            foreach (object value in values) {
                result = context.LanguageContext.Operation(
                    PythonOperationKind.Multiply,
                    result,
                    value);
            }
            return result;
        }

        public static object sumprod(
            CodeContext context,
            [NotNone] IEnumerable first,
            [NotNone] IEnumerable second) {
            IEnumerator left = first.GetEnumerator();
            IEnumerator right = second.GetEnumerator();
            object result = 0;
            while (true) {
                bool hasLeft = left.MoveNext();
                bool hasRight = right.MoveNext();
                if (hasLeft != hasRight) {
                    throw PythonOps.ValueError(
                        "Inputs are not the same length");
                }
                if (!hasLeft) return result;
                object product = context.LanguageContext.Operation(
                    PythonOperationKind.Multiply,
                    left.Current,
                    right.Current);
                result = context.LanguageContext.Operation(
                    PythonOperationKind.Add,
                    result,
                    product);
            }
        }

        // new in CPython 3.5
        public static object gcd(BigInteger x, BigInteger y) {
            var res = BigInteger.GreatestCommonDivisor(x, y);
            if (res.AsInt32(out var i))
                return i;
            return res;
        }

        public static object gcd([NotNone] params object?[] values) {
            if (values.Length == 0) return 0;
            object result = BigInteger.Abs(
                Converter.ConvertToBigInteger(PythonOps.Index(values[0])));
            for (int i = 1; i < values.Length; i++) {
                result = gcd(
                    result,
                    Converter.ConvertToBigInteger(
                        PythonOps.Index(values[i])));
            }
            return result;
        }

        public static object gcd(object? x, object? y) {
            return gcd(ObjectToBigInteger(x), ObjectToBigInteger(y));

            static BigInteger ObjectToBigInteger(object? x) {
                BigInteger a;
                switch (PythonOps.Index(x)) {
                    case int i:
                        a = i;
                        break;
                    case BigInteger bi:
                        a = bi;
                        break;
                    default:
                        throw new InvalidOperationException();
                }
                return a;
            }
        }

        public static readonly double nan =
            BitConverter.Int64BitsToDouble(0x7ff8000000000000L);

        public static readonly double inf = double.PositiveInfinity; // new in CPython 3.5

        // new in CPython 3.5
        public static bool isclose(double a, double b, double rel_tol = 1e-09, double abs_tol = 0.0) {
            if (rel_tol < 0 || abs_tol < 0) throw PythonOps.ValueError("tolerances must be non-negative");
            //if (double.IsNaN(a) || double.IsNaN(b)) return false;
            if (a == b) return true;
            if (double.IsInfinity(a) || double.IsInfinity(b)) return false;
            return Math.Abs(a - b) <= Math.Max(rel_tol * Math.Max(Math.Abs(a), Math.Abs(b)), abs_tol);
        }

        #region Private Implementation Details

        private static void DecomposeDouble(double d, out double m, out int e) {
            DoubleOps.DecomposeDouble(d, out int sign, out long mantissa, out int exponent);

            if (mantissa < 1L << 52) {
                // subnormal: scale into the normal range so the mantissa has 53 bits
                DoubleOps.DecomposeDouble(d * (1L << 54), out sign, out mantissa, out exponent);
                exponent -= 54;
            }

            m = sign * mantissa / (double)(1L << 53);
            e = exponent + 53;
        }

        private static double Check(double v) {
            return PythonOps.CheckMath(v);
        }

        private static double Check(double input, double output) {
            return PythonOps.CheckMath(input, output);
        }

        private static double Check(double in0, double in1, double output) {
            return PythonOps.CheckMath(in0, in1, output);
        }

        #endregion
    }
}
