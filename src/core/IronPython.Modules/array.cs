// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

using SpecialName = System.Runtime.CompilerServices.SpecialNameAttribute;
using NotNull = System.Diagnostics.CodeAnalysis.NotNullAttribute;

[assembly: PythonModule("array", typeof(IronPython.Modules.ArrayModule))]
namespace IronPython.Modules {
    public static class ArrayModule {
        public const string __doc__ = "Provides arrays for native data types.  These can be used for compact storage or native interop via ctypes";
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly PythonType/*!*/ ArrayType = DynamicHelpers.GetPythonTypeFromType(typeof(array));

        public static readonly PythonTuple typecodes = PythonTuple.MakeTuple(
            "b", "B", "u", "w", "h", "H", "i", "I", "l", "L", "q", "Q", "f", "d", "e", "Zf", "Zd");

        private static array ArrayReconstructor(CodeContext context, [NotNone] PythonType cls, [NotNone] string typecode, int mformat_code, [NotNone] Bytes items) {
            if (typecode.Length != 1 && typecode is not ("Zf" or "Zd"))
                throw PythonOps.TypeError("expected character, got {0}", PythonOps.GetPythonTypeName(typecode));
            if (!typecodes.Contains(typecode))
                throw PythonOps.ValueError("bad typecode (must be b, B, u, w, h, H, i, I, l, L, q, Q, f, d, e, Zf or Zd)");

            var actualTypeCode = MachineFormatToTypeCode(mformat_code, out bool isBigEndian, out string? encoding);
            if (TypeCodeToMachineFormat(typecode) == mformat_code) {
                // if typecodes are equivelent, use original
                actualTypeCode = typecode;
            }

            var arrayType = DynamicHelpers.GetPythonTypeFromType(typeof(array));

            if (!cls.IsSubclassOf(arrayType)) {
                throw PythonOps.TypeError($"{cls} is not a subtype of array.array");
            }

            array res;
            if (cls == arrayType) {
                res = new array(actualTypeCode);
            } else if (cls.CreateInstance(context, actualTypeCode) is array arr) {
                res = arr;
            } else {
                throw PythonOps.TypeError($"{cls} is not a subtype of array.array");
            }

            if (encoding == null) {
                res.frombytes(items);
                if (isBigEndian) res.byteswap();
            } else {
                res.fromunicode(context, StringOps.RawDecode(context, items, encoding, null));
            }
            return res;

            static string MachineFormatToTypeCode(int machineFormat, out bool isBigEndian, out string? encoding) {
                isBigEndian = machineFormat % 2 == 1;
                encoding = machineFormat switch
                {
                    18 => "UTF-16-LE",
                    19 => "UTF-16-BE",
                    20 => "UTF-32-LE",
                    21 => "UTF-32-BE",
                    _ => null,
                };
                return machineFormat switch
                {
                    0 => "B",
                    1 => "b",
                    2 => "H",
                    3 => "H",
                    4 => "h",
                    5 => "h",
                    6 => "I",
                    7 => "I",
                    8 => "i",
                    9 => "i",
                    10 => "Q",
                    11 => "Q",
                    12 => "q",
                    13 => "q",
                    14 => "f",
                    15 => "f",
                    16 => "d",
                    17 => "d",
                    18 => "u",
                    19 => "u",
                    20 => "u",
                    21 => "u",
                    22 => "Zf",
                    23 => "Zf",
                    24 => "Zd",
                    25 => "Zd",
                    26 => "e",
                    27 => "e",
                    _ => throw PythonOps.ValueError("invalid machine code format"),
                };
            }
        }

        private static int TypeCodeToMachineFormat(string typeCode) {
            return typeCode switch
            {
                "b" => 1,
                "B" => 0,
                "u" => 18,
                "w" => 20,
                "h" => 4,
                "H" => 2,
                "i" => 8,
                "I" => 6,
                "l" => TypecodeOps.IsCLong32Bit ? 8 : 12,
                "L" => TypecodeOps.IsCLong32Bit ? 6 : 10,
                "q" => 12,
                "Q" => 10,
                "f" => 14,
                "d" => 16,
                "e" => 26,
                "Zf" => 22,
                "Zd" => 24,
                _ => throw new InvalidOperationException(),// should never happen
            };
        }

        public static readonly BuiltinFunction _array_reconstructor = BuiltinFunction.MakeFunction(nameof(_array_reconstructor), ArrayUtils.ConvertAll(typeof(ArrayModule).GetMember(nameof(ArrayReconstructor), BindingFlags.NonPublic | BindingFlags.Static), x => (MethodBase)x), typeof(ArrayModule));

        [PythonType]
        public class array : IEnumerable, IWeakReferenceable, ICollection, ICodeFormattable, IList<object>, IStructuralEquatable, IBufferProtocol {
            //  _data is readonly to ensure proper size locking during buffer exports
            private readonly ArrayData _data;
            private readonly char _typeCode;
            private readonly char _complexElementCode;
            private readonly string _declaredTypeCode;
            private WeakRefTracker? _tracker;

