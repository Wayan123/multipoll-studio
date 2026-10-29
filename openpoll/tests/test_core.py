"""Protocol, framing and format tests against known vectors and a real pymodbus slave."""
from __future__ import annotations

import socket
import threading
import time

import pytest

from multipoll_studio import framing, protocol
from multipoll_studio.engine import PollEngine
from multipoll_studio.formats import DisplayFormat, decode, encode, format_value, parse_value
from multipoll_studio.model import PollDefinition, address_label, parse_plc_address
from multipoll_studio.protocol import FC, ModbusError, Status
from multipoll_studio.support import CheckKind, add_check, parse_hex_string
from multipoll_studio.transport import ConnectionKind, ConnectionSettings, ModbusMaster, SerialMode

# --- framing vectors ------------------------------------------------------------

def test_crc_matches_spec_example():
    # Modbus over Serial Line spec: 01 03 00 00 00 0A -> CRC C5 CD (low byte first)
    assert framing.rtu_frame(1, bytes.fromhex("0300 0000 0A".replace(" ", ""))) == bytes.fromhex("01030000000AC5CD")


def test_lrc_matches_modbus_poll_help_example():
    # mbpoll.chm Test Center example: ":010300000000A" + LRC -> "F2" CR LF
    frame = add_check(bytes.fromhex("3A 30 31 30 33 30 30 30 30 30 30 30 41".replace(" ", "")), CheckKind.LRC)
    assert frame.endswith(b"F2\r\n")


def test_ascii_round_trip_and_lrc_error():
    frame = framing.ascii_frame(17, bytes.fromhex("0300 6B00 03".replace(" ", "")))
    assert frame == b":1103006B00037E\r\n"
    assert framing.ascii_unframe(frame, 17) == bytes.fromhex("03006B0003")
    with pytest.raises(ModbusError) as exc:
        framing.ascii_unframe(b":1103006B00037F\r\n", 17)
    assert exc.value.status is Status.CRC_ERROR


def test_rtu_detects_bad_crc_and_wrong_slave():
    good = framing.rtu_frame(1, b"\x03\x02\x00\x07")
    with pytest.raises(ModbusError) as exc:
        framing.rtu_unframe(good[:-1] + bytes([good[-1] ^ 1]), 1)
    assert exc.value.status is Status.CRC_ERROR
    with pytest.raises(ModbusError) as exc:
        framing.rtu_unframe(good, 2)
    assert exc.value.status is Status.RESPONSE_ERROR


def test_mbap_transaction_mismatch():
    frame = framing.mbap_frame(5, 1, b"\x03\x02\x00\x01")
    with pytest.raises(ModbusError) as exc:
        framing.mbap_unframe(frame, 6, 1)
    assert exc.value.status is Status.TRANSACTION_ID_ERROR


def test_test_center_hex_separators():
    assert parse_hex_string("01,03.00 00\t00 0a") == bytes.fromhex("01030000000A")
    with pytest.raises(ValueError):
        parse_hex_string("123")


# --- PDU validation ---------------------------------------------------------------

def test_exception_response_maps_to_status():
    req = protocol.read_request(3, 0, 1)
    with pytest.raises(ModbusError) as exc:
        protocol.parse_response(req, b"\x83\x02")
    assert exc.value.status is Status.ILLEGAL_DATA_ADDRESS


def test_byte_count_mismatch_is_rejected():
    req = protocol.read_request(3, 0, 2)
    with pytest.raises(ModbusError) as exc:
        protocol.parse_response(req, b"\x03\x02\x00\x01")
    assert exc.value.status is Status.BYTE_COUNT_ERROR


@pytest.mark.parametrize("fc,qty", [(3, 126), (1, 2001), (3, 0)])
def test_quantity_limits(fc, qty):
    with pytest.raises(ValueError):
        protocol.read_request(fc, 0, qty)


def test_address_span_cannot_wrap():
    with pytest.raises(ValueError):
        protocol.read_request(3, 65530, 10)


# --- display formats ---------------------------------------------------------------

