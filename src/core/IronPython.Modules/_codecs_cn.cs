// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Text;

using IronPython.Runtime;
using IronPython.Runtime.Operations;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

[assembly: PythonModule("_codecs_cn", typeof(IronPython.Modules.ChineseCodecs))]
namespace IronPython.Modules {
    public static class ChineseCodecs {
        public static MultibyteCodec getcodec([NotNone] string name) {
            Encoding encoding = name.ToLowerInvariant() switch {
                "gbk" or "cp936" => Encoding.GetEncoding(936),
                "gb2312" => Encoding.GetEncoding("gb2312"),
                "gb18030" => Encoding.GetEncoding("gb18030"),
                _ => throw PythonOps.LookupError("no such codec is supported: {0}", name)
            };
            return new MultibyteCodec(encoding, name);
        }
    }

    [PythonType("MultibyteCodec")]
    public sealed class MultibyteCodec {
        private readonly Encoding _encoding;
        private readonly string _name;

        internal MultibyteCodec(Encoding encoding, string name) {
            _encoding = encoding;
            _name = name;
        }

        public PythonTuple encode(
            CodeContext context,
            [NotNone] string input,
            string? errors = null) {
            Bytes encoded = StringOps.DoEncode(
                context,
                input,
                errors,
                _name,
                _encoding,
                includePreamble: false);
            return PythonTuple.MakeTuple(encoded, input.Length);
        }

        public PythonTuple decode(
            CodeContext context,
            [NotNone] IBufferProtocol input,
            string? errors = null) {
            using IPythonBuffer buffer = input.GetBuffer();
            int length = buffer.AsReadOnlySpan().Length;
            string decoded = StringOps.DoDecode(context, buffer, errors, _name, _encoding);
            return PythonTuple.MakeTuple(decoded, length);
        }
    }
}
