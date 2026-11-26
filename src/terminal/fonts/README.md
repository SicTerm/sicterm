# Terminal fonts

The fonts Settings → Terminal → Font family offers beyond the ones Windows ships (docs/decisions/terminal-fonts.md), as their
projects publish them for the web, unmodified: regular and bold, each under the SIL Open Font License 1.1, whose text is
beside it. `build.mjs` copies them into the page's assets and their licences into its notices.

| Font | Version | Files from |
| --- | --- | --- |
| JetBrains Mono | 2.304 | [JetBrainsMono-2.304.zip](https://github.com/JetBrains/JetBrainsMono/releases/tag/v2.304), `fonts/webfonts` |
| Fira Code | 6.002 | [Fira_Code_v6.2.zip](https://github.com/tonsky/FiraCode/releases/tag/6.2), `woff2`; the licence from the repository at that tag |
| IBM Plex Mono | 2.004 | [ibm-plex-mono.zip](https://github.com/IBM/plex/releases/tag/%40ibm%2Fplex-mono%401.1.0), `fonts/complete/woff2` |
| Victor Mono | 1.560 | [VictorMonoAll.zip](https://github.com/rubjo/victor-mono/tree/master/public), `WOFF2` |
