"""Deterministic Modbus device simulator for testing MultiPoll Studio device profiles and settings writes.

Standard library only (Python 3.10+). Several simulated devices share one bus and are addressed by their
current slave id; the same bus is reachable on two ports of the given host:
  PORT      Modbus RTU frames (slave, PDU, CRC-16/Modbus) over a TCP stream ("RTU over TCP")
  PORT + 1  Modbus TCP (MBAP header, unit id = slave id)

Devices (initial ids):
  1 ADM-4204V  Volison 4-channel voltage output   FC03/06/16, config block 0x009F-0x00A7
  2 PZEM-016   Peacefair energy meter             FC03/04/06/0x42, also answers general address 0xF8
  3 SDM120     Eastron energy meter               FC03/04/16, float32 registers, even address/count only
  4 BGT-SMPS   Soil 8-in-1 sensor                 FC03/06, also answers broadcast-query address 0xFE
  5 XY-MD02    Temperature/humidity sensor        FC03/04/06/16

Bus rules: unknown id or bad CRC -> no reply; an id matched by two devices -> no reply (bus collision);
unsupported function -> exception 01; address outside the device map -> 02; bad value or quantity -> 03.
A write that changes a slave id is answered from the old id; the new id takes effect after the reply.
Baud and parity writes are stored and reported only; they do not change the TCP transports.

    python device_sim.py [--host 0.0.0.0] [--port 5030]
"""
import argparse
import asyncio
import math
import struct

SILENCE = 0.02  # seconds of silence that ends an RTU frame of unknown length


def crc16(data: bytes) -> int:
    crc = 0xFFFF
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = (crc >> 1) ^ 0xA001 if crc & 1 else crc >> 1
    return crc


def log(text: str) -> None:
    print(f"device sim: {text}", flush=True)


class ModbusError(Exception):
    def __init__(self, code: int) -> None:
        super().__init__(code)
        self.code = code


def float_words(value: float) -> list[int]:
    hi, lo = struct.unpack(">HH", struct.pack(">f", value))
    return [hi, lo]


def words_float(hi: int, lo: int) -> float:
    return struct.unpack(">f", struct.pack(">HH", hi, lo))[0]


class Device:
    name = ""
    functions: frozenset[int] = frozenset()

    def __init__(self, slave_id: int) -> None:
        self.id = slave_id
        self.next_id = slave_id

    def log(self, change: str) -> None:
        log(f"{self.name} {change}")

    def set_id(self, value: int) -> None:
        """Schedules a slave id change; it takes effect in commit(), after the reply has been sent."""
        self.next_id = value

    def commit(self) -> None:
        if self.next_id != self.id:
            self.log(f"slave id {self.id} -> {self.next_id}")
            self.id = self.next_id

    def set_setting(self, attr: str, label: str, value: int) -> None:
        old = getattr(self, attr)
        if old != value:
            setattr(self, attr, value)
            self.log(f"{label} {old} -> {value}")

    def handle(self, fc: int, data: bytes) -> bytes:
        """Returns the reply PDU for one request PDU (function code + data); raises ModbusError."""
        if fc not in self.functions:
            raise ModbusError(1)
        if fc in (3, 4):
            if len(data) != 4:
                raise ModbusError(3)
            addr, count = struct.unpack(">HH", data)
            if not 1 <= count <= 125:
                raise ModbusError(3)
            values = self.read(fc, addr, count)
            return bytes([fc, 2 * count]) + struct.pack(f">{count}H", *values)
        if fc == 6:
            if len(data) != 4:
                raise ModbusError(3)
            addr, value = struct.unpack(">HH", data)
            self.write(6, addr, [value])
            return bytes([fc]) + data
        if fc == 16:
            if len(data) < 5:
                raise ModbusError(3)
            addr, count, nbytes = struct.unpack(">HHB", data[:5])
            if not 1 <= count <= 123 or nbytes != 2 * count or len(data) != 5 + nbytes:
                raise ModbusError(3)
            self.write(16, addr, list(struct.unpack(f">{count}H", data[5:])))
            return bytes([fc]) + data[:4]
        return self.special(fc, data)

    def read(self, fc: int, addr: int, count: int) -> list[int]:
        raise ModbusError(1)

    def write(self, fc: int, addr: int, values: list[int]) -> None:
        raise ModbusError(1)

    def special(self, fc: int, data: bytes) -> bytes:
        raise ModbusError(1)


