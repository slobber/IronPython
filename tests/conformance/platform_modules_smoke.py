"""Cross-platform smoke coverage for native-backed Python modules."""

import bz2
import ctypes
import hashlib
import os
import socket
import sqlite3
import ssl
import sys
import zlib

from compression import zstd


PAYLOAD = b"CyberStone.IronPython platform smoke\n" * 256


def check_compression():
    assert zlib.decompress(zlib.compress(PAYLOAD)) == PAYLOAD
    assert bz2.decompress(bz2.compress(PAYLOAD)) == PAYLOAD
    assert zstd.decompress(zstd.compress(PAYLOAD)) == PAYLOAD


def check_hashing():
    expected = hashlib.sha256(PAYLOAD).hexdigest()
    assert expected == (
        "a2faf81a6569f2fbb044e2566412e5cff1fe241bbe5b0104abcc234e280611f4"
    )
    assert hashlib.md5(PAYLOAD).hexdigest() == (
        "b1de795ff5c2e6ad6ddf57010fa5d43b"
    )


def check_sockets():
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.bind(("127.0.0.1", 0))
    listener.listen(1)
    host, port = listener.getsockname()

    client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        client.connect((host, port))
        server, _ = listener.accept()
        try:
            client.sendall(b"socket-ok")
            assert server.recv(9) == b"socket-ok"
        finally:
            server.close()
    finally:
        client.close()
        listener.close()


def check_ssl():
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE
    assert isinstance(ssl.OPENSSL_VERSION, str)


def check_ctypes():
    if os.name == "nt":
        process_id = ctypes.windll.kernel32.GetCurrentProcessId()
    else:
        process_id = ctypes.CDLL(None).getpid()
    assert process_id > 0


def check_sqlite():
    connection = sqlite3.connect(":memory:")
    try:
        connection.execute("create table probe(value integer)")
        connection.execute("insert into probe values (42)")
        assert connection.execute("select value from probe").fetchone() == (42,)
        assert sqlite3.sqlite_version_info >= (3, 35, 0)
    finally:
        connection.close()


def main():
    check_compression()
    check_hashing()
    check_sockets()
    check_ssl()
    check_ctypes()
    check_sqlite()
    print(
        "PLATFORM_MODULES_OK",
        sys.platform,
        os.uname().machine if hasattr(os, "uname") else os.environ.get(
            "PROCESSOR_ARCHITECTURE", "unknown"
        ),
        ssl.OPENSSL_VERSION,
        sqlite3.sqlite_version,
    )


if __name__ == "__main__":
    main()
