# DBot Chrome Extension

DBot is the JobPilot AI Chrome extension. It can run as a browser side panel so the analysis stays beside the website you are reviewing.

## Requirements

- Google Chrome 114 or later (Manifest V3 side panel support)
- Node.js 22 or later
- npm
- The JobPilot ASP.NET Core API running locally (see `services/api/README.md`)

## Build

Run these commands from this directory:

```powershell
npm install
npm run typecheck
npm run build
```

The unpacked extension is generated in `dist/`. The build bundles the popup/side-panel UI and copies `public/service-worker.js` and `public/manifest.json` into the output.

## Load or reload in Chrome

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Choose **Load unpacked** and select this directory's `dist` folder.
4. Pin DBot from the Extensions menu.
5. Click the DBot toolbar icon. Chrome opens the side panel beside the active webpage.

The side panel is the persistent workspace while browsing. It stays inside Chrome, next to the web page; it is not a floating window over other applications.

After changing extension code, run `npm run build` again and click **Reload** for DBot on the `chrome://extensions` page.

## Fetch job details from page HTML

- **Fetch from current page** inspects structured `JobPosting` data when available, then looks for visible job-description sections, headings, and company details in the page.
- The detected title, company, and description are placed in the form so you can review and edit them before analysis.
- Extraction is heuristic. Some job boards render content in a way that may require manual copy/paste; login pages, protected browser pages, PDFs, and anti-bot screens may not be readable.

## Automatic settings

Two settings are available in the side panel:

- **Auto-fetch on page changes**: when enabled, DBot checks HTTP/HTTPS pages you browse for likely job descriptions. Chrome asks for optional page access first. You can leave it off and use the current-tab fetch button instead.
- **Auto-analyse detected jobs**: optional, and only available when Auto-fetch is enabled. A detected job description and your saved profile are sent to the configured AI provider without another confirmation. Leave this off if you prefer to review each extracted description first.

Both settings are off by default. No applications are submitted. The extension does not bypass login, CAPTCHA, or anti-bot measures. Site access can be revoked from Chrome's extension settings.

## Run analysis

1. Start the API using the instructions in `services/api/README.md`.
2. Open a job listing, click the DBot toolbar icon, and choose **Fetch from current page**, or paste the description manually.
3. Check the extracted title, company, and description.
4. Click **Analyze job fit**. If Auto-analyse is enabled, detected jobs are analysed automatically.
5. Review the recommendation, fit score, matched requirements, gaps, and questions.

Successful analyses are saved to the API's shared saved-job history, which also appears in the JobPilot dashboard.

## Privacy and permissions

- Page extraction runs in the active webpage only when requested manually or when Auto-fetch has been enabled.
- Broad access to HTTP/HTTPS sites is optional and is requested only when you turn on Auto-fetch.
- Auto-fetch only reads pages; it does not submit forms or applications.
- Auto-analyse sends extracted job descriptions plus the configured candidate profile to the AI provider. The free Gemini tier may use submitted content to improve Google's products, so avoid unnecessary personal identifiers.
- The Gemini API key remains on the backend and is never bundled into the extension.

## Current boundaries

- Extraction is heuristic; job-board markup varies and may need manual corrections.
- The extension does not generate CVs or submit applications.
- The recommendation is decision support, not a prediction of interview success.
