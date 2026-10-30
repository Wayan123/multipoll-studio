# MultiPoll Studio for Windows

A native, lightweight Windows version of MultiPoll Studio, written in **C# WinForms on .NET Framework 4.8**. It has the
features of the Python version (`../openpoll`) plus the long-run/industrial features listed below (see
`CHANGELOG.md`; the Python edition does not have those yet).

- **One EXE, ~350 KiB**, no installer, no extra DLLs (the trend chart uses the charting library that ships with .NET Framework). .NET Framework 4.8 is already included in Windows 10 (1903+) and
  Windows 11, so no runtime download is needed. Startup and memory use are far below Tauri/Electron.
- Does not need admin rights (`asInvoker`) and is DPI-aware per monitor.
- Settings are stored in `HKCU\Software\MultiPollStudio`; the write audit log is `%LOCALAPPDATA%\MultiPollStudio\audit.jsonl`. Document files (`.mpoll`) and workspaces (`.mpollw`) use JSON
  in the same format as the Python version, so files can be swapped between them as long as they only use the ten
  Modbus Poll display formats (the Python version does not know the 0.3 data types yet).
- GPL-3.0-or-later (see `../openpoll/LICENSE`).

## 0.5.0 naming and file cutover

The application is now **MultiPoll Studio**, executable `MultiPollStudio.exe`.
New documents use `.mpoll` (`multipoll-document`), workspaces `.mpollw` (`multipoll-workspace`),
and device profiles use `multipoll-profile`. The Python edition uses the same new identifiers.
The old `.opoll` / `.opollw` format identifiers and old profile identifier are not accepted.
Existing files, `HKCU\Software\OpenPoll`, and `%LOCALAPPDATA%\OpenPoll` are left untouched; there is no
automatic import of old settings or data. Recreate documents/workspaces and update custom profile identifiers
deliberately, keeping the original files as backups. Review connection targets and write protection before connecting.

## Toolbar

The toolbar below the menu provides New/Open/Save/Print/Close, connect/disconnect active target,
Poll Setup, Read Once, periodic polling toggle, FC05/06/15/16/22/23, Traffic, Frame Console and Help.
Hover for the command, shortcut and exact active ID/port/host. The label at the right also shows the active target.
Buttons that need a connection are disabled when that target is disconnected; document commands still work offline.
**Connect/Disconnect on the toolbar affect the active connection**, including other windows sharing that bus.
The menu's F3/F4/F5 connection commands retain their existing application-wide/main-connection behavior.
Frame Console follows the active window's connection; its raw bytes must still contain the intended ID.
Traffic observes all connections. Function-code buttons only open dialogs: Send still enforces write protection,
confirmation and audit. `?` opens the troubleshooting guide; `What's this?` / Shift+F1 enters help mode:
click a toolbar command to explain it without executing it, or Escape to cancel.
View → Toolbar hides/shows the bar; buttons move to its overflow menu on narrow windows.
Toolbar icons are drawn by this project, not extracted from Modbus Poll.

## Features

