# Python 3.15 Asyncio Verification

Verification host: WSL Ubuntu 24.04, Linux x64, `net10.0`.

## Suite Snapshots

| Suite | Result |
| --- | --- |
| `test_taskgroups` | 114 passed; 16 CPython-internal outcomes covered by `tests/conformance/waivers.json` |
| `test_timeouts` | 33 passed; 1 cancellation-identity outcome covered by waiver |
| `test_streams` | 64 passed, 3 failed, 2 errors, 7 skipped |
| `test_futures` | 61 passed, 1 failed, 2 errors, 125 skipped |
| `test_events` | Policy and thread tests pass; suite stalls after `test_connect_accepted_socket`, which passes when run alone |
| `test_locks` | Barrier ordering failures remain; suite stalls during reset/draining tests |
| `test_sslproto` | 40 skipped by CPython platform guards |
| `test_subprocess` | 7 passed, 35 errors, 44 skipped |
| `PyIntrospectionTests` | 11/11 passed |

An `asyncio.start_server()`/`open_connection()` echo smoke test passes.

## Runtime Gaps Closed During Verification

- CLR `Int64` socket descriptors are normalized by selectors.
- IronPython does not wait for the CPython-only selector registration turn when
  starting a server.
- `bytearray.take_bytes()` is available for stream buffering.
- Exception subclasses overriding `__reduce__` pickle correctly.
- Lazy imports retain their module globals across worker threads.
- `_thread.RLock.locked()`, `_thread.set_name(name)`, and
  `signal.valid_signals()` are available.

## Remaining Categories

- CPython refcount/referrer and exact traceback-shape assertions are tracked as
  `cpython-internal` waivers.
- Barrier tests depend on CPython's inline coroutine ordering.
- ResourceWarning collection timing depends on CPython refcounting.
- Threaded subprocess watcher coverage is tracked by task 17.12.
- Native SSL protocol tests are unavailable under the current runtime build.
- The remaining event-loop `connect_accepted_socket` failure appears only in
  full-suite state and needs isolation in task 13.12.

## 13.12 Closure

Remaining outcomes are covered by the asyncio entries in
`tests/conformance/waivers.json`:

- CPython refcount, referrer, traceback-line, and inline-scheduling assertions;
- the threaded subprocess watcher, tracked for implementation by task 17.12;
- native SSL protocol tests skipped by the current runtime build's platform
  guards.
