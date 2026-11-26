// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Scripting;
using Microsoft.Scripting.Runtime;

namespace IronPython.Runtime {
    /// <summary>
    /// Adapts an IDictionary[object, object] for use as a PythonDictionary used for
    /// our debug frames.  Also hides the special locals which start with $.
    /// </summary>
    [Serializable]
    internal class DebuggerDictionaryStorage : DictionaryStorage {
        private IDictionary<object, object> _data;
        private readonly CommonDictionaryStorage _hidden;

        public DebuggerDictionaryStorage(IDictionary<object, object> data) {
            Debug.Assert(data != null);

            _hidden = new CommonDictionaryStorage();
            foreach (var key in data.Keys) {
                if (key is string strKey && strKey.Length > 0 && strKey[0] == '$') {
                    _hidden.Add(strKey, null);
                }
            }

            _data = data;
        }

        public override void Add(ref DictionaryStorage storage, object key, object value) {
            AddNoLock(ref storage, key, value);
        }

        public override void AddNoLock(ref DictionaryStorage storage, object key, object value) {
            _hidden.Remove(key);

            if (_data.TryGetValue(key, out object current)
                && current is ClosureCell cell) {
                cell.Value = value;
            } else {
                _data[key] = value;
            }
        }

        public override bool Contains(object key) {
            if (_hidden.Contains(key)) {
                return false;
            }

            if (!_data.TryGetValue(key, out object value)) {
                return false;
            }
            return value is not ClosureCell cell
                || cell.Value != Uninitialized.Instance;
        }

        public override bool Remove(ref DictionaryStorage storage, object key) {
            if (_hidden.Contains(key)) {
                return false;
            }

            return _data.Remove(key);
        }

        public override DictionaryStorage AsMutable(ref DictionaryStorage storage) => this;

        public override bool TryGetValue(object key, out object value) {
            if (_hidden.Contains(key)) {
                value = null;
                return false;
            }

            if (!_data.TryGetValue(key, out value)) {
                return false;
            }
            if (value is ClosureCell cell) {
                value = cell.Value;
                if (value == Uninitialized.Instance) {
                    value = null;
                    return false;
                }
            }
            return true;
        }

        public override int Count {
            get {
                return _data.Count - _hidden.Count;
            }
        }

        public override void Clear(ref DictionaryStorage storage) {
            _data = new Dictionary<object, object>();
            _hidden.Clear();
        }

        public override List<KeyValuePair<object, object>> GetItems() {
            List<KeyValuePair<object, object>> res = new List<KeyValuePair<object, object>>(Count);
            foreach (var kvp in _data) {
                if (!_hidden.Contains(kvp.Key)) {
                    object value = kvp.Value;
                    if (value is ClosureCell cell) {
                        value = cell.Value;
                        if (value == Uninitialized.Instance) {
                            continue;
                        }
                    }
                    res.Add(new KeyValuePair<object, object>(
                        kvp.Key,
                        value));
                }
            }
            return res;
        }

        public override bool HasNonStringAttributes() {
            return true;
        }
    }
}