| Area | Details |
|---|---|
| Connection | Serial RTU/ASCII (COM), Modbus TCP, Modbus UDP, RTU/ASCII over TCP, RTU/ASCII over UDP |
| Timing | Connect timeout, response timeout (with a "Suggest" button for serial from baud and frame size), minimum delay between polls; network links reconnect with back-off 2 s → 30 s ±10 % jitter |
| Serial | Baud, data bits, parity, stop bits, RTS toggle + delay (RS-485), DSR/CTS flow, remove echo |
| Reads | 01, 02, 03, 04; scan interval per window, periodic polling toggle, single read |
| Writes | 05, 06, 15, 16, 22 Mask Write, 23 Read/Write; or type straight into the grid (Space toggles a coil) |
| Display | 16/32/64-bit signed/unsigned, float32/64, BCD16/32, Unix time 32-bit, ASCII string (1–64 registers), bit field, with explicit byte-order labels ABCD, CDAB, BADC or DCBA; per window or per cell |
| Interpret As | Ctrl+I or context menu: the selected registers decoded as every type × byte order side by side; follows selection and live values; *Use for cell* applies the chosen row |
| Grid | Multiple MDI windows, alias per address, PLC addresses (40001) or protocol addresses (00000), rows 10/20/50/100 |
| Diagnostics | Requests, errors, unit, function and interval plus success % and RTT; error counters, traffic (sequence, direction and hex bytes), Frame Console (CRC/LRC); stale values shown grey |
| Tags | Per address: name, scale/offset (value = raw × scale + offset), unit, decimals, write limits; grid shows and accepts engineering values; CSV import/export (Setup menu, Ctrl+T) |
| Alarms | HH/H/L/LL in engineering units with per-limit deadband (H=20, deadband 1 clears below 19), cell colours, count in status line, events in the log, alarm history |
| Trend | Up to 12 values from any windows (context menu "Add to Trend", Ctrl+R), 1 min–24 h window, gaps when a poll fails, CSV/PNG export |
| Logging | CSV/TSV/fixed, ISO-8601 with ms and UTC offset or `Z`, header row with tag names/units, status and RTT columns, failed polls kept as rows, on-change with deadband + heartbeat, daily or size rotation with "keep N files" |
| Safe writes | Write protection switch (Connection menu, Ctrl+Shift+W) refuses every write locally, including Frame Console frames that are not known reads (FC08 restart/listen-only, broadcasts); optional confirmation showing target, function, address and old→new value; append-only audit log with a SHA-256 hash chain |
| Discovery | Read-only unit-ID scan (cancelable, delay between probes, warning about live plants); FC43/14 Read Device Identification |
| Find Device | Ctrl+F: read-only search over baud × parity (× 8N2) × ID, common defaults first, quick or full range, time estimate; recognises profiled devices by their model register; *Use* applies baud, parity and ID |
| Device profiles | Ctrl+D: built-in maps for ADM-4204C/V, BGT-SMPS, PZEM-014/016, SDM120 (new and legacy), XY-MD02, JXBS-3001-TR/-NPK, RS-…-N01; one window per register block with names, scaling, units, types, write limits; own profiles as JSON in `%LOCALAPPDATA%\MultiPollStudio\profiles` |
| Device Settings | Ctrl+E: set slave ID, baud, parity and other settings with one click (no register or function code to type), windows and connection follow, value read back to verify; factory reset / reset energy; find a forgotten ID via the device's general address |
| Diagnosis | *Why? / Fix…* on a failed window (Ctrl+G): likely causes, manual checks, and fixes that never write to the device (find device, raise timeout, FC03↔FC04, probe valid addresses, poll the ID that answered, Remove Echo, address −1, poll slower, reconnect); Help → Troubleshooting Guide (F1) |
| Connections | Main connection plus an own COM port or TCP host per window (Connection → Window Connection); same port = shared and polled in turn, different ports poll in parallel |
| Other | Save/open documents (tags included) and workspaces, Use as Default, Print, Always On Top |

Shortcuts: F3 Connect, F4 Disconnect, F5 Quick Connect, F8 Poll Setup, F6 Read Once,
Shift+F6 toggle polling, Alt+F5…F8 write 05/06/15/16, Alt+T Frame Console, F11 Error Counters, F12 Reset, Alt+L/Alt+O log.

Frame Console list files now use the first line `MultiPoll Studio Frame List` in both editions. Previous lists with
`Test Center` as the first line must be updated before opening; preserve a backup. Existing `.mpoll` format keys,
byte order semantics and Modbus protocol framing have not changed.

## Build

There is no `.csproj` or NuGet. `build.sh` calls the `csc.exe` (Roslyn) that ships with Visual Studio and is run from WSL:

```sh
./build.sh            # dist/MultiPollStudio.exe (Release)
./build.sh Debug      # with PDB
./build.sh uitest     # dist/MultiPollStudioUiTest.exe (GUI smoke test)
```

Set `CSC=/path/to/csc.exe` if Visual Studio is somewhere else. Visual Studio Build Tools (free) also provides
Roslyn `csc.exe`. The old `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` is **not** enough: it only
supports C# 5, while this code uses C# 7.3 (exception filters, `async`/`await` with expression-bodied lambdas).

