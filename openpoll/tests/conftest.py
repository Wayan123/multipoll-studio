"""Shared fixtures: a real pymodbus slave on loopback, over TCP and UDP."""
from __future__ import annotations

import asyncio
import socket
import threading

import pytest
from pymodbus import FramerType
from pymodbus.datastore import ModbusSequentialDataBlock, ModbusServerContext, ModbusSlaveContext
from pymodbus.server import ModbusTcpServer, ModbusUdpServer

SLAVE_ID = 1


def _free_port(kind: int) -> int:
    with socket.socket(socket.AF_INET, kind) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class Slave:
    """Runs a pymodbus 3.8 server in a background event loop.

    ``set``/``get`` go through the same ModbusSlaveContext the server uses, so
    they take Modbus protocol addresses directly.
    """

    def __init__(self, server_cls, framer: FramerType, kind: int, port: int | None = None):
        self.port = port or _free_port(kind)
        self.store = ModbusSlaveContext(
            co=ModbusSequentialDataBlock(0, [0] * 1001),
            di=ModbusSequentialDataBlock(0, [0] * 1001),
            hr=ModbusSequentialDataBlock(0, [0] * 1001),
            ir=ModbusSequentialDataBlock(0, [0] * 1001),
        )
        context = ModbusServerContext(slaves={SLAVE_ID: self.store}, single=False)
        self._loop = asyncio.new_event_loop()
        self._ready = threading.Event()
        self._server_cls, self._framer, self._context = server_cls, framer, context
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()
        assert self._ready.wait(10), "slave did not start"

    def _serve(self):
        asyncio.set_event_loop(self._loop)
        self._loop.run_until_complete(self._main())

    async def _main(self):
        self.server = self._server_cls(self._context, framer=self._framer, address=("127.0.0.1", self.port))
        await self.server.serve_forever(background=True)
        self._stopped = asyncio.Event()
        self._ready.set()
        await self._stopped.wait()

    def set(self, fc: int, address: int, values: list[int]) -> None:
        self.store.setValues(fc, address, values)

    def get(self, fc: int, address: int, count: int) -> list[int]:
        return self.store.getValues(fc, address, count)

    def close(self):
        fut = asyncio.run_coroutine_threadsafe(self.server.shutdown(), self._loop)
        fut.result(10)
        self._loop.call_soon_threadsafe(self._stopped.set)
        self._thread.join(10)


@pytest.fixture
def tcp_slave():
    s = Slave(ModbusTcpServer, FramerType.SOCKET, socket.SOCK_STREAM)
    yield s
    s.close()


@pytest.fixture
def rtu_over_tcp_slave():
    s = Slave(ModbusTcpServer, FramerType.RTU, socket.SOCK_STREAM)
    yield s
    s.close()


@pytest.fixture
def ascii_over_tcp_slave():
    s = Slave(ModbusTcpServer, FramerType.ASCII, socket.SOCK_STREAM)
    yield s
    s.close()


@pytest.fixture
def udp_slave():
    s = Slave(ModbusUdpServer, FramerType.SOCKET, socket.SOCK_DGRAM)
    yield s
    s.close()
