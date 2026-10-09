# JobPilot Dashboard

A lightweight TypeScript + Vite dashboard for JobPilot AI. It uses the existing ASP.NET Core API and does not need a separate backend.

## Local development

1. Configure `services/api/appsettings.Local.json` as described in the API README. On first startup, the API imports `candidate-profile.json` if you already have one. After that, edit the profile from the dashboard's **My Profile** page.
2. From the repository root, run the API:

   ```powershell
   cd services/api
   dotnet run --urls http://127.0.0.1:5080
   ```

3. In a second terminal, run the dashboard:

   ```powershell
   cd apps/dashboard
   npm install
   npm run dev
   ```

4. Open the local URL printed by Vite (usually `http://127.0.0.1:5173`).

The API uses a local SQLite file at `services/api/jobpilot.db` by default. The database file is ignored by Git. Every successful analysis from either the dashboard or DBot is automatically saved to history. The dashboard can filter saved jobs, open prior results, and update application status and notes. **My Profile** displays a career storyboard and lets you edit your summary, experience timeline, skills, education, languages, certifications, work authorisation, and matching notes. Profile changes are saved in the database and used by future analyses.

From each saved job's detail panel, attach the exact CV used for that application as a PDF or DOCX file (maximum 10 MB), download it later, or remove it. A replacement overwrites that job's previous attachment. This stores a CV you already have; automatic per-job CV generation is not implemented yet.

## Database configuration

SQLite is the default for local development. To use PostgreSQL, set `Database:Provider` to `Postgres` and configure `ConnectionStrings:JobPilot`, or set `DATABASE_URL` to a PostgreSQL URI such as the one provided by a managed database. The API recognises Render/Neon-style `postgresql://` URLs and requires TLS for them.

The API creates the initial schema on startup and includes an idempotent compatibility step for the profile and CV attachment tables. For future schema evolution, introduce versioned EF Core migrations.

## Deployment security

Do not expose the API publicly yet. Before deploying a personal dashboard, add authentication/API protection so other people cannot read your saved jobs or use your Gemini quota. Keep `GEMINI_API_KEY`, the database connection string and other secrets in the backend hosting environment. Set `DASHBOARD_ORIGIN` to the exact deployed dashboard origin to enable production CORS.

For a static-host deployment, set the build-time variable `VITE_API_BASE_URL` to the HTTPS URL of the deployed API. The dashboard makes direct browser requests to that API.
