using IronPython.Compiler;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
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

        [Test]
        public void AsyncComprehensionsAndGeneratorExpressionsAreSupported() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            ScriptScope scope = engine.CreateScope();
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

async def source():
    yield 1
    yield 2
    yield 3

async def list_values():
    return [item async for item in source()]

async def set_values():
    return sorted({item * 2 async for item in source()})

async def dict_values():
    return {item: item * 10 async for item in source()}

async def generator_values():
    items = (item + 10 async for item in source())
    return [item async for item in items]

async def nested_values():
    return [[item async for item in source()] for prefix in range(2)]

list_result = run_coro(list_values())
set_result = run_coro(set_values())
dict_result = run_coro(dict_values())
generator_result = run_coro(generator_values())
nested_result = run_coro(nested_values())
", scope);
            } finally {
            }

            Assert.That(
                scope.GetVariable<IronPython.Runtime.PythonList>("list_result")
                    .Select(item => (int)item),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(
                scope.GetVariable<IronPython.Runtime.PythonList>("set_result")
                    .Select(item => (int)item),
                Is.EqualTo(new[] { 2, 4, 6 }));
            var dictResult =
                scope.GetVariable<IronPython.Runtime.PythonDictionary>("dict_result");
            Assert.That(
                dictResult.ContainsKey(1) && dictResult.ContainsKey(2)
                    && dictResult.ContainsKey(3),
                Is.True);
            Assert.That(
                scope.GetVariable<IronPython.Runtime.PythonList>("generator_result")
                    .Select(item => (int)item),
                Is.EqualTo(new[] { 11, 12, 13 }));
            Assert.That(
                scope.GetVariable<IronPython.Runtime.PythonList>("nested_result")
                    .Select(row => ((IronPython.Runtime.PythonList)row).Select(item => (int)item).ToArray()),
                Is.EqualTo(new[] {
                    new[] { 1, 2, 3 },
                    new[] { 1, 2, 3 }
                }));
        }

        [Test]
        public void AsyncWithUsesFullContextManagerProtocol() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            ScriptScope scope = engine.CreateScope();
            try {
                engine.Execute(@"
import asyncio

log = []

class SuppressingContext:
    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc, traceback):
        log.append('%s:%s:%s' % (
            exc_type.__name__,
            str(exc),
            traceback is not None,
        ))
        return True

class BrokenEnter:
    async def __aenter__(self):
        raise RuntimeError('enter failed')

    async def __aexit__(self, exc_type, exc, traceback):
        log.append('broken-exit')

async def values():
    yield 1

async def main():
    async with SuppressingContext():
        raise ValueError('body failed')

    try:
        async with BrokenEnter():
            pass
    except RuntimeError as exc:
        log.append(str(exc))

    iterator = values()
    first = await anext(iterator)
    default = await anext(iterator, 42)
    log.append('%s:%s' % (first, default))

asyncio.run(main())
", scope);
            } finally {
            }

            var log = scope.GetVariable<IronPython.Runtime.PythonList>("log");
            Assert.That(
                log.Select(item => item?.ToString()),
                Is.EqualTo(new[] {
                    "ValueError:body failed:True",
                    "enter failed",
                    "1:42"
                }));
        }

        [Test]
        public void AwaitInFinallyResumesBeforePropagatingHandledException() {
            var options = new Dictionary<string, object> {
                ["NoSite"] = true
            };
            ScriptEngine engine = Python.CreateEngine(options);
            ScriptScope scope = engine.CreateScope();
            try {
                engine.Execute(@"
import asyncio

events = []

async def main():
    try:
        try:
            raise ValueError('body')
        finally:
            events.append('before')
            await asyncio.sleep(0)
            events.append('after')
    except ValueError as exc:
        events.append('caught:' + str(exc))

asyncio.run(main())
", scope);
            } finally {
            }

            var events = scope.GetVariable<IronPython.Runtime.PythonList>("events");
            Assert.That(
                events.Select(item => item?.ToString()),
                Is.EqualTo(new[] {
                    "before",
                    "after",
                    "caught:body"
                }));
        }

    }
}
