"""Connection settings and a blocking Modbus master over serial, TCP or UDP.

Supports the five connection types Modbus Poll documents:
serial (RTU/ASCII), Modbus TCP, Modbus UDP, and RTU/ASCII tunnelled over TCP or UDP.
One ``ModbusMaster`` performs one transaction at a time; callers serialize access.
"""
from __future__ import annotations

import socket
import time
from dataclasses import asdict, dataclass, fields
from enum import Enum
from typing import Callable

from . import framing
from .protocol import ModbusError, Status, is_broadcastable, parse_response


class ConnectionKind(str, Enum):
    SERIAL = "serial"
    TCP = "tcp"
    UDP = "udp"
    SERIAL_OVER_TCP = "serial_over_tcp"
    SERIAL_OVER_UDP = "serial_over_udp"

    @property
    def label(self) -> str:
        return {
            ConnectionKind.SERIAL: "Serial Port",
            ConnectionKind.TCP: "Modbus TCP/IP",
            ConnectionKind.UDP: "Modbus UDP/IP",
            ConnectionKind.SERIAL_OVER_TCP: "Modbus RTU/ASCII Over TCP/IP",
            ConnectionKind.SERIAL_OVER_UDP: "Modbus RTU/ASCII Over UDP/IP",
        }[self]

    @property
    def is_network(self) -> bool:
        return self is not ConnectionKind.SERIAL

    @property
    def uses_mbap(self) -> bool:
        return self in (ConnectionKind.TCP, ConnectionKind.UDP)


class SerialMode(str, Enum):
    RTU = "rtu"
    ASCII = "ascii"


@dataclass
class ConnectionSettings:
    kind: ConnectionKind = ConnectionKind.TCP
    serial_mode: SerialMode = SerialMode.RTU
    # serial line
    port: str = "/dev/ttyUSB0"
    baudrate: int = 9600
    databits: int = 8
    parity: str = "N"  # N, E, O
    stopbits: int = 1
    # network
    host: str = "127.0.0.1"
    tcp_port: int = 502
    connect_timeout_ms: int = 3000
    # timing
    response_timeout_ms: int = 1000
    delay_between_polls_ms: int = 20
    # advanced serial options
    rts_toggle: bool = False
    rts_disable_delay_ms: int = 0
    dsr_flow: bool = False
    cts_flow: bool = False
    remove_echo: bool = False

    def to_dict(self) -> dict:
        d = asdict(self)
        d["kind"] = self.kind.value
        d["serial_mode"] = self.serial_mode.value
        return d

    @classmethod
    def from_dict(cls, data: dict) -> "ConnectionSettings":
        known = {f.name for f in fields(cls)}
        values = {k: v for k, v in data.items() if k in known}
        if "kind" in values:
            values["kind"] = ConnectionKind(values["kind"])
        if "serial_mode" in values:
            values["serial_mode"] = SerialMode(values["serial_mode"])
        return cls(**values)

    def describe(self) -> str:
        if self.kind is ConnectionKind.SERIAL:
            return (f"{self.port}: {self.baudrate}-{self.databits}-{self.parity}-{self.stopbits} "
                    f"{self.serial_mode.value.upper()}")
        suffix = "" if self.kind.uses_mbap else f" ({self.serial_mode.value.upper()})"
        return f"{self.kind.label} {self.host}:{self.tcp_port}{suffix}"


TrafficCallback = Callable[[str, bytes], None]  # ("Tx" | "Rx", raw bytes)


class _Stream:
    def write(self, data: bytes) -> None: ...
    def read_some(self, timeout_s: float) -> bytes: ...
    def discard_input(self) -> None: ...
    def close(self) -> None: ...


