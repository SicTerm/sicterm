# Third-party notices

SicTerm is released under the MIT License ([LICENSE](LICENSE)). A SicTerm build also contains the parts below, each under its own
license. Versions are the ones pinned in `Directory.Packages.props`, the projects' `packages.lock.json` files and
`src/terminal/package-lock.json`.

## In the app

| Part | From | License |
| --- | --- | --- |
| .NET runtime | Microsoft, [dotnet/runtime](https://github.com/dotnet/runtime) | MIT |
| Windows App SDK and WinUI (`Microsoft.WindowsAppSDK.*`) | Microsoft, [microsoft/WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) | Microsoft Software License Terms for the Windows App SDK, `license.txt` in each package |
| WebView2 SDK (`Microsoft.Web.WebView2`) | Microsoft | BSD-style, `LICENSE.txt` in the package |
| Console host (`Microsoft.Windows.Console.ConPTY`) | Microsoft, [microsoft/terminal](https://github.com/microsoft/terminal) | MIT |
| `Microsoft.Data.Sqlite` | Microsoft, [dotnet/efcore](https://github.com/dotnet/efcore) | MIT |
| `SQLitePCLRaw` | Eric Sink, SourceGear, [ericsink/SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) | Apache-2.0 |
| SQLite (`e_sqlite3`) | [sqlite.org](https://www.sqlite.org/copyright.html) | Public domain |
| Velopack, with its Setup and updater | Velopack Ltd, [velopack/velopack](https://github.com/velopack/velopack) | MIT |

## In the terminal page

`npm run build` in `src/terminal` writes these parts' full license texts to `THIRD-PARTY-NOTICES.md` beside the page
(`Assets\terminal` in a build).

| Part | From | License |
| --- | --- | --- |
| xterm.js and its fit, search, web-links and unicode-graphemes addons | [xtermjs/xterm.js](https://github.com/xtermjs/xterm.js) | MIT |
| JetBrains Mono, Fira Code, IBM Plex Mono, Victor Mono (fonts) | their projects, listed in [`src/terminal/fonts/README.md`](src/terminal/fonts/README.md) | SIL Open Font License 1.1 |

## Fonts in the interface

| Part | License |
| --- | --- |
| Sora | SIL Open Font License 1.1, [`Sora-OFL.txt`](src/Sic.App/Assets/fonts/Sora-OFL.txt) |
| Azeret Mono | SIL Open Font License 1.1, [`AzeretMono-OFL.txt`](src/Sic.App/Assets/fonts/AzeretMono-OFL.txt) |

## Names and logos

Claude Code, Codex, Hermes, OpenCode and PowerShell are their owners' names and marks. SicTerm starts the tools installed on
the PC and ships none of their code. An agent's tile is the tool's own icon read from that install, or a letter; OpenCode's
logo, which its project publishes, is the one logo in the repository. The MIT
License above does not cover any of these names or logos.
