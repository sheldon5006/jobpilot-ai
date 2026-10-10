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

## Make a tailored CV

1. Load a job in the **Job** tab (fetch it or paste it).
2. Optionally open **Customise CV prompt**. *Standing instructions* are saved in the extension and used for every CV; *extra instructions for this job* apply once. **Reset to default** restores the built-in instructions.
3. Click **Make CV**. If the job hasn't been analysed yet, DBot analyses and saves it first, then generates the CV. A `Saved` job becomes an **Attempt** in the dashboard.
4. Edit any section, then **Save edits** or **Download PDF** (an editable Word file is also available). The CV is also available under the job in the dashboard.

Truthfulness rules can't be overridden by your instructions. Role titles, dates, education, languages and certifications come straight from your profile. Skills that aren't in your profile are removed. AI-written bullet points can still over-state your experience, so check them before you send the CV.

## My profile tab

Shows your saved profile with click-to-copy rows (contact details, summary, work authorisation, languages, each role, skills and education), so you can quickly paste them into application forms. Add contact details under **My Profile** in the dashboard.

## Ask AI tab

Paste a question from an application form (for example, "Why are you interested in this position?"). DBot drafts a first-person answer from your profile. If **Use the current job** is ticked, the answer is tailored to the job in the Job tab. Choose a length, add optional instructions, then copy the answer. If a fact your profile doesn't contain is needed (such as a salary expectation or start date), the answer shows a `[placeholder]` instead of guessing.

## Privacy and permissions

- Contact details are stored in your profile but are never sent to the AI provider. They are added locally to generated CVs.

- Page extraction runs in the active webpage only when requested manually or when Auto-fetch has been enabled.
- Broad access to HTTP/HTTPS sites is optional and is requested only when you turn on Auto-fetch.
- Auto-fetch only reads pages; it does not submit forms or applications.
- Auto-analyse sends extracted job descriptions plus the configured candidate profile to the AI provider. The free Gemini tier may use submitted content to improve Google's products, so avoid unnecessary personal identifiers.
- The Gemini API key remains on the backend and is never bundled into the extension.

## Current boundaries

- Extraction is heuristic; job-board markup varies and may need manual corrections.
- Generated CVs and answers are drafts: review every line before sending. The extension never submits applications.
- The recommendation is decision support, not a prediction of interview success.
