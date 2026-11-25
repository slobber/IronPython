// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;

using IronPython.Runtime;
using IronPython.Runtime.Operations;

[assembly: PythonModule("_imp", typeof(IronPython.Modules.PythonImport))]
namespace IronPython.Modules {
    public static class PythonImport {
        public const string __doc__ = "Provides functions for programmatically creating and importing modules and packages.";
        public const int pyc_magic_number_token = 0x0A0D0E52; // Python 3.15 magic 3666 plus b"\r\n".
        public const string check_hash_based_pycs = "never";

        internal const int PythonSource = 1;
        internal const int PythonCompiled = 2;
        internal const int CExtension = 3;
        internal const int PythonResource = 4;
        internal const int PackageDirectory = 5;
        internal const int CBuiltin = 6;
        internal const int PythonFrozen = 7;
        internal const int PythonCodeResource = 8;
        internal const int SearchError = 0;
        internal const int ImporterHook = 9;

        public static PythonList extension_suffixes() {
            // TODO: support extensions?
            return new PythonList();
        }

        public static bool lock_held(CodeContext/*!*/ context) {
            // held by any thread
            Lock importLock = context.LanguageContext.ImportLock;
            if (importLock.IsHeldByCurrentThread) return true; // the current thread already holds the lock
            if (!importLock.TryEnter()) return true; // try an exclusive lock, another thread might already hold the lock
            importLock.Exit();
            return false;
        }

        public static void acquire_lock(CodeContext/*!*/ context) {
            context.LanguageContext.ImportLock.Enter();
        }

        public static void release_lock(CodeContext/*!*/ context) {
            Lock importLock = context.LanguageContext.ImportLock;
            if (!importLock.IsHeldByCurrentThread) {
                throw PythonOps.RuntimeError("not holding the import lock");
            }
            importLock.Exit();
        }

        public static object init_builtin(CodeContext/*!*/ context, [NotNone] string/*!*/ name) {
            if (name == null) throw PythonOps.TypeError("init_builtin() argument 1 must be string, not None");

            PythonContext pc = context.LanguageContext;

            // check if the module is already in sys.modules and reload it if it is
            if (pc.SystemStateModules.TryGetValue(name, out var moduleObj) && moduleObj is PythonModule module && module.GetName() == name && module.IsBuiltin) {
                Debug.Assert(module.__dict__._storage is ModuleDictionaryStorage);
                ((ModuleDictionaryStorage)module.__dict__._storage).Reload();
                return module;
            }

            return Importer.ImportBuiltin(context, name);
        }

        // new in Python 3.5
        public static object create_builtin(CodeContext/*!*/ context, object spec) {
            // TODO: implement this properly - https://github.com/IronLanguages/ironpython3/issues/1430
            var name = PythonOps.GetBoundAttr(context, spec, "name") as string;
            return init_builtin(context, name);
        }

        // new in Python 3.5
        public static void exec_builtin(CodeContext/*!*/ context, object mod) {
            // TODO: implement this properly - https://github.com/IronLanguages/ironpython3/issues/1430
        }

        public static Bytes source_hash(CodeContext/*!*/ context, long key, object source) {
            if (source is not IBufferProtocol bufferProtocol) {
                throw PythonOps.TypeError("expected a bytes-like object, got '{0}'", PythonOps.GetPythonTypeName(source));
            }

            using IPythonBuffer buffer = bufferProtocol.GetBuffer();
            ulong hash = SipHash13(unchecked((ulong)key), 0, buffer.AsReadOnlySpan());
            byte[] result = new byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(result, hash);
            return Bytes.Make(result);
        }

        public static object init_frozen(string name) {
            return null;
        }

        public static object get_frozen_object(string name) {
            throw PythonOps.ImportError("No such frozen object named {0}", name);
        }

        public static object find_frozen(string name) {
            return null;
        }

        public static int is_builtin(CodeContext/*!*/ context, string/*!*/ name) {
            if (name == null) throw PythonOps.TypeError("is_builtin() argument 1 must be string, not None");
            if (context.LanguageContext.BuiltinModules.TryGetValue(name, out Type ty)) {
                if (ty.Assembly == typeof(PythonContext).Assembly) {
                    // supposedly these can't be re-initialized and return -1 to
                    // indicate that here, but CPython does allow passing them
                    // to init_builtin.
                    return -1;
                }


                return 1;
            }
            return 0;
        }

        public static bool is_frozen(string name) {
            return false;
        }

        public static bool is_frozen_package(string name) {
            return false;
        }

        public static void _override_frozen_modules_for_tests(int @override) {
            // IronPython does not load CPython frozen modules. Keep the CPython
            // test-support API available while treating both override modes as
            // no-ops.
        }

        public static void _set_lazy_attributes(object module, string name) {
            // Lazy values are installed directly by the compiler today. The import
            // system still calls this hook for every loaded module, so it must be
            // safe even when warning handling passes a partially initialized module.
        }

        public static void _fix_co_filename() {
            throw new NotImplementedException();
        }

        private static ulong SipHash13(ulong k0, ulong k1, ReadOnlySpan<byte> source) {
            ulong v0 = k0 ^ 0x736f6d6570736575UL;
            ulong v1 = k1 ^ 0x646f72616e646f6dUL;
            ulong v2 = k0 ^ 0x6c7967656e657261UL;
            ulong v3 = k1 ^ 0x7465646279746573UL;
            int offset = 0;

            while (source.Length - offset >= sizeof(ulong)) {
                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, sizeof(ulong)));
                offset += sizeof(ulong);
                v3 ^= value;
                SipRound(ref v0, ref v1, ref v2, ref v3);
                v0 ^= value;
            }

            ulong tail = (ulong)source.Length << 56;
            for (int i = 0; i < source.Length - offset; i++) {
                tail |= (ulong)source[offset + i] << (i * 8);
            }

            v3 ^= tail;
            SipRound(ref v0, ref v1, ref v2, ref v3);
            v0 ^= tail;
            v2 ^= 0xff;
            SipRound(ref v0, ref v1, ref v2, ref v3);
            SipRound(ref v0, ref v1, ref v2, ref v3);
            SipRound(ref v0, ref v1, ref v2, ref v3);
            return (v0 ^ v1) ^ (v2 ^ v3);
        }

        private static void SipRound(ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3) {
            SipHalfRound(ref v0, ref v1, ref v2, ref v3, 13, 16);
            SipHalfRound(ref v2, ref v1, ref v0, ref v3, 17, 21);
        }

        private static void SipHalfRound(ref ulong a, ref ulong b, ref ulong c, ref ulong d, int s, int t) {
            a += b;
            c += d;
            b = RotateLeft(b, s) ^ a;
            d = RotateLeft(d, t) ^ c;
            a = RotateLeft(a, 32);
        }

        private static ulong RotateLeft(ulong value, int amount) => (value << amount) | (value >> (64 - amount));
    }
}
