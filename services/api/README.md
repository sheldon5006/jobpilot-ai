# JobPilot API

Local ASP.NET Core API for DBot job-description analysis.

## Requirements

- .NET 10 SDK
- Either Ollama with the `qwen3:4b` model, or a Gemini API key created through Google AI Studio
- Gemini free-tier limits may apply. No automatic paid fallback is implemented.

## 1. Configure the candidate profile

From this directory, run:

```powershell
Copy-Item candidate-profile.example.json candidate-profile.json
```

Edit `candidate-profile.json` with your accurate experience, dates, qualifications, skills, languages, and constraints. This file is intentionally ignored by Git: it can contain personal information. Never put your real profile in the example file or commit the local profile.

On first startup, the API imports this file into the database if no saved profile exists. After import, the database is the source of truth; use the dashboard's **My Profile** editor to make changes. Editing the JSON afterward will not overwrite the saved profile. `DBOT_PROFILE_PATH` can change the source path used for that initial import.

## 2. Configure the AI provider

Copy the local settings template if you have not already created your local settings file:

```powershell
Copy-Item .\appsettings.Local.example.json .\appsettings.Local.json
notepad .\appsettings.Local.json
```

The API loads `appsettings.Local.json` at startup. This file is ignored by Git. Never commit or share it. It is plain-text storage, not encryption.

### Option A: Gemini

Set `AI.Provider` to `Gemini`, and replace `PASTE_YOUR_NEW_GEMINI_API_KEY_HERE` with your API key. The default model is `gemini-3.1-flash-lite`. You can leave the Ollama section in the file; it is ignored while Gemini is selected.

Gemini free-tier limits apply. Google's free-tier data terms may allow submitted content to be used to improve Google products, so avoid adding unnecessary personal identifiers to your candidate profile. The API does not fall back to paid use automatically.

### Option B: Local Ollama

Make sure Ollama is running and the local model is available:

```powershell
ollama list
```

Use the already-downloaded `qwen3:4b` model, or download it with `ollama pull qwen3:4b`. Then set these local settings:

```json
{
  "AI": {
    "Provider": "Ollama"
  },
  "Ollama": {
    "BaseUrl": "http://127.0.0.1:11434",
    "Model": "qwen3:4b"
  }
}
```

Keep the `Gemini` section if you may want to switch back later; no Gemini API key is required when `AI.Provider` is `Ollama`. Local mode sends analysis requests to your Ollama service instead of Gemini and does not consume Gemini API quota.

**After changing the provider or model, restart the ASP.NET Core API.**

## How the fit score works

The fit score is computed by the API from requirement-level evidence. The model does not choose it.

1. **Requirements.** The model lists every distinct requirement stated in the vacancy (up to 10) exactly once. Each one is a match or a gap, and is marked Must-have, Preferred or Unknown. For each requirement it quotes the vacancy wording in the vacancy's own language. The API keeps a requirement only if that quote is found in the vacancy, so English requirement names from German job ads stay grounded and invented requirements are dropped.
2. **Evidence.** A match must cite profile-fact IDs. Its credit depends on where the cited facts come from:

   | Evidence | Credit |
   | --- | --- |
   | Work experience, dates, education, languages, certifications | 100% |
   | Professional skills list | 85% |
   | Internship | 80% |
   | Professional summary or candidate notes | 70% |
   | Personal or academic projects | 60% |

   When several facts are cited, their credits are averaged. A match whose cited facts don't establish the requirement becomes an Unverified gap, so the requirement still counts against the score.
