// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

using ZstdSharp;
using ZstdSharp.Unsafe;

[assembly: PythonModule("_zstd", typeof(IronPython.Modules.PythonZstd))]
namespace IronPython.Modules {
    public static class PythonZstd {
        private static PythonType? _zstdError;
        private static PythonType? _compressionParameterType;
        private static PythonType? _decompressionParameterType;

        public static PythonType ZstdError {
            get {
                if (_zstdError is null) {
                    Interlocked.CompareExchange(
                        ref _zstdError,
                        PythonExceptions.CreateSubType(
                            DefaultContext.Default.LanguageContext,
                            PythonExceptions.Exception,
                            "ZstdError",
                            "compression.zstd",
                            null,
                            (message, inner) => new RuntimeException(message, inner)),
                        null);
                }
                return _zstdError;
            }
        }

        public const int ZSTD_CLEVEL_DEFAULT = Compressor.DefaultCompressionLevel;
        public const int zstd_version_number = 10600;
        public const string zstd_version = "1.6.0";

        public const int ZSTD_c_compressionLevel = (int)ZSTD_cParameter.ZSTD_c_compressionLevel;
        public const int ZSTD_c_windowLog = (int)ZSTD_cParameter.ZSTD_c_windowLog;
        public const int ZSTD_c_hashLog = (int)ZSTD_cParameter.ZSTD_c_hashLog;
        public const int ZSTD_c_chainLog = (int)ZSTD_cParameter.ZSTD_c_chainLog;
        public const int ZSTD_c_searchLog = (int)ZSTD_cParameter.ZSTD_c_searchLog;
        public const int ZSTD_c_minMatch = (int)ZSTD_cParameter.ZSTD_c_minMatch;
        public const int ZSTD_c_targetLength = (int)ZSTD_cParameter.ZSTD_c_targetLength;
        public const int ZSTD_c_strategy = (int)ZSTD_cParameter.ZSTD_c_strategy;
        public const int ZSTD_c_enableLongDistanceMatching = (int)ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching;
        public const int ZSTD_c_ldmHashLog = (int)ZSTD_cParameter.ZSTD_c_ldmHashLog;
        public const int ZSTD_c_ldmMinMatch = (int)ZSTD_cParameter.ZSTD_c_ldmMinMatch;
        public const int ZSTD_c_ldmBucketSizeLog = (int)ZSTD_cParameter.ZSTD_c_ldmBucketSizeLog;
        public const int ZSTD_c_ldmHashRateLog = (int)ZSTD_cParameter.ZSTD_c_ldmHashRateLog;
        public const int ZSTD_c_contentSizeFlag = (int)ZSTD_cParameter.ZSTD_c_contentSizeFlag;
        public const int ZSTD_c_checksumFlag = (int)ZSTD_cParameter.ZSTD_c_checksumFlag;
        public const int ZSTD_c_dictIDFlag = (int)ZSTD_cParameter.ZSTD_c_dictIDFlag;
        public const int ZSTD_c_nbWorkers = (int)ZSTD_cParameter.ZSTD_c_nbWorkers;
        public const int ZSTD_c_jobSize = (int)ZSTD_cParameter.ZSTD_c_jobSize;
        public const int ZSTD_c_overlapLog = (int)ZSTD_cParameter.ZSTD_c_overlapLog;
        public const int ZSTD_d_windowLogMax = (int)ZSTD_dParameter.ZSTD_d_windowLogMax;
        public const int ZSTD_DStreamOutSize = 128 * 1024;

        public const int ZSTD_fast = (int)ZSTD_strategy.ZSTD_fast;
        public const int ZSTD_dfast = (int)ZSTD_strategy.ZSTD_dfast;
        public const int ZSTD_greedy = (int)ZSTD_strategy.ZSTD_greedy;
        public const int ZSTD_lazy = (int)ZSTD_strategy.ZSTD_lazy;
        public const int ZSTD_lazy2 = (int)ZSTD_strategy.ZSTD_lazy2;
        public const int ZSTD_btlazy2 = (int)ZSTD_strategy.ZSTD_btlazy2;
        public const int ZSTD_btopt = (int)ZSTD_strategy.ZSTD_btopt;
        public const int ZSTD_btultra = (int)ZSTD_strategy.ZSTD_btultra;
        public const int ZSTD_btultra2 = (int)ZSTD_strategy.ZSTD_btultra2;

