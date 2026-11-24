using IronPython.Compiler;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using NUnit.Framework;
using System.Collections.Generic;
using Assert = NUnit.Framework.Assert;

namespace IronPython.Tests {
    [TestFixture]
    [NonParallelizable]
    public sealed class CoroutineSemanticsTest {
        [Test]
        public void SendThrowAndCloseFollowCpythonSemantics() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            ScriptScope scope = engine.CreateScope();
            bool useGeneratedParser = Parser.UseGeneratedParser;
            if (string.Equals(
                System.Environment.GetEnvironmentVariable("CYBERSTONE_IRONPYTHON_GENERATED_PARSER"),
                "1",
                System.StringComparison.Ordinal)) {
                Parser.UseGeneratedParser = true;
            }
            try {
                engine.Execute(@"
import clr

log = []

async def lazy():
    log.append('started')
    return 42

coro = lazy()
lazy_initial = list(log)
try:
    coro.send(None)
except StopIteration as exc:
    sent_result = exc.value

async def throw_body():
    log.append('throw-body-ran')

throw_coro = throw_body()
try:
    throw_coro.throw(ValueError('boom'))
except ValueError as exc:
    throw_message = str(exc)

close_coro = lazy()
close_coro.close()
close_result = 'closed'
", scope);
            } finally {
                Parser.UseGeneratedParser = useGeneratedParser;
            }

            Assert.That(scope.GetVariable<IronPython.Runtime.PythonList>("lazy_initial"), Is.Empty);
            Assert.That(scope.GetVariable<int>("sent_result"), Is.EqualTo(42));
            Assert.That(scope.GetVariable<string>("throw_message"), Is.EqualTo("boom"));
            Assert.That(scope.GetVariable<string>("close_result"), Is.EqualTo("closed"));
        }

        [Test]
        public void AsyncGeneratorsSupportAsendAthrowAndAclose() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            ScriptScope scope = engine.CreateScope();
            bool useGeneratedParser = Parser.UseGeneratedParser;
            if (string.Equals(
                System.Environment.GetEnvironmentVariable("CYBERSTONE_IRONPYTHON_GENERATED_PARSER"),
                "1",
                System.StringComparison.Ordinal)) {
                Parser.UseGeneratedParser = true;
            }
            try {
                engine.Execute(@"
def run_coro(coro):
    value = None
    while True:
        try:
            task = coro.send(value)
            task.Wait()
            value = None
        except StopIteration as exc:
            return exc.value

async def asend_body():
    got1 = yield 1
    got2 = yield ('after1', got1)
    yield ('after2', got2)

async def drive_asend():
    gen = asend_body()
    out = []
    out.append(await gen.asend(None))
    out.append(await gen.asend('A'))
    out.append(await gen.asend('B'))
    try:
        await gen.asend('C')
    except StopAsyncIteration:
        out.append('stop')
    return out

async def athrow_body():
    try:
        yield 1
    except ValueError as exc:
        yield ('caught', str(exc))
    yield 3

async def drive_athrow():
    gen = athrow_body()
    out = []
    out.append(await gen.asend(None))
    out.append(await gen.athrow(ValueError('boom')))
    out.append(await gen.asend(None))
    try:
        await gen.asend(None)
    except StopAsyncIteration:
        out.append('stop')
    return out

close_log = []

async def aclose_body():
    try:
        yield 1
        yield 2
    finally:
        close_log.append('cleanup')

async def drive_aclose():
    gen = aclose_body()
    first = await gen.asend(None)
    await gen.aclose()
    return first

asend_result = repr(run_coro(drive_asend()))
athrow_result = repr(run_coro(drive_athrow()))
aclose_result = run_coro(drive_aclose())
aclose_log = list(close_log)
", scope);
            } finally {
                Parser.UseGeneratedParser = useGeneratedParser;
            }

            Assert.That(
                scope.GetVariable<string>("asend_result"),
                Is.EqualTo("[1, ('after1', 'A'), ('after2', 'B'), 'stop']"));
            Assert.That(
                scope.GetVariable<string>("athrow_result"),
                Is.EqualTo("[1, ('caught', 'boom'), 3, 'stop']"));
            Assert.That(scope.GetVariable<int>("aclose_result"), Is.EqualTo(1));
            var acloseLog = scope.GetVariable<IronPython.Runtime.PythonList>("aclose_log");
            Assert.That(acloseLog, Does.Contain("cleanup"));
            Assert.That(acloseLog.Count, Is.EqualTo(1));
        }

    }
}