## Trying it out

1. Run a demo slave (in WSL or anywhere): `python tools/demo_slave.py --port 5020`
   (needs `pip install 'pymodbus>=3.8,<3.9'`). It opens TCP :5020, RTU-over-TCP :5021, UDP :5022, ASCII-over-TCP :5023.
2. Run `dist\MultiPollStudio.exe`, press F3, choose Modbus TCP/IP, 127.0.0.1, port 5020, OK.
   UDP from Windows to WSL: use the WSL IP (`ip addr show eth0`), because WSL's localhost forwarding only carries TCP.
3. Real sensor via USB-RS485: F3, Serial Port, pick the COM port, 9600 8N1, RTU; F8, slave 1, function 03, address 0, quantity 8.

## Tests

```sh
MultiPollStudio.exe --selftest                          # offline: CRC/LRC/MBAP, PDUs, data types × byte orders, addresses, documents, tags, alarms, logging, rotation, audit chain, KPIs
MultiPollStudio.exe --selftest <host> 5020              # + every function code over TCP/RTU/UDP/ASCII, engine, write protection, audit, scan, FC43
MultiPollStudio.exe --selftest --serial=COM26           # + read-only FC03 against a real serial slave (never writes)
MultiPollStudioUiTest.exe <host> 5020 <outdir>          # GUI: 4 windows, grid edit, every write dialog, Test Center, save/load, log, tags/alarms, protection, trend, data types, Interpret As, reconnect
MultiPollStudioUiTest.exe COM26 9600 <outdir>           # GUI read-only against a real serial slave (write protection on, scaling, log, trend, Find Device)
MultiPollStudioUiTest.exe devices <host> 5030 <outdir>  # device features against tools/device_sim.py: profiles, Device Settings, recovery, diagnosis fixes, Find Device, two connections
MultiPollStudioUiTest.exe toolbar <host> 5030 <outdir>  # active-ID/bus routing, pause/read, connect/disconnect, protected write, raw read, context help
```

