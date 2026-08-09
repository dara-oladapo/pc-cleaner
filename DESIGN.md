# Design system

PC Cleaner's visual identity, in one place, so new screens stay consistent with the rest of the app.

A clickable prototype of every screen lives at [`docs/prototype/index.html`](docs/prototype/index.html) — open it in a browser. It uses the same fonts and the same token values as the app, so it is the fastest way to see a change before building it, and the reference the XAML is expected to match.

Rendered stills of that prototype are in [`docs/prototype/screenshots/`](docs/prototype/screenshots) for anyone who wants to see the screens without running anything. They are renders of the prototype, not captures of the built app — the app has to be run on Windows or a Mac to see the real thing.

## Why this look

The two default "AI tool" aesthetics are cream-paper-serif-terracotta and near-black-with-one-acid-accent. Both are template answers, not choices made for this app. Instead: a cool "lab report" palette (pale blue-gray, not cream) with one functional accent color, and monospace tabular numerals as the running signature — every size and count in the app reads like a terminal/`du -h` readout, reinforcing "precise diagnostic tool" over "sales funnel with a scan button."

## The signature element: the capacity readout

The dashboard opens on a single horizontal bar of the real drive, split into three segments — occupied, reclaimable, free — with the reclaimable slice sitting against the free segment, because that is where it is about to go. Backed by `IDriveSpaceReader` (`System.IO.DriveInfo`, no dependency).

Avast Cleanup and CleanMyMac both open on a big circular gauge; this is the deliberate opposite bet. It is also the app's own row meter promoted to hero scale, so the device that marks every result row is the same device that headlines the app. Because it draws a real proportion of a real disk, it stays honest at any window size — the three segments are star-width `GridLength`s, not pixel figures.

## Color tokens