        public static PythonTuple get_param_bounds(int parameter, bool is_compress) {
            if (is_compress) {
                return parameter switch {
                    ZSTD_c_compressionLevel => PythonTuple.MakeTuple(
                        Compressor.MinCompressionLevel,
                        Compressor.MaxCompressionLevel),
                    ZSTD_c_windowLog => PythonTuple.MakeTuple(10, 31),
                    ZSTD_c_hashLog => PythonTuple.MakeTuple(6, 30),
                    ZSTD_c_chainLog => PythonTuple.MakeTuple(6, 30),
                    ZSTD_c_searchLog => PythonTuple.MakeTuple(1, 30),
                    ZSTD_c_minMatch => PythonTuple.MakeTuple(3, 7),
                    ZSTD_c_targetLength => PythonTuple.MakeTuple(0, 131075),
                    ZSTD_c_strategy => PythonTuple.MakeTuple(ZSTD_fast, ZSTD_btultra2),
                    ZSTD_c_enableLongDistanceMatching
                        or ZSTD_c_contentSizeFlag
                        or ZSTD_c_checksumFlag
                        or ZSTD_c_dictIDFlag => PythonTuple.MakeTuple(0, 1),
                    ZSTD_c_nbWorkers
                        or ZSTD_c_jobSize
                        or ZSTD_c_overlapLog => PythonTuple.MakeTuple(0, 0),
                    _ => PythonTuple.MakeTuple(int.MinValue, int.MaxValue)
                };
            }
            return parameter == ZSTD_d_windowLogMax
                ? PythonTuple.MakeTuple(10, 31)
                : PythonTuple.MakeTuple(0, int.MaxValue);
        }

        public static void set_parameter_types(
            [NotNone] PythonType compressionType,
            [NotNone] PythonType decompressionType) {
            _compressionParameterType = compressionType;
            _decompressionParameterType = decompressionType;
        }

        public static Bytes train_dict(
            CodeContext context,
            [NotNone] IBufferProtocol chunks,
            [NotNone] PythonTuple chunkSizes,
            int dictSize) {
            if (dictSize <= 0) {
                throw PythonOps.ValueError(
                    "dict_size must be a positive integer");
            }
            byte[] all = GetBytesExact(chunks, "chunks");
            if (chunkSizes.Count == 0) {
                if (all.Length == 0) {
                    throw MakeError("Zstd dictionary training failed");
                }
                throw PythonOps.ValueError(
                    "The samples size tuple doesn't match the concatenation's size");
            }
            var samples = new List<byte[]>(chunkSizes.Count);
            int offset = 0;
            foreach (object size in chunkSizes) {
                int length;
                try {
                    length = ConvertInt(size, "sample size");
                } catch (OverflowException) {
                    throw PythonOps.ValueError("invalid sample size");
                }
                if (length < 0 || offset + length > all.Length) {
                    throw PythonOps.ValueError("invalid sample size");
                }
                var sample = new byte[length];
                Array.Copy(all, offset, sample, 0, length);
                samples.Add(sample);
                offset += length;
            }
            if (offset != all.Length) {
                throw PythonOps.ValueError(
                    "The samples size tuple doesn't match the concatenation's size");
            }
            try {
                return Bytes.Make(DictBuilder.TrainFromBuffer(samples, dictSize));
            } catch (Exception exception) {
                throw MakeError(exception.Message);
            }
        }

        public static Bytes finalize_dict(
            [NotNone] IBufferProtocol content,
            [NotNone] IBufferProtocol chunks,
            [NotNone] PythonTuple chunkSizes,
            int dictSize,
            int level) {
            if (dictSize <= 0) {
                throw PythonOps.ValueError(
                    "dict_size must be a positive integer");
            }
            byte[] chunksBytes = GetBytesExact(chunks, "chunks");
            byte[] contentBytes = GetBytesExact(content, "content");
            var sizes = new nuint[chunkSizes.Count];
            long total = 0;
            for (int i = 0; i < chunkSizes.Count; i++) {
                int length;
                try {
                    length = ConvertInt(
                        chunkSizes[i],
                        "sample size");
                } catch (OverflowException) {
                    throw PythonOps.ValueError("invalid sample size");
                }
                if (length < 0) {
                    throw PythonOps.ValueError("invalid sample size");
                }
                sizes[i] = (nuint)length;
                total += length;
            }
            if (total != chunksBytes.Length) {
                throw PythonOps.ValueError(
                    "The samples size tuple doesn't match the concatenation's size");
            }
            byte[] result = new byte[dictSize];
            unsafe {
                fixed (byte* outputPointer = result)
                fixed (byte* contentPointer = contentBytes)
                fixed (byte* samplesPointer = chunksBytes)
                fixed (nuint* sizesPointer = sizes) {
                    var parameters = new ZDICT_params_t {
                        compressionLevel = level
                    };
                    nuint written = Methods.ZDICT_finalizeDictionary(
                        outputPointer,
                        (nuint)result.Length,
                        contentPointer,
                        (nuint)contentBytes.Length,
                        samplesPointer,
                        sizesPointer,
                        (uint)sizes.Length,
                        parameters);
                    if (Methods.ZDICT_isError(written)) {
                        throw MakeError(
                            "Zstd dictionary finalization failed");
                    }
                    return Bytes.Make(
                        result.AsSpan(0, checked((int)written)).ToArray());
                }
            }
        }

