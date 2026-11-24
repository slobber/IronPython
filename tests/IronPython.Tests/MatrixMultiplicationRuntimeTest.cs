using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting.Hosting;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Assert = NUnit.Framework.Assert;

namespace IronPython.Tests {
    [TestFixture]
    public sealed class MatrixMultiplicationRuntimeTest {
        [Test]
        public void DispatchesMatmulReverseAndInPlaceOperators() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            engine.SetSearchPaths(new[] { GetStdLibPath() });
            ScriptScope scope = engine.CreateScope();
            engine.Execute(@"
class MatrixOperand:
    def __matmul__(self, other):
        return 'matmul'
    def __rmatmul__(self, other):
        return 'rmatmul'
    def __imatmul__(self, other):
        return 'imatmul'

left = MatrixOperand()
forward = left @ 2
reverse = 2 @ left
left @= 2
inplace = left
", scope);

            Assert.That(scope.GetVariable<string>("forward"), Is.EqualTo("matmul"));
            Assert.That(scope.GetVariable<string>("reverse"), Is.EqualTo("rmatmul"));
            Assert.That(scope.GetVariable<string>("inplace"), Is.EqualTo("imatmul"));
        }

        [Test]
        public void OperatorModuleExposesMatmulAndImatmul() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            engine.SetSearchPaths(new[] { GetStdLibPath() });
            ScriptScope scope = engine.CreateScope();
            engine.Execute(@"
import operator
class MatrixOperand:
    def __matmul__(self, other):
        return 'matmul'
    def __imatmul__(self, other):
        return 'imatmul'

forward = operator.matmul(MatrixOperand(), 2)
inplace = operator.imatmul(MatrixOperand(), 2)
", scope);

            Assert.That(scope.GetVariable<string>("forward"), Is.EqualTo("matmul"));
            Assert.That(scope.GetVariable<string>("inplace"), Is.EqualTo("imatmul"));
        }

        [Test]
        public void ReportsCpythonCompatibleUnsupportedOperandErrors() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            ScriptScope scope = engine.CreateScope();
            engine.Execute(@"
try:
    1 @ 2
except Exception as exc:
    matmul_type = type(exc).__name__
    matmul_message = str(exc)

value = 1
try:
    value @= 2
except Exception as exc:
    imatmul_type = type(exc).__name__
    imatmul_message = str(exc)
", scope);

            Assert.That(scope.GetVariable<string>("matmul_type"), Is.EqualTo("TypeError"));
            Assert.That(
                scope.GetVariable<string>("matmul_message"),
                Is.EqualTo("unsupported operand type(s) for @: 'int' and 'int'"));
            Assert.That(scope.GetVariable<string>("imatmul_type"), Is.EqualTo("TypeError"));
            Assert.That(
                scope.GetVariable<string>("imatmul_message"),
                Is.EqualTo("unsupported operand type(s) for @=: 'int' and 'int'"));
        }

        private static string GetStdLibPath() {
            string configured = Environment.GetEnvironmentVariable("CYBERSTONE_IRONPYTHON_STDLIB");
            if (!string.IsNullOrEmpty(configured)) {
                return Path.Combine(configured, "lib");
            }

            string assemblyDirectory = Path.GetDirectoryName(typeof(PythonContext).Assembly.Location);
            string candidate = Path.GetFullPath(
                Path.Combine(assemblyDirectory, "..", "..", "..", "..", "IronPython.StdLib", "lib"));
            Assert.That(Directory.Exists(candidate), Is.True, $"Standard library not found at {candidate}.");
            return candidate;
        }
    }
}
