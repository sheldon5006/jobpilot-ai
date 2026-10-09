interface MatchedRequirement {
  requirement: string;
  evidence: string;
  evidenceIds: string[];
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
  mandatoryRequirementsStatus: string;
  evidenceValidationWarnings: string[];
  requirementValidationWarnings: string[];
  requiresHumanReview: boolean;
  note: string;
}

interface ApiProblem {
  title?: string;
  detail?: string;
}

interface ExtractedJobDetails {
  found: boolean;
  title: string;
  company: string;
  description: string;
  url: string;
  pageTitle: string;
  reason: string;
  extractionSource: string;
}

interface DbotSettings {
  autoFetchEnabled: boolean;
  autoAnalyzeEnabled: boolean;
}

interface SettingsResponse {
  ok: boolean;
  error?: string;
  settings?: DbotSettings;
  allSitesPermission?: boolean;
}

interface FetchResponse {
  ok: boolean;
  error?: string;
  tabId?: number;
  job?: ExtractedJobDetails;
  source?: "manual";
}

const API_BASE_URL = "http://127.0.0.1:5080";
const ALL_HTTP_ORIGINS = ["http://*/*", "https://*/*"];
const autoAnalyzedUrls = new Set<string>();
let settings: DbotSettings = { autoFetchEnabled: false, autoAnalyzeEnabled: false };

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
const mandatoryStatus = element<HTMLElement>("#mandatory-status");
const evidenceWarningSection = element<HTMLElement>("#evidence-warning-section");
const evidenceWarningsList = element<HTMLUListElement>("#evidence-warnings-list");
const requirementWarningSection = element<HTMLElement>("#requirement-warning-section");
const requirementWarningsList = element<HTMLUListElement>("#requirement-warnings-list");
const resultMeta = element<HTMLElement>("#result-meta");
const matchesList = element<HTMLUListElement>("#matches-list");
const gapsList = element<HTMLUListElement>("#gaps-list");
const questionsList = element<HTMLUListElement>("#questions-list");

const fetchPageButton = element<HTMLButtonElement>("#fetch-current-page-button");
const pageFetchStatus = element<HTMLElement>("#page-fetch-status");
const autoFetchToggle = element<HTMLInputElement>("#auto-fetch-toggle");
const autoAnalyzeToggle = element<HTMLInputElement>("#auto-analyze-toggle");

function setPageStatus(message: string, kind: "info" | "success" | "error" = "info"): void {
  pageFetchStatus.textContent = message;
  pageFetchStatus.className = `page-fetch-status ${kind}`;
}

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
    evidence.textContent = item.evidence || "No supporting profile fact supplied.";
    listItem.append(heading, evidence);
    if (Array.isArray(item.evidenceIds) && item.evidenceIds.length > 0) {
      const sources = document.createElement("p");
      sources.className = "evidence-source";
      sources.textContent = `Verified profile facts: ${item.evidenceIds.join(", ")}`;
      listItem.append(sources);
    }
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
  mandatoryStatus.textContent = result.mandatoryRequirementsStatus || "Unknown";
  const requirementWarnings = Array.isArray(result.requirementValidationWarnings) ? result.requirementValidationWarnings : [];
  requirementWarningSection.hidden = requirementWarnings.length === 0;
  renderStrings(requirementWarningsList, requirementWarnings, "No vacancy requirement warnings.");
  const warnings = Array.isArray(result.evidenceValidationWarnings) ? result.evidenceValidationWarnings : [];
  evidenceWarningSection.hidden = warnings.length === 0;
  renderStrings(evidenceWarningsList, warnings, "No evidence warnings.");
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
  mandatoryStatus.textContent = "Unknown — analysis not completed.";
  evidenceWarningSection.hidden = true;
  showEmptyList(evidenceWarningsList, "Not available.");
  requirementWarningSection.hidden = true;
  showEmptyList(requirementWarningsList, "Not available.");
  resultMeta.textContent = "Check the API setup, candidate profile and free-tier quota, then try again.";
  showEmptyList(matchesList, "Not available.");
  showEmptyList(gapsList, "Not available.");
  showEmptyList(questionsList, "Not available.");
  resultPanel.hidden = false;
}

