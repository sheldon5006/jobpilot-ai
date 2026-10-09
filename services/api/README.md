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

The API reads it from the API content directory by default. Override the path with `DBOT_PROFILE_PATH` if needed.

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
- In Gemini mode, the Gemini key stays on the backend and is never returned to the browser.
- Analysis is decision support only. It does not submit applications.
- If required profile facts are unknown, the model should flag them for review.
- Free-tier quota errors are returned clearly; there is no paid-provider fallback.