        public static PythonTuple get_frame_info([NotNone] IBufferProtocol frame) {
            byte[] data = GetBytes(frame);
            if (data.Length < 6) {
                throw MakeError("not less than the frame header");
            }
            if (data[0] != 0x28
                || data[1] != 0xB5
                || data[2] != 0x2F
                || data[3] != 0xFD) {
                throw MakeError("not less than the frame header");
            }
            ulong? size = GetFrameContentSize(data);
            object decompressedSize = size is null
                ? null!
                : size > int.MaxValue
                    ? new BigInteger(size.Value)
                    : (int)size.Value;
            return PythonTuple.MakeTuple(
                decompressedSize,
                GetDictionaryId(data));
        }

        public static int get_frame_size([NotNone] IBufferProtocol frame) {
            byte[] data = GetBytes(frame);
            unsafe {
                fixed (byte* pointer = data) {
                    nuint result = Methods.ZSTD_findFrameCompressedSize(
                        pointer,
                        (nuint)data.Length);
                    if (Methods.ZSTD_isError(result)) {
                        throw MakeError(
                            "not less than this complete frame");
                    }
                    return checked((int)result);
                }
            }
        }

        private static unsafe uint GetDictionaryId(byte[] data) {
            fixed (byte* pointer = data) {
                return Methods.ZSTD_getDictID_fromFrame(
                    pointer,
                    (nuint)data.Length);
            }
        }

        private static unsafe ulong? GetFrameContentSize(byte[] data) {
            fixed (byte* pointer = data) {
                ulong size = Methods.ZSTD_getFrameContentSize(
                    pointer,
                    (nuint)data.Length);
                if (size == ulong.MaxValue) {
                    return null;
                }
                if (size == ulong.MaxValue - 1) {
                    throw MakeError("not less than the frame header");
                }
                return size;
            }
        }

        private static ulong? GetContentSizeFromHeader(byte[] data) {
            if (data.Length < 6) {
                throw MakeError("not less than the frame header");
            }
            int descriptor = data[4];
            bool singleSegment = (descriptor & 0x20) != 0;
            int dictionarySize = 0;
            int dictionaryCode = descriptor & 0x03;
            if (dictionaryCode != 0) {
                dictionarySize = 1 << dictionaryCode;
            }
            int contentSizeCode = descriptor >> 6;
            int contentSizeLength = contentSizeCode switch {
                0 => singleSegment ? 1 : 0,
                1 => 2,
                2 => 4,
                _ => 8
            };
            int offset = 5
                + (singleSegment ? 0 : 1)
                + dictionarySize;
            if (contentSizeLength == 0) {
                return null;
            }
            if (offset + contentSizeLength > data.Length) {
                throw MakeError("not less than the frame header");
            }
            ulong value = 0;
            for (int i = 0; i < contentSizeLength; i++) {
                value |= (ulong)data[offset + i] << (8 * i);
            }
            if (contentSizeLength == 2) {
                value += 256;
            }
            return value;
        }

        internal static byte[] GetBytes(IBufferProtocol value) {
            using var buffer = value.GetBuffer();
            return buffer.AsUnsafeArray() ?? buffer.ToArray();
        }