@pytest.mark.parametrize("fmt,value", [
    (DisplayFormat.SIGNED, -2), (DisplayFormat.UNSIGNED, 65535), (DisplayFormat.LONG, -123456789),
    (DisplayFormat.LONG_INVERSE, 2**31 - 1), (DisplayFormat.FLOAT, 1.5), (DisplayFormat.FLOAT_INVERSE, -0.25),
    (DisplayFormat.DOUBLE, 3.141592653589793), (DisplayFormat.DOUBLE_INVERSE, -1e300),
])
def test_encode_decode_round_trip(fmt, value):
    assert decode(encode(value, fmt), fmt) == value


def test_inverse_swaps_word_order_only():
    assert encode(1.0, DisplayFormat.FLOAT) == [0x3F80, 0x0000]
    assert encode(1.0, DisplayFormat.FLOAT_INVERSE) == [0x0000, 0x3F80]
    assert encode(0x12345678, DisplayFormat.LONG_INVERSE) == [0x5678, 0x1234]


def test_out_of_range_values_rejected():
    for fmt, v in [(DisplayFormat.SIGNED, 32768), (DisplayFormat.UNSIGNED, -1), (DisplayFormat.LONG, 2**31),
                   (DisplayFormat.FLOAT, 1e40)]:
        with pytest.raises(ValueError):
            encode(v, fmt)


def test_hex_binary_display_and_parse():
    assert format_value(0xBEEF, DisplayFormat.HEX) == "0xBEEF"
    assert format_value(0xA5F0, DisplayFormat.BINARY) == "1010 0101 1111 0000"
    assert parse_value("1010 0101 1111 0000", DisplayFormat.BINARY) == 0xA5F0
    assert parse_value("0xbeef", DisplayFormat.HEX) == 0xBEEF


# --- addressing ------------------------------------------------------------------------

def test_plc_address_labels_match_original_conventions():
    assert address_label(3, 0, plc=True) == "40001"
    assert address_label(4, 9, plc=True) == "30010"
    assert address_label(1, 0, plc=True) == "00001"
    assert address_label(2, 9999, plc=True) == "110000"
    assert address_label(3, 0, plc=False) == "00000"
    assert parse_plc_address("4x0010") == (3, 9)
    assert parse_plc_address("410000") == (3, 9999)


# --- against a live slave ------------------------------------------------------------------

def _master(port: int, kind=ConnectionKind.TCP, mode=SerialMode.RTU, traffic=None) -> ModbusMaster:
    m = ModbusMaster(ConnectionSettings(kind=kind, serial_mode=mode, host="127.0.0.1", tcp_port=port,
                                        response_timeout_ms=1000), on_traffic=traffic)
    m.connect()
    return m


def _exercise_all_functions(m: ModbusMaster, slave) -> None:
    slave.set(3, 0, [1, 2, 3, 0xFFFF])
    assert m.transact(1, protocol.read_request(FC.READ_HOLDING_REGISTERS, 0, 4)) == [1, 2, 3, 0xFFFF]

    m.transact(1, protocol.write_single_register(10, 0xBEEF))
    assert slave.get(3, 10, 1) == [0xBEEF]

    m.transact(1, protocol.write_multiple_registers(20, [7, 8, 9]))
    assert slave.get(3, 20, 3) == [7, 8, 9]

    m.transact(1, protocol.write_single_coil(5, True))
    m.transact(1, protocol.write_multiple_coils(8, [1, 0, 1, 1, 0, 0, 0, 0, 1]))
    assert m.transact(1, protocol.read_request(FC.READ_COILS, 5, 12)) == [1, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 1]

    slave.set(3, 30, [0x00F2])
    m.transact(1, protocol.mask_write_register(30, 0x00F0, 0x0025))
    assert slave.get(3, 30, 1) == [(0x00F2 & 0x00F0) | (0x0025 & ~0x00F0 & 0xFFFF)]

    slave.set(3, 40, [11, 12])
    assert m.transact(1, protocol.read_write_registers(40, 2, 50, [99])) == [11, 12]
    assert slave.get(3, 50, 1) == [99]

    slave.set(4, 0, [321])
    slave.set(2, 0, [1, 1, 0])
    assert m.transact(1, protocol.read_request(FC.READ_INPUT_REGISTERS, 0, 1)) == [321]
    assert m.transact(1, protocol.read_request(FC.READ_DISCRETE_INPUTS, 0, 3)) == [1, 1, 0]


