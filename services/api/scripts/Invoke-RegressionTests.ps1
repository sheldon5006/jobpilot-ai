param(
    [switch]$SkipScoreStability,
    [ValidateRange(0, 100)]
    [int]$ScoreTolerance = 10
)

$ErrorActionPreference = "Stop"
$apiUrl = "http://127.0.0.1:5080/api/jobs/analyze"
$profilePath = Join-Path $PSScriptRoot "..\candidate-profile.json"

if (-not (Test-Path $profilePath)) {
    throw "Candidate profile not found at '$profilePath'. Configure services/api/candidate-profile.json first."
}

try {
    $profile = Get-Content -Raw -Path $profilePath | ConvertFrom-Json
} catch {
    throw "Could not read candidate-profile.json as JSON: $($_.Exception.Message)"
}

$profileAnchors = @()
$profileAnchors += @($profile.professionalSkills)
$profileAnchors += @($profile.projectAndAcademicSkills)
foreach ($experience in @($profile.experience)) {
    $profileAnchors += @($experience.evidence)
}
$profileAnchors = @(
    $profileAnchors |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Sort-Object -Unique
)

$cases = @(
    [pscustomobject]@{
        Name = "1 - Strong technical match"
        JobTitle = ".NET Software Developer"
        JobDescription = "We need a Software Developer to build web applications using C#, ASP.NET Core, REST APIs, SQL Server, and Angular. Requirements are practical experience developing enterprise web applications, working with APIs, and using relational databases. No language proficiency, degree, or specific employment-duration requirement is specified."
    },
    [pscustomobject]@{
        Name = "2 - German preferred"
        JobTitle = "Full-Stack Developer (.NET/Angular)"
        JobDescription = "Required: practical development experience with C#, ASP.NET Core, REST APIs, SQL Server, and Angular. German language skills are preferred but not mandatory. No specific German certificate is required."
    },
    [pscustomobject]@{
        Name = "3 - German C2 mandatory"
        JobTitle = ".NET Developer - German C2 Required"
        JobDescription = "Required: development experience with C#, ASP.NET Core, REST APIs, SQL Server, and Angular. Applicants must already demonstrate German proficiency at CEFR C2 before starting. This is a strict mandatory requirement, not a preference. Candidates who are still learning German do not meet this requirement."
    }
)

function Invoke-JobAnalysis {
    param([Parameter(Mandatory)]$Case)

    $body = @{
        jobTitle = $Case.JobTitle
        company = "DBot Automated Regression Test"
        jobDescription = $Case.JobDescription
    } | ConvertTo-Json -Depth 8

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = Invoke-RestMethod -Uri $apiUrl -Method Post -ContentType "application/json" -Body $body -TimeoutSec 180
        $timer.Stop()

        return [pscustomobject]@{
            Success = $true
            Result = $result
            Seconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
            Error = ""
        }
    } catch {
        $timer.Stop()
        return [pscustomobject]@{
            Success = $false
            Result = $null
            Seconds = [math]::Round($timer.Elapsed.TotalSeconds, 2)
            Error = $_.Exception.Message
        }
    }
}