        private static byte[] CombineInput(
            byte[] first,
            byte[] second) {
            if (first.Length == 0) return second;
            if (second.Length == 0) return first;
            byte[] result = new byte[first.Length + second.Length];
            Array.Copy(first, result, first.Length);
            Array.Copy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private static byte[] GetBytesExact(
            IBufferProtocol value,
            string parameterName) {
            if (PythonOps.GetPythonTypeName(value) != "bytes") {
                throw PythonOps.TypeError(
                    "{0} must be bytes",
                    parameterName);
            }
            return GetBytes(value);
        }

        internal static Exception MakeError(string message)
            => PythonExceptions.CreateThrowable(ZstdError, message);

        [PythonType("ZstdDict")]
        public sealed class ZstdDict {
            public ZstdDict(
                [NotNone] IBufferProtocol content,
                [ParamDictionary] IDictionary<object, object> kwargs) {
                bool isRaw = false;
                foreach (KeyValuePair<object, object> pair in kwargs) {
                    if (!Equals(pair.Key, "is_raw")
                        || pair.Value is not bool rawValue) {
                        throw PythonOps.TypeError(
                            "invalid ZstdDict argument");
                    }
                    isRaw = rawValue;
                }
                Bytes data = Bytes.Make(GetBytes(content));
                if (!isRaw && data.Count < 8) {
                    throw PythonOps.ValueError(
                        "ZstdDict content is too small");
                }
                if (!isRaw) {
                    byte[] bytes = data.ToArray();
                    if (bytes[0] != 0x37
                        || bytes[1] != 0xA4
                        || bytes[2] != 0x30
                        || bytes[3] != 0xEC) {
                        throw PythonOps.ValueError(
                            "ZstdDict dictionary header is invalid");
                    }
                }
                dict_content = data;
            }

            public Bytes dict_content { get; }

            public int dict_id {
                get {
                    byte[] bytes = dict_content.ToArray();
                    if (bytes.Length < 8
                        || bytes[0] != 0x37
                        || bytes[1] != 0xA4
                        || bytes[2] != 0x30
                        || bytes[3] != 0xEC) {
                        return 0;
                    }
                    return unchecked((int)(
                        (uint)bytes[4]
                        | ((uint)bytes[5] << 8)
                        | ((uint)bytes[6] << 16)
                        | ((uint)bytes[7] << 24)));
                }
            }

            public bool is_raw {
                get {
                    byte[] bytes = dict_content.ToArray();
                    return bytes.Length < 4
                        || bytes[0] != 0x37
                        || bytes[1] != 0xA4
                        || bytes[2] != 0x30
                        || bytes[3] != 0xEC;
                }
            }

            public ZstdDict as_digested_dict => this;
            public ZstdDict as_undigested_dict => this;
            public ZstdDict as_prefix => this;

            public int __len__() => dict_content.Count;

            public string __repr__()
                => $"<ZstdDict dict_id={dict_id} dict_size={dict_content.Count}>";
        }

        [PythonType("ZstdCompressor")]
        public sealed class ZstdCompressor {
            public const int CONTINUE = 0;
            public const int FLUSH_BLOCK = 1;
            public const int FLUSH_FRAME = 2;
            public const int FINISH = 3;

            private readonly Compressor _compressor;
            private readonly object _syncRoot = new();
            private int _lastMode = FLUSH_FRAME;
            private bool _streamStarted;
            private bool _hasPendingData;
            private bool _contentSizeDisabled;
            private bool _pledgedSizeSet;

            public ZstdCompressor(
                CodeContext context,
                object? level = null,
                PythonDictionary? options = null,
                object? zstd_dict = null) {
                if (level is BigInteger hugeLevel
                    && (hugeLevel < int.MinValue
                        || hugeLevel > int.MaxValue)) {
                    throw PythonOps.ValueError(
                        "illegal compression level; the valid range is [{0}, {1}]",
                        Compressor.MinCompressionLevel,
                        Compressor.MaxCompressionLevel);
                }
                int levelValue = level is null
                    ? ZSTD_CLEVEL_DEFAULT
                    : ConvertInt(level, "level");
                if (levelValue < Compressor.MinCompressionLevel
                    || levelValue > Compressor.MaxCompressionLevel) {
                    throw PythonOps.ValueError(
                        "illegal compression level {0}; the valid range is [{1}, {2}]",
                        levelValue,
                        Compressor.MinCompressionLevel,
                        Compressor.MaxCompressionLevel);
                }
                _compressor = new Compressor(levelValue);
                ZstdDict? dictionary = ConvertZstdDict(zstd_dict, "zstd_dict");
                if (dictionary is not null) {
                    ValidateDictionary(dictionary, true);
                    try {
                        _compressor.LoadDictionary(
                            dictionary.dict_content.ToArray());
                    } catch (ZstdException exception) {
                        throw MakeDictionaryError(exception, true);
                    }
                }
                ApplyOptions(_compressor, options);
                if (options is not null) {
                    foreach (KeyValuePair<object, object> item in options) {
                        if (ConvertInt(item.Key, "parameter")
                                == ZSTD_c_contentSizeFlag
                            && ConvertInt(item.Value, "value") == 0) {
                            _contentSizeDisabled = true;
                        }
                    }
                }
            }

            public Bytes compress(
                CodeContext context,
                [NotNone] IBufferProtocol data,
                int mode = CONTINUE) {
                lock (_syncRoot) {
                    if (mode is < CONTINUE or > FLUSH_FRAME) {
                        throw PythonOps.ValueError("invalid flush mode");
                    }
                    byte[] input = GetBytes(data);
                    _lastMode = mode;
                    if (mode == CONTINUE && input.Length == 0) {
                        return Bytes.Make(Array.Empty<byte>());
                    }
                    if (mode == FLUSH_BLOCK
                        && input.Length == 0
                        && !_hasPendingData) {
                        return Bytes.Make(Array.Empty<byte>());
                    }
                    if (mode == FLUSH_FRAME && !_streamStarted) {
                        _compressor.SetParameter(
                            ZSTD_cParameter.ZSTD_c_contentSizeFlag,
                            1);
                        _hasPendingData = false;
                        return Bytes.Make(_compressor.Wrap(input).ToArray());
                    }
                    if (!_streamStarted) {
                        if (!_pledgedSizeSet) {
                            _compressor.SetParameter(
                                ZSTD_cParameter.ZSTD_c_contentSizeFlag,
                                0);
                        }
                        _streamStarted = true;
                    }
                    if (input.Length != 0) {
                        _hasPendingData = true;
                    }
                    Bytes result = Process(input, mode == FLUSH_FRAME);
                    if (mode == FLUSH_BLOCK) {
                        Bytes flushed = Flush(false);
                        result = Bytes.Make(CombineInput(
                            result.ToArray(),
                            flushed.ToArray()));
                    }
                    if (mode != CONTINUE) {
                        _hasPendingData = false;
                    }
                    return result;
                }
            }

            public Bytes flush(int mode = FLUSH_FRAME) {
                lock (_syncRoot) {
                    if (mode is < FLUSH_BLOCK or > FLUSH_FRAME) {
                        throw PythonOps.ValueError("invalid flush mode");
                    }
                    _lastMode = mode;
                    if (!_streamStarted) {
                        if (mode == FLUSH_BLOCK) {
                            return Bytes.Make(Array.Empty<byte>());
                        }
                        _compressor.SetParameter(
                            ZSTD_cParameter.ZSTD_c_contentSizeFlag,
                            1);
                        return Bytes.Make(
                            _compressor.Wrap(Array.Empty<byte>()).ToArray());
                    }
                    if (mode == FLUSH_BLOCK && !_hasPendingData) {
                        return Bytes.Make(Array.Empty<byte>());
                    }
                    Bytes result = Flush(mode == FLUSH_FRAME);
                    _hasPendingData = false;
                    if (mode == FLUSH_FRAME) {
                        _streamStarted = false;
                    }
                    return result;
                }
            }

            public int last_mode {
                get {
                    lock (_syncRoot) {
                        return _lastMode;
                    }
                }
            }

            public void set_pledged_input_size(object? value) {
                lock (_syncRoot) {
                    ulong size;
                    if (value is null) {
                        size = ulong.MaxValue;
                    } else {
                        BigInteger big = Converter.ConvertToBigInteger(
                            PythonOps.Index(value));
                        if (big < 0 || big > ulong.MaxValue - 2) {
                            throw PythonOps.ValueError(
                                "pledged input size should be a positive int less than {0}",
                                ulong.MaxValue - 1);
                        }
                        size = (ulong)big;
                    }
                    if (_streamStarted && _lastMode != FLUSH_FRAME) {
                        throw PythonOps.ValueError(
                            "can only set pledged input size when last_mode == FLUSH_FRAME");
                    }
                    try {
                        if (!_contentSizeDisabled) {
                            _compressor.SetParameter(
                                ZSTD_cParameter.ZSTD_c_contentSizeFlag,
                                1);
                        }
                        _compressor.SetPledgedSrcSize(size);
                        _pledgedSizeSet = true;
                    } catch (Exception exception) {
                        throw PythonOps.ValueError(exception.Message);
                    }
                }
            }

            private Bytes Process(byte[] input, bool final) {
                int capacity = Math.Max(
                    128 * 1024,
                    Compressor.GetCompressBound(input.Length) + 1024);
                byte[] output = new byte[capacity];
                try {
                    OperationStatus status = _compressor.WrapStream(
                        input,
                        output,
                        out int consumed,
                        out int written,
                        final);
                    if (status == OperationStatus.InvalidData
                        || consumed != input.Length) {
                        throw MakeError("zstd compression failed");
                    }
                    Array.Resize(ref output, written);
                    if (final) {
                        _streamStarted = false;
                    }
                    return Bytes.Make(output);
                } catch (Exception exception) when (
                    exception.GetPythonException() is null) {
                    throw MakeError(exception.Message);
                }
            }

            private Bytes Flush(bool final) {
                byte[] output = new byte[128 * 1024];
                try {
                    OperationStatus status = _compressor.FlushStream(
                        output,
                        out int written,
                        final);
                    if (status == OperationStatus.InvalidData) {
                        throw MakeError("zstd compression failed");
                    }
                    Array.Resize(ref output, written);
                    return Bytes.Make(output);
                } catch (Exception exception) when (
                    exception.GetPythonException() is null) {
                    throw MakeError(exception.Message);
                }
            }
        }

        [PythonType("ZstdDecompressor")]
        public sealed class ZstdDecompressor {
            private readonly Decompressor _decompressor = new();
            private readonly byte[]? _dictionaryContent;
            private readonly Dictionary<int, int> _options;
            private readonly object _syncRoot = new();
            private byte[] _pendingInput = Array.Empty<byte>();
            private byte[] _frameOutput = Array.Empty<byte>();
            private int _frameOutputOffset;
            private bool _frameComplete;
            private byte[] _trailingData = Array.Empty<byte>();
            private bool _zeroLengthRequest;
            private bool _outputLimitReached;

            public ZstdDecompressor(
                CodeContext context,
                object? zstd_dict = null,
                PythonDictionary? options = null) {
                ZstdDict? dictionary = ConvertZstdDict(
                    zstd_dict,
                    "zstd_dict");
                if (dictionary is not null) {
                    _dictionaryContent = dictionary.dict_content.ToArray();
                    ValidateDictionary(dictionary, false);
                    try {
                        _decompressor.LoadDictionary(_dictionaryContent);
                    } catch (ZstdException exception) {
                        throw MakeDictionaryError(exception, false);
                    }
                }
                _options = ApplyDecompressionOptions(
                    _decompressor,
                    options);
            }

            public bool eof {
                get {
                    lock (_syncRoot) {
                        return IsEofNoLock;
                    }
                }
            }

            public Bytes unused_data {
                get {
                    lock (_syncRoot) {
                        return IsEofNoLock
                            ? Bytes.Make(_trailingData)
                            : Bytes.Make(Array.Empty<byte>());
                    }
                }
            }

            public bool needs_input {
                get {
                    lock (_syncRoot) {
                        if (IsEofNoLock) {
                            return false;
                        }
                        if (_frameOutputOffset < _frameOutput.Length) {
                            return false;
                        }
                        return !_zeroLengthRequest
                            && !_outputLimitReached;
                    }
                }
            }

            private bool IsEofNoLock
                => _frameComplete
                    && _frameOutputOffset >= _frameOutput.Length;

            public Bytes decompress(
                CodeContext context,
                [NotNone] IBufferProtocol data,
                int maxLength = -1) {
                byte[] supplied = GetBytes(data);
                lock (_syncRoot) {
                    try {
                        return DecompressLocked(supplied, maxLength);
                    } catch (Exception exception) when (
                        exception.GetPythonException() is null) {
                        if (exception is ZstdException zstdException) {
                            throw MakeDecompressionError(zstdException);
                        }
                        throw MakeError(exception.Message);
                    }
                }
            }

            private Bytes DecompressLocked(
                byte[] supplied,
                int maxLength) {
                if (IsEofNoLock && supplied.Length == 0) {
                    throw PythonExceptions.CreateThrowable(
                        PythonExceptions.EOFError,
                        "end of stream");
                }

                _zeroLengthRequest = maxLength == 0;
                _outputLimitReached = false;
                int outputLimit = maxLength < 0
                    ? int.MaxValue
                    : maxLength;

                if (_frameComplete) {
                    if (supplied.Length != 0) {
                        _trailingData = CombineInput(
                            _trailingData,
                            supplied);
                    }
                    return EmitFrameOutput(outputLimit);
                }

                byte[] input = CombineInput(_pendingInput, supplied);
                _pendingInput = Array.Empty<byte>();
                if (input.Length == 0) {
                    return Bytes.Make(Array.Empty<byte>());
                }

                FrameSizeStatus status = FindFrame(
                    input,
                    out int frameSize,
                    out Exception? frameError);
                if (status == FrameSizeStatus.Complete) {
                    byte[] output = DecompressFrame(
                        input.AsSpan(0, frameSize).ToArray());
                    _frameOutput = output;
                    _frameComplete = true;
                    _trailingData = input
                        .AsSpan(frameSize)
                        .ToArray();
                    return EmitFrameOutput(outputLimit);
                }

                if (status == FrameSizeStatus.Incomplete) {
                    _pendingInput = input;
                    byte[] partial = DecompressAvailableOutput(input);
                    if (partial.Length >= _frameOutput.Length) {
                        _frameOutput = partial;
                    }
                    return EmitFrameOutput(outputLimit);
                }

                throw frameError
                    ?? MakeError("invalid zstd data");
            }

            private Bytes EmitFrameOutput(int outputLimit) {
                int available = _frameOutput.Length - _frameOutputOffset;
                int count = outputLimit < 0
                    ? available
                    : Math.Min(available, outputLimit);
                if (!_frameComplete
                    && OutputLimitWasReached(
                        outputLimit,
                        count,
                        available)) {
                    _outputLimitReached = true;
                }
                if (count <= 0) {
                    return Bytes.Make(Array.Empty<byte>());
                }
                byte[] result = _frameOutput
                    .AsSpan(_frameOutputOffset, count)
                    .ToArray();
                _frameOutputOffset += count;
                return Bytes.Make(result);
            }

            private static bool OutputLimitWasReached(
                int outputLimit,
                int count,
                int available) {
                return outputLimit >= 0
                    && count == outputLimit
                    && available >= outputLimit;
            }

            private byte[] DecompressFrame(byte[] frame) {
                if (IsSkippableFrame(frame)) {
                    return Array.Empty<byte>();
                }
                ulong capacity = Decompressor.GetDecompressedSize(frame);
                if (capacity > int.MaxValue) {
                    throw MakeError(
                        "decompressed content is too large");
                }
                byte[] output = new byte[(int)capacity];
                if (!_decompressor.TryUnwrap(
                    frame,
                    output,
                    out int written)) {
                    throw MakeError(
                        "decompressed content is larger than expected");
                }
                Array.Resize(ref output, written);
                return output;
            }

            private byte[] DecompressAvailableOutput(byte[] input) {
                using var decompressor = CreateDecompressor();
                var output = new ArrayBufferWriter<byte>();
                byte[] buffer = new byte[ZSTD_DStreamOutSize];
                int consumed = 0;
                while (true) {
                    OperationStatus status = decompressor.UnwrapStream(
                        input.AsSpan(consumed),
                        buffer,
                        out int stepConsumed,
                        out int stepWritten);
                    consumed += stepConsumed;
                    if (stepWritten > 0) {
                        output.Write(buffer.AsSpan(0, stepWritten));
                    }
                    if (status != OperationStatus.Done
                        && stepWritten == buffer.Length) {
                        continue;
                    }
                    if (status == OperationStatus.Done
                        || status == OperationStatus.NeedMoreData
                        || (stepConsumed == 0 && stepWritten == 0)) {
                        break;
                    }
                    if (status == OperationStatus.InvalidData) {
                        throw MakeError("invalid zstd data");
                    }
                }
                return output.WrittenSpan.ToArray();
            }

            private Decompressor CreateDecompressor() {
                var decompressor = new Decompressor();
                if (_dictionaryContent is not null) {
                    decompressor.LoadDictionary(_dictionaryContent);
                }
                foreach (KeyValuePair<int, int> option in _options) {
                    decompressor.SetParameter(
                        (ZSTD_dParameter)option.Key,
                        option.Value);
                }
                return decompressor;
            }

            private static bool IsSkippableFrame(byte[] frame) {
                return frame.Length >= 8
                    && frame[0] >= 0x50
                    && frame[0] <= 0x5F
                    && frame[1] == 0x2A
                    && frame[2] == 0x4D
                    && frame[3] == 0x18;
            }

            private static unsafe FrameSizeStatus FindFrame(
                byte[] input,
                out int frameSize,
                out Exception? error) {
                fixed (byte* pointer = input) {
                    nuint result = Methods.ZSTD_findFrameCompressedSize(
                        pointer,
                        (nuint)input.Length);
                    if (!Methods.ZSTD_isError(result)) {
                        frameSize = checked((int)result);
                        error = null;
                        return FrameSizeStatus.Complete;
                    }
                    frameSize = 0;
                    ZSTD_ErrorCode code = Methods.ZSTD_getErrorCode(result);
                    if (code
                        == ZSTD_ErrorCode.ZSTD_error_srcSize_wrong) {
                        error = null;
                        return FrameSizeStatus.Incomplete;
                    }
                    error = MakeErrorForCode(
                        code,
                        Methods.ZSTD_getErrorName(result));
                    return FrameSizeStatus.Invalid;
                }
            }

            private static Exception MakeDecompressionError(
                ZstdException exception) {
                return MakeErrorForCode(
                    exception.Code,
                    exception.Message);
            }
        }

        private enum FrameSizeStatus {
            Complete,
            Incomplete,
            Invalid
        }

        private static Exception MakeErrorForCode(
            ZSTD_ErrorCode code,
            string fallback) {
            return code switch {
                ZSTD_ErrorCode.ZSTD_error_checksum_wrong
                    => MakeError(
                        "Restored data doesn't match checksum"),
                ZSTD_ErrorCode.ZSTD_error_prefix_unknown
                    => MakeError("Unknown frame descriptor"),
                _ => MakeError(fallback)
            };
        }

        private static Exception MakeDictionaryError(
            ZstdException exception,
            bool compression) {
            if (exception.Code
                    is ZSTD_ErrorCode.ZSTD_error_dictionary_corrupted
                    or ZSTD_ErrorCode.ZSTD_error_dictionary_wrong) {
                return MakeError(
                    compression
                        ? "ZSTD_CDict creation failed: Dictionary content."
                        : "ZSTD_DDict creation failed: Dictionary content.");
            }
            return MakeError(exception.Message);
        }

        private static unsafe void ValidateDictionary(
            ZstdDict dictionary,
            bool compression) {
            if (dictionary.is_raw) {
                return;
            }
            byte[] content = dictionary.dict_content.ToArray();
            fixed (byte* pointer = content) {
                if (compression) {
                    ZSTD_CDict_s* cdict = Methods.ZSTD_createCDict(
                        pointer,
                        (nuint)content.Length,
                        ZSTD_CLEVEL_DEFAULT);
                    if (cdict == null) {
                        throw MakeError(
                            "ZSTD_CDict creation failed: Dictionary content.");
                    }
                    Methods.ZSTD_freeCDict(cdict);
                } else {
                    ZSTD_DDict_s* ddict = Methods.ZSTD_createDDict(
                        pointer,
                        (nuint)content.Length);
                    if (ddict == null) {
                        throw MakeError(
                            "ZSTD_DDict creation failed: Dictionary content.");
                    }
                    Methods.ZSTD_freeDDict(ddict);
                }
            }
        }

        private static ZstdDict? ConvertZstdDict(
            object? value,
            string parameterName) {
            if (value is null) {
                return null;
            }
            if (value is PythonTuple tuple
                && tuple.Count == 2
                && tuple[1] is BigInteger overflowingValue
                && (overflowingValue < int.MinValue
                    || overflowingValue > int.MaxValue)) {
                throw PythonOps.OverflowError(
                    "{0} does not fit in a C int",
                    parameterName);
            }
            if (value is not ZstdDict dictionary) {
                throw PythonOps.TypeError(
                    "{0} argument should be a ZstdDict object",
                    parameterName);
            }
            return dictionary;
        }

        private static int TryGetFrameSize(byte[] data) {
            unsafe {
                fixed (byte* pointer = data) {
                    nuint result = Methods.ZSTD_findFrameCompressedSize(
                        pointer,
                        (nuint)data.Length);
                    return Methods.ZSTD_isError(result)
                        ? 0
                        : checked((int)result);
                }
            }
        }

        private static void ApplyOptions(
            Compressor compressor,
            PythonDictionary? options) {
            if (options is null) {
                return;
            }
            foreach (KeyValuePair<object, object> item in options) {
                if (_decompressionParameterType is not null
                    && PythonOps.IsInstance(
                        item.Key,
                        _decompressionParameterType)) {
                    throw PythonOps.TypeError(
                        "options must not be a DecompressionParameter object");
                }
                int parameter = ConvertInt(item.Key, "parameter");
                int value = ConvertInt(item.Value, "value");
                if (!IsCompressionParameter(parameter)) {
                    throw PythonOps.ValueError(
                        "invalid compression parameter 'unknown parameter (key {0})'",
                        parameter);
                }
                PythonTuple bounds = get_param_bounds(parameter, true);
                int minimum = Converter.ConvertToInt32(bounds[0]);
                int maximum = Converter.ConvertToInt32(bounds[1]);
                if (value < minimum || value > maximum) {
                    string name = parameter == ZSTD_c_windowLog
                        ? "window_log"
                        : "compression";
                    throw PythonOps.ValueError(
                        "compression parameter '{0}' received an illegal value {1}; the valid range is [{2}, {3}]",
                        name,
                        value,
                        minimum,
                        maximum);
                }
                try {
                    compressor.SetParameter(
                        (ZSTD_cParameter)parameter,
                        value);
                } catch (Exception exception) {
                    throw PythonOps.ValueError(exception.Message);
                }
            }
        }

        private static Dictionary<int, int> ApplyDecompressionOptions(
            Decompressor decompressor,
            PythonDictionary? options) {
            var result = new Dictionary<int, int>();
            if (options is null) {
                return result;
            }
            foreach (KeyValuePair<object, object> item in options) {
                if (_compressionParameterType is not null
                    && PythonOps.IsInstance(
                        item.Key,
                        _compressionParameterType)) {
                    throw PythonOps.TypeError(
                        "options must not be a CompressionParameter object");
                }
                int parameter = ConvertInt(item.Key, "parameter");
                int value = ConvertInt(item.Value, "value");
                if (parameter != ZSTD_d_windowLogMax) {
                    throw PythonOps.ValueError(
                        "invalid decompression parameter 'unknown parameter (key {0})'",
                        parameter);
                }
                PythonTuple bounds = get_param_bounds(parameter, false);
                int minimum = Converter.ConvertToInt32(bounds[0]);
                int maximum = Converter.ConvertToInt32(bounds[1]);
                if (value < minimum || value > maximum) {
                    throw PythonOps.ValueError(
                        "decompression parameter 'window_log_max' received an illegal value {0}; the valid range is [{1}, {2}]",
                        value,
                        minimum,
                        maximum);
                }
                try {
                    decompressor.SetParameter(
                        (ZSTD_dParameter)parameter,
                        value);
                    result[parameter] = value;
                } catch (Exception exception) {
                    throw PythonOps.ValueError(exception.Message);
                }
            }
            return result;
        }

        private static bool IsCompressionParameter(int parameter) {
            return parameter is ZSTD_c_compressionLevel
                or ZSTD_c_windowLog
                or ZSTD_c_hashLog
                or ZSTD_c_chainLog
                or ZSTD_c_searchLog
                or ZSTD_c_minMatch
                or ZSTD_c_targetLength
                or ZSTD_c_strategy
                or ZSTD_c_enableLongDistanceMatching
                or ZSTD_c_ldmHashLog
                or ZSTD_c_ldmMinMatch
                or ZSTD_c_ldmBucketSizeLog
                or ZSTD_c_ldmHashRateLog
                or ZSTD_c_contentSizeFlag
                or ZSTD_c_checksumFlag
                or ZSTD_c_dictIDFlag
                or ZSTD_c_nbWorkers
                or ZSTD_c_jobSize
                or ZSTD_c_overlapLog;
        }

        private static int ConvertInt(object? value, string name) {
            object? indexed;
            try {
                indexed = PythonOps.Index(value);
            } catch (Exception) {
                throw PythonOps.TypeError("{0} must be an int", name);
            }
            if (indexed is int integer) {
                return integer;
            }
            if (indexed is BigInteger big && big >= int.MinValue && big <= int.MaxValue) {
                return (int)big;
            }
            if (indexed is BigInteger) {
                throw PythonOps.OverflowError(
                    "{0} does not fit in a C int",
                    name);
            }
            throw PythonOps.TypeError(
                "{0} must be an int",
                name);
        }
    }
}
