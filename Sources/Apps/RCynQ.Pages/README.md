# RCynQ.Pages

The project page: <https://depthrel.github.io/RCynQ/>. Plain static files with no build step, published by GitHub Pages
from this folder (`.github/workflows/pages.yml`) on every change in the `main` branch. A prototype: the name is told as
**R**emote **C**ode s**YN**chronization, **Q**uick, and the page is built around the main idea of RCynQ: code reaches a
remote machine without a commit, directly or through Relays.

| File | What for |
|---|---|
| `index.html`, `assets/site.css`, `assets/site.js` | The page: what RCynQ is, how the chain Workstation → Relay → Origin works, why Relays, what it is good at, the first steps and the downloads. |
| `assets/languages.js`, `assets/flags/*.svg` | The languages of the page (English, German, Spanish, French, Portuguese, Russian), the language picker with flags and the links to the documentation in the chosen language. |
| `assets/rcynq.svg` | The logo, the same as `Sources/Apps/RCynQ.UI/Assets/rcynq.svg`. |
| `updates.json` | The latest release. The "Download" buttons read it; without it they lead to the releases page. |

## First publication

1. Open the repository on GitHub: **Settings → Pages**.
2. Under **Build and deployment** choose **Source: GitHub Actions**.
3. Push a commit with this folder to `main` (or run the **Pages** workflow by hand on the **Actions** tab).
4. A couple of minutes later the page opens at <https://depthrel.github.io/RCynQ/>.

## A new release

1. Run `Scripts/build-release.ps1`, create the GitHub release `X.Y.Z` with its packages and publish it.
2. Fill in `updates.json` (it is `{}` until the first release, and the buttons lead to the releases page):

   ```json
   {
     "windows": { "version": "X.Y.Z", "url": "https://github.com/DepthRel/RCynQ/releases/download/X.Y.Z/RCynQ-X.Y.Z-setup.exe",
                  "zip": "https://github.com/DepthRel/RCynQ/releases/download/X.Y.Z/RCynQ-X.Y.Z-win-x64.zip" },
     "linux":   { "version": "X.Y.Z", "url": "https://github.com/DepthRel/RCynQ/releases/download/X.Y.Z/RCynQ-X.Y.Z-linux-x64.tar.gz",
                  "deb": "https://github.com/DepthRel/RCynQ/releases/download/X.Y.Z/RCynQ-X.Y.Z-linux-x64.deb" },
     "macos":   { "version": "X.Y.Z", "url": "https://github.com/DepthRel/RCynQ/releases/download/X.Y.Z/RCynQ-X.Y.Z-osx-arm64.tar.gz",
                  "intel": "https://github.com/DepthRel/RCynQ/releases/download/X.Y.Z/RCynQ-X.Y.Z-osx-x64.tar.gz" }
   }
   ```
3. Commit. Only after the release is published: an early `updates.json` sends every visitor to a 404.

## Rules of the page

- Nothing is loaded from other servers but one thing: the page counts visits with Umami Cloud (no cookies), its
  script from `cloud.umami.is` and its reports to `gateway.umami.is`. Besides visits it counts the `download` event of
  the download links (`data-umami-event-package`: `windows`, `windows-zip`, `linux-deb`, `linux-archive`, `macos`,
  `macos-intel`; `data-umami-event-place`: `card` or `hero`, the button at the top, which `site.js` marks for the
  system of the visitor). It counts only on `depthrel.github.io`, keeps the part of the address after `#` and `?` out
  of its reports and skips browsers that ask not to be tracked. The page pins what it may load with a
  `Content-Security-Policy`; there are no inline scripts or styles either.
- The English text is written right in the HTML (the page reads fine without scripts); the other languages (Deutsch,
  Español, Français, Português, Русский) are tables in `assets/site.js`. A table must have every `data-t` key the
  English page has. Without scripts the language picker is hidden.
- The language is chosen like in the application: the one the visitor picked before (kept in `localStorage`),
  otherwise the first language of the browser the page speaks, otherwise English.
- The documentation links in the footer lead to `Docs/En` or `Docs/Ru` by the `docs` table of `assets/languages.js`.
- A new language: one more entry in `list` of `assets/languages.js`, its flag in `assets/flags/<code>.svg` (3:2), one
  more table in `assets/site.js`.

## Look at it locally

Any static server in this folder, for example `npx http-server . -p 8765`, then <http://127.0.0.1:8765/>. Opening
`index.html` as a file does not work: the security policy of the page allows only its own site.
