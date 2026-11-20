# Contributing to PC Cleaner

Thank you for helping. PC Cleaner is free and open source under the Apache License 2.0, and contributions
are welcome: bug reports, ideas, fixes and new features.

## Before you start

- **Found a bug or something that deleted the wrong thing?** Open an issue with the matching template.
  Anything that removes a file it should not have is the most serious kind of bug here, so say what it
  removed and what you expected.
- **Want to add a feature?** Open a feature request first, so we can agree on the shape before you spend
  time on it.
- **Found a security problem?** Please do not open a public issue. See [SECURITY.md](SECURITY.md).

## Building and testing

You need the .NET 10 SDK (the repo pins it in `global.json`) and, for the app, the MAUI workloads:

```powershell
dotnet workload install maui-windows maccatalyst
dotnet build src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-windows10.0.19041.0
dotnet test tests/PcCleaner.Core.Tests/PcCleaner.Core.Tests.csproj
```

On a Mac, build the `net10.0-maccatalyst` target instead. The README has the details.

## Pull requests

- Keep a change to one thing. A bug fix does not need a refactor alongside it.
- Fill in the pull request template. It asks what you ran, not what you meant to run, and whether anything
  could delete the wrong thing.
- Add or update tests for logic in `PcCleaner.Core` and `PcCleaner.Presentation`. Both run in CI on Windows,
  macOS and Linux.
- The build treats warnings as errors.
- Follow [DESIGN.md](DESIGN.md) for anything on screen: use the tokens instead of literal colours or sizes,
  keep text in sentence case, and keep the tone calm and welcoming.

Everything you contribute is licensed under the Apache License 2.0, as section 5 of the licence says.

## Name and icon

The project's name and icon are trademarks of WiseLead Solutions and are not covered by the code licence.
See [TRADEMARKS.md](TRADEMARKS.md).
