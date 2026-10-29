"""Modbus PDU encoding/decoding and result status codes.

Status numbers follow Modbus Poll's documented automation ``ReadResult`` codes
so logs and scripts can be compared one-to-one.
"""
from __future__ import annotations

import struct
from enum import IntEnum


class FC(IntEnum):
    READ_COILS = 1
    READ_DISCRETE_INPUTS = 2
    READ_HOLDING_REGISTERS = 3
    READ_INPUT_REGISTERS = 4
    WRITE_SINGLE_COIL = 5
    WRITE_SINGLE_REGISTER = 6
    WRITE_MULTIPLE_COILS = 15
    WRITE_MULTIPLE_REGISTERS = 16
    MASK_WRITE_REGISTER = 22
    READ_WRITE_MULTIPLE_REGISTERS = 23


READ_FUNCTIONS = (FC.READ_COILS, FC.READ_DISCRETE_INPUTS, FC.READ_HOLDING_REGISTERS, FC.READ_INPUT_REGISTERS)
BIT_FUNCTIONS = (FC.READ_COILS, FC.READ_DISCRETE_INPUTS)
WRITE_FUNCTIONS = (FC.WRITE_SINGLE_COIL, FC.WRITE_SINGLE_REGISTER, FC.WRITE_MULTIPLE_COILS,
                   FC.WRITE_MULTIPLE_REGISTERS, FC.MASK_WRITE_REGISTER)

# Quantity limits from the Modbus Application Protocol v1.1b3.
MAX_READ_BITS = 2000
MAX_READ_REGISTERS = 125
MAX_WRITE_BITS = 1968
MAX_WRITE_REGISTERS = 123
MAX_RW_READ_REGISTERS = 125
MAX_RW_WRITE_REGISTERS = 121


class Status(IntEnum):
    OK = 0
    TIMEOUT = 1
    CRC_ERROR = 2
    RESPONSE_ERROR = 3
    WRITE_ERROR = 4
    READ_ERROR = 5
    NO_CONNECTION = 6
    NOT_INITIALIZED = 10
    BYTES_MISSING = 11
    BYTE_COUNT_ERROR = 12
    TRANSACTION_ID_ERROR = 13
    UNKNOWN_EXCEPTION = 0x80
    ILLEGAL_FUNCTION = 0x81
    ILLEGAL_DATA_ADDRESS = 0x82
    ILLEGAL_DATA_VALUE = 0x83
    SLAVE_DEVICE_FAILURE = 0x84
    ACKNOWLEDGE = 0x85
    SLAVE_DEVICE_BUSY = 0x86
    NEGATIVE_ACKNOWLEDGE = 0x87
    MEMORY_PARITY_ERROR = 0x88
    GATEWAY_PATH_UNAVAILABLE = 0x8A
    GATEWAY_TARGET_FAILED = 0x8B

    @property
    def text(self) -> str:
        return STATUS_TEXT[self]

    @property
    def is_exception(self) -> bool:
        return self >= Status.UNKNOWN_EXCEPTION


STATUS_TEXT = {
    Status.OK: "Response ok",
    Status.TIMEOUT: "Timeout Error",
    Status.CRC_ERROR: "CRC Error",
    Status.RESPONSE_ERROR: "Response Error",
    Status.WRITE_ERROR: "Write Error",
    Status.READ_ERROR: "Read Error",
    Status.NO_CONNECTION: "No Connection",
    Status.NOT_INITIALIZED: "Data Uninitialized",
    Status.BYTES_MISSING: "Bytes Missing Error",
    Status.BYTE_COUNT_ERROR: "Byte Count Error",
    Status.TRANSACTION_ID_ERROR: "Transaction ID Error",
    Status.UNKNOWN_EXCEPTION: "Unknown Exception",
    Status.ILLEGAL_FUNCTION: "Illegal Function",
    Status.ILLEGAL_DATA_ADDRESS: "Illegal Data Address",
    Status.ILLEGAL_DATA_VALUE: "Illegal Data Value",
    Status.SLAVE_DEVICE_FAILURE: "Slave Device Failure",
    Status.ACKNOWLEDGE: "Acknowledge",
    Status.SLAVE_DEVICE_BUSY: "Slave Device Busy",
    Status.NEGATIVE_ACKNOWLEDGE: "NAK-Negative Acknowledgment",
    Status.MEMORY_PARITY_ERROR: "Memory Parity Error",
    Status.GATEWAY_PATH_UNAVAILABLE: "Gateway Path Unavailable",
    Status.GATEWAY_TARGET_FAILED: "Gateway Target Device Failed to Respond",
}


class ModbusError(Exception):
    def __init__(self, status: Status, detail: str = ""):
        super().__init__(detail or status.text)
        self.status = status
        self.detail = detail


def _check_range(name: str, value: int, low: int, high: int) -> None:
    if not low <= value <= high:
        raise ValueError(f"{name} must be {low}..{high}, got {value}")


def _check_address_span(address: int, quantity: int) -> None:
    _check_range("address", address, 0, 0xFFFF)
    if address + quantity > 0x10000:
        raise ValueError(f"address {address} + quantity {quantity} exceeds 65536")


def read_request(function: int, address: int, quantity: int) -> bytes:
    function = FC(function)
    if function not in READ_FUNCTIONS:
        raise ValueError(f"function {function} is not a read function")
    limit = MAX_READ_BITS if function in BIT_FUNCTIONS else MAX_READ_REGISTERS
    _check_range("quantity", quantity, 1, limit)
    _check_address_span(address, quantity)
    return struct.pack(">BHH", function, address, quantity)


