interface MatchedRequirement {
  requirement: string;
  evidence: string;
  evidenceIds: string[];
}

interface RequirementGap {
  requirement: string;
  severity: string;
  status: string;
  explanation: string;
}

interface JobAnalysisResult {
  jobId?: string;
  analyzedAtUtc?: string;
  recommendation: string;
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
}

interface SavedJobListItem {
  id: string;
  jobTitle: string;
  company: string;
  matchScore: number;
  recommendation: string;
  detectedLanguage: string;
  summary: string;
  applicationStatus: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

interface SavedJobDetails {
  id: string;
  jobTitle: string;
  company: string;
  jobDescription: string;
  applicationStatus: string;
  notes: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  analysis: JobAnalysisResult;
}

interface ApiProblem {
  title?: string;
  detail?: string;
}

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || "http://127.0.0.1:5080").replace(/\/$/, "");
const statuses = ["Saved", "Applied", "Interview", "Rejected", "Offer"];
let savedJobs: SavedJobListItem[] = [];
let selectedJobId: string | null = null;

function element<T extends HTMLElement>(selector: string): T {
  const found = document.querySelector<T>(selector);
  if (!found) throw new Error(`Missing dashboard element: ${selector}`);
  return found;
}

const analyzeForm = element<HTMLFormElement>("#analyze-form");
const jobTitleInput = element<HTMLInputElement>("#job-title");
const companyInput = element<HTMLInputElement>("#company");
const descriptionInput = element<HTMLTextAreaElement>("#job-description");
const analyzeButton = element<HTMLButtonElement>("#analyze-button");
const analyzeButtonLabel = element<HTMLElement>("#analyze-button-label");
const formMessage = element<HTMLElement>("#form-message");
const refreshButton = element<HTMLButtonElement>("#refresh-button");
const apiStatus = element<HTMLElement>("#api-status");
const jobsList = element<HTMLElement>("#jobs-list");
const emptyState = element<HTMLElement>("#empty-state");
const jobCount = element<HTMLElement>("#job-count");
const jobSearch = element<HTMLInputElement>("#job-search");
const statusFilter = element<HTMLSelectElement>("#status-filter");
const detailsPanel = element<HTMLElement>("#job-details");
const saveMessage = element<HTMLElement>("#save-message");
const saveJobButton = element<HTMLButtonElement>("#save-job-button");
const applicationForm = element<HTMLFormElement>("#application-form");
const applicationStatusInput = element<HTMLSelectElement>("#application-status");
const notesInput = element<HTMLTextAreaElement>("#job-notes");

function setMessage(target: HTMLElement, message: string, kind: "error" | "success" | "info"): void {
  target.textContent = message;
  target.className = `form-message ${kind}`;
  target.hidden = !message;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: { "Content-Type": "application/json", ...(init?.headers || {}) }
    });
  } catch {
    throw new Error(`Cannot reach the API at ${API_BASE_URL}. Start the API and try again.`);
  }

  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    const problem = (payload ?? {}) as ApiProblem;
    throw new Error(problem.detail || problem.title || `API returned HTTP ${response.status}.`);
  }
  return payload as T;
}

function escapeStatus(status: string): string {
  return statuses.includes(status) ? status : "Saved";
}

function scoreClass(score: number): string {
  return score >= 80 ? "score-high" : score >= 60 ? "score-medium" : "score-low";
}

function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Date unknown";
  return new Intl.DateTimeFormat(undefined, { day: "2-digit", month: "short", year: "numeric" }).format(date);
}

function createTextElement(tag: string, className: string, text: string): HTMLElement {
  const item = document.createElement(tag);
  item.className = className;
  item.textContent = text;
  return item;
}

function renderStats(): void {
  element<HTMLElement>("#stat-total").textContent = String(savedJobs.length);
  element<HTMLElement>("#stat-strong").textContent = String(savedJobs.filter(job => job.matchScore >= 80).length);
  element<HTMLElement>("#stat-applied").textContent = String(savedJobs.filter(job => job.applicationStatus === "Applied").length);
  element<HTMLElement>("#stat-interviews").textContent = String(savedJobs.filter(job => job.applicationStatus === "Interview").length);
}

