# Dashboard: scan and clean, volumes, and tool breakdown

Date: 2026-11-20. Status: approved by the owner on 2026-11-20 and built.

## Intent

The dashboard can only scan. Cleaning means opening each tool in turn and confirming there. The owner wants
to do the whole job from the dashboard: pick the volumes, pick the tools and their parts, then scan and
clean in one go, with or without a confirmation.

## What the owner decided

- Two new actions next to "Scan everything": **Scan and clean** and **Scan and auto clean**.
- **Scan and auto clean removes everything that is ticked, with no confirmation.** This was asked about
  directly, because it changes the app's rule that nothing is deleted without one. Decision: everything ticked.
- The dashboard lists **all drives and volumes with checkboxes**.
- The dashboard lists **every tool, each with its own sub-items**, all tickable.

## Goals

1. Three actions on the dashboard:
   - **Scan everything**: unchanged. Reads, deletes nothing.
   - **Scan and clean**: scans, then shows one combined confirmation (one line per tool, with what is
     permanent and what goes to the Recycle Bin), then cleans every included tool.
   - **Scan and auto clean**: scans, then cleans every included tool with no confirmation.
2. Volumes: every volume the app offers (the same list as Settings), each with a checkbox. Ticking writes
   the same setting Settings writes. Unavailable volumes stay listed and marked.
3. Tools: Junk Files, Duplicates, Large Files, Cloud Drives, Developer (only while developer mode is on),
   Startup. Each row has an include checkbox and expands to its sub-items after a scan:
   - Junk Files: its categories, each with a checkbox and a size.
   - Duplicates: its groups, with the copies that would go.
   - Large Files: its files.
   - Cloud Drives: each cloud folder.
   - Developer: each cleanup item.
   - Startup: read only. It frees no space and is never part of a clean.
4. Unticking a tool skips it in both the scan and the clean.

## Non-goals

- No change to what each tool ticks by default. Large files start unticked, so an auto clean touches a
  large file only if it was ticked by hand. Duplicates keep one copy of every file. Junk leaves the large,
  slow-to-rebuild stores unticked. Cloud files become online-only, not deleted. Docker volumes stay unticked.
- No change to the tool pages or their own confirmations.
- No scheduled or background cleaning.
- No elevation prompt (tracked in issue 3).

## Anti-goals

- Auto clean must never remove something that was not ticked on screen.
- A run that was stopped must not go on to clean. Stopping a scan cancels the clean that would follow.
- A tool that fails must not stop the others, and must be reported, not swallowed.

## Constraints

- View models stay in `PcCleaner.Presentation`, with no MAUI reference.
- DESIGN.md: tokens only, Signal is the one accent, Danger only on destructive actions, mono numerals.
  Both destructive buttons use Danger. Copy is calm sentence case.
- Progress stays honest: "N of M tools finished" plus file counts, never an invented percentage.
- README and the PR template state "nothing is deleted without a confirmation". Both get updated to name
  auto clean as the one exception, chosen by the user each time they press it.

## Approaches

1. **Dashboard drives the existing tool view models (chosen).** Each tool view model gains a
   `CleanSelectedAsync(bool confirm)` method. Its existing command calls it with `confirm: true`, so the
   tool pages behave exactly as today. The dashboard calls it with `confirm: false` after its own combined
   confirmation, or after none for auto clean. One state shared by dashboard and tool pages, as now.
2. A separate dashboard cleaning service that re-reads selections and calls the Core cleaners itself.
   Rejected: it duplicates each tool's clean logic and the post-clean list updates, and the two would drift.
3. Run each tool's existing clean command and let five dialogs appear in a row. Rejected: it is not "one
   go", and it cannot do auto clean at all.

## Left to the builder

- Layout of the expandable tool rows and the volume rows, within DESIGN.md.
- Whether the Cloud Drives and Developer view models need small changes to expose a selected-bytes figure
  the dashboard can total.
- Tests: add `PcCleaner.Presentation` to the test project and cover the run with fakes. At minimum: auto
  clean never calls a confirmation, scan and clean stops when the confirmation is declined, an unticked
  tool is neither scanned nor cleaned, a stopped scan cleans nothing, one failing tool does not stop the rest.
