# Release checklist (GitHub)

Facts behind each item: `outputs/github-release-legal.md` (sources quoted there). Not legal advice.

## Must do before the first push

- [x] **Choose a product name.** MultiPoll Studio was selected on 2026-10-30; the executable, registry key,
      local-data directory, Python package/CLI and file identifiers have been renamed.
- [ ] **Complete name/trademark clearance before public release.** Choosing a different name is not legal clearance.
- [x] **Release authorization.** The project owner confirmed on 2026-10-30 that they are authorized to release
      this code publicly under GPL-3.0-or-later. This confirmation does not independently establish the legal
      identity of the copyright holder; publisher identity remains to be documented.
- [ ] **Keep third-party material out.** `.gitignore` excludes `re/artifacts/`, `re/chm/`, `re/out/`, every `*.exe`,
      `*.chm`, `*.pdf`. Git keeps deleted files in history, so check `git status` before the first commit; never
      `git add -f` those paths. AOSen.exe (Volison) and vendor manuals are not redistributed.
- [ ] **Run a secret scan** (`gitleaks detect`) on the history before pushing.
- [ ] Set the repository's security reporting: enable *Private vulnerability reporting* (see `SECURITY.md`).

## Already in place

- License GPL-3.0-or-later in `LICENSE`; `NOTICE.md` with the MODBUS® trademark line, non-affiliation with Witte
  Software, device-profile provenance and the no-warranty note.
- `re/REPORT.md` records a static study of the commercial application. This is not a formal clean-room process or
  a legal clearance. Review source, documentation, assets and complete Git history before publication; exclude
  proprietary copies and consult qualified counsel where needed. The conclusions of cases from other jurisdictions
  do not by themselves establish an Indonesian or worldwide legal result.
- Device profiles contain register facts with their sources; unverified facts are flagged in the file and the UI.
- `SECURITY.md`, `CONTRIBUTING.md` (DCO sign-off), `README.md`.

## Before a binary release

- [ ] Decide binary signing and document platform requirements. Unsigned EXEs can be blocked by Windows security
      policy; signing is not a guarantee of SmartScreen reputation or acceptance.
- [ ] Attach `MultiPollStudio.exe` and its SHA-256 to a GitHub Release; do not commit it.
- [ ] Decide the publisher identity (person or PT AGS, country) — it decides the EU CRA role (roadmap brief §2.5).
