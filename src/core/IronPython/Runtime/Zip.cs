// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

using IronPython.Runtime.Operations;
using IronPython.Runtime.Types;

namespace IronPython.Runtime {

    [PythonType("zip")]
    [Documentation(@"zip(iter1 [,iter2 [...]]) --> zip object

Return a zip object whose .__next__() method returns a tuple where
the i-th element comes from the i-th iterable argument.  The .__next__()
method continues until the shortest iterable in the argument sequence
is exhausted and then it raises StopIteration.")]
    public class Zip : IEnumerator {
        private readonly IEnumerator[] enumerators;
        private readonly bool strict;
        private object? current;

        public Zip(
            CodeContext context,
            [ParamDictionary] IDictionary<object, object> keywordArguments,
            [NotNone] params object[] iters) {
            if (iters == null) throw PythonOps.TypeError("zip argument #{0} must support iteration", 1);
            if (keywordArguments.TryGetValue("strict", out object? rawStrict)) {
                strict = PythonOps.IsTrue(rawStrict);
            }
            foreach (object key in keywordArguments.Keys) {
                if (!Equals(key, "strict")) {
                    throw PythonOps.TypeError("zip() got an unexpected keyword argument '{0}'", key);
                }
            }

            enumerators = new IEnumerator[iters.Length];
            for (var i = 0; i < iters.Length; i++) {
                if (PythonOps.TryGetEnumerator(context, iters[i], out IEnumerator? enumerator))
                    enumerators[i] = enumerator;
                else
                    throw PythonOps.TypeError("zip argument #{0} must support iteration", i + 1);
            }
        }

        [PythonHidden]
        public object Current => current ?? throw new InvalidOperationException();

        [PythonHidden]
        public bool MoveNext() {
            if (enumerators.Length > 0 && enumerators[0].MoveNext()) {
                var res = new object?[enumerators.Length];
                res[0] = enumerators[0].Current;

                for (var i = 1; i < enumerators.Length; i++) {
                    var enumerator = enumerators[i];
                    if (!enumerator.MoveNext()) {
                        if (strict) ThrowLonger(i);
                        current = null;
                        return false;
                    }

                    res[i] = enumerator.Current;
                }

                current = PythonTuple.MakeTuple(res);
                return true;
            }
            if (strict && enumerators.Length > 0) ThrowLonger(0);
            current = null;
            return false;
        }

        private void ThrowLonger(int shortest) {
            for (int i = 0; i < enumerators.Length; i++) {
                if (i != shortest && enumerators[i].MoveNext()) {
                    throw PythonOps.ValueError(
                        "zip() argument {0} is longer than argument {1}",
                        i + 1,
                        shortest + 1);
                }
            }
        }

        [PythonHidden]
        public void Reset() { throw new NotSupportedException(); }

        public PythonTuple __reduce__() {
            return PythonTuple.MakeTuple(
                DynamicHelpers.GetPythonTypeFromType(typeof(Zip)),
                PythonTuple.Make(enumerators)
            );
        }
    }
}
