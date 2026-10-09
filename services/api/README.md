# JobPilot API

Local ASP.NET Core API for DBot job-description analysis.

## Requirements

- .NET 10 SDK
- A Gemini API key created through Google AI Studio
- The free tier may have rate limits. No automatic paid fallback is implemented.

## 1. Configure the candidate profile

From this directory, run:

```powershell
Copy-Item candidate-profile.example.json candidate-profile.json
```

Edit `candidate-profile.json` with your accurate experience, dates, qualifications, skills, languages, and constraints. This file is intentionally ignored by Git: it can contain personal information. Never put your real profile in the example file or commit the local profile.

The API reads it from the API content directory by default. Override the path with `DBOT_PROFILE_PATH` if needed.

## 2. Configure the Gemini key in a local file

1. From this directory, copy the example configuration:

```powershell
Copy-Item .\appsettings.Local.example.json .\appsettings.Local.json
```

2. Open `appsettings.Local.json` in your editor and replace `PASTE_YOUR_NEW_GEMINI_API_KEY_HERE` with your new key. Set the model to `gemini-3.1-flash-lite` for the faster, cost-efficient default used by DBot, or choose another model currently available to your API project.

```powershell
notepad .\appsettings.Local.json
```

The API explicitly loads `appsettings.Local.json` at startup. The file is ignored by Git; the tracked example contains only a placeholder. Do not copy a real key into the example file, commit it, share it, or publish it. This is plain-text local development storage, not encryption; anyone with access to your Windows account and file can read it. The API also continues to support `GEMINI_API_KEY` and `GEMINI_MODEL` environment variables as fallbacks.

The API does not fall back to paid use automatically.

**Free-tier privacy note:** Google's Gemini API pricing page states that free-tier content may be used to improve its products. DBot sends the job description and candidate profile only when you explicitly click Analyze. Keep personal identifiers such as phone number, home address and email out of the analysis profile unless you have a clear reason to send them.

## 3. Run the API

```powershell
dotnet run --urls http://127.0.0.1:5080
```

Health check:

```powershell
Invoke-RestMethod http://127.0.0.1:5080/api/health
```

## Endpoints

- `GET /api/health` — local health check.
- `POST /api/jobs/analyze` — analyses a supplied job description against the local candidate profile.

Example request:

```powershell
$body = @{
  jobTitle = "Werkstudent Software Developer (.NET)"
  company = "Example GmbH"
  jobDescription = "We are looking for a working student with C#, ASP.NET Core, SQL Server, REST API and Angular knowledge. English is required; German is a plus."
} | ConvertTo-Json

Invoke-RestMethod -Uri http://127.0.0.1:5080/api/jobs/analyze -Method Post -ContentType "application/json" -Body $body
```

## Current boundaries

- Job descriptions and candidate profile are not stored by this API.
- The Gemini key stays on the backend and is never returned to the browser.
- Analysis is decision support only. It does not submit applications.
- If required profile facts are unknown, the model should flag them for review.
- Free-tier quota errors are returned clearly; there is no paid-provider fallback.
