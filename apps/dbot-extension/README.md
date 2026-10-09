# DBot Chrome Extension

First milestone: a Manifest V3 popup that accepts a job title, company, and job description, validates the input, and displays a prototype status.

## Requirements

- Node.js 22 or later
- npm
- Google Chrome or another Chromium-based browser that supports Manifest V3

## Run the type check and build

From this directory:

```powershell
npm install
npm run typecheck
npm run build
```

The production extension files are generated in `dist/`.

## Load the extension in Chrome

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Choose **Load unpacked**.
4. Select this folder's `dist` directory.
5. Pin DBot from the Extensions menu and open it.

After changing code, run `npm run build` again and reload DBot on the extensions page.

## Current limits

- No AI provider or backend is connected.
- The description is entered manually.
- No job information is transmitted or persisted.
- The current submit action validates the input and confirms that the prototype captured it; it does not produce a job-fit score.

These boundaries are intentional for the first milestone.
