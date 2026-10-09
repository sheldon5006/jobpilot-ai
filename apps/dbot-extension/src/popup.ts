interface MatchedRequirement {
  requirement: string;
  evidence: string;
}

interface RequirementGap {
  requirement: string;
  severity: string;
  explanation: string;
}

interface JobAnalysisResult {
  recommendation: "Apply" | "Review" | "Skip" | string;
  matchScore: number;
  detectedLanguage: string;
  englishSummary: string;
  summary: string;
  matchedRequirements: MatchedRequirement[];
  gaps: RequirementGap[];
  questionsToVerify: string[];
  rationale: string;
  requiresHumanReview: boolean;
  note: string;
}

interface ApiProblem {
  title?: string;
  detail?: string;
}

const API_BASE_URL = "http://127.0.0.1:5080";

function element<T extends HTMLElement>(selector: string): T {
  const found = document.querySelector<T>(selector);
  if (!found) {
    throw new Error(`DBot could not initialise: missing element ${selector}`);
  }
  return found;
}

const form = element<HTMLFormElement>("#job-form");
const jobTitleInput = element<HTMLInputElement>("#job-title");
const companyInput = element<HTMLInputElement>("#company");
const descriptionInput = element<HTMLTextAreaElement>("#job-description");
const analyzeButton = element<HTMLButtonElement>("#analyze-button");
const buttonLabel = element<HTMLElement>("#button-label");
const resultPanel = element<HTMLElement>("#result");
const resultTitle = element<HTMLElement>("#result-title");
const resultEyebrow = element<HTMLElement>("#result-eyebrow");
const resultIcon = element<HTMLElement>("#result-icon");
const recommendationPill = element<HTMLElement>("#recommendation-pill");
const matchScore = element<HTMLElement>("#match-score");
const englishSummary = element<HTMLElement>("#english-summary");
const resultCopy = element<HTMLElement>("#result-copy");
const resultMeta = element<HTMLElement>("#result-meta");
const matchesList = element<HTMLUListElement>("#matches-list");
const gapsList = element<HTMLUListElement>("#gaps-list");
const questionsList = element<HTMLUListElement>("#questions-list");

function showEmptyList(target: HTMLUListElement, message: string): void {
  target.replaceChildren();
  const item = document.createElement("li");
  item.className = "empty-list-item";
  item.textContent = message;
  target.append(item);
}

function renderMatches(items: MatchedRequirement[]): void {
  matchesList.replaceChildren();
  if (items.length === 0) {
    showEmptyList(matchesList, "No matched requirements were returned.");
    return;
  }

  for (const item of items) {
    const listItem = document.createElement("li");
    const heading = document.createElement("strong");
    const evidence = document.createElement("p");
    heading.textContent = item.requirement || "Requirement";
    evidence.textContent = item.evidence || "No supporting evidence supplied.";
    listItem.append(heading, evidence);
    matchesList.append(listItem);
  }
}

function renderGaps(items: RequirementGap[]): void {
  gapsList.replaceChildren();
  if (items.length === 0) {
    showEmptyList(gapsList, "No explicit gaps were identified. Verify that all mandatory requirements are covered.");
    return;
  }

  for (const item of items) {
    const listItem = document.createElement("li");
    const heading = document.createElement("strong");
    const severity = document.createElement("span");
    const detail = document.createElement("p");
    heading.textContent = item.requirement || "Requirement to check";
    severity.className = "gap-severity";
    severity.textContent = item.severity || "Unknown";
    detail.textContent = item.explanation || "Review this requirement manually.";
    listItem.append(heading, severity, detail);
    gapsList.append(listItem);
  }
}

function renderStrings(target: HTMLUListElement, items: string[], emptyMessage: string): void {
  target.replaceChildren();
  if (items.length === 0) {
    showEmptyList(target, emptyMessage);
    return;
  }

  for (const value of items) {
    const item = document.createElement("li");
    item.textContent = value;
    target.append(item);
  }
}

function recommendationClass(value: string): "apply" | "review" | "skip" {
  switch (value.toLowerCase()) {
    case "apply":
      return "apply";
    case "skip":
      return "skip";
    default:
      return "review";
  }
}

