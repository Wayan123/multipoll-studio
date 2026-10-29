"""Background polling engine shared by every poll window.

A single worker thread owns the ModbusMaster. It services queued one-shot jobs
(writes, Frame Console frames, read-once) before periodic reads, honours each
window's scan rate, and enforces the connection's minimum delay between polls.
Results are delivered through callbacks invoked on the worker thread; GUI code
must marshal them to its own thread.
"""
from __future__ import annotations

import threading
import time
from collections import deque
from concurrent.futures import Future
from dataclasses import dataclass, field
from typing import Callable

from . import protocol
from .model import PollDefinition
from .protocol import ModbusError, Status
from .transport import ConnectionSettings, ModbusMaster

RECONNECT_INTERVAL_S = 2.0


@dataclass
class PollStats:
    tx: int = 0
    errors: int = 0
    by_status: dict[Status, int] = field(default_factory=dict)

    def record(self, status: Status) -> None:
        self.tx += 1
        if status is not Status.OK:
            self.errors += 1
            self.by_status[status] = self.by_status.get(status, 0) + 1

    def reset(self) -> None:
        self.tx = 0
        self.errors = 0
        self.by_status.clear()


@dataclass
class PollResult:
    poll_id: int
    definition: PollDefinition
    status: Status
    values: list[int] | None
    detail: str
    timestamp: float


ResultCallback = Callable[[PollResult], None]


@dataclass
class _Poll:
    poll_id: int
    definition: PollDefinition
    callback: ResultCallback
    stats: PollStats = field(default_factory=PollStats)
    next_due: float = 0.0
    once: bool = False