            public array([NotNone] string type) {
                if (type is null) {
                    throw PythonOps.TypeErrorForBadInstance("expected character, got {0}", type);
                }
                if (type.Length != 1 && type is not ("Zf" or "Zd")) {
                    throw PythonOps.ValueError("bad typecode");
                }
                if (type == "Z") {
                    throw PythonOps.ValueError("bad typecode");
                }

                _declaredTypeCode = type;
                if (type is "Zf" or "Zd") {
                    _typeCode = 'Z';
                    _complexElementCode = type[1];
                } else {
                    _typeCode = type[0];
                    _complexElementCode = '\0';
                }
                _data = CreateData(_typeCode, _complexElementCode);
                if (_typeCode == 'u') {
                    PythonOps.Warn(
                        DefaultContext.Default,
                        PythonExceptions.DeprecationWarning,
                        "The 'u' type code is deprecated and will be removed in Python 3.16");
                }
            }

            public array([NotNone] string type, [NotNone] Bytes initializer) : this(type) {
                frombytes(initializer);
            }

            public array([NotNone] string type, [NotNone] ByteArray initializer) : this(type) {
                frombytes(initializer);
            }

            public array([NotNone] string type, [NotNone] array initializer) : this(type) {
                if (_typeCode is not ('u' or 'w') && initializer._typeCode is 'u' or 'w') {
                    throw PythonOps.TypeError("cannot use a unicode array to initialize an array with typecode '{0}'", _typeCode);
                }

                ExtendIter(initializer);
            }

            public array([NotNone] string type, object? initializer) : this(type) {
                if (_typeCode is not ('u' or 'w')) {
                    if (initializer is string || initializer is Extensible<string>)
                        throw PythonOps.TypeError("cannot use a str to initialize an array with typecode '{0}'", _typeCode);
                    if (initializer is array arr && arr._typeCode is 'u' or 'w')
                        throw PythonOps.TypeError("cannot use a unicode array to initialize an array with typecode '{0}'", _typeCode);
                }

                ExtendIter(initializer);
            }

            private array(char typeCode, ArrayData data, char complexElementCode = '\0') {
                _typeCode = typeCode;
                _complexElementCode = complexElementCode;
                _declaredTypeCode = typeCode == 'Z'
                    ? "Z" + complexElementCode
                    : ScriptingRuntimeHelpers.CharToString(typeCode);
                _data = data;
            }

            private static ArrayData CreateData(char typecode, char complexElementCode = '\0') {
                return (typecode) switch
                {
                    'b' => new ArrayData<sbyte>(),
                    'B' => new ArrayData<byte>(),
                    'u' => new ArrayData<char>(),
                    'w' => new ArrayData<char>(),
                    'h' => new ArrayData<short>(),
                    'H' => new ArrayData<ushort>(),
                    'i' => new ArrayData<int>(),
                    'I' => new ArrayData<uint>(),
                    'l' => TypecodeOps.IsCLong32Bit ? new ArrayData<int>() : new ArrayData<long>(),
                    'L' => TypecodeOps.IsCLong32Bit ? new ArrayData<uint>() : new ArrayData<ulong>(),
                    'q' => new ArrayData<long>(),
                    'Q' => new ArrayData<ulong>(),
                    'f' => new ArrayData<float>(),
                    'd' => new ArrayData<double>(),
                    'e' => new ArrayData<Half>(),
                    'Z' => new ArrayData<Complex>(),
                    _ => throw PythonOps.ValueError("bad typecode (must be b, B, u, w, h, H, i, I, l, L, q, Q, f, d, e, Zf or Zd)"),
                };
            }

            [SpecialName]
            public array InPlaceAdd([NotNone] array other) {
                ExtendArray(other);
                return this;
            }

            public static array operator +([NotNone] array self, [NotNone] array other) {
                if (self.typecode != other.typecode) throw PythonOps.TypeError("cannot add different typecodes");

                array res = new array(self.typecode);
                res.ExtendArray(self);
                res.ExtendArray(other);
                return res;
            }

            [SpecialName]
            public array InPlaceMultiply(int value) {
                if (value <= 0) {
                    _data.Clear();
                } else {
                    _data.InPlaceMultiply(value);
                }
                return this;
            }

            public static array operator *([NotNone] array array, int value) {
                if ((BigInteger)value * array.__len__() * array.itemsize > SysModule.maxsize) {
                    throw PythonOps.MemoryError("");
                }

                if (value <= 0) {
                    return new array(array.typecode);
                }

                return new array(array._typeCode, array._data.Multiply(value), array._complexElementCode);
            }

            public static array operator *([NotNone] array array, BigInteger value) {
                int intValue;
                if (!value.AsInt32(out intValue)) {
                    throw PythonOps.OverflowError("cannot fit 'long' into an index-sized integer");
                } else if (value * array.__len__() * array.itemsize > SysModule.maxsize) {
                    throw PythonOps.MemoryError("");
                }

                return array * intValue;
            }

            public static array operator *(int value, [NotNone] array array) {
                return array * value;
            }

            public static array operator *(BigInteger value, [NotNone] array array) {
                return array * value;
            }

            public void append(object? iterable) {
                _data.Add(iterable);
            }

            internal IntPtr GetArrayAddress() {
                return _data.GetAddress();
            }

            public PythonTuple buffer_info() {
                return PythonTuple.MakeTuple(
                    _data.GetAddress().ToPython(),
                    _data.Count
                );
            }

            public void byteswap() {
                MemoryStream s = ToStream();
                byte[] bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);

