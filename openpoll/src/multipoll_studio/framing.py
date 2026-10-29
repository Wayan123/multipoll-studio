"""Application data unit framing for Modbus RTU, ASCII and TCP (MBAP)."""
from __future__ import annotations

import struct

from .protocol import ModbusError, Status


def _crc_table() -> list[int]:
    table = []
    for byte in range(256):
        crc = byte
        for _ in range(8):
            crc = (crc >> 1) ^ 0xA001 if crc & 1 else crc >> 1
        table.append(crc)
    return table


_CRC_TABLE = _crc_table()


def crc16(data: bytes) -> int:
    """Modbus CRC-16 (poly 0xA001 reflected, init 0xFFFF). Transmitted low byte first."""
    crc = 0xFFFF
    for b in data:
        crc = (crc >> 8) ^ _CRC_TABLE[(crc ^ b) & 0xFF]
    return crc


def lrc(data: bytes) -> int:
    """Modbus ASCII LRC: two's complement of the 8-bit sum."""
    return (-sum(data)) & 0xFF


# --- RTU ---------------------------------------------------------------------

def rtu_frame(slave: int, pdu: bytes) -> bytes:
    body = bytes([slave]) + pdu
    return body + struct.pack("<H", crc16(body))


def rtu_response_length(header: bytes) -> int | None:
    """Total RTU frame length implied by the first bytes, or None if more bytes are needed.

    ``header`` starts with the slave address.
    """
    if len(header) < 2:
        return None
    fc = header[1]
    if fc & 0x80:
        return 5
    if fc in (1, 2, 3, 4, 23):
        if len(header) < 3:
            return None
        return 3 + header[2] + 2
    if fc in (5, 6, 15, 16):
        return 8
    if fc == 22:
        return 10
    raise ModbusError(Status.RESPONSE_ERROR, f"unexpected function {fc:02X} in RTU response")


def rtu_unframe(frame: bytes, slave: int) -> bytes:
    if len(frame) < 4:
        raise ModbusError(Status.BYTES_MISSING, f"RTU frame of {len(frame)} bytes")
    body, crc = frame[:-2], struct.unpack("<H", frame[-2:])[0]
    if crc16(body) != crc:
        raise ModbusError(Status.CRC_ERROR, f"CRC {crc:04X}, computed {crc16(body):04X}")
    if body[0] != slave:
        raise ModbusError(Status.RESPONSE_ERROR, f"slave {body[0]} answered, expected {slave}")
    return body[1:]


# --- ASCII -------------------------------------------------------------------

def ascii_frame(slave: int, pdu: bytes) -> bytes:
    body = bytes([slave]) + pdu
    return b":" + (body + bytes([lrc(body)])).hex().upper().encode() + b"\r\n"


def ascii_unframe(frame: bytes, slave: int) -> bytes:
    frame = frame.strip()
    if not frame.startswith(b":"):
        raise ModbusError(Status.RESPONSE_ERROR, "ASCII frame missing ':'")
    try:
        raw = bytes.fromhex(frame[1:].decode("ascii"))
    except ValueError as exc:
        raise ModbusError(Status.RESPONSE_ERROR, f"ASCII frame is not hex: {exc}") from None
    if len(raw) < 3:
        raise ModbusError(Status.BYTES_MISSING, f"ASCII frame of {len(raw)} bytes")
    body, check = raw[:-1], raw[-1]
    if lrc(body) != check:
        raise ModbusError(Status.CRC_ERROR, f"LRC {check:02X}, computed {lrc(body):02X}")
    if body[0] != slave:
        raise ModbusError(Status.RESPONSE_ERROR, f"slave {body[0]} answered, expected {slave}")
    return body[1:]


# --- TCP (MBAP) --------------------------------------------------------------

MBAP_LEN = 7


def mbap_frame(transaction: int, unit: int, pdu: bytes) -> bytes:
    return struct.pack(">HHHB", transaction & 0xFFFF, 0, len(pdu) + 1, unit) + pdu


def mbap_header(header: bytes) -> tuple[int, int, int, int]:
    """Returns (transaction, protocol, length, unit)."""
    return struct.unpack(">HHHB", header[:MBAP_LEN])


def mbap_unframe(frame: bytes, transaction: int, unit: int) -> bytes:
    if len(frame) < MBAP_LEN + 1:
        raise ModbusError(Status.BYTES_MISSING, f"MBAP frame of {len(frame)} bytes")
    tid, proto, length, uid = mbap_header(frame)
    if tid != transaction & 0xFFFF:
        raise ModbusError(Status.TRANSACTION_ID_ERROR, f"transaction {tid}, expected {transaction & 0xFFFF}")
    if proto != 0:
        raise ModbusError(Status.RESPONSE_ERROR, f"protocol id {proto}")
    if length != len(frame) - 6:
        raise ModbusError(Status.BYTES_MISSING, f"MBAP length {length}, received {len(frame) - 6}")
    if uid != unit:
        raise ModbusError(Status.RESPONSE_ERROR, f"unit {uid} answered, expected {unit}")
    return frame[MBAP_LEN:]
