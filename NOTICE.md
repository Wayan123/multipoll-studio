# Notices

## Independence

MultiPoll Studio implements the public Modbus protocols. Its development history includes a static study of
commercial software resources and documentation to identify behavior; this was not a formal clean-room process.
The proprietary binaries, help files and extracted resources are not included in the project distribution.
The project is not affiliated with the original vendor. `re/REPORT.md` documents the recorded study and its limits;
it is not a legal clearance or a guarantee that every similarity has been identified.

## Trademarks

- MODBUS® is a registered trademark of Schneider Electric USA, Inc., used under license by Modbus Organization, Inc.
  MultiPoll Studio is not affiliated with or certified by Schneider Electric or the Modbus Organization.
- "Modbus Poll" and "Modbus Slave" are products of Witte Software. MultiPoll Studio is not affiliated with Witte Software.
- Device and vendor names in `openpoll-win/profiles/` (Volison, Peacefair, Eastron, JXCT, and others) identify the
  devices a profile describes. They belong to their owners; no endorsement is implied.

## Device profiles

The profiles in `openpoll-win/profiles/*.json` list sources for register maps and configuration facts. Profiles
marked `"status": "partial"` or settings marked `"verified": false` include facts not confirmed in a
manufacturer-hosted document. Check the actual device manual before writing settings. Manuals are not redistributed.

## No warranty

MultiPoll Studio can write to industrial devices. It is provided without warranty (GPL-3.0 §15–16). Use write protection on
live plants, and only change device settings on equipment you are authorised to configure.