                byte[] tmp = new byte[itemsize];
                for (int i = 0; i < bytes.Length; i += itemsize) {
                    int half = _typeCode == 'Z' ? itemsize / 2 : itemsize;
                    for (int offset = 0; offset < itemsize; offset += half) {
                        for (int j = 0; j < half; j++) {
                            tmp[j] = bytes[i + offset + j];
                        }
                        for (int j = 0; j < half; j++) {
                            bytes[i + offset + j] = tmp[half - (j + 1)];
                        }
                    }
                }
                _data.Clear();
                MemoryStream ms = new MemoryStream(bytes);
                FromStream(ms);
            }

            public int count(object? x) => _data.CountItems(x);

            private void ExtendArray(array pa) {
                if (typecode != pa.typecode) {
                    throw PythonOps.TypeError("can only extend with array of same kind");
                }

                if (pa._data.Count == 0) return;
                _data.AddRange(pa._data);
            }

            private void ExtendIter(object? iterable) {
                if (_typeCode == 'B' && iterable is Bytes bytes) {
                    FromBytes(bytes);
                    return;
                }

                if (_typeCode is ('u' or 'w') && iterable is string str) {
                    FromUnicode(str);
                    return;
                }

                IEnumerator ie = PythonOps.GetEnumerator(iterable);
                while (ie.MoveNext()) {
                    append(ie.Current);
                }
            }

            public void extend(object? iterable) {
                if (iterable is array pa) {
                    ExtendArray(pa);
                } else {
                    ExtendIter(iterable);
                }
            }

            public void fromlist(CodeContext context, [NotNone] PythonList iterable) {
                IEnumerator ie = PythonOps.GetEnumerator(context, iterable);

                List<object> items = new List<object>();
                while (ie.MoveNext()) {
                    if (!_data.CanStore(ie.Current)) {
                        throw PythonOps.TypeError("expected {0}, got {1}",
                            DynamicHelpers.GetPythonTypeFromType(_data.StorageType).Name,
                            PythonOps.GetPythonTypeName(ie.Current));
                    }
                    items.Add(ie.Current);
                }

                ExtendIter(items);
            }

            public void fromfile(CodeContext/*!*/ context, [NotNone] PythonIOModule._IOBase f, int n) {
                int bytesNeeded = n * itemsize;
                Bytes bytes = (Bytes)f.read(context, bytesNeeded);
                frombytes(bytes);
                if (bytes.Count < bytesNeeded) throw PythonOps.EofError("file not large enough");
            }

            public void frombytes([NotNone] IBufferProtocol buffer) {
                using IPythonBuffer pb = buffer.GetBuffer();
                if ((pb.NumBytes() % itemsize) != 0) throw PythonOps.ValueError("bytes length not a multiple of item size");

                if (buffer is Bytes b) {
                    FromBytes(b);
                    return;
                }

                // TODO: eliminate data copy
                FromStream(new MemoryStream(pb.ToArray(), false));
            }

            private void FromBytes(Bytes b) {
                FromStream(new MemoryStream(b.UnsafeByteArray, false));
            }

            public void fromstring(CodeContext/*!*/ context, [NotNone] Bytes b) {
                PythonOps.Warn(context, PythonExceptions.DeprecationWarning, "fromstring() is deprecated. Use frombytes() instead.");
                if ((b.Count % itemsize) != 0) throw PythonOps.ValueError("bytes length not a multiple of itemsize");
                FromBytes(b);
            }

            public void fromstring(CodeContext/*!*/ context, [NotNone] string s) {
                PythonOps.Warn(context, PythonExceptions.DeprecationWarning, "fromstring() is deprecated. Use frombytes() instead.");
                if ((s.Length % itemsize) != 0) throw PythonOps.ValueError("bytes length not a multiple of itemsize");
                byte[] bytes = new byte[s.Length];
                for (int i = 0; i < bytes.Length; i++) {
                    bytes[i] = checked((byte)s[i]);
                }
                MemoryStream ms = new MemoryStream(bytes);

                FromStream(ms);
            }

            public void fromunicode(CodeContext/*!*/ context, [NotNone] string s) {
                if (_typeCode is not ('u' or 'w')) {
                    throw PythonOps.ValueError("fromunicode() may only be called on type 'u' arrays");
                }

                FromUnicode(s);
            }

            private void FromUnicode(string s) {
                ArrayData<char> data = (ArrayData<char>)_data;
                data.AddRange(s);
            }

            public int index(object? x) {
                if (x == null) throw PythonOps.ValueError("got None, expected value");
                int res = _data.IndexOf(x);
                if (res == -1) throw PythonOps.ValueError("x not found");
                return res;
            }

            public int index(object? x, object? start, object? stop = null) {
                return ((PythonList)tolist()).index(
                    x,
                    start is null ? 0 : Converter.ConvertToIndex(start),
                    stop is null ? _data.Count : Converter.ConvertToIndex(stop));
            }

            public void insert(int i, [NotNone] object x) {
                if (i > _data.Count) i = _data.Count;
                if (i < 0) i = _data.Count + i;
                if (i < 0) i = 0;

                _data.Insert(i, x);
            }

            public int itemsize
                => _typeCode switch {
                    'e' => 2,
                    'Z' => _complexElementCode == 'f' ? 8 : 16,
                    _ => TypecodeOps.GetTypecodeWidth(_typeCode)
                };

            public object pop() {
                return pop(-1);
            }

            public object pop(int i) {
                i = PythonOps.FixIndex(i, _data.Count);
                object res = this[i];
                _data.RemoveAt(i);
                return res;
            }