class PollEngine:
    def __init__(self, on_traffic: Callable[[str, bytes], None] | None = None,
                 on_connection: Callable[[bool, str], None] | None = None):
        self._on_traffic = on_traffic
        self._on_connection = on_connection
        self._master: ModbusMaster | None = None
        self._polls: dict[int, _Poll] = {}
        self._jobs: deque[tuple[Callable[[ModbusMaster], object], Future]] = deque()
        self._cv = threading.Condition()
        self._thread: threading.Thread | None = None
        self._running = False
        self._next_id = 1
        self._last_tx = 0.0
        self._last_connect_attempt = 0.0
        self.settings: ConnectionSettings | None = None

    # -- lifecycle --------------------------------------------------------------
    def start(self, settings: ConnectionSettings) -> None:
        """Open the connection synchronously (raising ModbusError on failure), then start polling."""
        self.stop()
        master = ModbusMaster(settings, on_traffic=self._on_traffic)
        master.connect()
        with self._cv:
            self.settings = settings
            self._master = master
            self._running = True
            for p in self._polls.values():
                p.next_due = 0.0
        self._thread = threading.Thread(target=self._run, name="multipoll-engine", daemon=True)
        self._thread.start()
        self._notify_connection(True, settings.describe())

    def stop(self) -> None:
        with self._cv:
            was_running = self._running
            self._running = False
            self._cv.notify_all()
        if self._thread is not None:
            self._thread.join(timeout=5)
            self._thread = None
        if self._master is not None:
            self._master.close()
            self._master = None
        with self._cv:
            while self._jobs:
                _, fut = self._jobs.popleft()
                fut.set_exception(ModbusError(Status.NO_CONNECTION))
        if was_running:
            self._notify_connection(False, "Disconnected")

    @property
    def running(self) -> bool:
        return self._running

    def _notify_connection(self, up: bool, text: str) -> None:
        if self._on_connection:
            self._on_connection(up, text)

    # -- poll registration ----------------------------------------------------------
    def add_poll(self, definition: PollDefinition, callback: ResultCallback) -> int:
        with self._cv:
            pid = self._next_id
            self._next_id += 1
            self._polls[pid] = _Poll(pid, definition, callback)
            self._cv.notify_all()
            return pid

    def update_poll(self, poll_id: int, definition: PollDefinition) -> None:
        with self._cv:
            p = self._polls[poll_id]
            p.definition = definition
            p.next_due = 0.0
            self._cv.notify_all()

    def remove_poll(self, poll_id: int) -> None:
        with self._cv:
            self._polls.pop(poll_id, None)

    def read_once(self, poll_id: int) -> None:
        with self._cv:
            p = self._polls[poll_id]
            p.once = True
            self._cv.notify_all()

    def stats(self, poll_id: int) -> PollStats:
        return self._polls[poll_id].stats

    def reset_stats(self, poll_id: int) -> None:
        with self._cv:
            self._polls[poll_id].stats.reset()

    # -- one-shot jobs ----------------------------------------------------------------
    def submit(self, job: Callable[[ModbusMaster], object]) -> Future:
        """Run ``job(master)`` on the worker thread ahead of periodic polls."""
        fut: Future = Future()
        with self._cv:
            if not self._running:
                fut.set_exception(ModbusError(Status.NO_CONNECTION))
                return fut
            self._jobs.append((job, fut))
            self._cv.notify_all()
        return fut

    def write(self, slave: int, pdu: bytes) -> Future:
        return self.submit(lambda m: m.transact(slave, pdu))

    def raw(self, data: bytes) -> Future:
        return self.submit(lambda m: m.raw_exchange(data))

    # -- worker -------------------------------------------------------------------------
    def _respect_min_delay(self) -> None:
        delay = self.settings.delay_between_polls_ms / 1000
        wait = self._last_tx + delay - time.monotonic()
        if wait > 0:
            time.sleep(wait)

    def _ensure_link(self) -> bool:
        """Reconnect a dropped network link at most every RECONNECT_INTERVAL_S seconds."""
        if self._master.connected:
            return True
        now = time.monotonic()
        if now - self._last_connect_attempt < RECONNECT_INTERVAL_S:
            return False
        self._last_connect_attempt = now
        try:
            self._master.connect()
        except ModbusError as exc:
            self._notify_connection(False, exc.detail or exc.status.text)
            return False
        self._notify_connection(True, self.settings.describe())
        return True

    def _run(self) -> None:
        while True:
            with self._cv:
                if not self._running:
                    return
                job = self._jobs.popleft() if self._jobs else None
                poll = None
                if job is None:
                    now = time.monotonic()
                    ready = [p for p in self._polls.values()
                             if p.once or (p.definition.enabled and p.next_due <= now)]
                    if ready:
                        poll = min(ready, key=lambda p: (not p.once, p.next_due))
                    else:
                        due = [p.next_due for p in self._polls.values() if p.definition.enabled]
                        timeout = max(0.0, min(due) - now) if due else None
                        self._cv.wait(timeout=min(timeout, 0.5) if timeout is not None else 0.5)
                        continue
            if job is not None:
                self._run_job(*job)
            else:
                self._run_poll(poll)

    def _run_job(self, job: Callable[[ModbusMaster], object], fut: Future) -> None:
        if not fut.set_running_or_notify_cancel():
            return
        self._respect_min_delay()
        try:
            if not self._ensure_link():
                raise ModbusError(Status.NO_CONNECTION)
            result = job(self._master)
        except BaseException as exc:  # delivered to the caller through the future
            fut.set_exception(exc)
        else:
            fut.set_result(result)
        finally:
            self._last_tx = time.monotonic()

    def _run_poll(self, poll: _Poll) -> None:
        d = poll.definition
        with self._cv:
            poll.once = False
            poll.next_due = time.monotonic() + d.scan_rate_ms / 1000
        self._respect_min_delay()
        values, detail = None, ""
        try:
            if not self._ensure_link():
                raise ModbusError(Status.NO_CONNECTION)
            values = self._master.transact(d.slave_id, protocol.read_request(d.function, d.address, d.quantity))
            status = Status.OK
        except ModbusError as exc:
            status, detail = exc.status, exc.detail
        except ValueError as exc:
            status, detail = Status.RESPONSE_ERROR, str(exc)
        finally:
            self._last_tx = time.monotonic()
        with self._cv:
            if poll.poll_id not in self._polls:
                return
            poll.stats.record(status)
        poll.callback(PollResult(poll.poll_id, d, status, values, detail, time.time()))