class Adm4204v(Device):
    name = "ADM-4204V"
    functions = frozenset({3, 6, 16})
    MODEL_CODE = 0x8110
    CONFIG = range(0x9F, 0xA8)
    DEFAULTS = {"baud": 3, "parity": 0, "ao_type": 0, "power_up": 0, "watchdog": 0, "wd_timer": 100}
    # config register -> (attribute, label, min, max)
    SETTINGS = {0xA1: ("baud", "baud code", 0, 7), 0xA2: ("parity", "parity code", 0, 2),
                0xA3: ("ao_type", "AO type", 0, 2), 0xA4: ("power_up", "power-up output", 0, 1),
                0xA5: ("watchdog", "watchdog", 0, 1), 0xA6: ("wd_timer", "watchdog timer", 0, 65535)}

    def __init__(self) -> None:
        super().__init__(1)
        self.limits = {a: 10000 for a in range(0x00, 0x04)}
        self.limits.update({a: 1 for a in range(0x20, 0x24)})
        self.limits.update({a: 10000 for a in range(0x24, 0x2C)})
        self.regs = dict.fromkeys(self.limits, 0)
        self.__dict__.update(self.DEFAULTS)
        self.restore_armed = False

    def get(self, addr: int) -> int:
        if addr in self.regs:
            return self.regs[addr]
        if addr == 0x9F:
            return self.MODEL_CODE
        if addr == 0xA0:
            return self.id
        if addr in self.SETTINGS:
            return getattr(self, self.SETTINGS[addr][0])
        return 0  # 0xA7 restore register reads 0

    def valid(self, addr: int) -> bool:
        return addr in self.regs or addr in self.CONFIG

    def read(self, fc: int, addr: int, count: int) -> list[int]:
        span = range(addr, addr + count)
        if not all(self.valid(a) for a in span):
            raise ModbusError(2)
        return [self.get(a) for a in span]

    def write(self, fc: int, addr: int, values: list[int]) -> None:
        span = range(addr, addr + len(values))
        if not all(self.valid(a) for a in span):
            raise ModbusError(2)
        if fc == 16 and any(a in self.CONFIG for a in span):
            raise ModbusError(1)
        for a, v in zip(span, values):
            if a in self.regs and v > self.limits[a]:
                raise ModbusError(3)
        if fc == 16:
            for a, v in zip(span, values):
                self.regs[a] = v
            return
        a, v = addr, values[0]
        if a in self.regs:
            self.regs[a] = v
        elif a == 0x9F:
            raise ModbusError(3)
        elif a == 0xA0:
            if not 1 <= v <= 254:
                raise ModbusError(3)
            self.set_id(v)
        elif a in self.SETTINGS:
            attr, label, lo, hi = self.SETTINGS[a]
            if not lo <= v <= hi:
                raise ModbusError(3)
            self.set_setting(attr, label, v)
        else:  # 0xA7: 0xAA55 arms, then 0x55AA restores the configuration defaults
            if v not in (0xAA55, 0x55AA):
                self.restore_armed = False
                raise ModbusError(3)
            if v == 0x55AA and self.restore_armed:
                self.__dict__.update(self.DEFAULTS)
                self.log("factory reset (slave id 1, baud code 3, parity code 0, AO type 0, power-up 0, "
                         "watchdog 0, timer 100)")
                self.set_id(1)
            self.restore_armed = v == 0xAA55


class Pzem016(Device):
    name = "PZEM-016"
    functions = frozenset({3, 4, 6, 0x42})
    GENERAL_ADDRESS = 0xF8

    def __init__(self) -> None:
        super().__init__(2)
        self.energy = 123456
        self.alarm_threshold = 2300

    def inputs(self) -> list[int]:
        return [2301, 1000, 0, 2300, 0, self.energy & 0xFFFF, self.energy >> 16, 500, 100, 0]

    def read(self, fc: int, addr: int, count: int) -> list[int]:
        if fc == 4:
            if addr + count > 10:
                raise ModbusError(2)
            return self.inputs()[addr:addr + count]
        if addr < 1 or addr + count > 3:
            raise ModbusError(2)
        return [self.alarm_threshold, self.id][addr - 1:addr - 1 + count]

    def write(self, fc: int, addr: int, values: list[int]) -> None:
        v = values[0]
        if addr == 1:
            if v < 1:
                raise ModbusError(3)
            self.set_setting("alarm_threshold", "power alarm threshold", v)
        elif addr == 2:
            if not 1 <= v <= 247:
                raise ModbusError(3)
            self.set_id(v)
        else:
            raise ModbusError(2)

    def special(self, fc: int, data: bytes) -> bytes:
        if data:
            raise ModbusError(3)
        self.energy = 0
        self.log("energy reset")
        return bytes([0x42])