Add `--out=<file>` to write the self-test results to a file. The exit code equals the number of failures.
`MultiPollStudioUiTest.exe` uses a unique registry key (`HKCU\Software\MultiPollStudio-UiTest-<GUID>`, removed afterwards) and audit files under its output directory. Use a separate output directory for each run.
Run the test EXEs from a Windows folder: the window icon cannot be loaded from a `\\wsl.localhost\` path.

`tools/device_sim.py` (standard library only) simulates ADM-4204V (ID 1), PZEM-016 (2, also 0xF8), SDM120 (3), BGT-SMPS
(4, also 0xFE) and XY-MD02 (5) on one bus: RTU over TCP on the given port, Modbus TCP on port + 1. Setting writes
change the simulated device (an ID change answers from the old ID, then takes effect).

## Code layout

```
src/Core/Protocol.cs   PDUs, validation, status codes
src/Core/Framing.cs    RTU CRC-16, ASCII LRC, MBAP, hex
src/Core/Transport.cs  SerialPort / TCP / UDP, ModbusMaster
src/Core/Engine.cs     single worker thread: one-shot jobs before periodic polls
src/Core/Formats.cs    data types × byte orders (DisplayFormat), text/number conversion, Interpret As table
src/Core/Model.cs      PollDefinition, ViewOptions, PLC addresses
src/Core/Support.cs    traffic, Test Center, renderer, logger + rotation, audit log, JSON
src/Core/Tags.cs       tags, scaling, alarm evaluation, tag map CSV
src/Core/Discovery.cs  unit-ID scan, FC43/14 device identification
src/Core/LineSearch.cs Find Device: baud × parity × ID search
src/Core/Diagnosis.cs  status → causes, checks, one-click fixes; troubleshooting guide
src/Core/Profiles.cs   device profile JSON loader (strict), settings encoding, recovery and custom requests
src/UI/*.cs            MainForm (MDI, connections, fixes), PollWindow (grid), dialogs, DeviceDialogs, TrendWindow
profiles/*.json        built-in device profiles (embedded at build time)
src/SelfTest.cs        --selftest
tests/UiSmoke.cs       MultiPollStudioUiTest.exe
tests/ToolbarSmoke.cs  active-target toolbar regression scenario
```

## Device profile format

One JSON file per device (`"format": "multipoll-profile"`, `"version": 1`). Unknown keys are rejected so a typo does
not silently drop a setting. Addresses are protocol addresses (base 0). Main parts, see `profiles/` for examples:

| Key | Content |
|---|---|
| `id`, `name`, `vendor`, `category`, `status` | `status`: `observed` (all facts from a manufacturer manual) or `partial` |
| `serial` | `baud`, `parity` (`N`/`E`/`O`), `data_bits`, `stop_bits`, `baud_fixed`; `slave_id` = default ID |
| `identify` | `function`, `register`, `values` [{`value`, `label`}]: model register that Find Device reads |
| `blocks` | windows: `name`, `function`, `address`, `quantity`, `scan_rate_ms`, `points` [{`offset`, `name`, `format`, `scale`, `offset_eu`, `unit`, `decimals`, `write_min`, `write_max`}] |
| `settings` | `key`, `name`, `role` (`slave_id`/`baud`/`parity`/`other`), `read_function` (0 = not readable), `write_function` (6/16/0), `register`, `encoding` (`u16`/`f32`), `min`/`max` or `choices` [{`value`, `label`, `baud`, `parity`, `stop_bits`}], `apply` (`immediate`/`auto_restart`/`power_cycle`), `verified`, `note` |
| `actions` | `key`, `name`, `confirm`, `steps` [{`function`, `register`, `value`/`values`} or {`raw_pdu`: hex}], `verified` |
| `recovery` | `address` (general address), `function`, `register`, `count`, `id_from` (`reply` = the answering address, `register` = the value), `verified`, `note` |
| `before_write` | instruction shown before any setting write (e.g. a front-panel set mode) |
| `sources` | [{`title`, `url`, `status`}] where every fact comes from |

## Tag map CSV

`Setup → Export Tag Map` writes, and `Import Tag Map` reads (strict: bad rows are rejected with their line number):

```
offset,name,scale,offset_eu,unit,decimals,hh,h,l,ll,deadband,write_min,write_max,format
0,Suhu,0.1,0,°C,1,,45,0,,0.5,,,
2,,1,0,,,,,,,0,,,u32_cdab
7,pH,0.01,0,,2,,8.5,5.5,,0.1,,,
```

`offset` is the position in the window (0 = first address). Empty cells keep defaults (scale 1, offset 0, no limit).
An optional last column `format` sets the cell's display format with the same keys as `.mpoll` files.

## Format keys

Documents and tag maps store a format as a short key. The ten presets keep the Modbus Poll / Python names
(`signed`, `float_inverse`, …). Other formats are a type plus a byte-order suffix when the order is not ABCD:

| Type | Key | Registers |
|---|---|---|
| Signed / unsigned / hex / binary 16-bit | `i16`, `u16`, `hex16`, `bin16` (`_ba` = bytes swapped) | 1 |
| BCD | `bcd16`, `bcd32` | 1, 2 |
| 32-bit | `i32`, `u32`, `f32`, `unix32` (seconds since 1970, UTC) | 2 |
| 64-bit | `i64`, `u64`, `f64` | 4 |
| ASCII string, *n* registers | `str8` = 8 registers / 16 characters | *n* (1–64) |
| Bit field, first bit *s*, *w* bits | `bits4.3` = bits 4–6 | 1 |

Order suffixes: `_cdab` (word swap, Modbus Poll "Inverse"), `_badc` (bytes swapped in each register), `_dcba`
(little-endian). Example: `f32_badc`. Writing a bit field merges the new bits into the register's last read value and
sends FC06; a stale or missing value refuses the write. Integer cells accept `0x…` as a raw bit pattern
(`0xFFFF` in a Signed cell is -1). Unix time accepts seconds or an ISO-8601 date (`2026-01-01T07:00:00+07:00`).
