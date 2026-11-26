"""Smoke coverage for synchronous and asyncio subprocess execution."""

import asyncio
import os
import subprocess
import sys


def native_posix_spawn():
    if sys.platform == "win32" or not hasattr(os, "posix_spawn"):
        return

    command = ["/bin/sh", "-c", "exit 0"]
    env = {
        "HOME": os.environ.get("HOME", "/tmp"),
        "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
    }
    pid = os.posix_spawn(command[0], command, env)
    assert os.waitpid(pid, 0)[1] == 0

    if hasattr(os, "POSIX_SPAWN_CLOSEFROM"):
        pid = os.posix_spawn(
            command[0],
            command,
            env,
            file_actions=[(os.POSIX_SPAWN_CLOSEFROM, 3)],
        )
        assert os.waitpid(pid, 0)[1] == 0
    print("NATIVE_POSIX_SPAWN_OK", flush=True)


def synchronous():
    if sys.platform == "win32":
        command = [sys.executable, "-c", "import sys; sys.stdout.write('subprocess-ok')"]
    else:
        command = ["/bin/sh", "-c", "printf subprocess-ok"]
    result = subprocess.run(
        command,
        check=True,
        capture_output=True,
        text=True,
    )
    assert result.stdout == "subprocess-ok"


async def asynchronous():
    if sys.platform == "win32":
        command = [sys.executable, "-c", "import sys; sys.stdout.write('subprocess-ok')"]
    else:
        command = ["/bin/sh", "-c", "printf subprocess-ok"]
    process = await asyncio.create_subprocess_exec(
        *command,
        stdout=asyncio.subprocess.PIPE,
    )
    print("ASYNC_PROCESS_CREATED", flush=True)
    output, _ = await process.communicate()
    print("ASYNC_PROCESS_COMMUNICATED", flush=True)
    assert output == b"subprocess-ok"
    assert process.returncode == 0


async def event_loop_round_trip():
    received = asyncio.get_running_loop().create_future()

    async def handle(reader, writer):
        data = await reader.readexactly(4)
        received.set_result(data)
        writer.write(data)
        await writer.drain()
        writer.close()
        await writer.wait_closed()

    server = await asyncio.start_server(handle, "127.0.0.1", 0)
    host, port = server.sockets[0].getsockname()[:2]
    reader, writer = await asyncio.open_connection(host, port)
    try:
        writer.write(b"ping")
        await writer.drain()
        assert await reader.readexactly(4) == b"ping"
        assert await received == b"ping"
    finally:
        writer.close()
        await writer.wait_closed()
        server.close()
        await server.wait_closed()


def main():
    native_posix_spawn()
    synchronous()
    print("SYNCHRONOUS_OK", flush=True)
    if sys.platform != "win32":
        spawn = os.posix_spawn

        def traced_spawn(path, args, env, **kwargs):
            print(
                "POSIX_SPAWN_REQUEST",
                path,
                args,
                kwargs,
                flush=True,
            )
            return spawn(path, args, env, **kwargs)

        os.posix_spawn = traced_spawn
    asyncio.run(asynchronous())
    print("ASYNC_SUBPROCESS_OK", flush=True)
    if sys.platform == "win32":
        from asyncio import windows_events
        asyncio.run(
            event_loop_round_trip(),
            loop_factory=windows_events.SelectorEventLoop,
        )
    else:
        asyncio.run(event_loop_round_trip())
    print("SUBPROCESS_OK")


if __name__ == "__main__":
    main()