def write_single_coil(address: int, on: bool) -> bytes:
    _check_range("address", address, 0, 0xFFFF)
    return struct.pack(">BHH", FC.WRITE_SINGLE_COIL, address, 0xFF00 if on else 0x0000)


def write_single_register(address: int, value: int) -> bytes:
    _check_range("address", address, 0, 0xFFFF)
    _check_range("value", value, 0, 0xFFFF)
    return struct.pack(">BHH", FC.WRITE_SINGLE_REGISTER, address, value)


def pack_bits(bits: list[int] | list[bool]) -> bytes:
    out = bytearray((len(bits) + 7) // 8)
    for i, bit in enumerate(bits):
        if bit:
            out[i // 8] |= 1 << (i % 8)
    return bytes(out)


def unpack_bits(data: bytes, count: int) -> list[int]:
    return [(data[i // 8] >> (i % 8)) & 1 for i in range(count)]


def write_multiple_coils(address: int, bits: list[int] | list[bool]) -> bytes:
    _check_range("quantity", len(bits), 1, MAX_WRITE_BITS)
    _check_address_span(address, len(bits))
    payload = pack_bits(bits)
    return struct.pack(">BHHB", FC.WRITE_MULTIPLE_COILS, address, len(bits), len(payload)) + payload


def _pack_registers(values: list[int]) -> bytes:
    for v in values:
        _check_range("register value", v, 0, 0xFFFF)
    return struct.pack(f">{len(values)}H", *values)


def write_multiple_registers(address: int, values: list[int]) -> bytes:
    _check_range("quantity", len(values), 1, MAX_WRITE_REGISTERS)
    _check_address_span(address, len(values))
    payload = _pack_registers(values)
    return struct.pack(">BHHB", FC.WRITE_MULTIPLE_REGISTERS, address, len(values), len(payload)) + payload


def mask_write_register(address: int, and_mask: int, or_mask: int) -> bytes:
    _check_range("address", address, 0, 0xFFFF)
    _check_range("AND mask", and_mask, 0, 0xFFFF)
    _check_range("OR mask", or_mask, 0, 0xFFFF)
    return struct.pack(">BHHH", FC.MASK_WRITE_REGISTER, address, and_mask, or_mask)


def read_write_registers(read_address: int, read_quantity: int, write_address: int, values: list[int]) -> bytes:
    _check_range("read quantity", read_quantity, 1, MAX_RW_READ_REGISTERS)
    _check_range("write quantity", len(values), 1, MAX_RW_WRITE_REGISTERS)
    _check_address_span(read_address, read_quantity)
    _check_address_span(write_address, len(values))
    payload = _pack_registers(values)
    return struct.pack(">BHHHHB", FC.READ_WRITE_MULTIPLE_REGISTERS, read_address, read_quantity,
                       write_address, len(values), len(payload)) + payload


def is_broadcastable(pdu: bytes) -> bool:
    """Writes may be broadcast to slave 0 on serial lines; reads and FC23 may not."""
    return pdu[0] in WRITE_FUNCTIONS


def expected_quantity(request: bytes) -> int:
    """Number of read items (bits or registers) the request asks for; 0 for pure writes."""
    fc = request[0]
    if fc in READ_FUNCTIONS:
        return struct.unpack_from(">H", request, 3)[0]
    if fc == FC.READ_WRITE_MULTIPLE_REGISTERS:
        return struct.unpack_from(">H", request, 3)[0]
    return 0


def parse_response(request: bytes, response: bytes) -> list[int] | None:
    """Validate ``response`` against ``request``. Returns read values, or None for writes.

    Raises ModbusError carrying the matching Status on any mismatch.
    """
    if not response:
        raise ModbusError(Status.RESPONSE_ERROR, "empty response PDU")
    fc = request[0]
    rfc = response[0]
    if rfc == fc | 0x80:
        if len(response) != 2:
            raise ModbusError(Status.RESPONSE_ERROR, "malformed exception response")
        code = response[1]
        try:
            raise ModbusError(Status(0x80 | code), f"exception code {code:02X}")
        except ValueError:
            raise ModbusError(Status.UNKNOWN_EXCEPTION, f"exception code {code:02X}") from None
    if rfc != fc:
        raise ModbusError(Status.RESPONSE_ERROR, f"function {rfc:02X} does not match request {fc:02X}")

    if fc in READ_FUNCTIONS or fc == FC.READ_WRITE_MULTIPLE_REGISTERS:
        quantity = expected_quantity(request)
        bits = fc in BIT_FUNCTIONS
        expected_bytes = (quantity + 7) // 8 if bits else 2 * quantity
        if len(response) < 2 or response[1] != expected_bytes or len(response) != 2 + expected_bytes:
            got = response[1] if len(response) >= 2 else 0
            raise ModbusError(Status.BYTE_COUNT_ERROR, f"byte count {got}, expected {expected_bytes}")
        data = response[2:]
        if bits:
            return unpack_bits(data, quantity)
        return list(struct.unpack(f">{quantity}H", data))

    if fc in (FC.WRITE_SINGLE_COIL, FC.WRITE_SINGLE_REGISTER, FC.WRITE_MULTIPLE_COILS, FC.WRITE_MULTIPLE_REGISTERS):
        if response != request[:5]:
            raise ModbusError(Status.RESPONSE_ERROR, "write echo does not match request")
        return None
    if fc == FC.MASK_WRITE_REGISTER:
        if response != request:
            raise ModbusError(Status.RESPONSE_ERROR, "mask write echo does not match request")
        return None
    raise ModbusError(Status.RESPONSE_ERROR, f"unsupported function {fc:02X}")
