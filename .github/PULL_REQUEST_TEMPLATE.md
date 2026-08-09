<!--
Keep this short. Delete any section that doesn't apply rather than writing "N/A" in it.
The diff says what changed; this should say why, and what you actually checked.
-->

## What and why

<!-- What this changes, and the problem it solves. Link the issue if there is one: Fixes #123 -->

## How it was verified

<!--
Be specific about what you ran, not what you intended to run. "Built on Windows" and "I think it's
fine" are different claims — say which one is true. There is no Linux target and no CI on tags, so
the PR build below is often the first time anything gets compiled.
-->

- [ ] `dotnet build src/PcCleaner.App/PcCleaner.App.csproj -f net10.0-windows10.0.19041.0`
- [ ] `dotnet test tests/PcCleaner.Core.Tests/PcCleaner.Core.Tests.csproj`
- [ ] Ran the app and exercised the change by hand
- [ ] Checked both light and dark themes (any UI change)

Platform tested on: <!-- Windows 11 / macOS 15 / not run — CI only -->

## Anything that could delete the wrong thing?

<!--
Delete this section unless the change touches scanning, selection, or deletion.

This app removes files. A scanner that reports a path it shouldn't, a selection default that
pre-ticks something risky, or a delete that skips its confirmation is the most damaging class of
bug here — junk cleaning is a permanent delete with no Recycle Bin round-trip. If this PR touches
any of that, say what you did to convince yourself it's safe.
-->

## UI changes

<!--
Delete this section if there are none. Otherwise:
- Does it follow DESIGN.md — tokens rather than literals, Signal as the only accent, Danger only on
  destructive actions, mono numerals for figures?
- Is there a designed empty state, or does the screen just go blank?
- If progress is shown, is it honest? The scanners report a file count, not a percentage.
- Screenshots of both themes are worth more than a description.
-->

## Notes for the reviewer

<!-- Anything you're unsure about, deliberately left out, or want a second opinion on. -->