function renderJobs(): void {
  const query = jobSearch.value.trim().toLocaleLowerCase();
  const status = statusFilter.value;
  const filtered = savedJobs.filter(job => {
    const matchesSearch = `${job.jobTitle} ${job.company} ${job.summary}`.toLocaleLowerCase().includes(query);
    return matchesSearch && (status === "All" || job.applicationStatus === status);
  });

  jobCount.textContent = `${filtered.length} ${filtered.length === 1 ? "job" : "jobs"}`;
  jobsList.replaceChildren();
  emptyState.hidden = savedJobs.length !== 0;
  jobsList.hidden = savedJobs.length === 0;

  if (savedJobs.length > 0 && filtered.length === 0) {
    jobsList.append(createTextElement("p", "empty-filter", "No saved jobs match these filters."));
    return;
  }

  for (const job of filtered) {
    const card = document.createElement("button");
    card.type = "button";
    card.className = `job-card${job.id === selectedJobId ? " selected" : ""}`;
    card.setAttribute("aria-pressed", String(job.id === selectedJobId));

    const top = document.createElement("div");
    top.className = "job-card-top";
    const title = createTextElement("strong", "job-card-title", job.jobTitle || "Untitled role");
    const score = createTextElement("span", `job-score ${scoreClass(job.matchScore)}`, `${job.matchScore} / 100`);
    top.append(title, score);

    const company = createTextElement("span", "job-company", job.company || "Company not specified");
    const bottom = document.createElement("div");
    bottom.className = "job-card-bottom";
    const statusTag = createTextElement("span", `status-tag status-${job.applicationStatus.toLowerCase()}`, job.applicationStatus);
    const recommendation = createTextElement("span", `recommendation-text recommendation-${job.recommendation.toLowerCase()}`, job.recommendation);
    bottom.append(statusTag, recommendation, createTextElement("span", "job-date", formatDate(job.createdAtUtc)));

    card.append(top, company, bottom);
    card.addEventListener("click", () => void openJob(job.id));
    jobsList.append(card);
  }
}

function renderStrings(target: HTMLElement, values: string[], emptyText: string): void {
  target.replaceChildren();
  if (values.length === 0) {
    target.append(createTextElement("li", "empty-detail", emptyText));
    return;
  }
  for (const value of values) {
    target.append(createTextElement("li", "", value));
  }
}

function renderMatches(target: HTMLElement, matches: MatchedRequirement[]): void {
  target.replaceChildren();
  if (matches.length === 0) {
    target.append(createTextElement("li", "empty-detail", "No matched requirements were returned."));
    return;
  }
  for (const match of matches) {
    const item = document.createElement("li");
    item.append(createTextElement("strong", "", match.requirement || "Matched requirement"));
    item.append(createTextElement("p", "", match.evidence || "No supporting profile fact supplied."));
    target.append(item);
  }
}

function renderGaps(target: HTMLElement, gaps: RequirementGap[]): void {
  target.replaceChildren();
  if (gaps.length === 0) {
    target.append(createTextElement("li", "empty-detail", "No explicit gaps were identified."));
    return;
  }
  for (const gap of gaps) {
    const item = document.createElement("li");
    item.append(createTextElement("strong", "", gap.requirement || "Requirement to check"));
    item.append(createTextElement("span", "gap-label", `${gap.severity || "Unknown"} · ${gap.status || "Unverified"}`));
    item.append(createTextElement("p", "", gap.explanation || "Review this requirement manually."));
    target.append(item);
  }
}

async function loadJobs(): Promise<void> {
  refreshButton.disabled = true;
  apiStatus.textContent = "Connecting…";
  try {
    savedJobs = await api<SavedJobListItem[]>("/api/jobs");
    apiStatus.textContent = "Connected";
    element<HTMLElement>(".connection-dot").classList.add("connected");
    renderStats();
    renderJobs();

    if (selectedJobId && savedJobs.some(job => job.id === selectedJobId)) {
      await openJob(selectedJobId, false);
    }
  } catch (error) {
    apiStatus.textContent = "Unavailable";
    element<HTMLElement>(".connection-dot").classList.remove("connected");
    jobsList.hidden = false;
    jobsList.replaceChildren(createTextElement("p", "api-error", error instanceof Error ? error.message : "Could not load jobs."));
    emptyState.hidden = true;
  } finally {
    refreshButton.disabled = false;
  }
}

