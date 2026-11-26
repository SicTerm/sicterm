# Contributing to SicTerm

Thanks for helping. The [README](README.md) covers what SicTerm is and how to build it; this page covers how work is done here.

## Workflow

- **Pull before you change anything.** If your working tree isn't clean or your branch has diverged from `origin`, sort that out first.
- **Work on a branch and open a pull request** against `main`. The owner reviews and merges.
- **Commit each step once it's verified,** and push it. Don't let work pile up locally; unfinished work goes on its branch, pushed.
- **Testing with real agents** (Claude Code, Codex, Hermes, OpenCode) is as welcome as code. Open an issue with what you did, what you expected and what happened; `sic.log` in `%LOCALAPPDATA%\SuperIntelligentChat` and a screenshot help.

## Verify a change

Run the four commands from the README before every commit. What each one proves:

- `dotnet test` builds and tests **only `Sic.Core`**. A broken `src/Sic.App` still passes it, so after any change under `src/Sic.App` run `scripts\Publish-App.ps1 -Smoke` too (about three minutes). The smoke test publishes the app and drives it against a scratch data folder and fixture history.
- **Warnings are errors** (`Directory.Build.props`).
- **NuGet restores from lock files.** After changing a version in `Directory.Packages.props`, commit the updated `packages.lock.json` files with it. Otherwise never commit a lock file: a `dotnet test` or a Debug build can rewrite `src/Sic.Core/packages.lock.json` without its `win-x64` section, so check `git status` and `git checkout -- src/Sic.Core/packages.lock.json` before you commit. `Publish-App.ps1` leaves it intact.
- **The smoke test needs an audio device.** Its ready-chime check fails with `playable=False` in a remote session with no audio; that isn't the code.
- **To look at the real window with real history** without touching your own data, copy `settings.json`, `layout.json` and `catalog.db` from `%LOCALAPPDATA%\SuperIntelligentChat` into a scratch folder and run `scripts\Review-Shell.ps1 -DataDir <that folder>`.

## Conventions

- **Decisions are recorded.** A change of behavior that someone could reasonably have made differently gets a decision record: one file per topic, opening with **Decided:** and **Status:**, then the problem, the decision and the alternatives considered. The owner reviews each record, and approved records are published in `docs/decisions`. Code comments cite the record that explains why.
- **Interface words.** Notices and errors state facts concisely and never address the reader: no "you", and no instructions ("Unpinned folders remain unpinned", not "Folders you unpinned stay unpinned"). Where something can be done, say where, as a fact ("off in Settings → Agents"). A notice whose title says it all has no message. The app is called SicTerm in every text. Settings are a label and a control, with a tooltip only for what the label doesn't say.
- **Size things to real agents.** Fixes, limits and tests follow what Claude Code, Codex, Hermes and OpenCode actually do. Extremes no agent produces are parked rather than built.
- **The agents are theirs.** SicTerm starts each agent's own unmodified tool, ships no agent's code and no agent's logo apart from OpenCode's, and doesn't edit agents' configuration files.
- **Some internal names keep the old SIC name** so existing installs carry over: the data folder `%LOCALAPPDATA%\SuperIntelligentChat`, the registry keys under `HKCU\Software\Super-Intelligent-Chat`, the `SIC-` prefix of the names SicTerm gave new Hermes sessions until November 2026 (still recognized, no longer given), and `TERM_PROGRAM`. Don't rename them.
- **Generated files** aren't edited by hand: the mark's drawings come from `scripts\Build-MarkDrawings.ps1`, the icons from `scripts\Build-Icon.ps1` and the chrome's `Controls\MarkGeometry.g.cs` from `scripts\Build-MarkGeometry.ps1`; the terminal page in `src\Sic.App\Assets\terminal` is built from `src\terminal`.
- **Line endings** are set by `.gitattributes`, so they don't depend on your Git settings.

## Releases

Only the owner builds and uploads releases, with `scripts\Build-Release.ps1 -Upload` after bumping `<Version>` in `src\Sic.App\Sic.App.csproj`.
