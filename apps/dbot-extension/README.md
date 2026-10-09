# DBot Chrome Extension

DBot is the job-description analysis extension inside the JobPilot AI monorepo.

## Requirements

- Node.js 22 or later
- npm
- Google Chrome or another Chromium-based browser supporting Manifest V3
- The JobPilot ASP.NET Core API running locally (see `services/api/README.md`)

## Build

Run these commands from this directory:

```powershell
npm install
npm run typecheck
npm run build
```

The built extension is generated in `dist/`.

## Load the extension in Chrome

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Choose **Load unpacked**.
4. Select this directory's `dist` folder.
5. Pin DBot from the Extensions menu and open it.

After changing extension code, run `npm run build` again and reload DBot on the extensions page.

## Run AI analysis locally

1. Start the API using the instructions in `services/api/README.md`.
2. Paste a job title, company, and job description into DBot.
3. Click **Analyze job fit**.
4. Review the recommendation, heuristic fit score, separate mandatory-requirements status, matched requirements with profile-fact IDs, evidence warnings, gaps, and questions.

The extension sends the job description to the local API only when you click Analyze. The API also sends the configured candidate profile and job description to Gemini. Free-tier content may be used by Google to improve its products; avoid unnecessary personal identifiers.

## Current boundaries

- The extension does not submit applications.
- Job and candidate profile information are not persisted by the current API.
- The API key stays in the backend environment and is never bundled into the extension.
- The recommendation is decision support, not a prediction of interview success.