            public void remove(object? value) {
                if (!_data.Remove(value)) throw PythonOps.ValueError("couldn't find value to remove");
            }

            public void reverse()
                => _data.Reverse();

            public void clear() => _data.Clear();

            public virtual object this[int index] {
                get {
                    index = PythonOps.FixIndex(index, _data.Count);
                    switch (_typeCode) {
                        case 'b': return (int)((ArrayData<sbyte>)_data)[index];
                        case 'B': return (int)((ArrayData<byte>)_data)[index];
                        case 'u':
                        case 'w': return ScriptingRuntimeHelpers.CharToString(((ArrayData<char>)_data)[index]);
                        case 'h': return (int)((ArrayData<short>)_data)[index];
                        case 'H': return (int)((ArrayData<ushort>)_data)[index];
                        case 'i': return ((ArrayData<int>)_data)[index];
                        case 'I': return (BigInteger)((ArrayData<uint>)_data)[index];
                        case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                        case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                        case 'q': return (BigInteger)((ArrayData<long>)_data)[index];
                        case 'Q': return (BigInteger)((ArrayData<ulong>)_data)[index];
                        case 'f': return (double)((ArrayData<float>)_data)[index];
                        case 'd': return ((ArrayData<double>)_data)[index];
                        case 'e': return (double)((ArrayData<Half>)_data)[index];
                        case 'Z': return ((ArrayData<Complex>)_data)[index];
                        default: throw new InvalidOperationException(); // should never happen
                    }
                }
                [param: AllowNull]
                set {
                    index = PythonOps.FixIndex(index, _data.Count);
                    _data[index] = value;
                }
            }

            internal byte[] RawGetItem(int index) {
                MemoryStream ms = new MemoryStream();
                BinaryWriter bw = new BinaryWriter(ms);
                switch (_typeCode) {
                    case 'b': bw.Write(((ArrayData<sbyte>)_data)[index]); break;
                    case 'B': bw.Write(((ArrayData<byte>)_data)[index]); break;
                    case 'u':
                    case 'w': WriteBinaryChar(bw, ((ArrayData<char>)_data)[index]); break;
                    case 'h': bw.Write(((ArrayData<short>)_data)[index]); break;
                    case 'H': bw.Write(((ArrayData<ushort>)_data)[index]); break;
                    case 'i': bw.Write(((ArrayData<int>)_data)[index]); break;
                    case 'I': bw.Write(((ArrayData<uint>)_data)[index]); break;
                    case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                    case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                    case 'q': bw.Write(((ArrayData<long>)_data)[index]); break;
                    case 'Q': bw.Write(((ArrayData<ulong>)_data)[index]); break;
                    case 'f': bw.Write(((ArrayData<float>)_data)[index]); break;
                    case 'd': bw.Write(((ArrayData<double>)_data)[index]); break;
                    case 'e': bw.Write((Half)((ArrayData<Half>)_data)[index]); break;
                    case 'Z':
                        Complex complex = ((ArrayData<Complex>)_data)[index];
                        if (_complexElementCode == 'f') {
                            bw.Write((float)complex.Real);
                            bw.Write((float)complex.Imaginary);
                        } else {
                            bw.Write(complex.Real);
                            bw.Write(complex.Imaginary);
                        }
                        break;
                    default: throw new InvalidOperationException(); // should never happen
                }
                return ms.ToArray();
            }

            public void __delitem__(int index) {
                _data.RemoveAt(PythonOps.FixIndex(index, _data.Count));
            }

            public void __delitem__([NotNone] Slice slice) {
                _data.RemoveSlice(slice);
            }

            [NotNull]
            public object? this[[NotNone] Slice index] {
                get {
                    var sliced = (PythonList)tolist()[index];
                    return new array(typecode, sliced);
                }
                set {
                    array arr = CheckSliceAssignType(value);

                    if (index.step != null) {
                        if (Object.ReferenceEquals(value, this)) value = this.tolist();

                        index.DoSliceAssign(SliceAssign, _data.Count, value);
                    } else {
                        index.Indices(_data.Count, out int start, out int stop, out int step);
                        if (stop < start) {
                            stop = start;
                        }
                        SliceNoStep(arr, start, stop);
                    }
                }
            }

            private array CheckSliceAssignType([NotNull] object? value) {
                if (!(value is array pa)) {
                    throw PythonOps.TypeError("can only assign array (not \"{0}\") to array slice", PythonOps.GetPythonTypeName(value));
                } else if (pa.typecode != typecode) {
                    throw PythonOps.TypeError("bad argument type for built-in operation");
                }
                return pa;
            }

            private void SliceNoStep(array arr, int start, int stop) {
                // replace between start & stop w/ values
                int count = stop - start;
                if (count == 0 && arr._data.Count == 0) return;

                if (ReferenceEquals(this, arr)) {
                    arr = new array(typecode, this);
                }
                _data.InsertRange(start, count, arr._data);
            }

            public array __copy__() {
                return new array(typecode, this);
            }

            public array __deepcopy__(object? memo) {
                return __copy__();
            }

