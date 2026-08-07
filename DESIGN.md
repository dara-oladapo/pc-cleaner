# Design system

PC Cleaner's visual identity, in one place, so new screens stay consistent with the rest of the app.

## Why this look

The two default "AI tool" aesthetics are cream-paper-serif-terracotta and near-black-with-one-acid-accent. Both are template answers, not choices made for this app. Instead: a cool "lab report" palette (pale blue-gray, not cream) with exactly one functional accent color, and monospace tabular numerals as the running signature — every size and count in the app reads like a terminal/`du -h` readout, reinforcing "precise diagnostic tool" over "sales funnel with a scan button."

## Color tokens

Defined in `Resources/Styles/Colors.xaml`. Light and dark are each intentional palettes (dark isn't just light-inverted).

| Token | Light | Dark | Use |
|---|---|---|---|
| `Paper` / `PaperDark` | `#EEF1F3` | `#12161B` | Page background |
| `Surface` / `SurfaceDark` | `#FFFFFF` | `#1A2027` | Cards, tab bar |
| `InkStrong` / `InkStrongDark` | `#12181D` | `#EDF1F3` | Primary text |
| `InkMuted` / `InkMutedDark` | `#5B6672` | `#8B99A6` | Secondary text, captions |
| `Hairline` / `HairlineDark` | `#D7DEE3` | `#2A323B` | Borders, dividers, meter tracks |
| `Signal` / `SignalDark` | `#0E7C7B` | `#4FD1CB` | The ONE accent — primary buttons, hero numbers, meter fills, pills |
| `Attention` / `AttentionDark` | `#B8863B` | `#D9A857` | Sparingly — "needs admin/root" badges only |

Rule: `Signal` is the only saturated color in the app. If a new screen wants to draw attention to something, reach for `Signal` weight/placement (bold, size, position) before reaching for a new color.

## Type

| Role | Family | Where |
|---|---|---|
| Display | Space Grotesk (`DisplayMedium` / `DisplayBold`) | Page titles, button labels, eyebrows, pill labels |
| Tabular numbers | JetBrains Mono (`MonoRegular` / `MonoMedium`) | Every size, count, and path in the app — the signature device |
| Body | OpenSans (already bundled) | Everything else — deliberately kept quiet/supporting |

Font files live in `Resources/Fonts/` and are registered in `MauiProgram.cs`.

## Components (`Resources/Styles/Styles.xaml`)

- **`PrimaryButton`** — solid `Signal` fill, 4px radius (not pill-shaped — reads as an instrument control). Use for the one primary action per screen (Scan, Clean Selected, Delete Selected).
- **Default `Button`** — ghost/outline. Use for secondary actions (Cancel, Add).
- **`InlineButton`** — compact ghost button for in-row actions (Remove, Enable/Disable).
- **`HeroNumber`** + **`Eyebrow`** + **`HeroCaption`** — the three-line header block every tool page opens with: small tracked label, big mono stat, status caption.
- **`Pill`** / **`PillLabel`** — small tinted tag for categories/sources. **`AttentionPill`** / **`AttentionPillLabel`** — same shape, `Attention` color, for elevation-required badges.
- **`MeterTrack`** / **`MeterFill`** — the inline proportional bar on list rows. Fill width is bound via `ShareOfMax` on the row's view model through `ShareToWidthConverter` (`Converters/ShareToWidthConverter.cs`). Real information (relative size), not decoration — don't add it somewhere the proportion isn't meaningful.
- **`Mono`** / **`MonoNumber`** — paths and figures respectively. `MonoNumber` is right-aligned for tabular scanning down a column.

## Icon

`Resources/AppIcon/appiconfg.svg` — three ascending bars sharing a baseline, echoing the meter-bar row device. Background is flat `Signal` teal (`#0E7C7B`). Splash screen reuses the same mark.

## Extending this

Adding a new screen: start from the hero block pattern (`Eyebrow` → `HeroNumber` → `HeroCaption`), reuse `Pill`/`MonoNumber`/`InlineButton` for rows before inventing new styles, and keep `Signal` as the only saturated color on the page.