class _SerialStream(_Stream):
    def __init__(self, s: ConnectionSettings):
        import serial  # pyserial; imported lazily so network-only use needs no serial stack

        self._settings = s
        try:
            self._ser = serial.Serial(
                port=s.port, baudrate=s.baudrate, bytesize=s.databits, parity=s.parity,
                stopbits=s.stopbits, timeout=0, rtscts=s.cts_flow, dsrdtr=s.dsr_flow,
            )
        except (serial.SerialException, OSError, ValueError) as exc:
            raise ModbusError(Status.NO_CONNECTION, f"Port {s.port} not available: {exc}") from None
        # pyserial asserts DTR on open. RTS starts low when RTS toggle drives an RS-485 transceiver.
        if s.rts_toggle:
            self._set_rts(False)

    def _set_rts(self, level: bool) -> None:
        """Drive RTS; devices without modem lines (ptys, some USB bridges) reject the ioctl."""
        try:
            self._ser.rts = level
        except OSError:
            pass

    def write(self, data: bytes) -> None:
        import serial

        try:
            if self._settings.rts_toggle:
                self._set_rts(True)
            self._ser.write(data)
            self._ser.flush()
            if self._settings.rts_toggle:
                time.sleep(self._settings.rts_disable_delay_ms / 1000)
                self._set_rts(False)
        except (serial.SerialException, OSError) as exc:
            raise ModbusError(Status.WRITE_ERROR, str(exc)) from None

    def read_some(self, timeout_s: float) -> bytes:
        import serial

        try:
            self._ser.timeout = max(timeout_s, 0)
            first = self._ser.read(1)
            if not first:
                return b""
            return first + self._ser.read(self._ser.in_waiting)
        except (serial.SerialException, OSError) as exc:
            raise ModbusError(Status.READ_ERROR, str(exc)) from None

    def discard_input(self) -> None:
        self._ser.reset_input_buffer()

    def close(self) -> None:
        self._ser.close()


class _SocketStream(_Stream):
    def __init__(self, s: ConnectionSettings, udp: bool):
        self._udp = udp
        try:
            if udp:
                infos = socket.getaddrinfo(s.host, s.tcp_port, type=socket.SOCK_DGRAM)
                family, kind, proto, _, addr = infos[0]
                self._sock = socket.socket(family, kind, proto)
                self._sock.connect(addr)
            else:
                self._sock = socket.create_connection((s.host, s.tcp_port), timeout=s.connect_timeout_ms / 1000)
                self._sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        except socket.timeout:
            raise ModbusError(Status.NO_CONNECTION, "Modbus Connection Failed. Connect Timeout") from None
        except OSError as exc:
            raise ModbusError(Status.NO_CONNECTION, f"Modbus Connection Failed. {exc}") from None

    def write(self, data: bytes) -> None:
        try:
            self._sock.sendall(data)
        except OSError as exc:
            raise ModbusError(Status.WRITE_ERROR, str(exc)) from None

    def read_some(self, timeout_s: float) -> bytes:
        self._sock.settimeout(max(timeout_s, 0.001))
        try:
            data = self._sock.recv(65535)
        except socket.timeout:
            return b""
        except ConnectionRefusedError:  # UDP: ICMP port unreachable
            raise ModbusError(Status.NO_CONNECTION, "connection refused") from None
        except OSError as exc:
            raise ModbusError(Status.READ_ERROR, str(exc)) from None
        if not data and not self._udp:
            raise ModbusError(Status.NO_CONNECTION, "connection closed by remote host")
        return data

    def discard_input(self) -> None:
        self._sock.setblocking(False)
        try:
            while self._sock.recv(65535):
                pass
        except (BlockingIOError, InterruptedError):
            pass
        except OSError:
            pass
        finally:
            self._sock.setblocking(True)

    def close(self) -> None:
        try:
            self._sock.close()
        except OSError:
            pass