async function analyzeCurrentJob(): Promise<void> {
  const description = descriptionInput.value.trim();
  if (description.length < 40) {
    descriptionInput.setCustomValidity("Please paste at least 40 characters from the job description.");
    descriptionInput.reportValidity();
    setPageStatus("The detected description is too short. Review it or paste the complete job description.", "error");
    return;
  }
  descriptionInput.setCustomValidity("");

  if (analyzeButton.disabled) return;
  analyzeButton.disabled = true;
  analyzeButton.setAttribute("aria-busy", "true");
  buttonLabel.textContent = "Analysing with Gemini…";
  resultPanel.hidden = true;

  try {
    const response = await fetch(`${API_BASE_URL}/api/jobs/analyze`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
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
    setPageStatus("Analysis complete. Review the recommendation and evidence below.", "success");
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
}

async function requestSettings(): Promise<void> {
  try {
    const response = await chrome.runtime.sendMessage({ type: "DBOT_GET_SETTINGS" }) as Promise<SettingsResponse>;
    if (!response?.ok || !response.settings) {
      throw new Error(response?.error || "Could not load settings.");
    }
    settings = response.settings;
    autoFetchToggle.checked = settings.autoFetchEnabled;
    autoAnalyzeToggle.checked = settings.autoAnalyzeEnabled;
    autoAnalyzeToggle.disabled = !settings.autoFetchEnabled;
    if (settings.autoFetchEnabled && response.allSitesPermission) {
      const result = await chrome.runtime.sendMessage({ type: "DBOT_FETCH_CURRENT_PAGE" }) as Promise<FetchResponse>;
      if (result?.ok && result.job) {
        await applyDetectedJob(result.job, settings.autoAnalyzeEnabled, "auto");
      } else if (result?.error) {
        setPageStatus(result.error, "info");
      }
    }
  } catch (error) {
    setPageStatus(error instanceof Error ? error.message : "Could not load settings.", "error");
  }
}

async function fetchCurrentPage(): Promise<void> {
  fetchPageButton.disabled = true;
  fetchPageButton.textContent = "Reading page HTML…";
  setPageStatus("Looking for structured job data and visible job-description content…", "info");
  try {
    const response = await chrome.runtime.sendMessage<FetchResponse>({ type: "DBOT_FETCH_CURRENT_PAGE" });
    if (!response?.ok || !response.job) {
      throw new Error(response?.error || "Could not fetch the current page.");
    }
    await applyDetectedJob(response.job, settings.autoAnalyzeEnabled, "manual");
  } catch (error) {
    setPageStatus(error instanceof Error ? error.message : "Could not fetch details from this page.", "error");
  } finally {
    fetchPageButton.disabled = false;
    fetchPageButton.textContent = "↧ Fetch from current page";
  }
}

async function applyDetectedJob(
  job: ExtractedJobDetails,
  shouldAutoAnalyze: boolean,
  source: "manual" | "auto"
): Promise<void> {
  if (!job.found) {
    setPageStatus(job.reason || "No job description detected. You can paste the description manually.", "error");
    return;
  }

  jobTitleInput.value = job.title || job.pageTitle || jobTitleInput.value;
  companyInput.value = job.company || companyInput.value;
  descriptionInput.value = job.description;
  descriptionInput.setCustomValidity("");

  let displayHost = job.url;
  try {
    displayHost = new URL(job.url).hostname;
  } catch {
    // Keep the complete URL if it isn't a regular web URL.
  }

  setPageStatus(
    `Fetched from ${displayHost} (${job.extractionSource}). Review the title, company and description before analysis.`,
    "success"
  );

  const analysisKey = job.url;
  if (shouldAutoAnalyze && !autoAnalyzedUrls.has(analysisKey)) {
    if (job.description.trim().length < 40) {
      setPageStatus("Job details were found, but the description is too short to analyse. Review it or paste more text.", "error");
      return;
    }
    autoAnalyzedUrls.add(analysisKey);
    await analyzeCurrentJob();
  } else if (source === "auto") {
    // The details have been filled without silently submitting anything to the AI provider.
    setPageStatus(
      `Job details detected on ${displayHost}. Review them and click Analyze job fit when ready.`,
      "success"
    );
  }
}

async function updateAutoFetchSetting(enabled: boolean): Promise<void> {
  if (enabled) {
    // Ask for broad site access only after the user explicitly enables automatic page reading.
    const permissionRequest = chrome.permissions.request({ origins: ALL_HTTP_ORIGINS });
    const granted = await permissionRequest;
    if (!granted) {
      autoFetchToggle.checked = false;
      settings.autoFetchEnabled = false;
      autoAnalyzeToggle.checked = false;
      autoAnalyzeToggle.disabled = true;
      setPageStatus("Automatic fetching stays off until you grant page access. You can still fetch the current tab manually.", "error");
      return;
    }
  }

  const requestedAnalyze = enabled && autoAnalyzeToggle.checked;
  const response = await chrome.runtime.sendMessage({
    type: "DBOT_SET_SETTINGS",
    autoFetchEnabled: enabled,
    autoAnalyzeEnabled: requestedAnalyze
  }) as Promise<SettingsResponse>;
  if (!response?.ok || !response.settings) {
    throw new Error(response?.error || "Could not save the automatic fetching setting.");
  }
  settings = response.settings;
  autoFetchToggle.checked = settings.autoFetchEnabled;
  autoAnalyzeToggle.checked = settings.autoAnalyzeEnabled;
  autoAnalyzeToggle.disabled = !settings.autoFetchEnabled;

  if (enabled) {
    setPageStatus(
      settings.autoAnalyzeEnabled
        ? "Auto-fetch and auto-analyse are enabled. Detected job descriptions will be sent to the configured AI provider."
        : "Auto-fetch is enabled. DBot will fill job details when you open a page; analysis waits until you click Analyze.",
      "success"
    );
    await fetchCurrentPage();
  } else {
    settings.autoAnalyzeEnabled = false;
    autoAnalyzeToggle.checked = false;
    autoAnalyzeToggle.disabled = true;
    setPageStatus("Automatic fetching is off. Use Fetch from current page when you need it.", "info");
  }
}

form.addEventListener("input", () => {
  descriptionInput.setCustomValidity("");
});

form.addEventListener("submit", event => {
  event.preventDefault();
  void analyzeCurrentJob();
});

fetchPageButton.addEventListener("click", () => {
  void fetchCurrentPage();
});

autoFetchToggle.addEventListener("change", () => {
  void updateAutoFetchSetting(autoFetchToggle.checked).catch(error => {
    autoFetchToggle.checked = settings.autoFetchEnabled;
    setPageStatus(error instanceof Error ? error.message : "Could not update settings.", "error");
  });
});

autoAnalyzeToggle.addEventListener("change", async () => {
  if (!settings.autoFetchEnabled) {
    autoAnalyzeToggle.checked = false;
    return;
  }
  try {
    const response = await chrome.runtime.sendMessage({
      type: "DBOT_SET_SETTINGS",
      autoFetchEnabled: true,
      autoAnalyzeEnabled: autoAnalyzeToggle.checked
    }) as Promise<SettingsResponse>;
    if (!response?.ok || !response.settings) throw new Error(response?.error || "Could not save auto-analysis setting.");
    settings = response.settings;
    autoAnalyzeToggle.checked = settings.autoAnalyzeEnabled;
    setPageStatus(
      settings.autoAnalyzeEnabled
        ? "Auto-analyse enabled. A detected job and your saved profile are sent to the AI provider without another confirmation."
        : "Auto-analyse disabled. DBot will fetch job details but wait for you to click Analyze.",
      "info"
    );
  } catch (error) {
    autoAnalyzeToggle.checked = settings.autoAnalyzeEnabled;
    setPageStatus(error instanceof Error ? error.message : "Could not update auto-analysis setting.", "error");
  }
});

chrome.runtime.onMessage.addListener((message: { type: string; tabId?: number; message?: string; job?: ExtractedJobDetails; source?: "auto"; autoAnalyze?: boolean }) => {
  if (message.type === "DBOT_SETTINGS_CHANGED") {
    void requestSettings();
    return;
  }

  if (message.type === "DBOT_PAGE_SCAN_FAILED") {
    void chrome.tabs.query({ active: true, lastFocusedWindow: true }).then(([tab]) => {
      if (tab?.id === message.tabId && message.message) setPageStatus(message.message, "error");
    });
    return;
  }

  if (message.type === "DBOT_JOB_DETAILS_DETECTED") {
    void chrome.tabs.query({ active: true, lastFocusedWindow: true }).then(([tab]) => {
      if (tab?.id !== message.tabId) return;
      if (!message.job) return;
      void applyDetectedJob(message.job, Boolean(message.autoAnalyze), message.source || "auto");
    });
  }
});

void requestSettings();
