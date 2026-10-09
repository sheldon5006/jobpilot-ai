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
$profileAnchors += @($profile.professionalSummary)
$profileAnchors += @($profile.education)
$profileAnchors += @($profile.certifications)
$profileAnchors += @($profile.workAuthorization)
foreach ($language in @($profile.languages)) {
    $profileAnchors += @("$($language.language) $($language.proficiency)")
}
foreach ($experience in @($profile.experience)) {
    $profileAnchors += @($experience.evidence)
}
$profileAnchors = @(
    $profileAnchors |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Sort-Object -Unique
)

$ignoredTokens = @(
    "a", "an", "the", "and", "or", "of", "to", "for", "with", "as",
    "is", "are", "be", "been", "being", "must", "have", "has", "had",
    "that", "this", "these", "those", "in", "on", "by", "from", "their",
    "candidate", "profile", "evidence", "explicitly", "supported"
)

function Get-NormalizedTokens {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $tokens = [regex]::Matches($Text.ToLowerInvariant(), '[a-z0-9+#.]+')
    $normalized = foreach ($item in $tokens) {
        $token = $item.Value.Trim('.')

        if ($token -eq "proficiency" -or $token -eq "proficient") {
            $token = "language"
        } elseif ($token -eq "skills") {
            $token = "skill"
        } elseif ($token -eq "apis") {
            $token = "api"
        } elseif ($token -match 'ies$' -and $token.Length -gt 4) {
            $token = $token.Substring(0, $token.Length - 3) + "y"
        } elseif ($token -match 's$' -and $token.Length -gt 4 -and
                  $token -notmatch '(ss|us|is)$') {
            $token = $token.Substring(0, $token.Length - 1)
        }

        if ($token.Length -gt 0 -and $ignoredTokens -notcontains $token) {
            $token
        }
    }

    return @($normalized | Sort-Object -Unique)
}

function Test-RequirementGroundedInVacancy {
    param(
        [Parameter(Mandatory)][string]$Requirement,
        [Parameter(Mandatory)][string]$Vacancy
    )

    $requirementTokens = @(Get-NormalizedTokens $Requirement)
    $vacancyTokens = @(Get-NormalizedTokens $Vacancy)

    if ($requirementTokens.Count -eq 0) {
        return $false
    }

    $missingTokens = @(
        $requirementTokens | Where-Object { $vacancyTokens -notcontains $_ }
    )

    return ($missingTokens.Count -eq 0)
}