            public PythonTuple __reduce_ex__(CodeContext context, int version) {
                PythonOps.TryGetBoundAttr(context, this, "__dict__", out object? dictObject);
                var dict = dictObject as PythonDictionary;

                if (version < 3) {
                    return PythonTuple.MakeTuple(
                        DynamicHelpers.GetPythonType(this),
                        PythonTuple.MakeTuple(
                            typecode,
                            tolist()
                        ),
                        dict
                    );
                }

                return PythonTuple.MakeTuple(
                    _array_reconstructor,
                    PythonTuple.MakeTuple(
                        DynamicHelpers.GetPythonType(this),
                        typecode,
                        TypeCodeToMachineFormat(typecode),
                        tobytes()
                    ),
                    dict);
            }

            private void SliceAssign(int index, object? value) {
                _data[index] = value;
            }

            public void tofile(CodeContext context, [NotNone] PythonIOModule._IOBase f) {
                f.write(context, tobytes());
            }

            public PythonList tolist() {
                PythonList res = new PythonList();
                for (int i = 0; i < _data.Count; i++) {
                    res.AddNoLock(this[i]);
                }
                return res;
            }

            public Bytes tobytes() {
                MemoryStream s = ToStream();
                byte[] bytes = new byte[s.Length];
                s.Read(bytes, 0, (int)s.Length);
                return Bytes.Make(bytes);
            }

            public Bytes tostring(CodeContext/*!*/ context) {
                PythonOps.Warn(context, PythonExceptions.DeprecationWarning, "tostring() is deprecated. Use tobytes() instead.");
                return tobytes();
            }

            public string tounicode(CodeContext/*!*/ context) {
                if (_typeCode is not ('u' or 'w')) throw PythonOps.ValueError("only unicode arrays can be converted to unicode");

                return new string(((ArrayData<char>)_data).Data, 0, _data.Count);
            }

            public string/*!*/ typecode {
                get { return _declaredTypeCode; }
            }

