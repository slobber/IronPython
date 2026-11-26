// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

[assembly: PythonModule("binascii", typeof(IronPython.Modules.PythonBinaryAscii))]
namespace IronPython.Modules {
    public static class PythonBinaryAscii {
        public const string __doc__ = "Provides functions for converting between binary data encoded in various formats and ASCII.";

        public static readonly Bytes BASE64_ALPHABET = Bytes.Make(
            Encoding.ASCII.GetBytes(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"));
        public static readonly Bytes URLSAFE_BASE64_ALPHABET = Bytes.Make(
            Encoding.ASCII.GetBytes(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"));
        public static readonly Bytes UU_ALPHABET = Bytes.Make(Enumerable.Range(32, 64).Select(value => (byte)value).ToArray());
        public static readonly Bytes CRYPT_ALPHABET = Bytes.Make(Encoding.ASCII.GetBytes(
            "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"));
        public static readonly Bytes BINHEX_ALPHABET = Bytes.Make(Encoding.ASCII.GetBytes(
            "!\"#$%&'()*+,-012345689@ABCDEFGHIJKLMNPQRSTUVXYZ[`abcdefhijklmpqr"));
        public static readonly Bytes BASE85_ALPHABET = Bytes.Make(Encoding.ASCII.GetBytes(
            "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+-;<=>?@^_`{|}~"));
        public static readonly Bytes ASCII85_ALPHABET = Bytes.Make(Enumerable.Range(33, 85).Select(value => (byte)value).ToArray());
        public static readonly Bytes Z85_ALPHABET = Bytes.Make(Encoding.ASCII.GetBytes(
            "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.-:+=^!/*?&<>()[]{}@%$#"));
        public static readonly Bytes BASE32_ALPHABET = Bytes.Make(Encoding.ASCII.GetBytes(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
        public static readonly Bytes BASE32HEX_ALPHABET = Bytes.Make(Encoding.ASCII.GetBytes(
            "0123456789ABCDEFGHIJKLMNOPQRSTUV"));

        private static readonly object _ErrorKey = new object();
        private static readonly object _IncompleteKey = new object();

        private static Exception Error(CodeContext/*!*/ context, params object[] args) {
            if (args.Length > 1 && args[0] is string format) {
                args = new object[] {
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        format,
                        args.Skip(1).ToArray())
                };
            }
            return PythonExceptions.CreateThrowable((PythonType)context.LanguageContext.GetModuleState(_ErrorKey), args);
        }
        private static Exception Incomplete(CodeContext/*!*/ context, params object[] args) {
            return PythonExceptions.CreateThrowable((PythonType)context.LanguageContext.GetModuleState(_IncompleteKey), args);
        }

        [SpecialName]
        public static void PerformModuleReload(PythonContext/*!*/ context, PythonDictionary/*!*/ dict) {
            context.EnsureModuleException(_ErrorKey, PythonExceptions.ValueError, dict, "Error", "binascii");
            context.EnsureModuleException(_IncompleteKey, dict, "Incomplete", "binascii");
        }

        #region uu

        public static Bytes a2b_uu(CodeContext/*!*/ context, [NotNone] IBufferProtocol data) {
            using var buffer = data.GetBufferNoThrow();
            if (buffer is null) {
                throw PythonOps.TypeError($"argument should be bytes, buffer or ASCII string, not '{PythonOps.GetPythonTypeName(data)}'");
            }
            return a2b_uu_impl(context, buffer.AsReadOnlySpan());

            static Bytes a2b_uu_impl(CodeContext/*!*/ context, ReadOnlySpan<byte> data) {
                if (data.Length == 0) {
                    throw Error(context, "Missing length byte");
                }

                int outputLength = (data[0] - 32) & 0x3f;
                var output = new List<byte>(outputLength);
                uint leftChar = 0;
                int leftBits = 0;
                int index = 1;
                while (output.Count < outputLength) {
                    byte current = index < data.Length ? data[index] : (byte)0;
                    if (current == (byte)'\n'
                        || current == (byte)'\r'
                        || index >= data.Length) {
                        current = 0;
                    } else if (current < 32 || current > 96) {
                        throw Error(context, "Illegal char");
                    } else {
                        current = (byte)((current - 32) & 0x3f);
                    }

                    leftChar = (leftChar << 6) | current;
                    leftBits += 6;
                    if (leftBits >= 8) {
                        leftBits -= 8;
                        output.Add((byte)(leftChar >> leftBits));
                        leftChar &= (1u << leftBits) - 1;
                    }
                    index++;
                }

                for (; index < data.Length; index++) {
                    byte current = data[index];
                    if (current != 32
                        && current != 96
                        && current != (byte)'\n'
                        && current != (byte)'\r') {
                        throw Error(context, "Trailing garbage");
                    }
                }
                return Bytes.Make(output.ToArray());
            }
        }

        public static Bytes a2b_uu(CodeContext/*!*/ context, [NotNone] string data)
            => a2b_uu(context, AsciiBytes(data));

        public static Bytes b2a_uu(
            CodeContext/*!*/ context,
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            bool backtick = false;
            foreach (KeyValuePair<object, object> item in kwargs) {
                switch (item.Key) {
                    case "backtick":
                        backtick = PythonOps.IsTrue(item.Value);
                        break;
                    default:
                        throw PythonOps.TypeError(
                            "b2a_uu() got an unexpected keyword argument '{0}'",
                            item.Key);
                }
            }

            using var buffer = data.GetBuffer();
            return b2a_uu_impl(context, buffer.AsReadOnlySpan(), backtick);

            static Bytes b2a_uu_impl(CodeContext/*!*/ context, ReadOnlySpan<byte> data, bool backtick) {
                if (data.Length > 45) throw Error(context, "At most 45 bytes at once");

                using var res = new MemoryStream();
                res.WriteByte((byte)(
                    backtick && data.Length == 0
                        ? '`'
                        : 32 + data.Length));

                int leftBits = 0;
                uint leftChar = 0;
                int offset = 0;
                int remaining = data.Length;
                while (remaining > 0 || leftBits != 0) {
                    if (remaining > 0) {
                        leftChar = (leftChar << 8) | data[offset++];
                        remaining--;
                    } else {
                        leftChar <<= 8;
                    }
                    leftBits += 8;
                    while (leftBits >= 6) {
                        int digit = (int)((leftChar >> (leftBits - 6)) & 0x3f);
                        leftBits -= 6;
                        res.WriteByte((byte)(
                            backtick && digit == 0
                                ? '`'
                                : 32 + digit));
                    }
                }
                res.WriteByte((byte)'\n');
                return Bytes.Make(res.ToArray());
            }
        }

        #endregion

        #region base64

        public static Bytes a2b_base64(
            CodeContext/*!*/ context,
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            using var buffer = data.GetBufferNoThrow();
            if (buffer is null) {
                throw PythonOps.TypeError($"argument should be bytes, buffer or ASCII string, not '{PythonOps.GetPythonTypeName(data)}'");
            }
            bool strictMode = false;
            bool strictModeSpecified = false;
            bool padded = true;
            bool canonical = false;
            Bytes alphabet = BASE64_ALPHABET;
            Bytes? ignoreChars = null;
            ParseOptions(
                kwargs,
                "a2b_base64",
                ("strict_mode", value => {
                    strictMode = PythonOps.IsTrue(value);
                    strictModeSpecified = true;
                }),
                ("padded", value => padded = PythonOps.IsTrue(value)),
                ("canonical", value => canonical = PythonOps.IsTrue(value)),
                ("alphabet", value => alphabet = RequireBytesOption(value, "alphabet", 64)),
                ("ignorechars", value => ignoreChars = OptionalBytes(value, "ignorechars")));
            bool strict = strictModeSpecified
                ? strictMode
                : ignoreChars is not null;
            return DecodeBase64(context, buffer.AsReadOnlySpan(), strict, padded, alphabet, ignoreChars, canonical);
        }

        public static Bytes a2b_base64(
            CodeContext/*!*/ context,
            [NotNone] string data,
            [ParamDictionary] IDictionary<object, object> kwargs)
            => a2b_base64(context, AsciiBytes(data), kwargs);

        public static Bytes b2a_base64(
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            bool newline = true;
            bool padded = true;
            int wrapcol = 0;
            byte[]? alphabet = null;
            foreach (KeyValuePair<object, object> item in kwargs) {
                switch (item.Key) {
                    case "newline":
                        newline = PythonOps.IsTrue(item.Value);
                        break;
                    case "padded":
                        padded = PythonOps.IsTrue(item.Value);
                        break;
                    case "wrapcol":
                        wrapcol = ConvertWrapColumn(item.Value);
                        break;
                    case "alphabet":
                        if (item.Value is not IBufferProtocol alphabetBuffer) {
                            throw PythonOps.TypeError(
                                "alphabet must be bytes-like");
                        }
                        alphabet = GetBytes(alphabetBuffer);
                        if (alphabet.Length != 64) {
                            throw PythonOps.ValueError(
                                "alphabet must contain exactly 64 bytes");
                        }
                        break;
                    default:
                        throw PythonOps.TypeError(
                            "b2a_base64() got an unexpected keyword argument '{0}'",
                            item.Key);
                }
            }

            using var buffer = data.GetBuffer();
            using var res = new MemoryStream();
            if (!buffer.AsReadOnlySpan().IsEmpty) {
                res.EncodeData(
                    buffer.AsReadOnlySpan(),
                    (byte)'=',
                    EncodeValue);
            }
            byte[] encoded = res.ToArray();
            int length = encoded.Length;
            if (!padded) {
                while (length > 0 && encoded[length - 1] == '=') {
                    length--;
                }
            }
            if (alphabet is not null) {
                for (int i = 0; i < length; i++) {
                    encoded[i] = alphabet[GetBase64Index(encoded[i])];
                }
            }
            if (wrapcol <= 0 || length <= wrapcol) {
                Array.Resize(ref encoded, length);
                if (newline) {
                    Array.Resize(ref encoded, length + 1);
                    encoded[length] = (byte)'\n';
                }
                return Bytes.Make(encoded);
            }

            using var wrapped = new MemoryStream();
            int lineLength = Math.Max(4, wrapcol / 4 * 4);
            for (int offset = 0; offset < length; offset += lineLength) {
                int count = Math.Min(lineLength, length - offset);
                wrapped.Write(encoded, offset, count);
                if (offset + count < length || newline) {
                    wrapped.WriteByte((byte)'\n');
                }
            }
            return Bytes.Make(wrapped.ToArray());

            static byte EncodeValue(int val) {
                if (val < 26) return (byte)('A' + val);
                if (val < 52) return (byte)('a' + val - 26);
                if (val < 62) return (byte)('0' + val - 52);
                switch (val) {
                    case 62:
                        return (byte)'+';
                    case 63:
                        return (byte)'/';
                    default:
                        throw new InvalidOperationException(
                            string.Format("Bad int val: {0}", val));
                }
            }
        }

        private static int GetBase64Index(byte value) {
            if (value >= 'A' && value <= 'Z') {
                return value - 'A';
            }
            if (value >= 'a' && value <= 'z') {
                return value - 'a' + 26;
            }
            if (value >= '0' && value <= '9') {
                return value - '0' + 52;
            }
            return value == '+' ? 62 : 63;
        }

        private static int ConvertWrapColumn(object value) {
            object indexed;
            try {
                indexed = PythonOps.Index(value);
            } catch (Exception) {
                throw PythonOps.TypeError("wrapcol must be an integer");
            }
            BigInteger column = Converter.ConvertToBigInteger(indexed);
            if (column < 0) {
                throw PythonOps.ValueError("wrapcol must be non-negative");
            }
            return column > int.MaxValue ? int.MaxValue : (int)column;
        }

        private static byte[] GetBytes(IBufferProtocol value) {
            using var buffer = value.GetBuffer();
            return buffer.AsUnsafeArray() ?? buffer.ToArray();
        }

        private static Bytes DecodeBase64(
            CodeContext context,
            ReadOnlySpan<byte> data,
            bool strictMode,
            bool padded,
            Bytes alphabet,
            Bytes? ignoreChars,
            bool canonical) {
            int[] decode = BuildDecodeTable(alphabet, 64);
            bool[] ignore = BuildIgnoreTable(ignoreChars);
            var output = new List<byte>(data.Length * 3 / 4 + 3);
            int quadPosition = 0;
            int leftChar = 0;
            int pads = 0;

            for (int index = 0; index < data.Length; index++) {
                byte current = data[index];
                if (padded && current == (byte)'=') {
                    pads++;
                    if (quadPosition >= 2 && quadPosition + pads <= 4) {
                        continue;
                    }
                    if (!strictMode || ignore[(byte)'=']) {
                        continue;
                    }
                    if (quadPosition == 1) {
                        break;
                    }
                    throw Error(
                        context,
                        quadPosition == 0 && output.Count == 0
                            ? "Leading padding not allowed"
                            : "Excess padding not allowed");
                }

                int value = decode[current];
                if (value < 0 || value >= 64) {
                    if (strictMode && !ignore[current]) {
                        throw Error(
                            context,
                            current == (byte)'='
                                ? "Padding not allowed"
                                : "Only base64 data is allowed");
                    }
                    continue;
                }

                if (pads != 0 && strictMode && !ignore[(byte)'=']) {
                    throw Error(
                        context,
                        quadPosition + pads == 4
                            ? "Excess data after padding"
                            : "Discontinuous padding not allowed");
                }
                pads = 0;

                switch (quadPosition) {
                    case 0:
                        quadPosition = 1;
                        leftChar = value;
                        break;
                    case 1:
                        quadPosition = 2;
                        output.Add((byte)((leftChar << 2) | (value >> 4)));
                        leftChar = value & 0x0f;
                        break;
                    case 2:
                        quadPosition = 3;
                        output.Add((byte)((leftChar << 4) | (value >> 2)));
                        leftChar = value & 0x03;
                        break;
                    default:
                        quadPosition = 0;
                        output.Add((byte)((leftChar << 6) | value));
                        leftChar = 0;
                        break;
                }
            }

            if (quadPosition == 1) {
                throw Error(
                    context,
                    "Invalid base64-encoded string: number of data characters ({0}) cannot be 1 more than a multiple of 4",
                    output.Count / 3 * 4 + 1);
            }
            if (padded && quadPosition != 0 && quadPosition + pads < 4) {
                throw Error(context, "Incorrect padding");
            }
            if (canonical && leftChar != 0) {
                throw Error(context, "Non-zero padding bits");
            }
            return Bytes.Make(output.ToArray());
        }

        public static Bytes a2b_ascii85(
            CodeContext context,
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            bool foldspaces = false;
            bool adobe = false;
            bool canonical = false;
            Bytes? ignoreChars = null;
            ParseOptions(
                kwargs,
                "a2b_ascii85",
                ("foldspaces", value => foldspaces = PythonOps.IsTrue(value)),
                ("adobe", value => adobe = PythonOps.IsTrue(value)),
                ("canonical", value => canonical = PythonOps.IsTrue(value)),
                ("ignorechars", value => ignoreChars = OptionalBytes(value, "ignorechars")));
            return DecodeAscii85(
                context,
                GetBytes(data),
                foldspaces,
                adobe,
                ignoreChars,
                canonical);
        }

        public static Bytes a2b_ascii85(
            CodeContext context,
            [NotNone] string data,
            [ParamDictionary] IDictionary<object, object> kwargs)
            => a2b_ascii85(context, AsciiBytes(data), kwargs);

        public static Bytes b2a_ascii85(
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            bool foldspaces = false;
            bool adobe = false;
            bool pad = false;
            int wrapcol = 0;
            ParseOptions(
                kwargs,
                "b2a_ascii85",
                ("foldspaces", value => foldspaces = PythonOps.IsTrue(value)),
                ("adobe", value => adobe = PythonOps.IsTrue(value)),
                ("pad", value => pad = PythonOps.IsTrue(value)),
                ("wrapcol", value => wrapcol = ConvertWrapColumn(value)));
            return EncodeAscii85(GetBytes(data), foldspaces, adobe, pad, wrapcol);
        }

        public static Bytes a2b_base85(
            CodeContext context,
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            Bytes alphabet = BASE85_ALPHABET;
            Bytes? ignoreChars = null;
            bool canonical = false;
            ParseOptions(
                kwargs,
                "a2b_base85",
                ("alphabet", value => alphabet = RequireBufferAlphabet(value, "alphabet", 85)),
                ("ignorechars", value => ignoreChars = OptionalBytes(value, "ignorechars")),
                ("canonical", value => canonical = PythonOps.IsTrue(value)));
            return DecodeBase85(context, GetBytes(data), alphabet, ignoreChars, canonical);
        }

        public static Bytes a2b_base85(
            CodeContext context,
            [NotNone] string data,
            [ParamDictionary] IDictionary<object, object> kwargs)
            => a2b_base85(context, AsciiBytes(data), kwargs);

        public static Bytes b2a_base85(
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            bool pad = false;
            int wrapcol = 0;
            Bytes alphabet = BASE85_ALPHABET;
            ParseOptions(
                kwargs,
                "b2a_base85",
                ("pad", value => pad = PythonOps.IsTrue(value)),
                ("wrapcol", value => wrapcol = ConvertWrapColumn(value)),
                ("alphabet", value => alphabet = RequireBufferAlphabet(value, "alphabet", 85)));
            return EncodeBase85(GetBytes(data), alphabet, pad, wrapcol);
        }

        public static Bytes a2b_base32(
            CodeContext context,
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            bool padded = true;
            bool canonical = false;
            Bytes alphabet = BASE32_ALPHABET;
            Bytes? ignoreChars = null;
            ParseOptions(
                kwargs,
                "a2b_base32",
                ("padded", value => padded = PythonOps.IsTrue(value)),
                ("canonical", value => canonical = PythonOps.IsTrue(value)),
                ("alphabet", value => alphabet = RequireBytesOption(value, "alphabet", 32)),
                ("ignorechars", value => ignoreChars = OptionalBytes(value, "ignorechars")));
            return DecodeBase32(context, GetBytes(data), padded, alphabet, ignoreChars, canonical);
        }

        public static Bytes a2b_base32(
            CodeContext context,
            [NotNone] string data,
            [ParamDictionary] IDictionary<object, object> kwargs)
            => a2b_base32(context, AsciiBytes(data), kwargs);

        public static Bytes b2a_base32(
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            bool padded = true;
            int wrapcol = 0;
            Bytes alphabet = BASE32_ALPHABET;
            ParseOptions(
                kwargs,
                "b2a_base32",
                ("padded", value => padded = PythonOps.IsTrue(value)),
                ("wrapcol", value => wrapcol = ConvertWrapColumn(value)),
                ("alphabet", value => alphabet = RequireBufferAlphabet(value, "alphabet", 32)));
            return EncodeBase32(GetBytes(data), padded, alphabet, wrapcol);
        }

        #endregion

        #region qp

        private const int MAXLINESIZE = 76;

        [Documentation("a2b_qp(data, header=False)\n    Decode a string of qp-encoded data.")]
        public static Bytes a2b_qp([NotNone] IBufferProtocol data, bool header = false) {
            using var buffer = data.GetBufferNoThrow();
            if (buffer is null) {
                throw PythonOps.TypeError($"argument should be bytes, buffer or ASCII string, not '{PythonOps.GetPythonTypeName(data)}'");
            }
            return a2b_qp_impl(buffer.AsReadOnlySpan(), header);

            static Bytes a2b_qp_impl(ReadOnlySpan<byte> ascii_data, bool header) {
                var datalen = ascii_data.Length;

                var incount = 0;

                MemoryStream odata = new MemoryStream();

                while (incount < datalen) {
                    if (ascii_data[incount] == '=') {
                        incount++;
                        if (incount >= datalen) break;
                        // Soft line breaks
                        if ((ascii_data[incount] == '\n') || (ascii_data[incount] == '\r')) {
                            if (ascii_data[incount] != '\n') {
                                while (incount < datalen && ascii_data[incount] != '\n') incount++;
                            }
                            if (incount < datalen) incount++;
                        } else if (ascii_data[incount] == '=') {
                            // broken case from broken python qp
                            odata.WriteByte((byte)'=');
                            incount++;
                        } else if ((incount + 1 < datalen) && TryParseHex(ascii_data[incount], out byte x) && TryParseHex(ascii_data[incount + 1], out byte x2)) {
                            // hexval
                            odata.WriteByte(unchecked((byte)((x << 4) | x2)));
                            incount += 2;
                        } else {
                            odata.WriteByte((byte)'=');
                        }
                    } else if (header && ascii_data[incount] == '_') {
                        odata.WriteByte((byte)' ');
                        incount++;
                    } else {
                        odata.WriteByte(ascii_data[incount]);
                        incount++;
                    }
                }

                return Bytes.Make(odata.ToArray());
            }
        }

        public static Bytes a2b_qp([NotNone] string data, bool header = false)
            => a2b_qp(AsciiBytes(data), header);

        [Documentation(@"b2a_qp(data, quotetabs=False, istext=True, header=False) -> s;
 Encode a string using quoted-printable encoding.

On encoding, when istext is set, newlines are not encoded, and white
space at end of lines is.  When istext is not set, \\r and \\n (CR/LF) are
both encoded.  When quotetabs is set, space and tabs are encoded.")]
        public static Bytes b2a_qp([NotNone] IBufferProtocol data, bool quotetabs = false, bool istext = true, bool header = false) {
            using var buffer = data.GetBuffer();
            return b2a_qp_impl(buffer.AsReadOnlySpan().MakeString(), quotetabs, istext, header);
        }

        private static Bytes b2a_qp_impl(string data, bool quotetabs, bool istext, bool header) {
            bool crlf = data.Contains("\r\n");

            StringBuilder odata = new StringBuilder();

            var incount = 0;
            var outcount = 0;
            var linelen = 0;

            while (incount < data.Length) {
                if ((data[incount] > 126) || (data[incount] == '=') || (header && data[incount] == '_') ||
                    ((data[incount] == '.') && (linelen == 0) && (incount + 1 == data.Length || data[incount + 1] == '\n' || data[incount + 1] == '\r' || data[incount + 1] == 0)) ||
                    (!istext && ((data[incount] == '\r') || (data[incount] == '\n'))) ||
                    ((data[incount] == '\t' || data[incount] == ' ') && (incount + 1 == data.Length)) ||
                   ((data[incount] < 33) && (data[incount] != '\r') && (data[incount] != '\n') &&
                    (quotetabs || (!quotetabs && ((data[incount] != '\t') && (data[incount] != ' ')))))) {

                    if ((linelen + 3) >= MAXLINESIZE) {
                        odata.Append('=');
                        if (crlf) odata.Append('\r');
                        odata.Append('\n');
                        linelen = 0;
                    }
                    odata.Append('=');
                    to_hex(data[incount], odata, outcount);
                    outcount += 2;
                    incount++;
                    linelen += 3;
                } else {
                    if (istext &&
                        ((data[incount] == '\n') ||
                         ((incount + 1 < data.Length) && (data[incount] == '\r') &&
                         (data[incount + 1] == '\n')))) {
                        linelen = 0;
                        /* Protect against whitespace on end of line */
                        if (outcount != 0 && ((odata[outcount - 1] == ' ') || (odata[outcount - 1] == '\t'))) {
                            char ch = odata[outcount - 1];
                            odata[outcount - 1] = '=';
                            to_hex(ch, odata, outcount);
                            outcount += 2;
                        }

                        if (crlf) { odata.Append('\r'); outcount++; }
                        odata.Append('\n'); outcount++;
                        if (data[incount] == '\r')
                            incount += 2;
                        else
                            incount++;
                    } else {
                        if ((incount + 1 != data.Length) &&
                           (data[incount + 1] != '\n') &&
                           (linelen + 1) >= MAXLINESIZE) {
                            odata.Append('='); outcount++;
                            if (crlf) { odata.Append('\r'); outcount++; }
                            odata.Append('\n'); outcount++;
                            linelen = 0;
                        }
                        linelen++;
                        if (header && data[incount] == ' ') {
                            odata.Append('_'); outcount++;
                            incount++;
                        } else {
                            odata.Append(data[incount++]); outcount++;
                        }
                    }
                }
            }

            return odata.ToString().ToBytes();

            static void to_hex(char ch, StringBuilder s, int index) {
                int uvalue = ch, uvalue2 = ch / 16;
                s.Append("0123456789ABCDEF"[uvalue2 % 16]);
                s.Append("0123456789ABCDEF"[uvalue % 16]);
            }
        }

        #endregion

        #region hqx

        // TODO: removed in 3.11
        public static object a2b_hqx(object? data) {
            throw new NotImplementedException();
        }
        // TODO: removed in 3.11
        public static object rledecode_hqx(object? data) {
            throw new NotImplementedException();
        }
        // TODO: removed in 3.11
        public static object rlecode_hqx(object? data) {
            throw new NotImplementedException();
        }
        // TODO: removed in 3.11
        public static object b2a_hqx(object? data) {
            throw new NotImplementedException();
        }

        private static readonly ushort[] crctab_hqx = new ushort[] {
            0x0000, 0x1021, 0x2042, 0x3063, 0x4084, 0x50a5, 0x60c6, 0x70e7,
            0x8108, 0x9129, 0xa14a, 0xb16b, 0xc18c, 0xd1ad, 0xe1ce, 0xf1ef,
            0x1231, 0x0210, 0x3273, 0x2252, 0x52b5, 0x4294, 0x72f7, 0x62d6,
            0x9339, 0x8318, 0xb37b, 0xa35a, 0xd3bd, 0xc39c, 0xf3ff, 0xe3de,
            0x2462, 0x3443, 0x0420, 0x1401, 0x64e6, 0x74c7, 0x44a4, 0x5485,
            0xa56a, 0xb54b, 0x8528, 0x9509, 0xe5ee, 0xf5cf, 0xc5ac, 0xd58d,
            0x3653, 0x2672, 0x1611, 0x0630, 0x76d7, 0x66f6, 0x5695, 0x46b4,
            0xb75b, 0xa77a, 0x9719, 0x8738, 0xf7df, 0xe7fe, 0xd79d, 0xc7bc,
            0x48c4, 0x58e5, 0x6886, 0x78a7, 0x0840, 0x1861, 0x2802, 0x3823,
            0xc9cc, 0xd9ed, 0xe98e, 0xf9af, 0x8948, 0x9969, 0xa90a, 0xb92b,
            0x5af5, 0x4ad4, 0x7ab7, 0x6a96, 0x1a71, 0x0a50, 0x3a33, 0x2a12,
            0xdbfd, 0xcbdc, 0xfbbf, 0xeb9e, 0x9b79, 0x8b58, 0xbb3b, 0xab1a,
            0x6ca6, 0x7c87, 0x4ce4, 0x5cc5, 0x2c22, 0x3c03, 0x0c60, 0x1c41,
            0xedae, 0xfd8f, 0xcdec, 0xddcd, 0xad2a, 0xbd0b, 0x8d68, 0x9d49,
            0x7e97, 0x6eb6, 0x5ed5, 0x4ef4, 0x3e13, 0x2e32, 0x1e51, 0x0e70,
            0xff9f, 0xefbe, 0xdfdd, 0xcffc, 0xbf1b, 0xaf3a, 0x9f59, 0x8f78,
            0x9188, 0x81a9, 0xb1ca, 0xa1eb, 0xd10c, 0xc12d, 0xf14e, 0xe16f,
            0x1080, 0x00a1, 0x30c2, 0x20e3, 0x5004, 0x4025, 0x7046, 0x6067,
            0x83b9, 0x9398, 0xa3fb, 0xb3da, 0xc33d, 0xd31c, 0xe37f, 0xf35e,
            0x02b1, 0x1290, 0x22f3, 0x32d2, 0x4235, 0x5214, 0x6277, 0x7256,
            0xb5ea, 0xa5cb, 0x95a8, 0x8589, 0xf56e, 0xe54f, 0xd52c, 0xc50d,
            0x34e2, 0x24c3, 0x14a0, 0x0481, 0x7466, 0x6447, 0x5424, 0x4405,
            0xa7db, 0xb7fa, 0x8799, 0x97b8, 0xe75f, 0xf77e, 0xc71d, 0xd73c,
            0x26d3, 0x36f2, 0x0691, 0x16b0, 0x6657, 0x7676, 0x4615, 0x5634,
            0xd94c, 0xc96d, 0xf90e, 0xe92f, 0x99c8, 0x89e9, 0xb98a, 0xa9ab,
            0x5844, 0x4865, 0x7806, 0x6827, 0x18c0, 0x08e1, 0x3882, 0x28a3,
            0xcb7d, 0xdb5c, 0xeb3f, 0xfb1e, 0x8bf9, 0x9bd8, 0xabbb, 0xbb9a,
            0x4a75, 0x5a54, 0x6a37, 0x7a16, 0x0af1, 0x1ad0, 0x2ab3, 0x3a92,
            0xfd2e, 0xed0f, 0xdd6c, 0xcd4d, 0xbdaa, 0xad8b, 0x9de8, 0x8dc9,
            0x7c26, 0x6c07, 0x5c64, 0x4c45, 0x3ca2, 0x2c83, 0x1ce0, 0x0cc1,
            0xef1f, 0xff3e, 0xcf5d, 0xdf7c, 0xaf9b, 0xbfba, 0x8fd9, 0x9ff8,
            0x6e17, 0x7e36, 0x4e55, 0x5e74, 0x2e93, 0x3eb2, 0x0ed1, 0x1ef0,
        };

        public static int crc_hqx([NotNone] IBufferProtocol data, int crc) {
            using var buffer = data.GetBuffer();
            return crc_hqx_impl(buffer.AsReadOnlySpan(), crc);

            static int crc_hqx_impl(ReadOnlySpan<byte> data, int crc) {
                crc &= 0xffff;
                foreach (var b in data) {
                    crc = ((crc << 8) & 0xff00) ^ crctab_hqx[(crc >> 8) ^ b];
                }
                return crc;
            }
        }

        #endregion

        #region crc32

        [Documentation("crc32(data[, crc]) -> string\n\nComputes a CRC (Cyclic Redundancy Check) checksum of data.")]
        public static object crc32([NotNone] IBufferProtocol data, uint crc = 0) {
            // TODO: [PythonIndex(overflow=mask)] uint crc = 0
            using var buffer = data.GetBuffer();
            var res = crc32(buffer.AsReadOnlySpan(), crc);
            if (res <= int.MaxValue) return (int)res;
            return (BigInteger)res;
        }

        private static uint crc32(ReadOnlySpan<byte> buffer, uint baseValue) {
            uint remainder = (baseValue ^ 0xffffffff);
            foreach (byte val in buffer) {
                remainder ^= val;
                for (int j = 0; j < 8; j++) {
                    if ((remainder & 0x01) != 0) {
                        remainder = (remainder >> 1) ^ 0xEDB88320;
                    } else {
                        remainder = (remainder >> 1);
                    }
                }
            }
            return (remainder ^ 0xffffffff);
        }

        #endregion

        #region hex

        public static Bytes b2a_hex(
            [NotNone] IBufferProtocol data,
            [AllowNull] object sep = null,
            int bytes_per_sep = 1) {
            using var buffer = data.GetBuffer();
            byte[] encoded = b2a_hex_impl(buffer.AsReadOnlySpan());
            if (sep is null) return Bytes.Make(encoded);
            byte[] separator;
            if (sep is string text) {
                if (text.Length != 1 || text[0] > 0xff) {
                    throw PythonOps.ValueError("sep must be length 1");
                }
                separator = new[] { (byte)text[0] };
            } else if (sep is IBufferProtocol separatorValue) {
                separator = GetBytes(separatorValue);
            } else {
                throw PythonOps.TypeError("sep must be str or bytes-like");
            }
            if (separator.Length != 1) {
                throw PythonOps.ValueError("sep must be length 1");
            }
            if (bytes_per_sep == 0) {
                throw PythonOps.ValueError("bytes_per_sep must not be zero");
            }

            int byteCount = buffer.NumBytes();
            bool fromRight = bytes_per_sep < 0;
            using var output = new MemoryStream(encoded.Length + byteCount / Math.Abs(bytes_per_sep));
            for (int index = 0; index < encoded.Length; index += 2) {
                int bytesBefore = index / 2;
                if (bytesBefore != 0) {
                    int distance = fromRight ? byteCount - bytesBefore : bytesBefore;
                    if (distance % Math.Abs(bytes_per_sep) == 0) {
                        output.WriteByte(separator[0]);
                    }
                }
                output.WriteByte(encoded[index]);
                output.WriteByte(encoded[index + 1]);
            }
            return Bytes.Make(output.ToArray());

            static byte[] b2a_hex_impl(ReadOnlySpan<byte> data) {
                byte[] res = new byte[data.Length * 2];
                for (var i = 0; i < data.Length; i++) {
                    var b = data[i];
                    res[2 * i] = ToAscii(b >> 4);
                    res[2 * i + 1] = ToAscii(b & 0xf);
                }
                return res;

                static byte ToAscii(int b) {
                    return (byte)(b < 10 ? '0' + b : 'a' + (b - 10));
                }
            }
        }

        public static Bytes hexlify(
            [NotNone] IBufferProtocol data,
            [AllowNull] object sep = null,
            int bytes_per_sep = 1)
            => b2a_hex(data, sep, bytes_per_sep);

        public static Bytes a2b_hex(
            CodeContext/*!*/ context,
            [NotNone] IBufferProtocol data,
            [ParamDictionary] IDictionary<object, object> kwargs) {
            using var buffer = data.GetBufferNoThrow();
            if (buffer is null) {
                throw PythonOps.TypeError($"argument should be bytes, buffer or ASCII string, not '{PythonOps.GetPythonTypeName(data)}'");
            }
            Bytes? ignoreChars = null;
            ParseOptions(
                kwargs,
                "a2b_hex",
                ("ignorechars", value => ignoreChars = OptionalBytes(value, "ignorechars")));
            bool[] ignore = BuildIgnoreTable(ignoreChars);
            byte[] filtered = ignoreChars is null
                ? buffer.AsReadOnlySpan().ToArray()
                : buffer.AsReadOnlySpan().ToArray()
                    .Where(value => !ignore[value] || IsHexDigit(value))
                    .ToArray();
            return a2b_hex_impl(context, filtered);

            static Bytes a2b_hex_impl(CodeContext/*!*/ context, ReadOnlySpan<byte> data) {
                if ((data.Length & 0x01) != 0) throw Error(context, "Odd-length string");

                byte[] res = new byte[data.Length / 2];
                for (int i = 0; i < res.Length; i++) {
                    var b1 = ParseHex(context, data[2 * i]);
                    var b2 = ParseHex(context, data[2 * i + 1]);
                    res[i] = (byte)(b1 * 16 + b2);
                }
                return Bytes.Make(res);

                static byte ParseHex(CodeContext/*!*/ context, byte b) {
                    if (TryParseHex(b, out byte x)) {
                        return x;
                    }
                    throw Error(context, "Non-hexadecimal digit found");
                }
            }
        }

        private static bool IsHexDigit(byte value) {
            return (value >= (byte)'0' && value <= (byte)'9')
                || (value >= (byte)'A' && value <= (byte)'F')
                || (value >= (byte)'a' && value <= (byte)'f');
        }

        public static Bytes a2b_hex(
            CodeContext/*!*/ context,
            [NotNone] string data,
            [ParamDictionary] IDictionary<object, object> kwargs)
            => a2b_hex(context, AsciiBytes(data), kwargs);

        public static Bytes unhexlify(
            CodeContext/*!*/ context,
            [NotNone] IBufferProtocol hexstr,
            [ParamDictionary] IDictionary<object, object> kwargs)
            => a2b_hex(context, hexstr, kwargs);

        public static Bytes unhexlify(
            CodeContext/*!*/ context,
            [NotNone] string hexstr,
            [ParamDictionary] IDictionary<object, object> kwargs)
            => a2b_hex(context, hexstr, kwargs);

        #endregion

        #region Private implementation

        private static void ParseOptions(
            IDictionary<object, object> kwargs,
            string functionName,
            params (string Name, Action<object> Apply)[] options) {
            foreach (KeyValuePair<object, object> item in kwargs) {
                bool handled = false;
                foreach ((string name, Action<object> apply) in options) {
                    if (string.Equals(item.Key as string, name, StringComparison.Ordinal)) {
                        apply(item.Value);
                        handled = true;
                        break;
                    }
                }
                if (!handled) {
                    throw PythonOps.TypeError(
                        "{0}() got an unexpected keyword argument '{1}'",
                        functionName,
                        item.Key);
                }
            }
        }

        private static Bytes RequireBytesOption(object value, string name, int length) {
            if (value is not Bytes bytes) {
                throw PythonOps.TypeError("{0} must be bytes-like", name);
            }
            if (bytes.Count != length) {
                throw PythonOps.ValueError(
                    "{0} must have length {1}",
                    name,
                    length);
            }
            return bytes;
        }

        private static Bytes RequireBufferAlphabet(object value, string name, int length) {
            if (value is string || value is not IBufferProtocol bufferValue) {
                throw PythonOps.TypeError("{0} must be bytes-like", name);
            }
            Bytes bytes = Bytes.Make(GetBytes(bufferValue));
            if (bytes.Count != length) {
                throw PythonOps.ValueError(
                    "{0} must have length {1}",
                    name,
                    length);
            }
            return bytes;
        }

        private static Bytes AsciiBytes(string value) {
            foreach (char character in value) {
                if (character > 0x7f) {
                    throw PythonOps.ValueError(
                        "string argument should contain only ASCII characters");
                }
            }
            return Bytes.Make(Encoding.ASCII.GetBytes(value));
        }

        private static Bytes? OptionalBytes(object value, string name) {
            if (value is string || value is not IBufferProtocol bufferValue) {
                throw PythonOps.TypeError("{0} must be bytes-like", name);
            }
            using var buffer = bufferValue.GetBufferNoThrow();
            if (buffer is null) {
                throw PythonOps.TypeError("{0} must be bytes-like", name);
            }
            return Bytes.Make(buffer.AsReadOnlySpan().ToArray());
        }

        private static int[] BuildDecodeTable(Bytes alphabet, int pad = -1) {
            var table = new int[256];
            Array.Fill(table, -1);
            for (int index = 0; index < alphabet.Count; index++) {
                table[alphabet[index]] = index;
            }
            if (pad >= 0) {
                table[(byte)'='] = pad;
            }
            return table;
        }

        private static bool[] BuildIgnoreTable(Bytes? ignoreChars) {
            var result = new bool[256];
            if (ignoreChars is null) return result;
            foreach (byte value in ignoreChars) {
                result[value] = true;
            }
            return result;
        }

        private static Bytes DecodeBase85(
            CodeContext context,
            byte[] data,
            Bytes alphabet,
            Bytes? ignoreChars,
            bool canonical) {
            int[] table = BuildDecodeTable(alphabet);
            bool[] ignore = BuildIgnoreTable(ignoreChars);
            var output = new List<byte>(data.Length * 4 / 5);
            uint left = 0;
            int groupPosition = 0;
            int inputGroupPosition = 0;
            int index = 0;
            int remaining = data.Length;
            while (remaining > 0 || groupPosition != 0) {
                byte current = remaining > 0 ? data[index] : (byte)0;
                int digit = remaining > 0 ? table[current] : 84;
                if (digit >= 0 && digit < 85) {
                    if (groupPosition == 4
                        && (left > uint.MaxValue / 85
                            || left * 85 > uint.MaxValue - (uint)digit)) {
                        throw Error(context, "Base85 overflow in hunk starting at byte {0}", index / 5 * 5);
                    }
                    left = left * 85 + (uint)digit;
                    groupPosition++;
                    if (remaining > 0) inputGroupPosition++;
                } else if (remaining > 0 && !ignore[current]) {
                    throw Error(context, "bad Base85 character at position {0}", index);
                }
                if (groupPosition != 5) {
                    if (remaining > 0) remaining--;
                    index++;
                    continue;
                }

                int chunkLength = inputGroupPosition == 5 ? 4 : inputGroupPosition - 1;
                if (chunkLength <= 0) {
                    throw Error(context, "Incomplete Base85 group");
                }
                for (int byteIndex = 0; byteIndex < chunkLength; byteIndex++) {
                    output.Add((byte)(left >> (24 - byteIndex * 8)));
                }
                if (canonical && chunkLength < 4) {
                    int padBytes = 4 - chunkLength;
                    uint canonicalTop = (left >> (padBytes * 8)) << (padBytes * 8);
                    uint divisor = Pow85[padBytes];
                    if (canonicalTop / divisor != left / divisor) {
                        throw Error(context, "Non-zero padding bits");
                    }
                }
                groupPosition = 0;
                inputGroupPosition = 0;
                left = 0;
                if (remaining > 0) remaining--;
                index++;
            }
            return Bytes.Make(output.ToArray());
        }

        private static Bytes DecodeAscii85(
            CodeContext context,
            byte[] data,
            bool foldspaces,
            bool adobe,
            Bytes? ignoreChars,
            bool canonical) {
            int start = 0;
            int end = data.Length;
            if (adobe) {
                if (end < 2 || data[end - 2] != (byte)'~' || data[end - 1] != (byte)'>') {
                    throw Error(context, "Ascii85 encoded byte sequences must end with b'~>'");
                }
                end -= 2;
                if (end - start >= 2 && data[start] == (byte)'<' && data[start + 1] == (byte)'~') {
                    start += 2;
                }
            }

            int[] table = new int[256];
            Array.Fill(table, -1);
            for (int alphabetIndex = 0; alphabetIndex < ASCII85_ALPHABET.Count; alphabetIndex++) {
                table[ASCII85_ALPHABET[alphabetIndex]] = alphabetIndex;
            }
            bool[] ignore = BuildIgnoreTable(ignoreChars);
            var output = new List<byte>((end - start) * 4 / 5 + 4);
            uint left = 0;
            int groupPosition = 0;
            int inputGroupPosition = 0;
            bool fromShortForm = false;
            int index = start;
            int remaining = end - start;
            while (remaining > 0 || groupPosition != 0) {
                byte current = remaining > 0 ? data[index] : (byte)0;
                int digit = remaining > 0 ? table[current] : 84;
                if (digit >= 0 && digit < 85) {
                    if (groupPosition == 4
                        && (left > uint.MaxValue / 85
                            || left * 85 > uint.MaxValue - (uint)digit)) {
                        throw Error(context, "Ascii85 overflow");
                    }
                    left = left * 85 + (uint)digit;
                    groupPosition++;
                    if (remaining > 0) inputGroupPosition++;
                } else if (remaining > 0 && ((current == (byte)'y' && foldspaces) || current == (byte)'z')) {
                    if (groupPosition != 0) {
                        throw Error(context, "'{0}' inside Ascii85 5-tuple", (char)current);
                    }
                    left = current == (byte)'y' ? 0x20202020u : 0u;
                    fromShortForm = current == (byte)'z';
                    groupPosition = 5;
                    inputGroupPosition = 5;
                } else if (remaining > 0 && !ignore[current]) {
                    throw Error(context, "Non-Ascii85 digit found: {0}", (char)current);
                }
                if (groupPosition != 5) {
                    if (remaining > 0) remaining--;
                    index++;
                    continue;
                }

                int chunkLength = inputGroupPosition == 5 ? 4 : inputGroupPosition - 1;
                if (chunkLength <= 0) {
                    throw Error(context, "Incomplete Ascii85 group");
                }
                for (int byteIndex = 0; byteIndex < chunkLength; byteIndex++) {
                    output.Add((byte)(left >> (24 - byteIndex * 8)));
                }
                if (canonical) {
                    if (chunkLength == 4 && left == 0 && !fromShortForm) {
                        throw Error(context, "Non-canonical encoding, use 'z' for all-zero groups");
                    }
                    if (chunkLength < 4) {
                        int padBytes = 4 - chunkLength;
                        uint canonicalTop = (left >> (padBytes * 8)) << (padBytes * 8);
                        uint divisor = Pow85[padBytes];
                        if (canonicalTop / divisor != left / divisor) {
                            throw Error(context, "Non-zero padding bits");
                        }
                    }
                }
                groupPosition = 0;
                inputGroupPosition = 0;
                left = 0;
                fromShortForm = false;
                if (remaining > 0) remaining--;
                index++;
            }
            return Bytes.Make(output.ToArray());
        }

        private static Bytes EncodeBase85(
            byte[] data,
            Bytes alphabet,
            bool pad,
            int wrapcol) {
            byte[] encoded = Encode85Core(data, alphabet, false, false, pad);
            return Bytes.Make(WrapLines(encoded, wrapcol, 5));
        }

        private static Bytes EncodeAscii85(
            byte[] data,
            bool foldspaces,
            bool adobe,
            bool pad,
            int wrapcol) {
            byte[] encoded = Encode85Core(data, ASCII85_ALPHABET, true, foldspaces, pad);
            if (adobe) {
                encoded = Encoding.ASCII.GetBytes("<~")
                    .Concat(encoded)
                    .Concat(Encoding.ASCII.GetBytes("~>"))
                    .ToArray();
                if (wrapcol > 0) {
                    if (wrapcol == 1) wrapcol = 2;
                    encoded = WrapAdobe(encoded, wrapcol);
                    if (encoded.Length >= 3
                        && encoded[encoded.Length - 2] == (byte)'\n') {
                        encoded[encoded.Length - 3] = (byte)'\n';
                        encoded[encoded.Length - 2] = (byte)'~';
                    }
                }
                return Bytes.Make(encoded);
            }
            return Bytes.Make(WrapLines(encoded, wrapcol, 1));
        }

        private static byte[] Encode85Core(
            byte[] data,
            Bytes alphabet,
            bool ascii85,
            bool foldspaces,
            bool pad) {
            using var output = new MemoryStream();
            int offset = 0;
            while (data.Length - offset >= 4) {
                uint value = ((uint)data[offset] << 24)
                    | ((uint)data[offset + 1] << 16)
                    | ((uint)data[offset + 2] << 8)
                    | data[offset + 3];
                offset += 4;
                if (ascii85 && value == 0) {
                    output.WriteByte((byte)'z');
                    continue;
                }
                if (ascii85 && foldspaces && value == 0x20202020u) {
                    output.WriteByte((byte)'y');
                    continue;
                }
                byte[] group = new byte[5];
                for (int index = 4; index >= 0; index--) {
                    group[index] = (byte)alphabet[(int)(value % 85)];
                    value /= 85;
                }
                output.Write(group, 0, group.Length);
            }

            int remaining = data.Length - offset;
            if (remaining > 0) {
                uint value = 0;
                for (int index = 0; index < 4; index++) {
                    value <<= 8;
                    if (index < remaining) value |= data[offset + index];
                }
                if (ascii85 && pad && value == 0) {
                    output.WriteByte((byte)'z');
                } else {
                    int groupLength = pad ? 5 : remaining + 1;
                    byte[] group = new byte[5];
                    for (int index = 4; index >= 0; index--) {
                        group[index] = (byte)alphabet[(int)(value % 85)];
                        value /= 85;
                    }
                    output.Write(group, 0, groupLength);
                }
            }
            return output.ToArray();
        }

        private static Bytes DecodeBase32(
            CodeContext context,
            byte[] data,
            bool padded,
            Bytes alphabet,
            Bytes? ignoreChars,
            bool canonical) {
            int[] table = BuildDecodeTable(alphabet, 32);
            bool[] ignore = BuildIgnoreTable(ignoreChars);
            var output = new List<byte>(data.Length * 5 / 8);
            int left = 0;
            int octaPosition = 0;
            int pads = 0;
            for (int index = 0; index < data.Length; index++) {
                byte current = data[index];
                if (padded && current == (byte)'=') {
                    pads++;
                    if ((octaPosition == 2 || octaPosition == 4 || octaPosition == 5 || octaPosition == 7)
                        && octaPosition + pads <= 8) {
                        continue;
                    }
                    if (ignore[(byte)'=']) continue;
                    if (octaPosition == 1 || octaPosition == 3 || octaPosition == 6) break;
                    throw Error(
                        context,
                        octaPosition == 0 && output.Count == 0
                            ? "Leading padding not allowed"
                            : "Excess padding not allowed");
                }

                int value = table[current];
                if (value < 0 || value >= 32) {
                    if (!ignore[current]) {
                        throw Error(
                            context,
                            current == (byte)'='
                                ? "Padding not allowed"
                                : "Only base32 data is allowed");
                    }
                    continue;
                }
                if (pads != 0 && !ignore[(byte)'=']) {
                    throw Error(
                        context,
                        octaPosition + pads == 8
                            ? "Excess data after padding"
                            : "Discontinuous padding not allowed");
                }
                pads = 0;

                switch (octaPosition) {
                    case 0:
                        octaPosition = 1;
                        left = value;
                        break;
                    case 1:
                        octaPosition = 2;
                        output.Add((byte)((left << 3) | (value >> 2)));
                        left = value & 0x03;
                        break;
                    case 2:
                        octaPosition = 3;
                        left = (left << 5) | value;
                        break;
                    case 3:
                        octaPosition = 4;
                        output.Add((byte)((left << 1) | (value >> 4)));
                        left = value & 0x0f;
                        break;
                    case 4:
                        octaPosition = 5;
                        output.Add((byte)((left << 4) | (value >> 1)));
                        left = value & 0x01;
                        break;
                    case 5:
                        octaPosition = 6;
                        left = (left << 5) | value;
                        break;
                    case 6:
                        octaPosition = 7;
                        output.Add((byte)((left << 2) | (value >> 3)));
                        left = value & 0x07;
                        break;
                    case 7:
                        octaPosition = 0;
                        output.Add((byte)((left << 5) | value));
                        left = 0;
                        break;
                }
            }

            if (octaPosition == 1 || octaPosition == 3 || octaPosition == 6) {
                throw Error(
                    context,
                    "Invalid base32-encoded string: number of data characters ({0}) cannot be 1, 3, or 6 more than a multiple of 8",
                    output.Count / 5 * 8 + octaPosition);
            }
            if (padded && octaPosition != 0 && octaPosition + pads < 8) {
                throw Error(context, "Incorrect padding");
            }
            if (canonical && left != 0) {
                throw Error(context, "Non-zero padding bits");
            }
            return Bytes.Make(output.ToArray());
        }

        private static Bytes EncodeBase32(
            byte[] data,
            bool padded,
            Bytes alphabet,
            int wrapcol) {
            using var output = new MemoryStream();
            int offset = 0;
            while (data.Length - offset >= 5) {
                ulong value = 0;
                for (int index = 0; index < 5; index++) {
                    value = (value << 8) | data[offset + index];
                }
                offset += 5;
                for (int shift = 35; shift >= 0; shift -= 5) {
                    output.WriteByte((byte)alphabet[(int)((value >> shift) & 0x1f)]);
                }
            }

            int remaining = data.Length - offset;
            if (remaining > 0) {
                ulong value = 0;
                for (int index = 0; index < 5; index++) {
                    value <<= 8;
                    if (index < remaining) value |= data[offset + index];
                }
                int chars = remaining switch {
                    1 => 2,
                    2 => 4,
                    3 => 5,
                    4 => 7,
                    _ => 0
                };
                int padding = 8 - chars;
                if (!padded) padding = 0;
                for (int index = 0; index < chars; index++) {
                    int shift = 35 - index * 5;
                    output.WriteByte((byte)alphabet[(int)((value >> shift) & 0x1f)]);
                }
                for (int index = 0; index < padding; index++) {
                    output.WriteByte((byte)'=');
                }
            }
            return Bytes.Make(WrapLines(output.ToArray(), wrapcol, 8));
        }

        private static byte[] WrapLines(byte[] data, int wrapcol, int minimumGroup) {
            if (wrapcol <= 0 || data.Length == 0 || data.Length <= wrapcol) return data;
            int width = Math.Max(minimumGroup, wrapcol / minimumGroup * minimumGroup);
            using var output = new MemoryStream(data.Length + data.Length / width);
            for (int offset = 0; offset < data.Length; offset += width) {
                int count = Math.Min(width, data.Length - offset);
                output.Write(data, offset, count);
                if (offset + count < data.Length) {
                    output.WriteByte((byte)'\n');
                }
            }
            return output.ToArray();
        }

        private static byte[] WrapAdobe(byte[] data, int wrapcol) {
            int width = Math.Max(1, wrapcol);
            using var output = new MemoryStream(data.Length + data.Length / width);
            int offset = 0;
            while (offset < data.Length) {
                int count = Math.Min(width, data.Length - offset);
                output.Write(data, offset, count);
                offset += count;
                if (offset < data.Length) output.WriteByte((byte)'\n');
            }
            return output.ToArray();
        }

        private static readonly uint[] Pow85 = {
            1u,
            85u,
            85u * 85u,
            85u * 85u * 85u
        };

        private delegate int DecodeByte(byte val);

        private static void EncodeData(this MemoryStream res, ReadOnlySpan<byte> data, byte empty, Func<int, byte> encFunc) {
            int bits;
            for (int i = 0; i < data.Length; i += 3) {
                switch (data.Length - i) {
                    case 1:
                        // only one char, emit 2 bytes &
                        // padding
                        bits = (data[i] & 0xff) << 16;
                        res.WriteByte(encFunc((bits >> 18) & 0x3f));
                        res.WriteByte(encFunc((bits >> 12) & 0x3f));
                        res.WriteByte(empty);
                        res.WriteByte(empty);
                        break;
                    case 2:
                        // only two chars, emit 3 bytes &
                        // padding
                        bits = ((data[i] & 0xff) << 16) | ((data[i + 1] & 0xff) << 8);
                        res.WriteByte(encFunc((bits >> 18) & 0x3f));
                        res.WriteByte(encFunc((bits >> 12) & 0x3f));
                        res.WriteByte(encFunc((bits >> 6) & 0x3f));
                        res.WriteByte(empty);
                        break;
                    default:
                        // got all 3 bytes, just emit it.
                        bits = ((data[i] & 0xff) << 16) |
                            ((data[i + 1] & 0xff) << 8) |
                            ((data[i + 2] & 0xff));
                        res.WriteByte(encFunc((bits >> 18) & 0x3f));
                        res.WriteByte(encFunc((bits >> 12) & 0x3f));
                        res.WriteByte(encFunc((bits >> 6) & 0x3f));
                        res.WriteByte(encFunc(bits & 0x3f));
                        break;
                }
            }
        }

        private const int IgnoreByte = -1; // skip this byte
        private const int EmptyByte = -2; // byte evaluates to 0 and may appear off the end of the stream
        private const int PadByte = -3; // pad bytes signal the end of the stream, unless there are too few to properly align
        private const int InvalidByte = -4; // raise exception for illegal byte
        private const int NoMoreBytes = -5; // signals end of stream

        private static int NextVal(CodeContext/*!*/ context, ReadOnlySpan<byte> data, ref int index, DecodeByte decFunc) {
            int res;
            while (index < data.Length) {
                res = decFunc(data[index++]);
                switch (res) {
                    case EmptyByte:
                        return 0;
                    case InvalidByte:
                        throw Error(context, "Illegal char");
                    case IgnoreByte:
                        break;
                    default:
                        return res;
                }
            }

            return NoMoreBytes;
        }

        private static int CountPadBytes(CodeContext/*!*/ context, ReadOnlySpan<byte> data, int bound, ref int index, DecodeByte decFunc) {
            int res = PadByte;
            int count = 0;
            while ((bound < 0 || count < bound) &&
                   (res = NextVal(context, data, ref index, decFunc)) == PadByte) {
                count++;
            }

            // we only want NextVal() to eat PadBytes - not real data
            if (res != PadByte && res != NoMoreBytes) index--;

            return count;
        }

        private static int GetVal(CodeContext/*!*/ context, ReadOnlySpan<byte> data, int align, bool bounded, ref int index, DecodeByte decFunc) {
            int res;
            while (true) {
                res = NextVal(context, data, ref index, decFunc);
                switch (res) {
                    case PadByte:
                        switch (align) {
                            case 0:
                            case 1:
                                CountPadBytes(context, data, -1, ref index, decFunc);
                                continue;
                            case 2:
                                if (CountPadBytes(context, data, 1, ref index, decFunc) > 0) {
                                    return NoMoreBytes;
                                } else {
                                    continue;
                                }
                            default:
                                return NoMoreBytes;
                        }
                    case NoMoreBytes:
                        if (bounded || align == 0) {
                            return NoMoreBytes;
                        } else {
                            throw Error(context, "Incorrect padding");
                        }
                    case EmptyByte:
                        return 0;
                    default:
                        return res;
                }
            }
        }

        private static MemoryStream DecodeWorker(CodeContext/*!*/ context, ReadOnlySpan<byte> data, bool bounded, DecodeByte decFunc) {
            var res = new MemoryStream();

            int i = 0;
            while (i < data.Length) {
                int intVal;

                int val0 = GetVal(context, data, 0, bounded, ref i, decFunc);
                if (val0 < 0) break;  // no more bytes...

                int val1 = GetVal(context, data, 1, bounded, ref i, decFunc);
                if (val1 < 0) break;  // no more bytes...

                int val2 = GetVal(context, data, 2, bounded, ref i, decFunc);
                if (val2 < 0) {
                    // 2 byte partial
                    intVal = (val0 << 18) | (val1 << 12);

                    res.WriteByte((byte)((intVal >> 16) & 0xff));
                    break;
                }

                int val3 = GetVal(context, data, 3, bounded, ref i, decFunc);
                if (val3 < 0) {
                    // 3 byte partial
                    intVal = (val0 << 18) | (val1 << 12) | (val2 << 6);

                    res.WriteByte((byte)((intVal >> 16) & 0xff));
                    res.WriteByte((byte)((intVal >> 8) & 0xff));
                    break;
                }

                // full 4-bytes
                intVal = (val0 << 18) | (val1 << 12) | (val2 << 6) | (val3);
                res.WriteByte((byte)((intVal >> 16) & 0xff));
                res.WriteByte((byte)((intVal >> 8) & 0xff));
                res.WriteByte((byte)(intVal & 0xff));
            }

            return res;
        }

        private static ReadOnlySpan<byte> RemovePrefix(CodeContext/*!*/ context, ReadOnlySpan<byte> data, DecodeByte decFunc) {
            int count = 0;
            while (count < data.Length) {
                int current = decFunc(data[count]);
                if (current == InvalidByte) {
                    throw Error(context, "Illegal char");
                }
                if (current >= 0) break;
                count++;
            }
            return count == 0 ? data : data.Slice(count);
        }

        private static void ProcessSuffix(CodeContext/*!*/ context, ReadOnlySpan<byte> data, DecodeByte decFunc) {
            for (int i = 0; i < data.Length; i++) {
                int current = decFunc(data[i]);
                if (current >= 0 || current == InvalidByte) {
                    throw Error(context, "Trailing garbage");
                }
            }
        }

        private static Bytes ToBytes(this string s) {
            if (StringOps.TryEncodeAscii(s, out Bytes? ascii))
                return ascii;
            throw PythonOps.ValueError("string argument should contain only ASCII characters");
        }

        private static bool TryParseHex(byte b, out byte x) {
            if (b.IsDigit()) {
                x = (byte)(b - '0');
            } else if (b >= 'A' && b <= 'F') {
                x = (byte)(b - 'A' + 10);
            } else if (b >= 'a' && b <= 'f') {
                x = (byte)(b - 'a' + 10);
            } else {
                x = default;
                return false;
            }
            return true;
        }
        #endregion
    }
}
