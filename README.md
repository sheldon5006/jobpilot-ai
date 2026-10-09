# JobPilot AI

JobPilot AI is a personal job-search assistant built around **DBot**, a browser extension that helps evaluate vacancies and prepare tailored application materials, and a web dashboard for tracking applications.

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
│   ├── dbot-extension/   # TypeScript Chrome extension
│   └── web/              # Angular application tracker (later milestone)
├── services/
│   └── api/              # ASP.NET Core API (later milestone)
├── database/             # PostgreSQL / EF Core migrations (later milestone)
├── docs/                 # Architecture and development notes
├── .gitignore
└── README.md
```

Folders will be introduced as we implement each milestone; the repository will not be scaffolded all at once.

## Planned first milestone: DBot job analysis

1. Accept a job description supplied by the user.
2. Translate it into English when necessary.
3. extract the job's requirements and compare them with the candidate profile.
4. Return a recommendation with evidence, gaps, and reasons.
5. Keep the result explainable; mandatory eligibility rules must not be overridden by an AI score.

After the extension's core analysis works, we will add the API, persistent storage, tailored CV generation, and the application-tracking dashboard.

## Architecture

- **DBot extension:** TypeScript and Chrome Extension Manifest V3.
- **Web dashboard:** Angular.
- **Backend API:** ASP.NET Core / C#.
- **Persistence:** PostgreSQL with Entity Framework Core.
- **AI:** An LLM provider accessed through the backend; API keys must never be bundled in the extension.

## Responsible use

DBot is intended to analyse job information the user is permitted to access and provide decision support. It must not fabricate qualifications or employment history. LinkedIn-specific automation must respect the platform's terms; the extension will not attempt to bypass CAPTCHA, authentication checks, or anti-bot controls.

## Development approach

We will build and test one small milestone at a time. Each milestone should have a clear purpose, a reproducible local test, and documentation before we move to the next.
