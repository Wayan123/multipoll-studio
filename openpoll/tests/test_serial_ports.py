import errno
import sys

import pytest
import serial
from serial.tools import list_ports
from serial.tools.list_ports_common import ListPortInfo

from multipoll_studio import transport
from multipoll_studio.app import main


@pytest.mark.skipif(sys.platform != "linux", reason="Linux udev serial aliases")
def test_ports_prefers_adapter_id_then_physical_port(tmp_path, monkeypatch, capsys):
    serial_dir = tmp_path / "serial"
    by_id = serial_dir / "by-id"
    by_path = serial_dir / "by-path"
    by_id.mkdir(parents=True)
    by_path.mkdir()

    devices = [tmp_path / f"ttyUSB{i}" for i in range(3)]
    for device in devices:
        device.touch()
    stable = by_id / "usb-adapter"
    stable.symlink_to("../../ttyUSB0")
    physical = by_path / "usb-socket"
    physical.symlink_to("../../ttyUSB0")
    second = by_path / "usb-other-socket"
    second.symlink_to("../../ttyUSB1")
    (by_id / "usb-unplugged").symlink_to("../../ttyUSB9")

    ports = [ListPortInfo(str(device)) for device in devices]
    ports[0].description, ports[0].vid, ports[0].pid = "USB Serial", 0x1A86, 0x7523
    monkeypatch.setattr(list_ports, "comports", lambda: ports)
    monkeypatch.setattr(transport, "_LINUX_SERIAL_DIR", serial_dir)

    assert main(["ports"]) == 0
    rows = capsys.readouterr().out.splitlines()
    assert rows[0] == f"{stable}\tUSB Serial [1A86:7523]"
    assert [row.split("\t")[0] for row in rows] == [str(stable), str(second), str(devices[2])]

    stable.unlink()
    assert main(["ports"]) == 0
    assert capsys.readouterr().out.splitlines()[0].split("\t")[0] == str(physical)


@pytest.mark.skipif(sys.platform != "linux", reason="Linux serial access guidance")
def test_read_reports_access_error_without_suggesting_root(monkeypatch, capsys):
    def denied(**kwargs):
        raise serial.SerialException(errno.EACCES, "Permission denied")

    monkeypatch.setattr(serial, "Serial", denied)
    assert main(["read", "--rtu", "/dev/ttyUSB0"]) == 2
    stderr = capsys.readouterr().err
    assert "/dev/ttyUSB0" in stderr
    assert "dialout" in stderr