def test_tcp_all_functions(tcp_slave):
    m = _master(tcp_slave.port)
    try:
        _exercise_all_functions(m, tcp_slave)
    finally:
        m.close()


def test_rtu_over_tcp_all_functions(rtu_over_tcp_slave):
    m = _master(rtu_over_tcp_slave.port, ConnectionKind.SERIAL_OVER_TCP, SerialMode.RTU)
    try:
        _exercise_all_functions(m, rtu_over_tcp_slave)
    finally:
        m.close()


def test_ascii_over_tcp_all_functions(ascii_over_tcp_slave):
    m = _master(ascii_over_tcp_slave.port, ConnectionKind.SERIAL_OVER_TCP, SerialMode.ASCII)
    try:
        _exercise_all_functions(m, ascii_over_tcp_slave)
    finally:
        m.close()


def test_udp_read(udp_slave):
    udp_slave.set(3, 0, [42, 43])
    m = _master(udp_slave.port, ConnectionKind.UDP)
    try:
        assert m.transact(1, protocol.read_request(3, 0, 2)) == [42, 43]
    finally:
        m.close()


def test_illegal_address_exception_from_slave(tcp_slave):
    m = _master(tcp_slave.port)
    try:
        with pytest.raises(ModbusError) as exc:
            m.transact(1, protocol.read_request(3, 990, 20))
        assert exc.value.status is Status.ILLEGAL_DATA_ADDRESS
    finally:
        m.close()


def test_traffic_callback_sees_tx_and_rx(tcp_slave):
    seen = []
    m = _master(tcp_slave.port, traffic=lambda d, b: seen.append((d, b)))
    try:
        m.transact(1, protocol.read_request(3, 0, 1))
    finally:
        m.close()
    assert [d for d, _ in seen] == ["Tx", "Rx"]
    assert seen[0][1][6:] == bytes.fromhex("01 03 00 00 00 01".replace(" ", ""))


def _silent_server():
    """TCP server that accepts and never answers, or answers with a truncated frame."""
    srv = socket.socket()
    srv.bind(("127.0.0.1", 0))
    srv.listen()
    conns = []

    def run():
        while True:
            try:
                c, _ = srv.accept()
            except OSError:
                return
            conns.append(c)

    threading.Thread(target=run, daemon=True).start()
    return srv, conns


def test_timeout_and_bytes_missing():
    srv, conns = _silent_server()
    port = srv.getsockname()[1]
    m = ModbusMaster(ConnectionSettings(kind=ConnectionKind.SERIAL_OVER_TCP, host="127.0.0.1", tcp_port=port,
                                        response_timeout_ms=200))
    m.connect()
    try:
        with pytest.raises(ModbusError) as exc:
            m.transact(1, protocol.read_request(3, 0, 2))
        assert exc.value.status is Status.TIMEOUT
        time.sleep(0.05)
        conns[-1].sendall(b"\x01\x03\x04\x00")  # announces 4 data bytes, sends 1
        with pytest.raises(ModbusError) as exc:
            m.transact(1, protocol.read_request(3, 0, 2))
        assert exc.value.status in (Status.BYTES_MISSING, Status.TIMEOUT)
    finally:
        m.close()
        srv.close()


def test_connect_refused_reports_no_connection():
    port = _unused_port()
    m = ModbusMaster(ConnectionSettings(kind=ConnectionKind.TCP, host="127.0.0.1", tcp_port=port,
                                        connect_timeout_ms=500))
    with pytest.raises(ModbusError) as exc:
        m.connect()
    assert exc.value.status is Status.NO_CONNECTION


def _unused_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


# --- engine -----------------------------------------------------------------------------

def test_engine_polls_multiple_windows_and_runs_writes(tcp_slave):
    tcp_slave.set(3, 0, [5, 6])
    tcp_slave.set(1, 0, [1, 0, 1])
    results: dict[int, list] = {}
    done = threading.Event()

    def collect(r):
        results.setdefault(r.poll_id, []).append(r)
        if len(results) == 2 and all(len(v) >= 2 for v in results.values()):
            done.set()

    engine = PollEngine()
    a = engine.add_poll(PollDefinition(function=3, address=0, quantity=2, scan_rate_ms=50), collect)
    b = engine.add_poll(PollDefinition(function=1, address=0, quantity=3, scan_rate_ms=50), collect)
    engine.start(ConnectionSettings(kind=ConnectionKind.TCP, host="127.0.0.1", tcp_port=tcp_slave.port,
                                    delay_between_polls_ms=0))
    try:
        assert done.wait(5)
        assert results[a][-1].values == [5, 6] and results[a][-1].status is Status.OK
        assert results[b][-1].values == [1, 0, 1]
        engine.write(1, protocol.write_single_register(1, 77)).result(5)
        assert tcp_slave.get(3, 1, 1) == [77]
        assert engine.stats(a).tx >= 2 and engine.stats(a).errors == 0
    finally:
        engine.stop()


