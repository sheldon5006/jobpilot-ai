# JobPilot AI

JobPilot AI is a personal job-search assistant built around **DBot**, a Chrome extension that analyses job descriptions and prepares tailored application materials, plus a web dashboard for tracking applications.

## Goals

- Translate job descriptions when needed.
- Compare a vacancy with a verified candidate profile.
- Return an explainable recommendation: **Apply**, **Review**, or **Skip**.
- Prepare role-specific CVs using only verified candidate information.
- Track evaluated jobs, generated CV versions, application status, and follow-ups.

## Monorepo layout

```text
jobpilot-ai/
├── apps/
│   ├── dbot-extension/   # TypeScript Chrome extension (current milestone)
│   └── web/              # Angular application tracker (later milestone)
├── services/
│   └── api/              # ASP.NET Core API + Gemini analysis
├── database/             # PostgreSQL / EF Core migrations (later milestone)
├── docs/                 # Architecture and development notes
├── .gitignore
└── README.md
```

Folders are introduced incrementally so each milestone can be run and verified locally.

## Current milestone: AI job-description analysis

1. Paste a job description into DBot.
2. Send it to the local ASP.NET Core API only when you click **Analyze job fit**.
3. The API reads the local candidate profile and calls the configurable Gemini API model.
4. DBot displays an **Apply**, **Review**, or **Skip** recommendation, fit score, matched requirements, gaps, and questions to verify.

See [the API setup guide](services/api/README.md) and [the extension setup guide](apps/dbot-extension/README.md).

## Architecture

- **DBot extension:** TypeScript and Chrome Extension Manifest V3.
- **Web dashboard:** Angular (planned).
- **Backend API:** ASP.NET Core / C#.
- **Persistence:** PostgreSQL with Entity Framework Core (planned).
- **AI:** Gemini API for this prototype, called from the backend. The provider and model are configurable. Never put API keys in the extension or commit them to Git.

## Privacy and cost

Gemini API free-tier limits apply. Google states that content sent through its free tier may be used to improve its products. The API sends the job description and the locally configured candidate profile to Gemini when analysis is requested. Keep unnecessary personal identifiers out of the candidate profile. No paid-provider fallback is configured.

## Responsible use

DBot is intended to analyse job information the user is permitted to access and provide decision support. It must not fabricate qualifications or employment history. LinkedIn-specific automation must respect the platform's terms; DBot will not attempt to bypass CAPTCHA, authentication checks, or anti-bot controls. The prototype never submits applications.

## Development approach

We build and test one small milestone at a time. Each milestone should have a clear purpose, reproducible local checks, and documentation before moving on.
