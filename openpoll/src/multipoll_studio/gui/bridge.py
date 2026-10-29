"""Thread marshalling: run callables on the Qt GUI thread from worker threads."""
from __future__ import annotations

from typing import Callable

from PySide6.QtCore import QObject, Signal


class _Bridge(QObject):
    call = Signal(object)

    def __init__(self):
        super().__init__()
        self.call.connect(lambda fn: fn())  # queued: emitted from worker, executed in GUI thread


_bridge: _Bridge | None = None


def install() -> None:
    """Create the bridge in the GUI thread. Must run after QApplication exists."""
    global _bridge
    if _bridge is None:
        _bridge = _Bridge()


def post_to_gui(fn: Callable[[], None]) -> None:
    _bridge.call.emit(fn)
