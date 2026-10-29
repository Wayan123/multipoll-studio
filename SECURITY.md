# Security Policy

## Supported versions

Only the latest release of each edition (`openpoll-win`, `openpoll`) receives fixes.

## Reporting a vulnerability

Please report privately through GitHub's "Report a vulnerability" button (Security → Advisories) on this repository.
Do not open a public issue for a vulnerability. Expect an acknowledgement within 7 days.

## Scope

In scope: MultiPoll Studio's own code, including parsing of `.mpoll` / `.mpollw` documents, tag-map CSV files, device-profile
JSON files and Modbus responses, and the write-protection and audit-log features.

Out of scope: weaknesses of the Modbus protocol itself (no authentication or encryption), devices MultiPoll Studio talks to,
and third-party device profiles you load from your own folder. Write protection guards against operator mistakes; it
is not an access-control mechanism.
