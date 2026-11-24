// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

using IronPython.Runtime;

[assembly: PythonModule("_sysconfig", typeof(IronPython.Modules.PythonSysConfig))]
namespace IronPython.Modules {
    public static class PythonSysConfig {
        public static PythonDictionary config_vars() {
            bool posix = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            return new PythonDictionary {
                ["EXT_SUFFIX"] = posix ? ".so" : ".pyd",
                ["SOABI"] = posix ? "ironpython-3.15" : string.Empty,
                ["ABIFLAGS"] = string.Empty,
                ["Py_DEBUG"] = false,
                ["Py_GIL_DISABLED"] = false,
                ["Py_ENABLE_SHARED"] = false,
                ["LDVERSION"] = "3.15",
                ["VERSION"] = "3.15"
            };
        }
    }
}
