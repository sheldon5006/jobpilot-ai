# JobPilot Dashboard

A lightweight TypeScript + Vite dashboard for JobPilot AI. It uses the existing ASP.NET Core API and does not need a separate backend.

## Local development

1. Configure `services/api/candidate-profile.json` and `services/api/appsettings.Local.json` as described in the API README.
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

The API uses a local SQLite file at `services/api/jobpilot.db` by default. The database file is ignored by Git. Every successful analysis from either the dashboard or DBot is automatically saved to history. The dashboard can filter saved jobs, open prior results, and update application status and notes.

## Database configuration

SQLite is the default for local development. To use PostgreSQL, set `Database:Provider` to `Postgres` and configure `ConnectionStrings:JobPilot`, or set `DATABASE_URL` to a PostgreSQL URI such as the one provided by a managed database. The API recognises Render/Neon-style `postgresql://` URLs and requires TLS for them.

The API currently creates the initial schema on startup with EF Core `EnsureCreated`. For later schema changes, add and apply EF Core migrations.

## Deployment security

Do not expose the API publicly yet. Before deploying a personal dashboard, add authentication/API protection so other people cannot read your saved jobs or use your Gemini quota. Keep `GEMINI_API_KEY`, the database connection string and other secrets in the backend hosting environment. Set `DASHBOARD_ORIGIN` to the exact deployed dashboard origin to enable production CORS.

For a static-host deployment, set the build-time variable `VITE_API_BASE_URL` to the HTTPS URL of the deployed API. The dashboard makes direct browser requests to that API.