3. **Gaps.** An Unverified gap earns 25% of its weight. An Unmet gap earns nothing, and the model may only mark a gap Unmet if it cites the profile fact that contradicts the requirement, such as the candidate's location against a required on-site city. Otherwise the gap is Unverified.
4. **Score.** The score is the weighted coverage × 100, where Must-have counts 3, Unknown 2 and Preferred 1. With fewer than three requirements, the score is pulled towards 50 and marked low-confidence.
5. **Caps and recommendation.**
   - An Unmet must-have caps the score at 35 and gives **Skip**.
   - An Unverified must-have caps it at 74, which is below the Apply threshold.
   - **Apply** needs a score of 75 or more with only preferred gaps. A score below 40 is **Skip**. Everything else is **Review**.

Deterministic eligibility checks still run before scoring:
- right-to-work and sponsorship conditions, when the vacancy states them;
- minimum experience, using dated work history;
- required certifications;
- current enrolment for working-student roles;
- explicit English or German CEFR levels.

Missing profile facts are Unverified, never assumed absent.

The model runs at temperature 0 with a fixed seed, so the same vacancy and profile give the same assessment. In a check on 10 saved vacancies, each analysed twice, every score was identical across the two runs. The profile is sent once, as a compact fact list grouped by section, which roughly halves the prompt size compared with the earlier JSON-plus-facts prompt.

The weights are a transparent heuristic, not a statistically validated probability of an interview or offer. The response includes `scoreBreakdown`: must-haves met/total, preferred met/total, evidence sources, any cap and a confidence flag.

## 3. Saved job history and dashboard

The API creates a local SQLite database at `services/api/jobpilot.db` by default. The file is ignored by Git. A successful analysis from either DBot or the dashboard is saved automatically, including the job description, fit score, recommendation, matched requirements, gaps, and questions. The database also stores the editable candidate profile and optional CV attachment for each saved job. The dashboard can update application status and notes.

Available endpoints:

- `GET /api/jobs` — list saved jobs, newest updated first.
- `GET /api/jobs/{id}` — get the saved job, original description, notes, and full analysis.
- `PUT /api/jobs/{id}` — update `applicationStatus` and `notes`. Allowed statuses: `Saved`, `Attempt`, `Applied`, `Interview`, `Rejected`, `Offer`.
- `POST /api/jobs/analyze` — analyze and save a job.

To run the dashboard, keep the API running in one terminal. In a second terminal from the repository root:

```powershell
cd apps/dashboard
npm install
npm run dev
```

Open the local URL shown by Vite, usually `http://127.0.0.1:5173`. The dashboard uses `VITE_API_BASE_URL` if set; otherwise, it calls `http://127.0.0.1:5080`.

### PostgreSQL for hosted deployment

SQLite is intended for local development. To use PostgreSQL, set `Database:Provider` to `Postgres` and set `ConnectionStrings:JobPilot` to a standard Npgsql connection string, or configure `DATABASE_URL` with a PostgreSQL connection URI from your managed provider. URI-based connections are configured to require TLS.

The initial schema is created with EF Core `EnsureCreated` for this prototype. Once the schema needs versioned upgrades, add EF Core migrations before evolving the deployed database.

**Access protection:** when the API listens on anything other than localhost, it requires Google sign-in restricted to `Auth:AllowedEmails`, and it refuses to start if `Auth:GoogleClientId`, `Auth:AllowedEmails` and `Auth:SigningKey` are missing. Keep the Gemini key, database credentials and signing key as server-side secrets. Set `DASHBOARD_ORIGIN` to the exact HTTPS origin of the deployed dashboard for production CORS. See `docs/deployment.md`.

## 4. Run the API

```powershell
dotnet run --urls http://127.0.0.1:5080
```

Health check:

```powershell
Invoke-RestMethod http://127.0.0.1:5080/api/health
```

## Endpoints