function Get-QualityIssues {
    param(
        [Parameter(Mandatory)]$Case,
        [Parameter(Mandatory)]$Result
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    $vacancy = [string]$Case.JobTitle + [Environment]::NewLine + [string]$Case.JobDescription

    # These controlled scenarios explicitly need no follow-up questions.
    foreach ($question in @($Result.questionsToVerify)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$question)) {
            $issues.Add("UNSUPPORTED_QUESTION: '$question'")
        }
    }

    # A match must correspond to vacancy text, and its evidence must cite the local profile.
    foreach ($match in @($Result.matchedRequirements)) {
        $requirement = ([string]$match.requirement -replace '\s*\(preferred\)\s*$', '').Trim()
        $evidence = ([string]$match.evidence).Trim()

        if ([string]::IsNullOrWhiteSpace($requirement) -or
            $vacancy.IndexOf($requirement, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            $issues.Add("UNSUPPORTED_MATCH: '$requirement' is not explicitly present in the vacancy text.")
        }

        if ($evidence.Length -lt 24) {
            $issues.Add("WEAK_EVIDENCE: '$requirement' evidence is too short to explain the profile support: '$evidence'")
        }

        $hasProfileAnchor = $false
        foreach ($anchor in $profileAnchors) {
            $anchorText = [string]$anchor
            if ($anchorText.Length -ge 2 -and
                $evidence.IndexOf($anchorText, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $hasProfileAnchor = $true
                break
            }
        }

        if (-not $hasProfileAnchor) {
            $issues.Add("UNSUPPORTED_EVIDENCE: '$requirement' evidence did not quote or name a known skill/profile evidence anchor: '$evidence'")
        }
    }

    # The score exposed by the API must agree with its calibration breakdown.
    $rationale = [string]$Result.rationale
    $calibrationMatch = [regex]::Match(
        $rationale,
        'Score calibration:\s*model estimate\s*(\d+)/100;.*?calibrated score\s*(\d+)/100',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    if (-not $calibrationMatch.Success) {
        $issues.Add("MISSING_SCORE_BREAKDOWN: rationale did not contain a parseable calibration breakdown.")
    } elseif ([int]$calibrationMatch.Groups[2].Value -ne [int]$Result.matchScore) {
        $issues.Add("SCORE_BREAKDOWN_MISMATCH: API score $($Result.matchScore) differs from the rationale's calibrated score $($calibrationMatch.Groups[2].Value).")
    }

    if ([int]$Result.matchScore -lt 0 -or [int]$Result.matchScore -gt 100) {
        $issues.Add("SCORE_OUT_OF_RANGE: $($Result.matchScore)")
    }

    return @($issues.ToArray())
}

function Test-ExpectedScenario {
    param(
        [Parameter(Mandatory)]$Case,
        [Parameter(Mandatory)]$Result
    )

    $gaps = @($Result.gaps)
    $questions = @($Result.questionsToVerify)

    switch ($Case.Name) {
        "1 - Strong technical match" {
            return (
                $Result.recommendation -eq "Apply" -and
                $gaps.Count -eq 0 -and
                $questions.Count -eq 0
            )
        }
        "2 - German preferred" {
            $germanGaps = @($gaps | Where-Object { $_.requirement -match "German|Deutsch" })
            $otherGaps = @($gaps | Where-Object { $_.requirement -notmatch "German|Deutsch" })
            return (
                $Result.recommendation -eq "Apply" -and
                $germanGaps.Count -eq 1 -and
                $germanGaps[0].severity -eq "Preferred" -and
                $otherGaps.Count -eq 0 -and
                $questions.Count -eq 0
            )
        }
        "3 - German C2 mandatory" {
            $germanGaps = @($gaps | Where-Object { $_.requirement -match "German|Deutsch" })
            return (
                $Result.recommendation -eq "Skip" -and
                $germanGaps.Count -eq 1 -and
                $germanGaps[0].severity -eq "Must-have" -and
                $germanGaps[0].status -eq "Unmet" -and
                $questions.Count -eq 0
            )
        }
    }

    return $false
}

$summary = [System.Collections.Generic.List[object]]::new()
$allIssues = [System.Collections.Generic.List[string]]::new()
$strongMatchScores = [System.Collections.Generic.List[int]]::new()

foreach ($case in $cases) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor DarkGray
    Write-Host $case.Name -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor DarkGray

    $call = Invoke-JobAnalysis -Case $case
    if (-not $call.Success) {
        $summary.Add([pscustomobject]@{
            Test = $case.Name
            Status = "REQUEST FAILED"
            Recommendation = ""
            Score = $null
            Gaps = $null
            Seconds = $call.Seconds
        })
        $allIssues.Add("$($case.Name): REQUEST FAILED after $($call.Seconds)s - $($call.Error)")
        Write-Host $call.Error -ForegroundColor Red
        continue
    }

    $result = $call.Result
    $scenarioPassed = Test-ExpectedScenario -Case $case -Result $result
    $issues = @(Get-QualityIssues -Case $case -Result $result)

    if (-not $scenarioPassed) {
        $issues += "SCENARIO_EXPECTATION_FAILED: recommendation/gaps/questions did not match expected behaviour."
    }

    if ($case.Name -eq "1 - Strong technical match") {
        $strongMatchScores.Add([int]$result.matchScore)
    }

    $status = if ($scenarioPassed -and $issues.Count -eq 0) { "PASS" } else { "FAIL" }
    $colour = if ($status -eq "PASS") { "Green" } else { "Red" }

    Write-Host "Recommendation: $($result.recommendation)"
    Write-Host "Score: $($result.matchScore)/100"
    Write-Host "Gaps: $(@($result.gaps).Count)"
    Write-Host "Time: $($call.Seconds)s"
    Write-Host "Regression result: $status" -ForegroundColor $colour

    foreach ($issue in $issues) {
        $allIssues.Add("$($case.Name): $issue")
        Write-Host " - $issue" -ForegroundColor Yellow
    }

    $summary.Add([pscustomobject]@{
        Test = $case.Name
        Status = $status
        Recommendation = $result.recommendation
        Score = $result.matchScore
        Gaps = @($result.gaps).Count
        Seconds = $call.Seconds
    })
}

if (-not $SkipScoreStability -and $summary.Count -ge 1 -and
    $summary[0].Test -eq "1 - Strong technical match" -and
    $summary[0].Status -ne "REQUEST FAILED") {
    Write-Host ""
    Write-Host "Checking score stability with two repeat requests..." -ForegroundColor Cyan

    for ($repeat = 2; $repeat -le 3; $repeat++) {
        $call = Invoke-JobAnalysis -Case $cases[0]
        if (-not $call.Success) {
            $allIssues.Add("SCORE_STABILITY: repeat $repeat failed after $($call.Seconds)s - $($call.Error)")
            Write-Host "Repeat $repeat failed: $($call.Error)" -ForegroundColor Red
            continue
        }

        $result = $call.Result
        $strongMatchScores.Add([int]$result.matchScore)
        Write-Host "Repeat $($repeat): score $($result.matchScore), recommendation $($result.recommendation), $($call.Seconds)s"

        if (-not (Test-ExpectedScenario -Case $cases[0] -Result $result)) {
            $allIssues.Add("SCORE_STABILITY: repeat $repeat changed expected strong-match behaviour.")
        }

        foreach ($issue in @(Get-QualityIssues -Case $cases[0] -Result $result)) {
            $allIssues.Add("SCORE_STABILITY repeat $($repeat): $issue")
        }
    }

    if ($strongMatchScores.Count -ge 2) {
        $minScore = ($strongMatchScores | Measure-Object -Minimum).Minimum
        $maxScore = ($strongMatchScores | Measure-Object -Maximum).Maximum
        $spread = $maxScore - $minScore
        Write-Host "Strong-match scores: $($strongMatchScores -join ', '); spread = $spread point(s)."

        if ($spread -gt $ScoreTolerance) {
            $allIssues.Add("SCORE_VARIANCE: repeated identical strong-match vacancy varied by $spread points; allowed spread is $ScoreTolerance.")
        } else {
            Write-Host "Score stability: PASS (spread within $ScoreTolerance points)." -ForegroundColor Green
        }
    }
} elseif ($SkipScoreStability) {
    Write-Host ""
    Write-Host "Score stability check skipped by request." -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "========== REGRESSION SUMMARY ==========" -ForegroundColor Cyan
$summary | Format-Table Test, Status, Recommendation, Score, Gaps, Seconds -AutoSize

if ($allIssues.Count -gt 0) {
    Write-Host ""
    Write-Host "Quality issues detected:" -ForegroundColor Yellow
    foreach ($issue in $allIssues) {
        Write-Host " - $issue" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "RESULT: FAIL ($($allIssues.Count) issue(s) detected)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "RESULT: PASS. Scenario, evidence, question, and score checks succeeded." -ForegroundColor Green
exit 0