def test_engine_disabled_poll_only_reads_once(tcp_slave):
    got = []
    engine = PollEngine()
    pid = engine.add_poll(PollDefinition(scan_rate_ms=20, enabled=False), got.append)
    engine.start(ConnectionSettings(kind=ConnectionKind.TCP, host="127.0.0.1", tcp_port=tcp_slave.port,
                                    delay_between_polls_ms=0))
    try:
        time.sleep(0.2)
        assert got == []
        engine.read_once(pid)
        time.sleep(0.3)
        assert len(got) == 1
    finally:
        engine.stop()


def test_engine_reconnects_after_slave_restart():
    """Polls report No Connection while the slave is down and recover once it is back."""
    from conftest import Slave
    from pymodbus import FramerType
    from pymodbus.server import ModbusTcpServer

    first = Slave(ModbusTcpServer, FramerType.SOCKET, socket.SOCK_STREAM)
    port = first.port
    statuses = []
    engine = PollEngine()
    engine.add_poll(PollDefinition(scan_rate_ms=50), lambda r: statuses.append(r.status))
    engine.start(ConnectionSettings(kind=ConnectionKind.TCP, host="127.0.0.1", tcp_port=port,
                                    response_timeout_ms=300, delay_between_polls_ms=0))
    try:
        assert _wait(lambda: Status.OK in statuses)
        first.close()
        assert _wait(lambda: statuses[-1] is Status.NO_CONNECTION)
        second = Slave(ModbusTcpServer, FramerType.SOCKET, socket.SOCK_STREAM, port=port)
        try:
            statuses.clear()
            assert _wait(lambda: Status.OK in statuses, 8)
        finally:
            second.close()
    finally:
        engine.stop()


def _wait(cond, seconds=5.0) -> bool:
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        if cond():
            return True
        time.sleep(0.02)
    return False


@pytest.mark.skipif(not hasattr(__import__("os"), "openpty"), reason="needs POSIX pty")
@pytest.mark.parametrize("mode", [SerialMode.RTU, SerialMode.ASCII])
def test_serial_line_through_pty(mode):
    """Real pyserial path: a pty pair stands in for an RS-485 link to a scripted slave."""
    import os

    master_fd, slave_fd = os.openpty()
    tty = os.ttyname(slave_fd)
    stop = threading.Event()

    def device():
        buf = b""
        while not stop.is_set():
            import select

            if not select.select([master_fd], [], [], 0.05)[0]:
                continue
            buf += os.read(master_fd, 256)
            if mode is SerialMode.RTU and len(buf) >= 8:
                pdu = framing.rtu_unframe(buf[:8], 7)
                buf = buf[8:]
            elif mode is SerialMode.ASCII and buf.endswith(b"\r\n"):
                pdu = framing.ascii_unframe(buf, 7)
                buf = b""
            else:
                continue
            assert pdu == protocol.read_request(3, 4, 2)
            reply = b"\x03\x04\x12\x34\xAB\xCD"
            frame = framing.rtu_frame(7, reply) if mode is SerialMode.RTU else framing.ascii_frame(7, reply)
            os.write(master_fd, frame)

    t = threading.Thread(target=device, daemon=True)
    t.start()
    m = ModbusMaster(ConnectionSettings(kind=ConnectionKind.SERIAL, serial_mode=mode, port=tty, baudrate=19200,
                                        response_timeout_ms=1000))
    try:
        m.connect()
        assert m.transact(7, protocol.read_request(3, 4, 2)) == [0x1234, 0xABCD]
    finally:
        m.close()
        stop.set()
        t.join(2)
        os.close(master_fd)
        os.close(slave_fd)