function renderAnalysis(result: JobAnalysisResult): void {
  const recommendation = recommendationClass(result.recommendation);
  recommendationPill.className = `recommendation-pill ${recommendation}`;
  recommendationPill.textContent = recommendation === "apply"
    ? "Apply"
    : recommendation === "skip"
      ? "Skip"
      : "Review";

  resultEyebrow.textContent = `AI ASSESSMENT · ${result.detectedLanguage || "Language unknown"}`;
  resultTitle.textContent = `${recommendationPill.textContent} recommendation`;
  resultIcon.textContent = recommendation === "apply" ? "✓" : recommendation === "skip" ? "×" : "!";
  matchScore.textContent = `${Math.max(0, Math.min(100, Math.round(result.matchScore || 0)))} / 100`;
  englishSummary.textContent = result.englishSummary || "No English summary was returned.";
  resultCopy.textContent = [result.summary, result.rationale].filter(Boolean).join(" ");
  resultMeta.textContent = [
    jobTitleInput.value.trim() || "Untitled role",
    companyInput.value.trim() || "Company not specified",
    `${descriptionInput.value.trim().length.toLocaleString()} characters analysed`
  ].join(" · ");

  renderMatches(Array.isArray(result.matchedRequirements) ? result.matchedRequirements : []);
  renderGaps(Array.isArray(result.gaps) ? result.gaps : []);
  renderStrings(
    questionsList,
    Array.isArray(result.questionsToVerify) ? result.questionsToVerify : [],
    "No extra questions returned. Still verify any legal or eligibility declarations yourself."
  );

  resultPanel.classList.remove("error-state");
  resultPanel.hidden = false;
}

function renderError(message: string): void {
  resultPanel.classList.add("error-state");
  resultEyebrow.textContent = "ANALYSIS NOT COMPLETED";
  resultTitle.textContent = "DBot could not analyse this job";
  resultIcon.textContent = "!";
  recommendationPill.className = "recommendation-pill review";
  recommendationPill.textContent = "Not analysed";
  matchScore.textContent = "--";
  englishSummary.textContent = "No recommendation was produced.";
  resultCopy.textContent = message;
  resultMeta.textContent = "Check the API setup, candidate profile and free-tier quota, then try again.";
  showEmptyList(matchesList, "Not available.");
  showEmptyList(gapsList, "Not available.");
  showEmptyList(questionsList, "Not available.");
  resultPanel.hidden = false;
}

form.addEventListener("input", () => {
  descriptionInput.setCustomValidity("");
});

form.addEventListener("submit", async (event: SubmitEvent) => {
  event.preventDefault();

  const description = descriptionInput.value.trim();
  if (description.length < 40) {
    descriptionInput.setCustomValidity("Please paste at least 40 characters from the job description.");
    descriptionInput.reportValidity();
    return;
  }
  descriptionInput.setCustomValidity("");

  analyzeButton.disabled = true;
  analyzeButton.setAttribute("aria-busy", "true");
  buttonLabel.textContent = "Analysing with Gemini…";
  resultPanel.hidden = true;

  try {
    const response = await fetch(`${API_BASE_URL}/api/jobs/analyze`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        jobTitle: jobTitleInput.value.trim(),
        company: companyInput.value.trim(),
        jobDescription: description
      })
    });

    const payload: unknown = await response.json().catch(() => null);

    if (!response.ok) {
      const problem = (payload ?? {}) as ApiProblem;
      throw new Error(problem.detail || problem.title || `Local API returned HTTP ${response.status}.`);
    }

    renderAnalysis(payload as JobAnalysisResult);
  } catch (error) {
    const message = error instanceof Error ? error.message : "An unexpected error occurred.";
    if (message.toLowerCase().includes("failed to fetch")) {
      renderError("Cannot reach the local API. Start the ASP.NET Core API at http://127.0.0.1:5080, then try again.");
    } else {
      renderError(message);
    }
  } finally {
    analyzeButton.disabled = false;
    analyzeButton.removeAttribute("aria-busy");
    buttonLabel.textContent = "Analyze job fit";
  }
});