class Sdm120(Device):
    name = "SDM120"
    functions = frozenset({3, 4, 16})
    INPUTS = {0x00: 230.2, 0x06: 1.5, 0x0C: 345.3, 0x12: 350.0, 0x18: -20.0, 0x1E: 0.98, 0x46: 50.0,
              0x48: 12.5, 0x156: 12.5, 0x158: 0.0}
    # holding float register -> (attribute, label, min, max)
    SETTINGS = {0x12: ("parity", "parity/stop code", 0, 3), 0x1C: ("baud", "baud code", 0, 4)}
    METER_CODE = 0xFC02

    def __init__(self) -> None:
        super().__init__(3)
        self.parity = 0
        self.baud = 2

    def read(self, fc: int, addr: int, count: int) -> list[int]:
        if addr % 2 or count % 2:
            raise ModbusError(2)
        words: list[int] = []
        for a in range(addr, addr + count, 2):
            if fc == 4:
                if not (a <= 0x4E or 0x156 <= a <= 0x158):
                    raise ModbusError(2)
                words += float_words(self.INPUTS.get(a, 0.0))
            elif a == self.METER_CODE:
                words += [0x0020, 0]
            elif a == 0x14:
                words += float_words(float(self.id))
            elif a in self.SETTINGS:
                words += float_words(float(getattr(self, self.SETTINGS[a][0])))
            else:
                raise ModbusError(2)
        return words

    def write(self, fc: int, addr: int, values: list[int]) -> None:
        if addr % 2 or len(values) % 2 or addr not in (0x12, 0x14, 0x1C):
            raise ModbusError(2)
        if len(values) != 2:
            raise ModbusError(3)
        f = words_float(*values)
        attr, label, lo, hi = self.SETTINGS.get(addr, ("id", "meter id", 1, 247))
        if not math.isfinite(f) or f != int(f) or not lo <= f <= hi:
            raise ModbusError(3)
        if addr == 0x14:
            self.set_id(int(f))
        else:
            self.set_setting(attr, label, int(f))


class BgtSmps(Device):
    name = "BGT-SMPS"
    functions = frozenset({3, 6})
    BROADCAST_QUERY = 0xFE
    DATA = [260, 356, 1234, 1356, 135, 138, 142, 686]

    def __init__(self) -> None:
        super().__init__(4)

    def read(self, fc: int, addr: int, count: int) -> list[int]:
        if addr + count > len(self.DATA):
            raise ModbusError(2)
        return self.DATA[addr:addr + count]

    def write(self, fc: int, addr: int, values: list[int]) -> None:
        if addr != 0x30:
            raise ModbusError(2)
        if not 1 <= values[0] <= 247:
            raise ModbusError(3)
        self.set_id(values[0])


class XyMd02(Device):
    name = "XY-MD02"
    functions = frozenset({3, 4, 6, 16})

    def __init__(self) -> None:
        super().__init__(5)
        self.baud = 9600

    def read(self, fc: int, addr: int, count: int) -> list[int]:
        if fc == 4:
            if addr < 1 or addr + count > 3:
                raise ModbusError(2)
            return [305, 546][addr - 1:addr - 1 + count]
        if addr < 0x101 or addr + count > 0x103:
            raise ModbusError(2)
        return [self.id, self.baud][addr - 0x101:addr - 0x101 + count]

    def write(self, fc: int, addr: int, values: list[int]) -> None:
        span = range(addr, addr + len(values))
        if addr < 0x101 or span.stop > 0x103:
            raise ModbusError(2)
        regs = dict(zip(span, values))
        if 0x101 in regs and not 1 <= regs[0x101] <= 247:
            raise ModbusError(3)
        if 0x102 in regs and regs[0x102] not in (9600, 19200):
            raise ModbusError(3)
        if 0x102 in regs:
            self.set_setting("baud", "baud", regs[0x102])
        if 0x101 in regs:
            self.set_id(regs[0x101])


