// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using IronPython.Runtime;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

[assembly: PythonModule("_multibytecodec", typeof(IronPython.Modules.MultibyteCodecs))]
namespace IronPython.Modules {
    public static class MultibyteCodecs {
        public const string __doc__ = "Managed multibyte codec support.";

        public static readonly PythonType MultibyteIncrementalEncoder =
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.MultibyteIncrementalEncoder));
        public static readonly PythonType MultibyteIncrementalDecoder =
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.MultibyteIncrementalDecoder));
        public static readonly PythonType MultibyteStreamReader =
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.MultibyteStreamReader));
        public static readonly PythonType MultibyteStreamWriter =
            DynamicHelpers.GetPythonTypeFromType(typeof(IronPython.Modules.MultibyteStreamWriter));
    }

    [PythonType]
    public class MultibyteIncrementalEncoder {
        public PythonTuple encode(
            CodeContext context,
            [NotNone] string input,
            string? errors = null) {
            return GetCodec(context, this).encode(context, input, errors);
        }

        public void reset() { }

        internal static MultibyteCodec GetCodec(CodeContext context, object self) {
            return PythonOps.GetBoundAttr(context, self, "codec") as MultibyteCodec
                ?? throw PythonOps.TypeError("codec is unexpected type");
        }
    }

    [PythonType]
    public class MultibyteIncrementalDecoder {
        public PythonTuple decode(
            CodeContext context,
            [NotNone] IBufferProtocol input,
            string? errors = null) {
            return MultibyteIncrementalEncoder.GetCodec(context, this).decode(context, input, errors);
        }

        public void reset() { }
    }

    [PythonType]
    public class MultibyteStreamReader {
        private readonly object _stream;
        private readonly string _errors;

        public MultibyteStreamReader(
            CodeContext context,
            [NotNone] object stream,
            [NotNone] string errors = "strict") {
            _stream = stream;
            _errors = errors;
        }

        public object read(CodeContext context, int size = -1) {
            object? data = PythonOps.Invoke(context, _stream, "read", size);
            if (data is not IBufferProtocol input) {
                throw PythonOps.TypeError("expected bytes-like object, got {0}", PythonOps.GetPythonTypeName(data));
            }
            return PythonOps.GetIndex(
                context,
                MultibyteIncrementalEncoder.GetCodec(context, this).decode(
                    context,
                    input,
                    _errors),
                0);
        }

        public object readline(CodeContext context, int size = -1) {
            object? data = PythonOps.Invoke(context, _stream, "readline", size);
            if (data is not IBufferProtocol input) {
                throw PythonOps.TypeError("expected bytes-like object, got {0}", PythonOps.GetPythonTypeName(data));
            }
            return PythonOps.GetIndex(
                context,
                MultibyteIncrementalEncoder.GetCodec(context, this).decode(
                    context,
                    input,
                    _errors),
                0);
        }

        public PythonList readlines(CodeContext context, int hint = -1) {
            var lines = new PythonList();
            while (true) {
                object line = readline(context);
                if (line is string text && text.Length == 0) break;
                lines.Add(line);
            }
            return lines;
        }

        public void reset() { }

        public string errors => _errors;

        public object stream => _stream;
    }

    [PythonType]
    public class MultibyteStreamWriter {
        private readonly object _stream;
        private readonly string _errors;

        public MultibyteStreamWriter(
            CodeContext context,
            [NotNone] object stream,
            [NotNone] string errors = "strict") {
            _stream = stream;
            _errors = errors;
        }

        public void write(CodeContext context, [NotNone] string input) {
            PythonTuple encoded = MultibyteIncrementalEncoder.GetCodec(context, this)
                .encode(context, input, _errors);
            PythonOps.Invoke(context, _stream, "write", encoded[0]);
        }

        public void writelines(CodeContext context, [NotNone] object lines) {
            foreach (object? line in PythonOps.GetCollection(lines)) {
                write(context, (string)line!);
            }
        }

        public void reset() { }

        public string errors => _errors;

        public object stream => _stream;
    }
}
