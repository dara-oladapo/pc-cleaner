# Third-party notices

PC Cleaner is licensed under the Apache License 2.0 (see [LICENSE](LICENSE)). It includes the following
third-party software and fonts, each under its own licence.

## Fonts

| Font | Licence | Where |
|---|---|---|
| DM Sans v4.004 (Regular, Medium, SemiBold, Bold), copyright 2014 The DM Sans Project Authors | SIL Open Font License 1.1 | [`src/PcCleaner.App/Resources/Fonts/`](src/PcCleaner.App/Resources/Fonts), licence text in [`OFL.txt`](src/PcCleaner.App/Resources/Fonts/OFL.txt) |

The font files are unmodified copies of the upstream static fonts from
<https://github.com/googlefonts/dm-fonts>.

## NuGet packages (direct dependencies)

| Package | Licence | Used by |
|---|---|---|
| Microsoft.Maui.Controls | MIT | the app |
| Microsoft.Extensions.Logging.Debug | MIT | the app (debug builds) |
| CommunityToolkit.Mvvm | MIT | view models |
| Velopack | MIT | installers and auto-update |
| xunit, xunit.runner.visualstudio | Apache-2.0 | tests only |
| Microsoft.NET.Test.Sdk, coverlet.collector | MIT | tests only |

Each package brings its own transitive dependencies, which carry their own licences. `dotnet list package
--include-transitive` lists them, and the package's own licence file ships inside its `.nupkg`.

## Colours and design rules

The colour values, the type scale and the rules in [DESIGN.md](DESIGN.md) follow the WiseLead design system.
The values are used here as plain constants. The design system's own packages are not part of this
repository.