- `GET /api/health` — local health check.
- `GET /api/profile`, `PUT /api/profile` — view and update the career profile used for analysis.
- `GET /api/jobs`, `GET /api/jobs/{id}`, and `PUT /api/jobs/{id}` — saved job history and tracker.
- `POST /api/jobs/analyze` — analyses a supplied job description against the saved candidate profile and saves the result.
- `POST /api/jobs/{id}/cv`, `GET /api/jobs/{id}/cv`, `DELETE /api/jobs/{id}/cv` — upload, download, or remove the CV associated with one saved job.
- `POST /api/jobs/{id}/generated-cv` — generate a tailored CV for a saved job (body: `{ "customInstructions": "…" }`). Moves a `Saved` job to `Attempt`.
- `GET`, `PUT`, `DELETE /api/jobs/{id}/generated-cv` — read, save manual edits to, or delete the generated CV; `GET /api/jobs/{id}/generated-cv/pdf` downloads it as a PDF (rendered with QuestPDF under its Community licence) and `GET /api/jobs/{id}/generated-cv/docx` as an editable Word file.
- `GET /api/cv/default-instructions` — the default, user-editable CV style instructions.
- `POST /api/assistant/answer` — draft an answer to an application-form question (`question`, optional `jobId`/job fields, `length`: short, medium or long, `customInstructions`).

Example request:

```powershell
$body = @{
  jobTitle = "Werkstudent Software Developer (.NET)"
  company = "Example GmbH"
  jobDescription = "We are looking for a working student with C#, ASP.NET Core, SQL Server, REST API and Angular knowledge. English is required; German is a plus."
} | ConvertTo-Json

Invoke-RestMethod -Uri http://127.0.0.1:5080/api/jobs/analyze -Method Post -ContentType "application/json" -Body $body
```

## Regression testing

Start the API locally and configure the provider you want to evaluate in `appsettings.Local.json`. Then run the reusable regression script from the repository root:

```powershell
.\services\api\scripts\Invoke-RegressionTests.ps1
```

The script tests seven scenarios: a strong technical match, preferred German, mandatory German C2, explicit work-authorisation/sponsorship conditions, a mandatory AWS certification, current enrolment for a Werkstudent role, and a minimum-experience threshold. Eligibility cases adapt to the verified facts in the local profile. It checks recommendations and gaps, allows only expected targeted verification questions, validates that matches and gaps are traceable to the vacancy, checks evidence IDs against the local profile-fact catalog, ensures displayed evidence text matches those facts, and validates the score-calibration explanation. The API exposes mandatoryRequirementsStatus separately from the fit score and emits requirementValidationWarnings or evidenceValidationWarnings when generated output cannot be grounded.

By default, it makes two extra calls for each of the first three scenarios to check score stability. It fails if repeated scores differ by more than 10 points. Skip the extra calls with `-SkipScoreStability`, or choose another tolerance with `-ScoreTolerance 15`.

The local candidate profile supports `workAuthorization` (an accurate, country-specific right-to-work/sponsorship statement) and `certifications` (exact credentials and validity details). Leave either unknown until you can verify it; never invent these values.

Backend rule and evidence tests can be run from the repository root:

```powershell
dotnet test .\services\api.tests\JobPilot.Api.Tests.csproj
```

The regression script retries HTTP 502/503/504 responses twice by default (three total attempts) with short backoff delays. Use `-TransientRetries 0` to disable these extra attempts or set `-TransientRetries 3` for up to three retries. HTTP 429 rate-limit responses are not immediately retried; the script reports affected scenarios or stability checks as **INCONCLUSIVE** instead of presenting a quota limit as a rule failure or a passing test.

The quality checks are development guardrails for these controlled scenarios, not proof of accuracy across all vacancies. The script reads the ignored local `candidate-profile.json`; do not commit that file or paste personal data into issues.

## Current boundaries

- Successful analyses, their job descriptions, the editable candidate profile, and attached per-job CV files are stored in the configured database. The local JSON profile is only the initial import source.
- In Gemini mode, the Gemini key stays on the backend and is never returned to the browser.
- Analysis is decision support only. It does not submit applications.
- If required profile facts are unknown, the model should flag them for review.
- Free-tier quota errors are returned clearly; there is no paid-provider fallback.
