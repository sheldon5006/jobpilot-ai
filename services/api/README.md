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

## Score calibration

The model supplies an initial 0–100 fit estimate. The backend then reconciles explicitly stated requirements against the local candidate profile. Deterministic eligibility checks cover:
- Existing right-to-work conditions and explicit no-sponsorship clauses (only when the vacancy states them).
- Explicit minimum relevant professional experience, using the profile summary or complete dated work history.
- Required professional certifications/licences and security clearance, checked against `certifications`.
- Current university enrolment for Werkstudent/working-student roles, checked against active/in-progress education entries.
- Explicit English/German requirements and CEFR levels.

Missing profile facts are treated as Unverified rather than assumed to be absent. An explicit conflict with a mandatory requirement is Unmet. Sponsorship availability alone is not treated as a disqualifier.

After those checks, the backend applies deterministic deductions to distinct gaps:

- Explicitly unmet must-have gap: 40 points each, capped at 50 points.
- Unverified must-have gap: 20 points each, capped at 50 points.
- Preferred gap: 5 points each, capped at 20 points.
- Gap whose severity is missing or unrecognised: 8 points each, capped at 24 points.
- Total deduction is capped at 60 points; the final score is constrained to 0–100.
- Duplicate gap requirements are counted once, using the highest applicable deduction.
- A clearly unmet must-have requirement forces `Skip`; an unverified must-have requirement forces `Review`. Preferred gaps do not block `Apply` by themselves.
- When one or more mandatory requirements are explicitly unmet, the final fit score is normalised to 20/100 for one unmet gate, 10/100 for two, and 0/100 for three or more. This makes hard-gate scores stable across model runs; the rationale still reports the model's initial estimate.
- A language gap is only scored when the vacancy explicitly states that language as required or preferred. The job ad is not evidence of the candidate's language proficiency.

These weights are a transparent heuristic, not a statistically validated probability of receiving an interview or offer. Evaluate them against labelled vacancies before treating scores as predictive.

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

## Regression testing

Start the API locally and configure the provider you want to evaluate in `appsettings.Local.json`. Then run the reusable regression script from the repository root:

```powershell
.\services\api\scripts\Invoke-RegressionTests.ps1
```

The script tests seven scenarios: a strong technical match, preferred German, mandatory German C2, explicit work-authorisation/sponsorship conditions, a mandatory AWS certification, current enrolment for a Werkstudent role, and a minimum-experience threshold. Eligibility cases adapt to the verified facts in the local profile. It checks recommendations and gaps, allows only expected targeted verification questions, verifies matched requirements against vacancy text, checks evidence against profile facts, and validates the score-calibration explanation.

By default, it makes two extra calls for each of the first three scenarios to check score stability. It fails if repeated scores differ by more than 10 points. Skip the extra calls with `-SkipScoreStability`, or choose another tolerance with `-ScoreTolerance 15`.

The local candidate profile supports `workAuthorization` (an accurate, country-specific right-to-work/sponsorship statement) and `certifications` (exact credentials and validity details). Leave either unknown until you can verify it; never invent these values.

The regression script retries HTTP 502/503/504 responses twice by default (three total attempts) with short backoff delays. Use `-TransientRetries 0` to disable these extra attempts or set `-TransientRetries 3` for up to three retries. Repeated failures are still reported as test failures.

The quality checks are development guardrails for these controlled scenarios, not proof of accuracy across all vacancies. The script reads the ignored local `candidate-profile.json`; do not commit that file or paste personal data into issues.

## Current boundaries

- Job descriptions and candidate profile are not stored by this API.
- In Gemini mode, the Gemini key stays on the backend and is never returned to the browser.
- Analysis is decision support only. It does not submit applications.
- If required profile facts are unknown, the model should flag them for review.
- Free-tier quota errors are returned clearly; there is no paid-provider fallback.