Defined in `Resources/Styles/Colors.xaml`. Light and dark are each intentional palettes (dark isn't just light-inverted).

| Token | Light | Dark | Use |
|---|---|---|---|
| `Paper` / `PaperDark` | `#EEF1F3` | `#12161B` | Page background |
| `Surface` / `SurfaceDark` | `#FFFFFF` | `#1A2027` | Cards, rows, the nav rail |
| `Sunken` / `SunkenDark` | `#E4E9EC` | `#0D1116` | Meter tracks, hover fills, wells |
| `InkStrong` / `InkStrongDark` | `#12181D` | `#EDF1F3` | Primary text |
| `InkMuted` / `InkMutedDark` | `#5B6672` | `#8B99A6` | Secondary text, captions |
| `Hairline` / `HairlineDark` | `#D7DEE3` | `#2A323B` | Borders, dividers |
| `Signal` / `SignalDark` | `#0E7C7B` | `#4FD1CB` | The accent — primary buttons, hero numbers, meter fills, pills |
| `Attention` / `AttentionDark` | `#B8863B` | `#D9A857` | Sparingly — "needs admin/root" badges only |
| `Danger` / `DangerDark` | `#A8322D` | `#E8756D` | Destructive actions and their confirmations, nothing else |
| `Occupied` / `OccupiedDark` | `#97A4B0` | `#46525F` | The in-use portion of the capacity bar |

Rule: `Signal` is the only saturated color used to draw attention. If a new screen wants to emphasise something, reach for `Signal` weight/placement (bold, size, position) before reaching for a new color.

**The one exception, and why it exists.** `Danger` is a second saturated color, and it earns its place: before it, "Scan" and "Delete Selected" were the identical teal button, so a single color meant both "look at this" and "destroy this irreversibly". Junk cleaning is a permanent delete with no Recycle Bin round-trip, and an interface that gives no warning about that is a defect, not a minimalism. `Danger` appears **only** on a destructive button and on its confirmation dialog. Do not reach for it to mean "error", "warning", or "important".

## Type

| Role | Family | Where |
|---|---|---|
| Display | Space Grotesk (`DisplayMedium` / `DisplayBold`) | Page titles, button labels, eyebrows, pill labels, nav items |
| Tabular numbers | JetBrains Mono (`MonoRegular` / `MonoMedium`) | Every size, count, and path in the app — the signature device |
| Body | IBM Plex Sans (`BodyRegular` / `BodySemibold`) | Everything else — deliberately kept quiet/supporting |

Body was Open Sans until the redesign. Open Sans is the .NET MAUI project template's default and the most anonymous UI face available — the templated choice the rest of this document argues against. IBM Plex Sans was drawn for technical documentation, sits naturally beside JetBrains Mono, and is OFL-licensed.

Aliases are named by role rather than by vendor (`BodyRegular`, not `OpenSansRegular`), so swapping a face is one line in `MauiProgram.cs` instead of a find-and-replace through every style. Font files live in `Resources/Fonts/`.

Sizes come from `Resources/Styles/Tokens.xaml` (`FontHero` … `FontPill`). Stay on the scale.

## Tokens (`Resources/Styles/Tokens.xaml`)

Spacing (`Space1`–`Space6`, a 4px base), page rhythm (`PagePadding`, `CardPadding`, `RowPadding`), the type scale, component sizes (`RailWidth`, `IconSize`, `MeterWidth`, `CapacityBarHeight`), and letter-spacing. Before this file these were hardcoded literals repeated across four pages, so changing the page rhythm meant editing every one and hoping they stayed in step.

`MeterWidth` in particular is load-bearing: both the `MeterTrack` style and the `ConverterParameter` on every meter fill read it, so the track and its fill can no longer drift apart the way the old hardcoded `'60'`/`'120'` pairs could.

## Icons (`Resources/Styles/Icons.xaml`)

Vector `PathGeometry` resources on a 20×20 grid, rendered through `Microsoft.Maui.Controls.Shapes.Path`. Not image assets: a `Path` takes its `Stroke` from a binding, so one definition covers light, dark, selected and unselected, where a `MauiImage` SVG is baked to a PNG at build time and can't be tinted. No icon font, no NuGet package.

Single-weight strokes, flat terminals, geometry over illustration — the same family as the app mark. Draw new ones on the same grid.

## Navigation

A locked Shell flyout, 216px, with a custom `Shell.ItemTemplate` (`Controls/IconFlyoutItem.cs` carries each destination's icon geometry). The previous bottom `TabBar` is a phone pattern: on a desktop window it spent the full width on four labels and left no room to name a fifth tool.

Selection shows as a tinted row background. MAUI hands the `Selected` visual state to the template's root element only and its setters can't reach a child, so the icon and label keep one color rather than tinting with the row.

## Components (`Resources/Styles/Styles.xaml`)

- **`PrimaryButton`** — solid `Signal` fill, 4px radius (not pill-shaped — reads as an instrument control). One per screen: the action you came to that screen to take.
- **`DangerButton`** — same shape, `Danger` fill. Destructive actions only, and always paired with a confirmation.
- **Default `Button`** — ghost/outline, for secondary actions (Stop scan, Add, Browse).
- **`InlineButton`** — compact ghost button for in-row actions (Remove, Enable/Disable, Select none).
- **`Card`** / **`RowCard`** — raised containers. Result rows are cards rather than hairline-separated bands: at twenty-plus results the hairline version read as one undifferentiated wall.
- **`HeroNumber`** + **`Eyebrow`** + **`HeroCaption`** — the three-line header block every tool page opens with.
- **`EmptyStateTitle`** / **`EmptyStateBody`** — an empty screen is an invitation to act. Say what is true, then what to do about it, then leave the primary button in reach.
- **`Pill`** / **`PillLabel`**, **`AttentionPill`**, **`QuietPill`** — tinted tags. `Quiet` is for states that are simply off ("Disabled"), where `Signal` would overstate them.
- **`MeterTrack`** / **`MeterFill`** — the inline proportional bar on rows, via `ShareToWidthConverter`. Real information (relative size), not decoration — don't add it where the proportion isn't meaningful.
- **`CapacityBar`** + `SegmentOccupied` / `SegmentReclaimable` / `SegmentFree` — the dashboard hero.
- **`ProgressPanel`** / **`StepMarker`** — see below.
- **`Mono`** / **`MonoNumber`** — paths and figures respectively. `MonoNumber` is right-aligned for scanning down a column.

## Theme

Three states, not two: **System (the default), Light, Dark**. The switcher is a three-segment control in the navigation rail footer — a setting you choose once and stop thinking about, so it shouldn't cost a nav destination.

System is not "whatever the OS said at launch". It maps to MAUI's `AppTheme.Unspecified`, which keeps following the OS, so a user on System sees the app change when their desktop switches at sunset. Choosing Light or Dark explicitly freezes it against the OS — that is the point of the override.

The choice is persisted in `Preferences` (registry on Windows, `NSUserDefaults` on macOS) and re-applied in the `App` constructor before the first window exists, so the app opens in the chosen theme rather than flashing the system one and correcting itself. An unreadable or unknown stored value falls back to System instead of throwing.

Because every colour already goes through `AppThemeBinding`, nothing else needs to know the theme changed. Keep it that way: a new screen that hardcodes a hex value will look correct in one theme and wrong in the other, and the switcher makes that trivially easy for a user to find.

## Progress, honestly

The scanners report a **running file count**, never a percentage — `IProgress<int>` is a count of files walked so far, with no denominator. So every scanning state pairs an indeterminate indicator with the real number, and no screen shows a progress fraction it cannot compute.

The dashboard is the single exception: "2 of 4 tools finished" is a real fraction, so it gets determinate `StepMarker`s. Don't add a percentage anywhere else.

## Destructive actions

**Every prompt, dialog, and alert is app UI.** No screen ever calls `DisplayAlert` or any other platform dialog. An OS alert ignores the design system, renders differently on Windows and Mac Catalyst, can't show what is about to be deleted, and reads as a system warning rather than part of this app — which would make the most consequential moment in the product the least consistent one. `Controls/DialogHost.xaml` is the app's own dialog: a scrim plus a centred card, hosted in every page's outer `Grid` and invisible until a view model asks for one. (It's also why the build no longer emits CS0618 — `Page.DisplayAlert` is obsolete in .NET 10.)

Every delete goes through `IDialogService.ConfirmAsync` before anything is touched. The copy differs by consequence:

- Junk cleaning is a **permanent** delete (`IJunkCleaner` deliberately bypasses the Recycle Bin, since caches are regenerable and filling the bin with them defeats the point). Its dialog says so, names the size, and its affirmative is `Danger`.
- Duplicates and large files are the user's own files and go to the Recycle Bin. Their dialogs name the destination and say it can be restored.

Destructive confirmations also carry a **manifest**: the largest few items with their sizes, and a `+ N more` line, built by `Services/DialogManifest.cs`. A dialog that says only "delete 14 items?" gets dismissed reflexively because there's nothing in it to check against; naming the biggest entries is what lets someone spot the one that shouldn't be there. Capped at four lines — a dialog listing forty paths is as unreadable as one listing none.

Cancelling, dismissing, tapping the scrim, and "we couldn't ask" all resolve to `false`. Anything other than a deliberate yes must never be read as one.

## Extending this

Adding a new screen: start from the hero block pattern (`Eyebrow` → `HeroNumber` → `HeroCaption`), reuse `Card`/`Pill`/`MonoNumber`/`InlineButton` before inventing new styles, take spacing from `Tokens.xaml` rather than typing a number, keep `Signal` as the only accent, and give the empty state as much thought as the full one.