async function openJob(id: string, scroll = true): Promise<void> {
  selectedJobId = id;
  renderJobs();
  detailsPanel.hidden = false;
  element<HTMLElement>("#detail-title").textContent = "Loading saved job…";
  try {
    const job = await api<SavedJobDetails>(`/api/jobs/${encodeURIComponent(id)}`);
    element<HTMLElement>("#detail-title").textContent = job.jobTitle || "Untitled role";
    element<HTMLElement>("#detail-company").textContent = [job.company || "Company not specified", formatDate(job.createdAtUtc)].join(" · ");
    element<HTMLElement>("#detail-score").textContent = `${job.analysis.matchScore} / 100`;
    element<HTMLElement>("#detail-score").className = scoreClass(job.analysis.matchScore);
    element<HTMLElement>("#detail-score-bar").style.width = `${Math.max(0, Math.min(100, job.analysis.matchScore))}%`;

    const recommendation = job.analysis.recommendation || "Review";
    const pill = element<HTMLElement>("#detail-recommendation");
    pill.textContent = recommendation;
    pill.className = `recommendation-pill recommendation-${recommendation.toLowerCase()}`;

    element<HTMLElement>("#detail-role-summary").textContent = job.analysis.englishSummary || "No role summary was returned.";
    element<HTMLElement>("#detail-assessment").textContent = [job.analysis.summary, job.analysis.rationale].filter(Boolean).join(" ");
    renderMatches(element<HTMLElement>("#detail-matches"), job.analysis.matchedRequirements || []);
    renderGaps(element<HTMLElement>("#detail-gaps"), job.analysis.gaps || []);
    renderStrings(element<HTMLElement>("#detail-questions"), job.analysis.questionsToVerify || [], "No extra questions returned.");
    applicationStatusInput.value = escapeStatus(job.applicationStatus);
    notesInput.value = job.notes || "";
    element<HTMLElement>("#detail-description").textContent = job.jobDescription;
    detailsPanel.hidden = false;
    setMessage(saveMessage, "", "info");
    if (scroll) detailsPanel.scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (error) {
    setMessage(saveMessage, error instanceof Error ? error.message : "Could not load this saved job.", "error");
  }
}

analyzeForm.addEventListener("submit", async (event: SubmitEvent) => {
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
  analyzeButtonLabel.textContent = "Analysing and saving…";
  setMessage(formMessage, "Sending this job for analysis. This can take a few seconds.", "info");

  try {
    const result = await api<JobAnalysisResult>("/api/jobs/analyze", {
      method: "POST",
      body: JSON.stringify({
        jobTitle: jobTitleInput.value.trim(),
        company: companyInput.value.trim(),
        jobDescription: description
      })
    });
    setMessage(formMessage, `Analysis saved · ${result.matchScore}/100 · ${result.recommendation}`, "success");
    await loadJobs();
    if (result.jobId) await openJob(result.jobId);
    else if (savedJobs.length > 0) await openJob(savedJobs[0].id);
  } catch (error) {
    setMessage(formMessage, error instanceof Error ? error.message : "Analysis could not be completed.", "error");
  } finally {
    analyzeButton.disabled = false;
    analyzeButton.removeAttribute("aria-busy");
    analyzeButtonLabel.textContent = "Analyse & save job";
  }
});

applicationForm.addEventListener("submit", async (event: SubmitEvent) => {
  event.preventDefault();
  if (!selectedJobId) return;
  saveJobButton.disabled = true;
  setMessage(saveMessage, "Saving changes…", "info");
  try {
    await api(`/api/jobs/${encodeURIComponent(selectedJobId)}`, {
      method: "PUT",
      body: JSON.stringify({
        applicationStatus: applicationStatusInput.value,
        notes: notesInput.value.trim()
      })
    });
    setMessage(saveMessage, "Application details saved.", "success");
    await loadJobs();
  } catch (error) {
    setMessage(saveMessage, error instanceof Error ? error.message : "Could not save changes.", "error");
  } finally {
    saveJobButton.disabled = false;
  }
});

jobSearch.addEventListener("input", renderJobs);
statusFilter.addEventListener("change", renderJobs);
refreshButton.addEventListener("click", () => void loadJobs());
element<HTMLButtonElement>("#close-details").addEventListener("click", () => {
  detailsPanel.hidden = true;
  selectedJobId = null;
  renderJobs();
});

void loadJobs();
