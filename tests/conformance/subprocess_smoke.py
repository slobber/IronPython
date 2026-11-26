"""Smoke coverage for synchronous and asyncio subprocess execution."""

import asyncio
import os
import subprocess
import sys


def native_posix_spawn():
    if not hasattr(os, "posix_spawn"):
        return

    command = ["/bin/sh", "-c", "exit 0"]
    pid = os.posix_spawn(command[0], command, os.environ)
    assert os.waitpid(pid, 0)[1] == 0

    if hasattr(os, "POSIX_SPAWN_CLOSEFROM"):
        pid = os.posix_spawn(
            command[0],
            command,
            os.environ,
            file_actions=[(os.POSIX_SPAWN_CLOSEFROM, 3)],
        )
        assert os.waitpid(pid, 0)[1] == 0


def synchronous():
    if sys.platform == "win32":
        command = [sys.executable, "-c", "print('subprocess-ok')"]
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
        command = [sys.executable, "-c", "print('subprocess-ok')"]
    else:
        command = ["/bin/sh", "-c", "printf subprocess-ok"]
    process = await asyncio.create_subprocess_exec(
        *command,
        stdout=asyncio.subprocess.PIPE,
    )
    output, _ = await process.communicate()
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
    asyncio.run(asynchronous())
    asyncio.run(event_loop_round_trip())
    print("SUBPROCESS_OK")


if __name__ == "__main__":
    main()