class ModbusMaster:
    """Blocking request/response engine. Not thread-safe; one transaction at a time."""

    BROADCAST_TURNAROUND_S = 0.1
    RAW_SILENCE_S = 0.05

    def __init__(self, settings: ConnectionSettings, on_traffic: TrafficCallback | None = None):
        self.settings = settings
        self.on_traffic = on_traffic
        self._stream: _Stream | None = None
        self._buf = bytearray()
        self._transaction = 0

    # -- connection ------------------------------------------------------------
    @property
    def connected(self) -> bool:
        return self._stream is not None

    def connect(self) -> None:
        self.close()
        s = self.settings
        if s.kind is ConnectionKind.SERIAL:
            self._stream = _SerialStream(s)
        else:
            self._stream = _SocketStream(s, udp=s.kind in (ConnectionKind.UDP, ConnectionKind.SERIAL_OVER_UDP))

    def close(self) -> None:
        if self._stream is not None:
            self._stream.close()
            self._stream = None
        self._buf.clear()

    def _ensure_connected(self) -> _Stream:
        if self._stream is None:
            raise ModbusError(Status.NO_CONNECTION)
        return self._stream

    # -- low level io ------------------------------------------------------------
    def _trace(self, direction: str, data: bytes) -> None:
        if self.on_traffic and data:
            self.on_traffic(direction, bytes(data))

    def _send(self, adu: bytes) -> None:
        stream = self._ensure_connected()
        stream.discard_input()
        self._buf.clear()
        self._trace("Tx", adu)
        try:
            stream.write(adu)
        except ModbusError:
            self._drop_if_network()
            raise
        if self.settings.kind is ConnectionKind.SERIAL and self.settings.remove_echo:
            deadline = time.monotonic() + self.settings.response_timeout_ms / 1000
            self._take(len(adu), deadline, received=bytearray())

    def _drop_if_network(self) -> None:
        if self.settings.kind.is_network:
            self.close()

    def _fill(self, deadline: float) -> bool:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return False
        try:
            chunk = self._stream.read_some(remaining)
        except ModbusError as exc:
            if exc.status is Status.NO_CONNECTION:
                self.close()
            raise
        self._buf += chunk
        return bool(chunk)

    def _take(self, n: int, deadline: float, received: bytearray) -> bytes:
        while len(self._buf) < n:
            if not self._fill(deadline) and time.monotonic() >= deadline:
                got = bytes(received) + bytes(self._buf)
                self._trace("Rx", got)
                if got:
                    raise ModbusError(Status.BYTES_MISSING, f"received {len(got)} bytes, frame incomplete")
                raise ModbusError(Status.TIMEOUT)
        out = bytes(self._buf[:n])
        del self._buf[:n]
        received += out
        return out

    # -- framing ----------------------------------------------------------------
    def _receive_rtu(self, deadline: float) -> bytes:
        received = bytearray()
        header = self._take(3, deadline, received)
        try:
            total = framing.rtu_response_length(header)
        except ModbusError:
            self._trace("Rx", bytes(received) + bytes(self._buf))
            raise
        self._take(total - 3, deadline, received)
        self._trace("Rx", bytes(received))
        return bytes(received)

    def _receive_ascii(self, deadline: float) -> bytes:
        received = bytearray()
        while not received.endswith(b"\r\n"):
            self._take(1, deadline, received)
            if received[:1] != b":":
                received.clear()  # skip noise before the start character
        self._trace("Rx", bytes(received))
        return bytes(received)

    def _receive_mbap(self, deadline: float) -> bytes:
        received = bytearray()
        header = self._take(framing.MBAP_LEN, deadline, received)
        length = framing.mbap_header(header)[2]
        if not 2 <= length <= 254:
            self._trace("Rx", bytes(received) + bytes(self._buf))
            raise ModbusError(Status.RESPONSE_ERROR, f"invalid MBAP length {length}")
        self._take(length - 1, deadline, received)
        self._trace("Rx", bytes(received))
        return bytes(received)

    # -- public api ---------------------------------------------------------------
    def execute(self, slave: int, pdu: bytes) -> bytes | None:
        """Send one request PDU and return the raw response PDU.

        Returns None for a serial broadcast (slave 0 write), which has no response.
        """
        if not 0 <= slave <= 255:
            raise ValueError(f"slave id must be 0..255, got {slave}")
        s = self.settings
        mbap = s.kind.uses_mbap
        if slave == 0 and not mbap and not is_broadcastable(pdu):
            raise ValueError("broadcast (slave 0) is only allowed for write functions")

        if mbap:
            self._transaction = (self._transaction + 1) & 0xFFFF
            adu = framing.mbap_frame(self._transaction, slave, pdu)
        elif s.serial_mode is SerialMode.RTU:
            adu = framing.rtu_frame(slave, pdu)
        else:
            adu = framing.ascii_frame(slave, pdu)

        self._send(adu)
        if slave == 0 and not mbap:
            time.sleep(self.BROADCAST_TURNAROUND_S)
            return None

        deadline = time.monotonic() + s.response_timeout_ms / 1000
        if mbap:
            return framing.mbap_unframe(self._receive_mbap(deadline), self._transaction, slave)
        if s.serial_mode is SerialMode.RTU:
            return framing.rtu_unframe(self._receive_rtu(deadline), slave)
        return framing.ascii_unframe(self._receive_ascii(deadline), slave)

    def transact(self, slave: int, pdu: bytes) -> list[int] | None:
        """Execute and validate. Returns read values, or None for writes and broadcasts."""
        response = self.execute(slave, pdu)
        if response is None:
            return None
        return parse_response(pdu, response)

    def raw_exchange(self, data: bytes) -> bytes:
        """Send arbitrary bytes and collect whatever arrives (Frame Console).

        Collection ends at the response timeout, or once the line has been silent
        for RAW_SILENCE_S after the first received byte.
        """
        self._send(data)
        deadline = time.monotonic() + self.settings.response_timeout_ms / 1000
        while time.monotonic() < deadline:
            gap_deadline = min(deadline, time.monotonic() + self.RAW_SILENCE_S) if self._buf else deadline
            if not self._fill(gap_deadline) and self._buf:
                break
        out = bytes(self._buf)
        self._buf.clear()
        self._trace("Rx", out)
        return out