function Test-EvidenceGroundedInProfile {
    param(
        [Parameter(Mandatory)][string]$Evidence,
        [Parameter(Mandatory)][string[]]$Anchors
    )

    $evidenceTokens = @(Get-NormalizedTokens $Evidence)
    if ($evidenceTokens.Count -lt 3) {
        return $false
    }

    foreach ($anchor in $Anchors) {
        $anchorTokens = @(Get-NormalizedTokens $anchor)
        if ($anchorTokens.Count -eq 0) {
            continue
        }

        $overlap = @(
            $anchorTokens | Where-Object { $evidenceTokens -contains $_ }
        ).Count

        $coverage = $overlap / [double]$anchorTokens.Count
        $minimumOverlap = [math]::Min(3, $anchorTokens.Count)

        if ($overlap -ge $minimumOverlap -and $coverage -ge 0.7) {
            return $true
        }
    }

    return $false
}

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
    },
    [pscustomobject]@{
        Name = "4 - Work authorization gate"
        JobTitle = ".NET Software Developer - Right to Work Required"
        JobDescription = "Required: practical development experience with C#, ASP.NET Core, REST APIs, SQL Server, and Angular. Applicants must already have the legal right to work in Germany. The employer cannot provide visa sponsorship. No degree or language requirement is specified."
        ExpectedQuestionPattern = "authori[sz]ed|right to work|sponsorship|work eligibility"
    },
    [pscustomobject]@{
        Name = "5 - Mandatory certification"
        JobTitle = ".NET Software Developer - AWS Certification Required"
        JobDescription = "Required: practical development experience with C#, ASP.NET Core, REST APIs, SQL Server, and Angular. An AWS Certified Developer - Associate certification is mandatory before starting; the candidate must currently hold this credential, not merely be preparing for it. No language requirement is specified."
        ExpectedQuestionPattern = "AWS|certification|credential|certificate"
    },
    [pscustomobject]@{
        Name = "6 - Werkstudent enrolment"
        JobTitle = "Werkstudent Software Developer (.NET)"
        JobDescription = "Applicants must be currently enrolled at a university throughout employment. Required: practical development experience with C#, ASP.NET Core, REST APIs, SQL Server, and Angular. No additional degree, language, or employment-duration requirement is specified."
        ExpectedQuestionPattern = "enrolled|student status|university|education"
    },
    [pscustomobject]@{
        Name = "7 - Minimum experience threshold"
        JobTitle = "Senior .NET Software Developer"
        JobDescription = "Required: practical development experience with C#, ASP.NET Core, REST APIs, SQL Server, and Angular. A minimum of 10 years of relevant professional experience is mandatory. No language, degree, or certification requirement is specified."
        ExpectedQuestionPattern = "experience|years|dates"
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

    # Controlled scenarios permit only a narrowly-scoped question when a mandatory
    # eligibility fact is intentionally absent from the profile.
    foreach ($question in @($Result.questionsToVerify)) {
        if ([string]::IsNullOrWhiteSpace([string]$question)) {
            continue
        }

        $expectedQuestionPattern = [string]$Case.ExpectedQuestionPattern
        if (-not [string]::IsNullOrWhiteSpace($expectedQuestionPattern) -and
            [regex]::IsMatch([string]$question, $expectedQuestionPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            continue
        }

        $issues.Add("UNSUPPORTED_QUESTION: '$question'")
    }

    # A match must correspond to vacancy text, and its evidence must cite the local profile.
    foreach ($match in @($Result.matchedRequirements)) {
        $requirement = ([string]$match.requirement -replace '\s*\(preferred\)\s*$', '').Trim()
        $evidence = ([string]$match.evidence).Trim()

        if ([string]::IsNullOrWhiteSpace($requirement) -or
            -not (Test-RequirementGroundedInVacancy -Requirement $requirement -Vacancy $vacancy)) {
            $issues.Add("UNSUPPORTED_MATCH: '$requirement' contains terms not grounded in the vacancy text.")
        }

        if ($evidence.Length -lt 24) {
            $issues.Add("WEAK_EVIDENCE: '$requirement' evidence is too short to explain the profile support: '$evidence'")
        }

        if (-not (Test-EvidenceGroundedInProfile -Evidence $evidence -Anchors $profileAnchors)) {
            $issues.Add("UNSUPPORTED_EVIDENCE: '$requirement' evidence did not substantially overlap with a known skill/profile evidence anchor: '$evidence'")
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
        "4 - Work authorization gate" {
            $authStatus = [string]$profile.workAuthorization
            $authUnknown = [string]::IsNullOrWhiteSpace($authStatus) -or
                $authStatus -match "\b(replace|unknown|not specified|not provided|tbd|n/a)\b"
            $authRequiresSponsorship = $authStatus -match "\b(?:require|requires|need|needs)\s+(?:(?:visa|employer|work)\s+)?sponsorship\b"
            $authExplicitlyUnmet = $authStatus -match "\b(?:not\s+(?:currently\s+)?authori[sz]ed|not\s+eligible\s+to\s+work|no\s+legal\s+right\s+to\s+work|does\s+not\s+have\s+(?:the\s+)?right\s+to\s+work)\b"
            $authPositive = $authStatus -match "\b(?:authori[sz]ed|eligible|legal\s+right|right\s+to\s+work|valid\s+work\s+permit)\b" -and
                -not $authExplicitlyUnmet -and
                $authStatus -notmatch "\b(?:student\s+(?:visa|residence\s+permit)|limited\s+working\s+hours|work\s+limits?)\b"

            $authGaps = @($gaps | Where-Object { $_.requirement -match "authori[sz]ation|right to work|sponsorship|work eligibility" })
            if ($authUnknown -or (-not $authRequiresSponsorship -and -not $authExplicitlyUnmet -and -not $authPositive)) {
                return (
                    $Result.recommendation -eq "Review" -and
                    $authGaps.Count -eq 1 -and
                    $authGaps[0].severity -eq "Must-have" -and
                    $authGaps[0].status -eq "Unverified" -and
                    $questions.Count -eq 1
                )
            }

            if ($authRequiresSponsorship -or $authExplicitlyUnmet) {
                return (
                    $Result.recommendation -eq "Skip" -and
                    $authGaps.Count -eq 1 -and
                    $authGaps[0].severity -eq "Must-have" -and
                    $authGaps[0].status -eq "Unmet" -and
                    $questions.Count -eq 0
                )
            }

            return (
                $authPositive -and
                $Result.recommendation -eq "Apply" -and
                $authGaps.Count -eq 0 -and
                $questions.Count -eq 0
            )
        }
        "5 - Mandatory certification" {
            $certs = @($profile.certifications)
            $hasAwsCredential = @($certs | Where-Object { [string]$_ -match "AWS\s+Certified\s+Developer(?:\s*[-–]\s*Associate)?" }).Count -gt 0
            $awsGaps = @($gaps | Where-Object { $_.requirement -match "AWS|certification|certificate" })
            if ($hasAwsCredential) {
                return (
                    $Result.recommendation -eq "Apply" -and
                    $awsGaps.Count -eq 0 -and
                    $questions.Count -eq 0
                )
            }

            return (
                $Result.recommendation -eq "Review" -and
                $awsGaps.Count -eq 1 -and
                $awsGaps[0].severity -eq "Must-have" -and
                $awsGaps[0].status -eq "Unverified" -and
                $questions.Count -eq 1
            )
        }
        "6 - Werkstudent enrolment" {
            $educationText = (@($profile.education) -join " ")
            $constraintsText = (@($profile.constraints) -join " ")
            $activeStudent = $educationText -match "\b(?:in\s+progress|ongoing|currently\s+studying|currently\s+enrolled|enrolled|expected\s+graduation|expected\s+completion)\b" -and
                $educationText -notmatch "\b(?:replace|unknown|not\s+specified|not\s+provided|tbd|n/a)\b"
            $notEnrolled = ($educationText + " " + $constraintsText) -match "\b(?:not\s+currently\s+enrolled|not\s+enrolled|not\s+currently\s+studying|no\s+longer\s+enrolled)\b"
            $studentGaps = @($gaps | Where-Object { $_.requirement -match "Werkstudent|working student|enrolled|enrollment|enrolment|student status" })

            if ($activeStudent -and -not $notEnrolled) {
                return (
                    $Result.recommendation -eq "Apply" -and
                    $studentGaps.Count -eq 0 -and
                    $questions.Count -eq 0
                )
            }

            if ($notEnrolled) {
                return (
                    $Result.recommendation -eq "Skip" -and
                    $studentGaps.Count -eq 1 -and
                    $studentGaps[0].severity -eq "Must-have" -and
                    $studentGaps[0].status -eq "Unmet" -and
                    $questions.Count -eq 0
                )
            }

            return (
                $Result.recommendation -eq "Review" -and
                $studentGaps.Count -eq 1 -and
                $studentGaps[0].severity -eq "Must-have" -and
                $studentGaps[0].status -eq "Unverified" -and
                $questions.Count -eq 1
            )
        }
        "7 - Minimum experience threshold" {
            $experienceGaps = @($gaps | Where-Object {
                $_.requirement -match "\b(?:years?|yrs?|months?)\b" -and
                $_.requirement -match "\bexperience\b"
            })
            $experienceMatches = @($Result.matchedRequirements | Where-Object {
                $_.requirement -match "\b(?:years?|yrs?|months?)\b" -and
                $_.requirement -match "\bexperience\b"
            })

            if ($experienceGaps.Count -eq 1) {
                if ($experienceGaps[0].severity -ne "Must-have") { return $false }
                if ($experienceGaps[0].status -eq "Unmet") {
                    return $Result.recommendation -eq "Skip" -and $questions.Count -eq 0
                }
                if ($experienceGaps[0].status -eq "Unverified") {
                    return $Result.recommendation -eq "Review" -and $questions.Count -eq 1
                }
                return $false
            }

            return (
                $experienceGaps.Count -eq 0 -and
                $experienceMatches.Count -eq 1 -and
                $Result.recommendation -eq "Apply" -and
                $questions.Count -eq 0
            )
        }
    }

    return $false
}

$summary = [System.Collections.Generic.List[object]]::new()
$allIssues = [System.Collections.Generic.List[string]]::new()
$scoreSamples = @{}
foreach ($case in $cases) {
    $scoreSamples[$case.Name] = [System.Collections.Generic.List[int]]::new()
}

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

    $scoreSamples[$case.Name].Add([int]$result.matchScore)

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

if (-not $SkipScoreStability -and $summary.Count -ge 3) {
    Write-Host ""
    Write-Host "Checking score stability for the three core scenarios..." -ForegroundColor Cyan

    for ($caseIndex = 0; $caseIndex -lt 3; $caseIndex++) {
        $stabilityCase = $cases[$caseIndex]
        Write-Host "Scenario: $($stabilityCase.Name)" -ForegroundColor DarkCyan

        for ($repeat = 2; $repeat -le 3; $repeat++) {
            $call = Invoke-JobAnalysis -Case $stabilityCase
            if (-not $call.Success) {
                $allIssues.Add("SCORE_STABILITY: '$($stabilityCase.Name)' repeat $repeat failed after $($call.Seconds)s - $($call.Error)")
                Write-Host "Repeat $repeat failed: $($call.Error)" -ForegroundColor Red
                continue
            }

            $result = $call.Result
            $scoreSamples[$stabilityCase.Name].Add([int]$result.matchScore)
            Write-Host "Repeat $($repeat): score $($result.matchScore), recommendation $($result.recommendation), $($call.Seconds)s"

            if (-not (Test-ExpectedScenario -Case $stabilityCase -Result $result)) {
                $allIssues.Add("SCORE_STABILITY: '$($stabilityCase.Name)' repeat $repeat changed the expected recommendation/gap behaviour.")
            }

            foreach ($issue in @(Get-QualityIssues -Case $stabilityCase -Result $result)) {
                $allIssues.Add("SCORE_STABILITY '$($stabilityCase.Name)' repeat $($repeat): $issue")
            }
        }

        $scores = @($scoreSamples[$stabilityCase.Name].ToArray())
        if ($scores.Count -ge 2) {
            $minScore = ($scores | Measure-Object -Minimum).Minimum
            $maxScore = ($scores | Measure-Object -Maximum).Maximum
            $spread = $maxScore - $minScore
            Write-Host "Scores: $($scores -join ', '); spread = $spread point(s)."

            if ($spread -gt $ScoreTolerance) {
                $allIssues.Add("SCORE_VARIANCE: '$($stabilityCase.Name)' varied by $spread points; allowed spread is $ScoreTolerance.")
            } else {
                Write-Host "Score stability: PASS (spread within $ScoreTolerance points)." -ForegroundColor Green
            }
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