class Bus:
    def __init__(self) -> None:
        self.pzem = Pzem016()
        self.bgt = BgtSmps()
        self.devices: list[Device] = [Adm4204v(), self.pzem, Sdm120(), self.bgt, XyMd02()]

    def resolve(self, addr: int) -> tuple[Device, int] | None:
        """Returns the answering device and the address its reply carries."""
        matches = [d for d in self.devices if d.id == addr]
        if len(matches) == 1:
            return matches[0], addr
        if matches:
            return None  # two devices answer at once: the master sees garbage
        if addr == Pzem016.GENERAL_ADDRESS:
            return self.pzem, addr
        if addr == BgtSmps.BROADCAST_QUERY:
            return self.bgt, self.bgt.id
        return None

    def process(self, addr: int, pdu: bytes) -> tuple[int, bytes] | None:
        target = self.resolve(addr) if pdu else None
        if target is None:
            return None
        device, reply_addr = target
        try:
            return reply_addr, device.handle(pdu[0], pdu[1:])
        except ModbusError as e:
            return reply_addr, bytes([pdu[0] | 0x80, e.code])

    def commit(self) -> None:
        """Applies slave id changes once the reply from the old id has been sent."""
        for d in self.devices:
            d.commit()

    def describe(self) -> str:
        extra = {Pzem016: f" (+0x{Pzem016.GENERAL_ADDRESS:02X})", BgtSmps: f" (+0x{BgtSmps.BROADCAST_QUERY:02X})"}
        return ", ".join(f"{d.name} id {d.id}{extra.get(type(d), '')}" for d in self.devices)


def rtu_frame_length(buf: bytearray) -> int | None:
    """Length of the RTU request at the start of buf, or None when it is not known (yet)."""
    if len(buf) < 2:
        return None
    fc = buf[1]
    if 1 <= fc <= 6:
        return 8
    if fc in (15, 16):
        return 9 + buf[6] if len(buf) >= 7 else None
    if fc == 0x42:
        return 4
    return None


async def send(writer: asyncio.StreamWriter, data: bytes, bus: Bus) -> None:
    writer.write(data)
    bus.commit()
    await writer.drain()


async def serve_rtu(bus: Bus, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
    buf = bytearray()
    try:
        while True:
            need = rtu_frame_length(buf)
            if need is not None and len(buf) >= need:
                frame = bytes(buf[:need])
                del buf[:need]
            else:
                try:
                    chunk = await asyncio.wait_for(reader.read(512), SILENCE if buf else None)
                except asyncio.TimeoutError:
                    # Inter-frame silence: the buffer holds a frame of unknown length (or a partial one,
                    # which then fails the CRC check and is dropped).
                    frame = bytes(buf)
                    buf.clear()
                else:
                    if not chunk:
                        break
                    buf += chunk
                    continue
            if len(frame) < 4 or crc16(frame[:-2]) != int.from_bytes(frame[-2:], "little"):
                continue
            reply = bus.process(frame[0], frame[1:-2])
            if reply is not None:
                body = bytes([reply[0]]) + reply[1]
                await send(writer, body + crc16(body).to_bytes(2, "little"), bus)
    except (ConnectionError, OSError):
        pass
    finally:
        writer.close()


async def serve_tcp(bus: Bus, reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
    try:
        while True:
            header = await reader.readexactly(7)
            tid, proto, length, unit = struct.unpack(">HHHB", header)
            if not 2 <= length <= 254:
                break
            pdu = await reader.readexactly(length - 1)
            reply = bus.process(unit, pdu) if proto == 0 else None
            if reply is not None:
                await send(writer, struct.pack(">HHHB", tid, 0, len(reply[1]) + 1, reply[0]) + reply[1], bus)
    except (asyncio.IncompleteReadError, ConnectionError, OSError):
        pass
    finally:
        writer.close()


async def main() -> None:
    ap = argparse.ArgumentParser(description="Modbus device simulator (RTU over TCP on PORT, Modbus TCP on PORT+1)")
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=5030)
    a = ap.parse_args()
    bus = Bus()
    rtu = await asyncio.start_server(lambda r, w: serve_rtu(bus, r, w), a.host, a.port)
    tcp = await asyncio.start_server(lambda r, w: serve_tcp(bus, r, w), a.host, a.port + 1)
    log(f"RTU-over-TCP {a.host}:{a.port}, Modbus TCP {a.host}:{a.port + 1}; devices: {bus.describe()}")
    async with rtu, tcp:
        await asyncio.gather(rtu.serve_forever(), tcp.serve_forever())


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
