import { initAskTab, refreshAskJobContext } from "./ask-tab";
import { initCvBuilder, resetCvBuilder } from "./cv-builder";
import { loadProfileTab } from "./profile-tab";
import { initAuth } from "./auth";
import { apiRequest, element } from "./shared";

interface JobAnalysisResult {
  jobId?: string | null;
  recommendation: "Apply" | "Review" | "Skip" | string;
  matchScore: number;
  englishSummary?: string;
  keyRequirements?: string[];
  candidateExpectations?: string[];
  matchedRequirements?: Array<{ requirement?: string }>;
  gaps?: Array<{ requirement?: string; severity?: string; status?: string }>;
}

interface ExtractedJobDetails {
  found: boolean;
  jobId?: string;
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

const ALL_HTTP_ORIGINS = ["http://*/*", "https://*/*"];
const autoAnalyzedUrls = new Set<string>();
const autoAnalysisCacheReady: Promise<void> = chrome.storage.session
  .get("dbotAutoAnalyzedUrls")
  .then(values => {
    const cached = values["dbotAutoAnalyzedUrls"];
    if (Array.isArray(cached)) {
      cached.filter((value): value is string => typeof value === "string").forEach(value => autoAnalyzedUrls.add(value));
    }
  })
  .catch(() => {
    // Session storage is only a deduplication convenience; analysis remains available without it.
  });
let settings: DbotSettings = { autoFetchEnabled: false, autoAnalyzeEnabled: false };

// The saved-job ID for the most recent analysis, and the form contents it was made from.
let savedJobId: string | null = null;
let savedJobSignature = "";

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
const matchScore = element<HTMLElement>("#match-score");
const englishSummary = element<HTMLElement>("#english-summary");
const keyRequirementsList = element<HTMLUListElement>("#key-requirements-list");
const candidateExpectationsList = element<HTMLUListElement>("#candidate-expectations-list");
const resultCopy = element<HTMLElement>("#result-copy");

const togglePageToolsButton = element<HTMLButtonElement>("#toggle-page-tools-button");
const pageToolsContent = element<HTMLDivElement>("#page-tools-content");
const fetchPageButton = element<HTMLButtonElement>("#fetch-current-page-button");
const pageFetchStatus = element<HTMLElement>("#page-fetch-status");
const autoFetchToggle = element<HTMLInputElement>("#auto-fetch-toggle");
const autoAnalyzeToggle = element<HTMLInputElement>("#auto-analyze-toggle");

function setPageStatus(message: string, kind: "info" | "success" | "error" = "info"): void {
  pageFetchStatus.textContent = message;
  pageFetchStatus.className = `page-fetch-status ${kind}`;
}

function normalizeRequirement(value: unknown): string {
  return String(value || "").replace(/\s+/g, " ").trim();
}

function inferExplicitRequirements(description: string): string[] {
  const text = description.normalize("NFKC");
  const inferred: string[] = [];

  // The model's ranking can overlook language criteria. Promote them directly
  // from the vacancy text when both languages are named in a requirements context.
  const mentionsGerman = /\b(?:deutsch(?:kenntnisse|kenntnis)?|german)\b/i.test(text);
  const mentionsEnglish = /\b(?:englisch(?:kenntnisse|kenntnis)?|english)\b/i.test(text);
  const hasLanguageCue = /\b(?:kenntnisse|sprachkenntnisse|languages?|proficiency|fluency|fluent|spoken|written|speaking|communication|wort und schrift)\b/i.test(text);
  if (mentionsGerman && mentionsEnglish && hasLanguageCue) {
    const veryGood = /sehr gute.{0,55}(?:deutsch|german)|(?:deutsch|german).{0,70}(?:englisch|english).{0,35}(?:wort und schrift|written|spoken)/i.test(text);
    inferred.push(veryGood
      ? "Very good German and English, spoken and written"
      : "German and English language skills");
  } else {
    if (mentionsGerman && /\b(?:kenntnisse|sprachkenntnisse|language|spoken|written|fließend|fliessend|verhandlungssicher)\b/i.test(text)) {
      inferred.push("German language skills");
    }
    if (mentionsEnglish && /\b(?:kenntnisse|sprachkenntnisse|language|spoken|written|fluent|proficiency)\b/i.test(text)) {
      inferred.push("English language skills");
    }
  }

  if (/mindestens.{0,25}(?:1|ein)\s+jahr.{0,40}(?:immatrikuliert|eingeschrieben|enrolled)|(?:enrolled|enrolment|enrollment).{0,70}(?:at least|minimum).{0,25}(?:1|one)\s+year/i.test(text)) {
    inferred.push("Remain enrolled at university for at least one more year");
  }

  const hoursRange = /\b14\s*h\s*[-–]\s*20\s*h\b|\b14\s*(?:-|–|bis|to)\s*20\s*(?:hours|stunden)\b/i.test(text);
  const twoOrThreeDays = /\b2\s*[-–]\s*3\s*(?:days|tage|days per week|tagen)\b/i.test(text)
    || /\ban\s+2\s*[-–]\s*3\s+days\b/i.test(text);
  if (hoursRange) {
    inferred.push(twoOrThreeDays
      ? "Available 14–20 hours per week across 2–3 weekdays"
      : "Available 14–20 hours per week");
  }

  if (/\b(?:vor ort|on.?site|in the office|im büro|im buero|design-office)\b/i.test(text)) {
    const officeHours = /\b10[:.]00\b/i.test(text) && /\b17[:.]30\b/i.test(text);
    if (/\b(?:köln|cologne)\b/i.test(text)) {
      const schedule = twoOrThreeDays ? " 2–3 days/week" : "";
      const hours = officeHours ? " (10:00–17:30)" : "";
      inferred.push("Available on-site in Cologne" + schedule + hours);
    } else {
      inferred.push("Available for on-site work");
    }
  }

  if (/\bsorgfältig\b|\bstrukturiert\b|\bcareful(?:ly)?\b|\bstructured\b/i.test(text)) {
    inferred.push("Careful, structured working style");
  }
  if (/\bteamarbeit\b|\bteamwork\b|\bkommunikationsfähigkeiten\b|\bkommunikationsfähigkeit\b|\bcommunication skills\b/i.test(text)) {
    inferred.push("Teamwork and strong communication");
  }
  if (/\b(?:mobile trends|mobile apps|new apps|neue apps|technische geräte|technischen geräten|technical devices|technical equipment)\b/i.test(text)) {
    inferred.push("Interest in apps/mobile technology and confidence with devices");
  }

  return inferred;
}

function requirementCategory(value: string): string | undefined {
  const text = value.toLocaleLowerCase();
  if (/\b(?:german|deutsch|english|englisch|language skills?|sprachkenntnisse)\b/i.test(text)) return "language";
  if (/\b(?:enrolled|enrolment|enrollment|university|immatrikuliert|eingeschrieben|student status)\b/i.test(text)) return "enrollment";
  if (/\b(?:14\s*[-–]\s*20|hours per week|weekly hours|days per week|weekdays|stunden pro woche)\b/i.test(text)) return "availability";
  if (/\b(?:on.?site|cologne|köln|vor ort|design.office|office attendance)\b/i.test(text)) return "location";
  if (/\b(?:careful|sorgfältig|structured|strukturiert)\b/i.test(text)) return "work-style";
  if (/\b(?:teamwork|teamarbeit|communication|kommunikation)\b/i.test(text)) return "teamwork";
  if (/\b(?:mobile|app|device|geräte|geräten|technolog)\b/i.test(text)) return "technology";
  return undefined;
}

function renderKeyRequirements(result: JobAnalysisResult): void {
  keyRequirementsList.replaceChildren();

  const inferred = inferExplicitRequirements(descriptionInput.value);
  const modelRequirements = Array.isArray(result.keyRequirements) ? result.keyRequirements : [];
  const gaps = Array.isArray(result.gaps) ? result.gaps : [];
  const matched = Array.isArray(result.matchedRequirements) ? result.matchedRequirements : [];
  const priorityGaps = gaps.filter(item => item.severity?.toLowerCase() === "must-have");
  const candidates = [
    ...inferred,
    ...modelRequirements,
    ...priorityGaps.map(item => item.requirement),
    ...matched.map(item => item.requirement),
    ...gaps.filter(item => item.severity?.toLowerCase() !== "must-have").map(item => item.requirement)
  ];

  const seen = new Set<string>();
  const seenCategories = new Set<string>();
  const requirements: string[] = [];
  for (const candidate of candidates) {
    const requirement = normalizeRequirement(candidate);
    const key = requirement.toLocaleLowerCase();
    const category = requirementCategory(requirement);
    if (!requirement || seen.has(key) || (category && seenCategories.has(category))) continue;
    seen.add(key);
    if (category) seenCategories.add(category);
    requirements.push(requirement);
    if (requirements.length >= 6) break;
  }

  if (requirements.length === 0) {
    const empty = document.createElement("li");
    empty.className = "requirement-chip empty-requirement";
    empty.textContent = "No specific requirements detected";
    keyRequirementsList.append(empty);
    return;
  }

  for (const requirement of requirements) {
    const chip = document.createElement("li");
    chip.className = "requirement-chip";
    chip.textContent = requirement;
    keyRequirementsList.append(chip);
  }
}

function renderCandidateExpectations(result: JobAnalysisResult): void {
  candidateExpectationsList.replaceChildren();
  const fromModel = Array.isArray(result.candidateExpectations)
    ? result.candidateExpectations.map(normalizeRequirement).filter(Boolean)
    : [];
  const inferred = inferExplicitRequirements(descriptionInput.value);
  // Keep explicit eligibility requirements visible even if the model's list is short.
  const candidates = [...inferred.slice(0, 4), ...fromModel, ...inferred.slice(4)];
  const expectations: string[] = [];
  const seen = new Set<string>();
  const seenCategories = new Set<string>();
  for (const candidate of candidates) {
    const value = normalizeRequirement(candidate);
    const key = value.toLocaleLowerCase();
    const category = requirementCategory(value);
    if (!value || seen.has(key) || (category && seenCategories.has(category))) continue;
    seen.add(key);
    if (category) seenCategories.add(category);
    expectations.push(value);
    if (expectations.length >= 5) break;
  }

  if (expectations.length === 0) {
    const item = document.createElement("li");
    item.textContent = "Review the required skills, eligibility and availability listed in the vacancy.";
    candidateExpectationsList.append(item);
    return;
  }

  for (const expectation of [...new Set(expectations)].slice(0, 5)) {
    const item = document.createElement("li");
    item.textContent = expectation;
    candidateExpectationsList.append(item);
  }
}

function setPageToolsCollapsed(collapsed: boolean): void {
  pageToolsContent.hidden = collapsed;
  togglePageToolsButton.textContent = collapsed ? "Tools +" : "Tools −";
  togglePageToolsButton.setAttribute("aria-expanded", String(!collapsed));
  togglePageToolsButton.setAttribute(
    "aria-label",
    collapsed ? "Show page detection settings" : "Hide page detection settings"
  );
}

function renderAnalysis(result: JobAnalysisResult): void {
  const strongFit = result.recommendation.trim().toLowerCase() === "apply";
  resultEyebrow.textContent = "AI JOB MATCH";
  resultTitle.textContent = "Overall assessment";
  resultIcon.textContent = strongFit ? "✓" : "×";
  matchScore.textContent = `${Math.max(0, Math.min(100, Math.round(result.matchScore || 0)))} / 100`;
  englishSummary.textContent = result.englishSummary?.trim()
    || "An English role summary was not returned. Review the job description above.";
  renderKeyRequirements(result);
  renderCandidateExpectations(result);
  resultCopy.textContent = strongFit ? "Strong fit" : "Not a strong fit";
  resultCopy.className = `result-copy fit-verdict ${strongFit ? "strong-fit" : "not-strong-fit"}`;
  resultPanel.classList.remove("error-state");
  resultPanel.hidden = false;
}

function renderError(message: string): void {
  resultPanel.classList.add("error-state");
  resultEyebrow.textContent = "ANALYSIS NOT COMPLETED";
  resultTitle.textContent = "Analysis failed";
  resultIcon.textContent = "!";
  matchScore.textContent = "--";
  englishSummary.textContent = "The role summary is unavailable because the analysis did not complete.";
  keyRequirementsList.replaceChildren();
  candidateExpectationsList.replaceChildren();
  resultCopy.textContent = message;
  resultCopy.className = "result-copy fit-verdict error-copy";
  resultPanel.hidden = false;
}

function currentJobSignature(): string {
  return [jobTitleInput.value.trim(), companyInput.value.trim(), descriptionInput.value.trim()].join("\u0000");
}

/** Returns the saved job ID for the form contents, analysing (and saving) the job first if needed. */
async function ensureSavedJob(): Promise<string | null> {
  if (savedJobId && savedJobSignature === currentJobSignature()) return savedJobId;
  return analyzeCurrentJob();
}

async function analyzeCurrentJob(): Promise<string | null> {
  const description = descriptionInput.value.trim();
  if (description.length < 40) {
    descriptionInput.setCustomValidity("Please paste at least 40 characters from the job description.");
    descriptionInput.reportValidity();
    setPageStatus("The detected description is too short. Review it or paste the complete job description.", "error");
    return null;
  }
  descriptionInput.setCustomValidity("");

  if (analyzeButton.disabled) return null;
  analyzeButton.disabled = true;
  analyzeButton.setAttribute("aria-busy", "true");
  buttonLabel.textContent = "Analysing job…";
  resultPanel.hidden = true;
  const signature = currentJobSignature();

  try {
    const result = await apiRequest<JobAnalysisResult>("/api/jobs/analyze", {
      method: "POST",
      body: JSON.stringify({
        jobTitle: jobTitleInput.value.trim(),
        company: companyInput.value.trim(),
        jobDescription: description
      })
    });
    renderAnalysis(result);
    setPageStatus("Analysis complete. Review the recommendation and evidence below.", "success");
    if (result.jobId && result.jobId !== savedJobId) resetCvBuilder();
    savedJobId = result.jobId ?? null;
    savedJobSignature = signature;
    return savedJobId;
  } catch (error) {
    renderError(error instanceof Error ? error.message : "An unexpected error occurred.");
    return null;
  } finally {
    analyzeButton.disabled = false;
    analyzeButton.removeAttribute("aria-busy");
    buttonLabel.textContent = "Analyze job fit";
  }
}

async function getActiveTabIdInCurrentWindow(): Promise<number> {
  // Resolve the tab from the side panel's own browser window rather than
  // asking the background service worker to guess the last-focused window.
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (tab?.id === undefined) {
    throw new Error("DBot could not identify the active tab in this browser window. Keep the job page and DBot side panel in the same Chrome window, then try again.");
  }
  return tab.id;
}

async function requestSettings(): Promise<void> {
  try {
    const response = (await chrome.runtime.sendMessage({ type: "DBOT_GET_SETTINGS" })) as SettingsResponse;
    if (!response?.ok || !response.settings) {
      throw new Error(response?.error || "Could not load settings.");
    }
    settings = response.settings;
    autoFetchToggle.checked = settings.autoFetchEnabled;
    autoAnalyzeToggle.checked = settings.autoAnalyzeEnabled;
    autoAnalyzeToggle.disabled = !settings.autoFetchEnabled;
    if (settings.autoFetchEnabled && response.allSitesPermission) {
      const tabId = await getActiveTabIdInCurrentWindow();
      const result = (await chrome.runtime.sendMessage({
        type: "DBOT_FETCH_CURRENT_PAGE",
        tabId
      })) as FetchResponse;
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
    const tabId = await getActiveTabIdInCurrentWindow();
    const response = (await chrome.runtime.sendMessage({
      type: "DBOT_FETCH_CURRENT_PAGE",
      tabId
    })) as FetchResponse;
    if (!response?.ok || !response.job) {
      throw new Error(response?.error || "Could not fetch the current page.");
    }
    await applyDetectedJob(response.job, settings.autoAnalyzeEnabled, "manual");
  } catch (error) {
    setPageStatus(error instanceof Error ? error.message : "Could not fetch details from this page.", "error");
  } finally {
    fetchPageButton.disabled = false;
    fetchPageButton.textContent = "↧ Fetch job details from page";
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

  const isDifferentJob = descriptionInput.value.trim() !== job.description.trim();
  jobTitleInput.value = job.title || job.pageTitle || jobTitleInput.value;
  companyInput.value = job.company || companyInput.value;
  descriptionInput.value = job.description;
  descriptionInput.setCustomValidity("");
  if (isDifferentJob) resetCvBuilder();

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
  await autoAnalysisCacheReady;
  if (shouldAutoAnalyze && !autoAnalyzedUrls.has(analysisKey)) {
    if (job.description.trim().length < 40) {
      setPageStatus("Job details were found, but the description is too short to analyse. Review it or paste more text.", "error");
      return;
    }
    // Remember in the current browser session to avoid duplicate Gemini calls if
    // the panel receives the same page event twice or is reopened.
    autoAnalyzedUrls.add(analysisKey);
    await chrome.storage.session.set({
      dbotAutoAnalyzedUrls: Array.from(autoAnalyzedUrls).slice(-100)
    }).catch(() => undefined);
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
  const response = (await chrome.runtime.sendMessage({
    type: "DBOT_SET_SETTINGS",
    autoFetchEnabled: enabled,
    autoAnalyzeEnabled: requestedAnalyze,
    ...(enabled ? { tabId: await getActiveTabIdInCurrentWindow() } : {})
  })) as SettingsResponse;
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
        : "Auto-fetch is enabled. DBot scans job pages and refreshes details when the selected LinkedIn job changes; analysis waits until you click Analyze.",
      "success"
    );
    await fetchCurrentPage();
  } else {
    settings.autoAnalyzeEnabled = false;
    autoAnalyzeToggle.checked = false;
    autoAnalyzeToggle.disabled = true;
    setPageStatus("Automatic fetching is off. Use Fetch job details from page when you need it.", "info");
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
    const response = (await chrome.runtime.sendMessage({
      type: "DBOT_SET_SETTINGS",
      autoFetchEnabled: true,
      autoAnalyzeEnabled: autoAnalyzeToggle.checked
    })) as SettingsResponse;
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
    void chrome.tabs.query({ active: true, currentWindow: true }).then(([tab]) => {
      if (tab?.id === message.tabId && message.message) setPageStatus(message.message, "error");
    });
    return;
  }

  if (message.type === "DBOT_JOB_DETAILS_DETECTED") {
    void chrome.tabs.query({ active: true, currentWindow: true }).then(([tab]) => {
      if (tab?.id !== message.tabId) return;
      if (!message.job) return;
      void applyDetectedJob(message.job, Boolean(message.autoAnalyze), message.source || "auto");
    });
  }
});

void chrome.storage.local.get("dbotPageToolsHidden")
  .then(values => setPageToolsCollapsed(values["dbotPageToolsHidden"] !== false))
  .catch(() => setPageToolsCollapsed(true));

togglePageToolsButton.addEventListener("click", () => {
  const collapsed = !pageToolsContent.hidden;
  setPageToolsCollapsed(collapsed);
  void chrome.storage.local.set({ dbotPageToolsHidden: collapsed }).catch(() => {
    setPageStatus("Could not save the current-page visibility preference.", "error");
  });
});

const tabButtons = Array.from(document.querySelectorAll<HTMLButtonElement>(".tab-button"));

function currentAskContext() {
  const upToDate = savedJobSignature === currentJobSignature();
  return {
    jobId: upToDate ? savedJobId : null,
    jobTitle: jobTitleInput.value.trim(),
    company: companyInput.value.trim(),
    jobDescription: descriptionInput.value.trim()
  };
}

function showTab(tab: string): void {
  for (const button of tabButtons) {
    const active = button.dataset.tab === tab;
    button.classList.toggle("active", active);
    button.setAttribute("aria-selected", String(active));
    const panel = document.getElementById(button.getAttribute("aria-controls") || "");
    if (panel) panel.hidden = !active;
  }
  if (tab === "profile") void loadProfileTab();
  if (tab === "ask") refreshAskJobContext(currentAskContext());
}

for (const button of tabButtons) {
  button.addEventListener("click", () => showTab(button.dataset.tab || "job"));
}

initCvBuilder({ ensureSavedJob });
initAskTab(currentAskContext);

void initAuth();
void requestSettings();