            internal MemoryStream ToStream() {
                MemoryStream ms = new MemoryStream();
                ToStream(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return ms;
            }

            internal void ToStream(Stream ms) {
                BinaryWriter bw = new BinaryWriter(ms);
                for (int i = 0; i < _data.Count; i++) {
                    switch (_typeCode) {
                        case 'b': bw.Write(((ArrayData<sbyte>)_data)[i]); break;
                        case 'B': bw.Write(((ArrayData<byte>)_data)[i]); break;
                        case 'u':
                        case 'w': WriteBinaryChar(bw, ((ArrayData<char>)_data)[i]); break;
                        case 'h': bw.Write(((ArrayData<short>)_data)[i]); break;
                        case 'H': bw.Write(((ArrayData<ushort>)_data)[i]); break;
                        case 'i': bw.Write(((ArrayData<int>)_data)[i]); break;
                        case 'I': bw.Write(((ArrayData<uint>)_data)[i]); break;
                        case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                        case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                        case 'q': bw.Write(((ArrayData<long>)_data)[i]); break;
                        case 'Q': bw.Write(((ArrayData<ulong>)_data)[i]); break;
                        case 'f': bw.Write(((ArrayData<float>)_data)[i]); break;
                        case 'd': bw.Write(((ArrayData<double>)_data)[i]); break;
                        case 'e': bw.Write(BitConverter.HalfToUInt16Bits(((ArrayData<Half>)_data)[i])); break;
                        case 'Z':
                            Complex complex = ((ArrayData<Complex>)_data)[i];
                            if (_complexElementCode == 'f') {
                                bw.Write((float)complex.Real);
                                bw.Write((float)complex.Imaginary);
                            } else {
                                bw.Write(complex.Real);
                                bw.Write(complex.Imaginary);
                            }
                            break;
                        default: throw new InvalidOperationException(); // should never happen
                    }
                }
            }

            private static void WriteBinaryChar(BinaryWriter bw, char c) {
                bw.Write((ushort)c);
            }

            internal byte[] ToByteArray() {
                if (_data is ArrayData<byte> data) {
                    Debug.Assert(_typeCode == 'B');
                    var res = new byte[data.Count];
                    data.CopyTo(res, 0);
                    return res;
                }

                return ToStream().ToArray();
            }

            internal void Clear() {
                _data.Clear();
            }

            internal void FromStream(Stream ms) {
                BinaryReader br = new BinaryReader(ms);

                if (_data is ArrayData<byte> data) {
                    Debug.Assert(_typeCode == 'B');
                    var length = (int)ms.Length;
                    data.AddRange(br.ReadBytes(length));
                    return;
                }

                for (int i = 0; i < ms.Length / itemsize; i++) {
                    switch (_typeCode) {
                        case 'b': ((ArrayData<sbyte>)_data).Add((sbyte)br.ReadByte()); break;
                        case 'B': ((ArrayData<byte>)_data).Add(br.ReadByte()); break;
                        case 'u':
                        case 'w': ((ArrayData<char>)_data).Add(ReadBinaryChar(br)); break;
                        case 'h': ((ArrayData<short>)_data).Add(br.ReadInt16()); break;
                        case 'H': ((ArrayData<ushort>)_data).Add(br.ReadUInt16()); break;
                        case 'i': ((ArrayData<int>)_data).Add(br.ReadInt32()); break;
                        case 'I': ((ArrayData<uint>)_data).Add(br.ReadUInt32()); break;
                        case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                        case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                        case 'q': ((ArrayData<long>)_data).Add(br.ReadInt64()); break;
                        case 'Q': ((ArrayData<ulong>)_data).Add(br.ReadUInt64()); break;
                        case 'f': ((ArrayData<float>)_data).Add(br.ReadSingle()); break;
                        case 'd': ((ArrayData<double>)_data).Add(br.ReadDouble()); break;
                        case 'e': ((ArrayData<Half>)_data).Add(BitConverter.UInt16BitsToHalf(br.ReadUInt16())); break;
                        case 'Z':
                            if (_complexElementCode == 'f') {
                                ((ArrayData<Complex>)_data).Add(new Complex(br.ReadSingle(), br.ReadSingle()));
                            } else {
                                ((ArrayData<Complex>)_data).Add(new Complex(br.ReadDouble(), br.ReadDouble()));
                            }
                            break;
                        default: throw new InvalidOperationException(); // should never happen
                    }
                }
            }

            // a version of FromStream that overwrites starting at 'index'
            internal void FromStream(Stream ms, int index) {
                BinaryReader br = new BinaryReader(ms);

                for (int i = index; i < ms.Length / itemsize + index; i++) {
                    switch (_typeCode) {
                        case 'b': ((ArrayData<sbyte>)_data)[i] = (sbyte)br.ReadByte(); break;
                        case 'B': ((ArrayData<byte>)_data)[i] = br.ReadByte(); break;
                        case 'u':
                        case 'w': ((ArrayData<char>)_data)[i] = ReadBinaryChar(br); break;
                        case 'h': ((ArrayData<short>)_data)[i] = br.ReadInt16(); break;
                        case 'H': ((ArrayData<ushort>)_data)[i] = br.ReadUInt16(); break;
                        case 'i': ((ArrayData<int>)_data)[i] = br.ReadInt32(); break;
                        case 'I': ((ArrayData<uint>)_data)[i] = br.ReadUInt32(); break;
                        case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                        case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                        case 'q': ((ArrayData<long>)_data)[i] = br.ReadInt64(); break;
                        case 'Q': ((ArrayData<ulong>)_data)[i] = br.ReadUInt64(); break;
                        case 'f': ((ArrayData<float>)_data)[i] = br.ReadSingle(); break;
                        case 'd': ((ArrayData<double>)_data)[i] = br.ReadDouble(); break;
                        case 'e': ((ArrayData<Half>)_data)[i] = BitConverter.UInt16BitsToHalf(br.ReadUInt16()); break;
                        case 'Z':
                            ((ArrayData<Complex>)_data)[i] = _complexElementCode == 'f'
                                ? new Complex(br.ReadSingle(), br.ReadSingle())
                                : new Complex(br.ReadDouble(), br.ReadDouble());
                            break;
                        default: throw new InvalidOperationException(); // should never happen
                    }
                }
            }

            // a version of FromStream that overwrites up to 'nbytes' bytes, starting at 'index' 
            // Returns the number of bytes written.
            internal long FromStream(Stream ms, int index, int nbytes) {
                BinaryReader br = new BinaryReader(ms);

                if (nbytes <= 0) {
                    return 0;
                }

                int len = Math.Min((int)(ms.Length - ms.Position), nbytes);
                for (int i = index; i < len / itemsize + index; i++) {
                    switch (_typeCode) {
                        case 'b': ((ArrayData<sbyte>)_data)[i] = (sbyte)br.ReadByte(); break;
                        case 'B': ((ArrayData<byte>)_data)[i] = br.ReadByte(); break;
                        case 'u':
                        case 'w': ((ArrayData<char>)_data)[i] = ReadBinaryChar(br); break;
                        case 'h': ((ArrayData<short>)_data)[i] = br.ReadInt16(); break;
                        case 'H': ((ArrayData<ushort>)_data)[i] = br.ReadUInt16(); break;
                        case 'i': ((ArrayData<int>)_data)[i] = br.ReadInt32(); break;
                        case 'I': ((ArrayData<uint>)_data)[i] = br.ReadUInt32(); break;
                        case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                        case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                        case 'q': ((ArrayData<long>)_data)[i] = br.ReadInt64(); break;
                        case 'Q': ((ArrayData<ulong>)_data)[i] = br.ReadUInt64(); break;
                        case 'f': ((ArrayData<float>)_data)[i] = br.ReadSingle(); break;
                        case 'd': ((ArrayData<double>)_data)[i] = br.ReadDouble(); break;
                        case 'e': ((ArrayData<Half>)_data)[i] = BitConverter.UInt16BitsToHalf(br.ReadUInt16()); break;
                        case 'Z':
                            ((ArrayData<Complex>)_data)[i] = _complexElementCode == 'f'
                                ? new Complex(br.ReadSingle(), br.ReadSingle())
                                : new Complex(br.ReadDouble(), br.ReadDouble());
                            break;
                        default: throw new InvalidOperationException(); // should never happen
                    }
                }

                if (len % itemsize > 0) {
                    // we have some extra bytes that we need to do a partial read on.                  
                    byte[] curBytes = ToBytes(len / itemsize + index);
                    for (int i = 0; i < len % itemsize; i++) {
                        curBytes[i] = br.ReadByte();
                    }

                    _data[len / itemsize + index] = FromBytes(curBytes);
                }

                return len;
            }

            // br.ReadChar() doesn't read 16-bit chars, it reads 8-bit chars.
            private static char ReadBinaryChar(BinaryReader br) {
                byte byteVal = br.ReadByte();
                return (char)((br.ReadByte() << 8) | byteVal);
            }

            private byte[] ToBytes(int index) {
                switch (_typeCode) {
                    case 'b': return new[] { (byte)((ArrayData<sbyte>)_data)[index] };
                    case 'B': return new[] { ((ArrayData<byte>)_data)[index] };
                    case 'u':
                    case 'w': return BitConverter.GetBytes(((ArrayData<char>)_data)[index]);
                    case 'h': return BitConverter.GetBytes(((ArrayData<short>)_data)[index]);
                    case 'H': return BitConverter.GetBytes(((ArrayData<ushort>)_data)[index]);
                    case 'i': return BitConverter.GetBytes(((ArrayData<int>)_data)[index]);
                    case 'I': return BitConverter.GetBytes(((ArrayData<uint>)_data)[index]);
                    case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                    case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                    case 'q': return BitConverter.GetBytes(((ArrayData<long>)_data)[index]);
                    case 'Q': return BitConverter.GetBytes(((ArrayData<ulong>)_data)[index]);
                    case 'f': return BitConverter.GetBytes(((ArrayData<float>)_data)[index]);
                    case 'd': return BitConverter.GetBytes(((ArrayData<double>)_data)[index]);
                    case 'e': return BitConverter.GetBytes(BitConverter.HalfToUInt16Bits(((ArrayData<Half>)_data)[index]));
                    case 'Z': {
                        Complex complex = ((ArrayData<Complex>)_data)[index];
                        return _complexElementCode == 'f'
                            ? BitConverter.GetBytes((float)complex.Real)
                                .Concat(BitConverter.GetBytes((float)complex.Imaginary)).ToArray()
                            : BitConverter.GetBytes(complex.Real)
                                .Concat(BitConverter.GetBytes(complex.Imaginary)).ToArray();
                    }
                    default: throw new InvalidOperationException(); // should never happen
                }
            }

            private object FromBytes(byte[] bytes) {
                switch (_typeCode) {
                    case 'b': return (sbyte)bytes[0];
                    case 'B': return bytes[0];
                    case 'u':
                    case 'w': return BitConverter.ToChar(bytes, 0);
                    case 'h': return BitConverter.ToInt16(bytes, 0);
                    case 'H': return BitConverter.ToUInt16(bytes, 0);
                    case 'i': return BitConverter.ToInt32(bytes, 0);
                    case 'I': return BitConverter.ToUInt32(bytes, 0);
                    case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                    case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                    case 'q': return BitConverter.ToInt64(bytes, 0);
                    case 'f': return BitConverter.ToSingle(bytes, 0);
                    case 'd': return BitConverter.ToDouble(bytes, 0);
                    case 'Q': return BitConverter.ToUInt64(bytes, 0);
                    case 'e': return BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, 0));
                    case 'Z':
                        return _complexElementCode == 'f'
                            ? new Complex(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4))
                            : new Complex(BitConverter.ToDouble(bytes, 0), BitConverter.ToDouble(bytes, 8));
                    default: throw new InvalidOperationException(); // should never happen
                }
            }

            #region IStructuralEquatable Members

            public const object __hash__ = null;

            int IStructuralEquatable.GetHashCode(IEqualityComparer comparer) {
                IStructuralEquatable dataTuple;

                switch (_typeCode) {
                    case 'b': dataTuple = PythonTuple.MakeTuple(((ArrayData<sbyte>)_data).Data); break;
                    case 'B': dataTuple = PythonTuple.MakeTuple(((ArrayData<byte>)_data).Data); break;
                    case 'u':
                    case 'w': dataTuple = PythonTuple.MakeTuple(((ArrayData<char>)_data).Data); break;
                    case 'h': dataTuple = PythonTuple.MakeTuple(((ArrayData<short>)_data).Data); break;
                    case 'H': dataTuple = PythonTuple.MakeTuple(((ArrayData<ushort>)_data).Data); break;
                    case 'i': dataTuple = PythonTuple.MakeTuple(((ArrayData<int>)_data).Data); break;
                    case 'I': dataTuple = PythonTuple.MakeTuple(((ArrayData<uint>)_data).Data); break;
                    case 'l': if (TypecodeOps.IsCLong32Bit) goto case 'i'; else goto case 'q';
                    case 'L': if (TypecodeOps.IsCLong32Bit) goto case 'I'; else goto case 'Q';
                    case 'q': dataTuple = PythonTuple.MakeTuple(((ArrayData<long>)_data).Data); break;
                    case 'Q': dataTuple = PythonTuple.MakeTuple(((ArrayData<ulong>)_data).Data); break;
                    case 'f': dataTuple = PythonTuple.MakeTuple(((ArrayData<float>)_data).Data); break;
                    case 'd': dataTuple = PythonTuple.MakeTuple(((ArrayData<double>)_data).Data); break;
                    case 'e': dataTuple = PythonTuple.MakeTuple(((ArrayData<Half>)_data).Data); break;
                    case 'Z': dataTuple = PythonTuple.MakeTuple(((ArrayData<Complex>)_data).Data); break;
                    default: throw new InvalidOperationException(); // should never happen
                }

                return dataTuple.GetHashCode(comparer);
            }

            bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer) {
                if (!(other is array pa)) return false;

                if (_data.Count != pa._data.Count) return false;

                for (int i = 0; i < _data.Count; i++) {
                    if (!comparer.Equals(this[i], pa[i])) {
                        return false;
                    }
                }

                return true;
            }

            #endregion

            #region IEnumerable Members

            IEnumerator IEnumerable.GetEnumerator() => new arrayiterator(this);

            #endregion

            #region ICodeFormattable Members

            public virtual string/*!*/ __repr__(CodeContext/*!*/ context) {
                string res = PythonOps.GetPythonTypeName(this) + "('" + typecode.ToString() + "'";
                if (_data.Count == 0) {
                    return res + ")";
                }

                StringBuilder sb = new StringBuilder(res);
                if (_typeCode == 'u') {
                    char quote = '\'';
                    string s = new string(((ArrayData<char>)_data).Data, 0, _data.Count);
                    if (s.Contains('\'') && !s.Contains('\"')) {
                        quote = '\"';
                    }

                    sb.Append(", ");
                    sb.Append(quote);

                    sb.Append(StringOps.ReprEncode(s, quote));
                    sb.Append(quote);
                    sb.Append(")");
                } else {
                    sb.Append(", [");
                    for (int i = 0; i < _data.Count; i++) {
                        if (i > 0) {
                            sb.Append(", ");
                        }

                        sb.Append(PythonOps.Repr(context, this[i]));
                    }
                    sb.Append("])");
                }

                return sb.ToString();
            }

            #endregion

            #region IWeakReferenceable Members

            WeakRefTracker? IWeakReferenceable.GetWeakRef() {
                return _tracker;
            }

            bool IWeakReferenceable.SetWeakRef(WeakRefTracker value) {
                _tracker = value;
                return true;
            }

            void IWeakReferenceable.SetFinalizer(WeakRefTracker value) {
                _tracker = value;
            }

            #endregion

            #region IPythonContainer Members

            public int __len__() {
                return _data.Count;
            }

            public bool __contains__(object? value) {
                return _data.Contains(value);
            }

            #endregion

            #region IRichComparable Members

            public static object? operator >([NotNone] array self, [NotNone] array other)
                => PythonOps.ArraysGreaterThan(DefaultContext.Default, self!, other!);

            public static object? operator <([NotNone] array self, [NotNone] array other)
                => PythonOps.ArraysLessThan(DefaultContext.Default, self!, other!);

            public static object? operator >=([NotNone] array self, [NotNone] array other)
                => PythonOps.ArraysGreaterThanOrEqual(DefaultContext.Default, self!, other!);

            public static object? operator <=([NotNone] array self, [NotNone] array other)
                => PythonOps.ArraysLessThanOrEqual(DefaultContext.Default, self!, other!);

            #endregion

            #region ICollection Members

            void ICollection.CopyTo(Array array, int index) {
                throw new NotImplementedException();
            }

            int ICollection.Count {
                get { return __len__(); }
            }

            bool ICollection.IsSynchronized {
                get { return false; }
            }

            object ICollection.SyncRoot {
                get { return this; }
            }

            #endregion

            #region IList<object> Members

            int IList<object>.IndexOf(object item) {
                return _data.IndexOf(item);
            }

            void IList<object>.Insert(int index, object item) {
                insert(index, item);
            }

            void IList<object>.RemoveAt(int index) {
                __delitem__(index);
            }

            #endregion

            #region ICollection<object> Members

            void ICollection<object>.Add(object item) {
                append(item);
            }

            void ICollection<object>.Clear() {
                __delitem__(new Slice(null, null));
            }

            bool ICollection<object>.Contains(object item) {
                return __contains__(item);
            }

            void ICollection<object>.CopyTo(object[] array, int arrayIndex) {
                throw new NotImplementedException();
            }

            int ICollection<object>.Count {
                get { return __len__(); }
            }

            bool ICollection<object>.IsReadOnly {
                get { return false; }
            }

            bool ICollection<object>.Remove(object item) {
                try {
                    remove(item);
                    return true;
                } catch (ArgumentException) {
                    return false;
                }
            }

            #endregion

            #region IEnumerable<object> Members

            IEnumerator<object> IEnumerable<object>.GetEnumerator() => new arrayiterator(this);

            #endregion

            #region IBufferProtocol Members

            IPythonBuffer IBufferProtocol.GetBuffer(BufferFlags flags, bool throwOnError) {
                return _data.GetBuffer(this, typecode, flags);
            }

            #endregion
        }

        [PythonType]
        public sealed class arrayiterator : IEnumerator<object> {
            private int _index;
            private readonly IList<object> _array;
            private bool _iterating;

            internal arrayiterator(array a) {
                _array = a;
                Reset();
            }

            [PythonHidden]
            public object Current => _array[_index];

            public object __iter__() => this;

            public object __reduce__(CodeContext context) {
                object? iter;
                context.TryLookupBuiltin("iter", out iter);
                if (_iterating) {
                    return PythonTuple.MakeTuple(iter, PythonTuple.MakeTuple(_array), _index + 1);
                }
                return PythonTuple.MakeTuple(iter, PythonTuple.MakeTuple(PythonTuple.EMPTY));
            }

            public void __setstate__(int state) {
                _index = state - 1;
                _iterating = _index < _array.Count;
            }

            void IDisposable.Dispose() { }

            [PythonHidden]
            public bool MoveNext() {
                if (_iterating) {
                    _index++;
                    _iterating = (_index < _array.Count);
                }
                return _iterating;
            }

            [PythonHidden]
            public void Reset() {
                _index = -1;
                _iterating = true;
            }
        }
    }
}
