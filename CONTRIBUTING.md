# Contributing

- Windows edition: `cd openpoll-win && ./build.sh && ./build.sh uitest` (WSL + Visual Studio's Roslyn `csc`, C# 7.3,
  no NuGet). Run `MultiPollStudio.exe --selftest`, then the GUI tests against the simulators:
  `python tools/demo_slave.py --port 5020` (needs `pymodbus>=3.8,<3.9`) and `python tools/device_sim.py --port 5030`,
  then `MultiPollStudioUiTest.exe 127.0.0.1 5020 <dir>` and `MultiPollStudioUiTest.exe devices 127.0.0.1 5030 <dir>`.
  Run the test EXEs from a Windows folder, not a `\\wsl.localhost\` path.
- Python edition: `cd openpoll && pip install -e .[test] && pytest`.
- Device profiles: one JSON file per device in `openpoll-win/profiles/`, schema in `openpoll-win/README.md`. Every
  register must come from the manufacturer's manual; cite it in `sources`. Mark anything you could not confirm
  `"verified": false` and set `"status": "partial"`. Do not add manuals or vendor software to the repository.
- Never commit third-party binaries, help files or manuals (see `.gitignore`).
- Sign off your commits (`git commit -s`, Developer Certificate of Origin).
