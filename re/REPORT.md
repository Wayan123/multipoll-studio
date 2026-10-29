# Modbus Poll 5.0.1 (build 450): reverse-engineering report

Analysis target: the Witte Software Modbus Poll install at
`C:\Program Files (x86)\Modbus Tools\Modbus Poll\` on this laptop.
The goal is a behavioral specification for writing an independent open-source alternative (`../openpoll`).

## Method and limits

- **Static analysis only.** The EXE was never run, debugged, or patched. The registration and trial
  mechanism (`Software\Nogle\aaa1\Year`, `Software\Witte Software\mbpoll\Register\Key`, string
  "Error in registration key") was noted as **observed** and deliberately **not analyzed further**.
- **No code was copied.** Nothing was decompiled or ported. OpenPoll is a clean implementation of the
  public Modbus specification. Sources: PE resources (menus, dialogs, string tables), the CHM help, and
  format strings in the binary.
- The proprietary artifacts (`artifacts/`, `chm/`, `out/help/`) are local analysis material.
  **Do not redistribute them or put them in a public repo.** The license (`license.txt`) only allows
  unmodified redistribution of the original installer.

| Artifact | SHA-256 |
|---|---|
| `mbpoll.exe` (769,536 B) | `dfcacbdafacd6085b963aa37a7585cf37d7c2f78b9896651d530b4940aab839c` |
| `mbpoll.chm` | `ba3edf580d7163c39e66a2e790725dc62f219c16ca0f89d1fe7d530ed08a4e6d` |
| `mbpoll.tlb` | `c10c759ea338f5151d479bd06da9160647bc5a5b204a5bd5dd63cec2e310880e` |

Reproduce (from `re/`):

```sh
objdump -x artifacts/mbpoll.exe > objdump_x.txt          # header, sections, imports
python tools/dump_resources.py artifacts/mbpoll.exe out   # menus/dialogs/strings
cmd.exe /c "hh.exe -decompile <dir> <chm>"                # CHM -> HTML (Windows hh.exe)
strings -n 4 -el artifacts/mbpoll.exe                     # UTF-16 format strings
```

REA `inspect_pe_resources` evidence: `ev_7f373655b64f08162e1d42d3e8ad8266ec42b8f86242b5936955ba47a81c6de3`
(113 resources, coverage complete).

## Binary facts (observed)

- VERSIONINFO: ProductVersion 5.0.1.450. PE32 i386, Windows GUI, linker 9.0 (Visual Studio 2008),
  timestamp 2010-04-20. MFC MDI application
  with static MFC (`afxwin1.inl` path, `CView`, `CMBPollSetup`, `CErrorCounter` RTTI).
- Imports: `WS2_32` (TCP/UDP), `SETUPAPI` (COM port enumeration), `KERNEL32` comm API
  (`SetCommTimeouts`, `ClearCommError`), `ole32/OLEAUT32` (OLE Automation and Excel), `WINSPOOL` (printing).
- CRC: a 256-entry CRC-16 table (poly 0xA001) at file offset `0x8FD18` (VA `0x491118`).
- Serial port opened as `\\.\COM%d`; settings stored in values `BaudRate DataBits Parity StopBits
  Timeout DTRDSR RTSCTS CLRRTSDelay RTU/ASCII IPAddress ServicePort ConnectionTimeout DelayBetweenPolls Echo`.

## Behavioral specification (implemented in OpenPoll)

**Connection** (dialog 103, help `hid_connection_connect`): five types. Serial RTU/ASCII, Modbus TCP,
Modbus UDP, RTU/ASCII over TCP, RTU/ASCII over UDP. Parameters: connect timeout, response timeout,
minimum delay between polls. Advanced: RTS toggle + RTS disable delay, DSR/CTS flow, remove echo.

**Read/write definition** (dialog 102): slave 0–255 (0 for TCP/UDP), function 01/02/03/04, address
base 0, quantity (coils ≤2000, registers ≤125), scan rate, Read/Write Enabled, Read/Write Once,
rows 10/20/50/100, hide alias, address in cell, PLC addresses (base 1).

**Address labels** (binary format strings `4%04i`, `4%05i`, `3x%04i`, ...): PLC mode = prefix
0/1/3/4 + (address+1), 4 digits, 5 digits once above 9999. Protocol mode = `%05i`.

**Display formats** (menu 129 / help `menu_display`): Signed, Unsigned, Hex, Binary, Long, Long Inverse,
Float, Float Inverse, Double, Double Inverse. "Inverse" = least significant register first. Formats can
be set per cell (the menu applies to the selection).

**Write functions:** 05, 06, 15, 16, 22 (Mask Write), 23 (Read/Write). Dialog 104 picks 06 or 16
for a single value. The grid can be edited directly (F2 / type / space toggles a coil).

**Status line** (string `Tx = %i: Err = %i: ID = %i: F = %02i: SR = %ims`, plus `(DISABLED)`).

**Traffic window** (dialog 112, strings `%06u-`, `Tx:`, `Rx:`, `%02X `): `000123-Tx:01 03 ...`,
Stop/Continue, Stop on Error, Copy, Save.

**Test Center** (dialog 113): raw hex separated by `,` `.` or space, add CRC/LRC (ASCII adds CR LF),
list file whose first line is `Test Center`.

**Logging** (dialog 110): each read / every N seconds, fixed width/comma/tab, log errors, log date,
only on change.

**Error/status codes** (help `automation_setup_functions`, string table 32785–32855):
0 OK, 1 timeout, 2 CRC, 3 response error, 4 write error, 5 read error, 6 port not open, 10 uninitialized,
11 bytes missing, 81h–87h Modbus exceptions; plus Byte count error, Transaction ID error, gateway 0Ah/0Bh.

## Intentional deviations in OpenPoll

| Modbus Poll | OpenPoll | Reason |
|---|---|---|
| OLE Automation (VB/Excel) | `openpoll read/poll/write` CLI | OLE is Windows-only; the CLI is cross-platform and scriptable |
| Excel Log (COM `Excel.Application`) | CSV/TSV log | No Excel dependency; CSV opens directly in Excel/LibreOffice |
| `.mbp` documents / `.mbw` workspaces (format undocumented, not analyzed) | JSON `.opoll` / `.opollw` | Readable and diff-friendly; no dependence on a proprietary format |
| Registration/trial, 2-write limit for unregistered copies (string 32856) | none | Free software |
