# SicTerm's GitHub graphics

Built from the shipped mark (simple-25 A, `src/Sic.App/Assets/brand/sic.svg`) on SicTerm Dark's base colour, `#01263E`, with
the wordmark in the shipped fonts, by `scripts/Build-GitHubGraphics.ps1`. Run that script again after the mark changes.

| File | Size | Where it goes |
| --- | --- | --- |
| `avatar.png` | 1024 × 1024 | The SicTerm organization's profile picture: github.com/organizations/SicTerm/settings/profile → Profile picture. GitHub rounds its corners; the mark stays inside the inscribed circle, so it also works on a personal account, which GitHub shows as a circle. |
| `social-preview.png` | 1280 × 640 | Each repository's Settings → General → Social preview → Edit → Upload an image. It's the card shown when a link to the repository is shared. GitHub asks for at least 640 × 320 and under 1 MB. GitHub offers the setting on public repositories only, so it's set once a repository goes public. |
| `mark-ondark.svg`, `mark-onlight.svg` | 256 × 256 | A README's logo, following the reader's GitHub colour mode (snippet below). |

The tagline names no agent: vendors' names stay out of SicTerm's name, logo and marketing headline.

## README logo

From a README at the repository root:

```html
<p align="center">
  <img src="docs/brand/github/mark-onlight.svg#gh-light-mode-only" alt="SicTerm" width="112" height="112">
  <img src="docs/brand/github/mark-ondark.svg#gh-dark-mode-only" alt="SicTerm" width="112" height="112">
</p>
```

The `#gh-light-mode-only` and `#gh-dark-mode-only` endings make GitHub show one image or the other by the theme chosen in
GitHub's own settings. A `<picture>` with `prefers-color-scheme` follows the browser's or Windows' mode instead, so a reader
with Windows in dark mode and GitHub in light got the white `>_` on a white page. Outside github.com (an editor's Markdown
preview, for one) both images show.

The organization's own profile page (`profile/README.md` in a public repository named `.github` under SicTerm) needs the
two SVGs copied into that repository, or full `https://raw.githubusercontent.com/...` links to a public copy. This
repository is private, so its files can't be linked from a public page.
