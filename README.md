# JobPilot AI

JobPilot AI is a personal job-search assistant built around **DBot**, a Chrome extension that analyses job descriptions and prepares tailored application materials, plus a web dashboard for tracking applications.

## Goals

- Translate job descriptions when needed.
- Compare a vacancy with a verified candidate profile.
- Return an explainable recommendation: **Apply**, **Review**, or **Skip**.
- Keep a career profile and timeline as the source of truth for job analysis.
- Generate a tailored CV for a job from the verified profile (editable, downloadable as DOCX), or attach the exact PDF/DOCX CV used for each application.
- Track evaluated jobs, generated CV versions, application status, and follow-ups.

## Monorepo layout

```text
jobpilot-ai/
├── apps/
│   ├── dbot-extension/   # TypeScript Chrome extension (current milestone)
│   └── dashboard/        # TypeScript + Vite job-search dashboard
├── services/
│   └── api/              # ASP.NET Core API + Gemini analysis
├── docs/                 # Architecture and development notes
├── .gitignore
└── README.md
```

Folders are introduced incrementally so each milestone can be run and verified locally.

## Current milestone: analysis history and personal dashboard

1. Analyse a job in DBot or paste a description into the dashboard.
2. The ASP.NET Core API reads the configured candidate profile and calls Gemini or Ollama.
3. Each successful analysis is saved to a local SQLite database by default.
4. The dashboard lists saved jobs and recommendations, and lets you update application status and notes.
5. My Profile presents an editable career storyboard; changes are persisted and used by future analyses.
6. Attach, download, or remove the CV file used for an individual saved job.
7. Generate a tailored CV with DBot's **Make CV** button; the job is marked as an **Attempt** in the dashboard.
8. Draft answers to application-form questions and copy profile details from the DBot side panel.
9. PostgreSQL can be configured for hosted deployment.

See [the API setup guide](services/api/README.md), [the extension setup guide](apps/dbot-extension/README.md), and [the dashboard guide](apps/dashboard/README.md).

## Architecture

- **DBot extension:** TypeScript and Chrome Extension Manifest V3.
- **Web dashboard:** TypeScript + Vite (initial tracker implemented).
- **Backend API:** ASP.NET Core / C#.
- **Persistence:** Entity Framework Core; SQLite locally and PostgreSQL when configured for hosting.
- **AI:** Gemini API for this prototype, called from the backend. The provider and model are configurable. Never put API keys in the extension or commit them to Git.

## Privacy and cost

Gemini API free-tier limits apply. Google states that content sent through its free tier may be used to improve its products. The API sends the job description and the saved candidate profile to Gemini when analysis is requested. Uploaded CV files are stored in the configured database and are not sent to Gemini by the current analysis endpoint. Keep unnecessary personal identifiers out of the candidate profile. No paid-provider fallback is configured.

## Responsible use

DBot is intended to analyse job information the user is permitted to access and provide decision support. It must not fabricate qualifications or employment history. LinkedIn-specific automation must respect the platform's terms; DBot will not attempt to bypass CAPTCHA, authentication checks, or anti-bot controls. The prototype never submits applications.

## Development approach

We build and test one small milestone at a time. Each milestone should have a clear purpose, reproducible local checks, and documentation before moving on.
