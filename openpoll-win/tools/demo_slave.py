"""Demo Modbus slave for trying MultiPoll Studio (needs pymodbus 3.8.x: pip install 'pymodbus>=3.8,<3.9').

Serves the same register map on four ports of the given host:
  PORT      Modbus TCP
  PORT + 1  Modbus RTU framing over TCP
  PORT + 2  Modbus UDP
  PORT + 3  Modbus ASCII framing over TCP
Holding registers 0..3 = 100, 65535 (-1), 0x3FC0 0x0000 (float 1.5). Input registers 0..1 change every 200 ms.
Discrete inputs alternate 1,0,1,0...

    python demo_slave.py [--host 0.0.0.0] [--port 5020]
"""
import argparse
import asyncio
import math

from pymodbus import FramerType
from pymodbus.datastore import ModbusSequentialDataBlock, ModbusServerContext, ModbusSlaveContext
from pymodbus.server import ModbusTcpServer, ModbusUdpServer


def build_store() -> ModbusSlaveContext:
    store = ModbusSlaveContext(co=ModbusSequentialDataBlock(0, [0] * 201), di=ModbusSequentialDataBlock(0, [1, 0] * 101),
                               hr=ModbusSequentialDataBlock(0, [0] * 201), ir=ModbusSequentialDataBlock(0, [0] * 201))
    store.setValues(3, 0, [100, 65535, 0x3FC0, 0x0000])
    return store


async def tick(store: ModbusSlaveContext) -> None:
    t = 0
    while True:
        store.setValues(4, 0, [t & 0xFFFF, int(1000 + 1000 * math.sin(t / 10)) & 0xFFFF])
        t += 1
        await asyncio.sleep(0.2)


async def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=5020)
    a = ap.parse_args()
    store = build_store()
    ctx = ModbusServerContext(slaves={1: store}, single=False)
    tcp = ModbusTcpServer(ctx, framer=FramerType.SOCKET, address=(a.host, a.port))
    rtu = ModbusTcpServer(ctx, framer=FramerType.RTU, address=(a.host, a.port + 1))
    udp = ModbusUdpServer(ctx, framer=FramerType.SOCKET, address=(a.host, a.port + 2))
    asc = ModbusTcpServer(ctx, framer=FramerType.ASCII, address=(a.host, a.port + 3))
    asyncio.create_task(tick(store))
    for server in (tcp, rtu, udp, asc):
        await server.serve_forever(background=True)
    print(f"demo slave: Modbus TCP {a.host}:{a.port}, RTU-over-TCP :{a.port + 1}, UDP :{a.port + 2}, "
          f"ASCII-over-TCP :{a.port + 3}, unit 1", flush=True)
    await asyncio.Event().wait()


if __name__ == "__main__":
    asyncio.run(main())
