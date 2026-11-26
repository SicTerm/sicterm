<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/github/mark-ondark.svg">
    <img src="docs/brand/github/mark-onlight.svg" alt="SicTerm" width="112" height="112">
  </picture>
</p>

# SicTerm

**Super-Intelligent-Chat Terminal.** Claude Code, Codex, Hermes and OpenCode side by side in one Windows app, with every conversation they have saved in one searchable list.

SicTerm runs the coding agents already installed on the PC, signed in with their own accounts, in tabs and split panes next to PowerShell. Conversations come from each agent's own saved history, which SicTerm reads but never changes, and resume through the agent itself in the folder they belong to. A local search index of that history, rebuilt from it at any time, makes every agent's conversations searchable in one place. SicTerm calls no model or AI service itself. Its only network request of its own is to GitHub, to check for updates.

**Status:** preview (0.9.x) for Windows 11 on x64. ARM64 PCs run it under emulation.

More about SicTerm is at [sicterm.app](https://sicterm.app).

## 🧩 One window for every agent

- **Agents side by side.** Up to 16 panes per tab, split right or down. Drag a tab or a pane to rearrange it, or gather every tab that shares a folder into one (F6).
- **Every conversation in one list.** The Sessions panel lists each agent's saved conversations, searchable by title, folder and content, and grouped by folder, day or agent. Any of them can be pinned or renamed. Resume opens the exact conversation in a tab or a split.
- **Quit and pick up where it left off.** Quitting asks each agent to save its session first. The next launch restores every tab without starting anything, and Resume all (F5) brings them back.
- **A clear signal when an agent needs attention.** A ring spins on a tab while its agent works, the dot turns yellow when a turn ends out of view, and a chime can ring. Claude Code, Codex and OpenCode also report when they are waiting on a permission, a question or a sign-in: the dot turns red and the status bar counts them. Pane headers show the model and how full its context window is, and the status bar shows the 5-hour and weekly limits of Claude and ChatGPT plans.
- **Folders decide the workspaces.** Each folder in the Folders list is a workspace with its own tab strip, holding the sessions in that folder or below it. Nothing is created or moved by hand, and a session outside every listed folder goes to Scratch. Switch workspaces from the name at the head of the tab strip, with Ctrl+Shift+A, or with Ctrl+Alt+1 to 9. Each folder can carry an emoji, a color and a sound, and the first run lists the folders used most in the history.
- **Built for Windows.** Open in SicTerm on File Explorer's right-click menu, a tray icon, full screen (F11), the side panel on either side, tabs at the top or bottom, and every shortcut in one reference (F1).
- **Themes and sounds are plain files.** Built-in themes include SicTerm, Lagoon, Windows, Harbor and Garnet in dark and light, Aurora and Outrun, and the popular palettes: Catppuccin, Nord, Dracula, Solarized, Gruvbox, Tokyo Night, Rosé Pine and One Half. Any theme can be copied to a file as a starting point (Settings → Theme → Copy to a file). The sidebar, panel and bars can be frosted or clear, and the window can be made partly see-through (Settings → General → Chrome, Window opacity).

## ⬇️ Install from the latest release

1. Download `SicTerm-win-Setup.exe` from the [latest release](https://github.com/SicTerm/sicterm/releases/latest) and run it. It installs for the current Windows account only, without admin rights, into `%LOCALAPPDATA%\SicTerm`, with shortcuts in Start and on the desktop.
2. Releases are signed by Christopher Luton through Microsoft's Artifact Signing. SmartScreen builds reputation for a new publisher over its first downloads, so an early download may still show a warning (More info → Run anyway).
3. SicTerm updates itself. It checks every six hours and installs a new version on quit.

Settings, layout and the search index are kept in `%LOCALAPPDATA%\SuperIntelligentChat` and survive an uninstall.

## 🛠️ Build from source

Building needs Windows 11 with these installed:

```powershell
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS
winget install Microsoft.PowerShell --source winget --installer-type wix
```

The .NET SDK version is pinned in [`global.json`](global.json). PowerShell 7 must be installed in `C:\Program Files\PowerShell\7`, because the terminal tests start it from there. Visual Studio is not needed. From the repository root, in PowerShell 7:

```powershell
npm ci --prefix .\src\terminal
npm run build --prefix .\src\terminal
dotnet test .\SuperIntelligentChat.slnx
pwsh -NoProfile -File .\scripts\Publish-App.ps1 -Smoke -SkipIdentity
```

The app is published to `artifacts\SicTerm\SicTerm.exe`. `-SkipIdentity` leaves out the package that adds Open in SicTerm to Windows 11's own right-click menu, which needs the Visual Studio Build Tools' C++ workload and the owner's Artifact Signing sign-in. Without it, the entry is under Show more options. The smoke test runs the published app against a scratch data folder and test history, so it never touches real data and makes no model requests.

## 📁 How the repository is laid out

| Path | What's there |
| --- | --- |
| `src/Sic.App` | The WinUI 3 app: window, panels, tabs and panes, settings. |
| `src/Sic.Core` | Everything that isn't UI: agents, history readers and the search index, themes, sounds, settings. |
| `src/terminal` | The terminal page (xterm.js) each pane hosts in WebView2, over ConPTY. |
| `src/Sic.StatusLine` | The status-line helper that reads Claude Code's usage limits. |
| `src/Sic.ExplorerCommand` | The C++ command behind Open in SicTerm on Windows 11's menu. |
| `tests/Sic.Core.Tests` | Unit tests for `Sic.Core`. |
| `scripts` | Publish, smoke test, release, and the icon and graphics builds. |
| `docs` | Brand graphics. |

Contributions are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers the workflow and conventions.

## 📦 Third-party parts and their licenses

SicTerm ships the Sora and Azeret Mono fonts (SIL Open Font License), Microsoft's console host (`Microsoft.Windows.Console.ConPTY`, MIT) and xterm.js (MIT). It ships none of the agents' code. Each agent's tile is the tool's own icon, read from its install, or a letter; OpenCode's published logo is the one exception. The full list, with each part's license, is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## ⚖️ MIT licensed

SicTerm is open source under the [MIT License](LICENSE). Use it, change it and share it freely.
