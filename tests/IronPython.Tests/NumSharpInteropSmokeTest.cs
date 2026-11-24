using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using NUnit.Framework;
using NumSharp;
using System.Collections.Generic;
using Assert = NUnit.Framework.Assert;

namespace IronPython.Tests {
    [TestFixture]
    public sealed class NumSharpInteropSmokeTest {
        [Test]
        public void MatrixProductCanCrossTheClrBoundary() {
            using NDArray left = np.array(new double[,] {
                { 1, 2 },
                { 3, 4 }
            });
            using NDArray right = np.array(new double[,] {
                { 5, 6 },
                { 7, 8 }
            });
            using NDArray product = np.matmul(left, right);

            Assert.That(product.shape[0], Is.EqualTo(2));
            Assert.That(product.shape[1], Is.EqualTo(2));
            Assert.That(product[0, 0].GetValue<double>(), Is.EqualTo(19));
            Assert.That(product[1, 1].GetValue<double>(), Is.EqualTo(50));

            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            ScriptScope scope = engine.CreateScope();
            scope.SetVariable("matrix", product);
            engine.Execute("rows, columns = matrix.shape[0], matrix.shape[1]", scope);

            Assert.That(scope.GetVariable<long>("rows"), Is.EqualTo(2));
            Assert.That(scope.GetVariable<long>("columns"), Is.EqualTo(2));
        }
    }
}
