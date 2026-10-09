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
  hasCv: boolean;
  cvFileName: string | null;
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
  cvFileName: string | null;
  cvUploadedAtUtc: string | null;
  cvSizeBytes: number | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  analysis: JobAnalysisResult;
}

interface ExperienceEntry {
  role: string;
  period: string;
  evidence: string[];
}

interface LanguageEntry {
  language: string;
  proficiency: string;
}

interface CandidateProfile {
  professionalSummary: string;
  targetRoles: string[];
  professionalSkills: string[];
  projectAndAcademicSkills: string[];
  experience: ExperienceEntry[];
  education: string[];
  languages: LanguageEntry[];
  workAuthorization: string;
  certifications: string[];
  constraints: string[];
}

interface CandidateProfileResponse {
  profile: CandidateProfile;
  updatedAtUtc: string;
}

interface ApiProblem {
  title?: string;
  detail?: string;
}

const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || "http://127.0.0.1:5080").replace(/\/$/, "");
const statuses = ["Saved", "Applied", "Interview", "Rejected", "Offer"];
const maxCvBytes = 10 * 1024 * 1024;
let savedJobs: SavedJobListItem[] = [];
let selectedJobId: string | null = null;
let currentProfile: CandidateProfile | null = null;

function element<T extends HTMLElement>(selector: string): T {
  const found = document.querySelector<T>(selector);
  if (!found) throw new Error(`Missing dashboard element: ${selector}`);
  return found;
}

const dashboardPage = element<HTMLElement>("#dashboard-page");
const profilePage = element<HTMLElement>("#my-profile-page");
const navLinks = Array.from(document.querySelectorAll<HTMLAnchorElement>(".nav-item"));
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
const cvUploadForm = element<HTMLFormElement>("#cv-upload-form");
const cvFileInput = element<HTMLInputElement>("#cv-file");
const uploadCvButton = element<HTMLButtonElement>("#upload-cv-button");
const downloadCvButton = element<HTMLButtonElement>("#download-cv-button");
const deleteCvButton = element<HTMLButtonElement>("#delete-cv-button");
const cvCurrentInfo = element<HTMLElement>("#cv-current-info");
const cvMessage = element<HTMLElement>("#cv-message");

const profileLoading = element<HTMLElement>("#profile-loading");
const profileStoryView = element<HTMLElement>("#profile-story-view");
const profileEditForm = element<HTMLFormElement>("#profile-edit-form");
const profileMessage = element<HTMLElement>("#profile-message");
const profileEditMessage = element<HTMLElement>("#profile-edit-message");
const editProfileButton = element<HTMLButtonElement>("#edit-profile-button");
const saveProfileButton = element<HTMLButtonElement>("#save-profile-button");
const experienceEditor = element<HTMLElement>("#experience-editor");
const profileSummaryInput = element<HTMLTextAreaElement>("#profile-summary-input");
const profileTargetRolesInput = element<HTMLTextAreaElement>("#profile-target-roles-input");
const profileSkillsInput = element<HTMLTextAreaElement>("#profile-skills-input");
const profileProjectSkillsInput = element<HTMLTextAreaElement>("#profile-project-skills-input");
const profileEducationInput = element<HTMLTextAreaElement>("#profile-education-input");
const profileCertificationsInput = element<HTMLTextAreaElement>("#profile-certifications-input");
const profileLanguagesInput = element<HTMLTextAreaElement>("#profile-languages-input");
const profileWorkAuthInput = element<HTMLTextAreaElement>("#profile-work-auth-input");
const profileConstraintsInput = element<HTMLTextAreaElement>("#profile-constraints-input");

function setMessage(target: HTMLElement, message: string, kind: "error" | "success" | "info"): void {
  target.textContent = message;
  target.className = `form-message ${kind}`;
  target.hidden = !message;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  const isFormData = typeof FormData !== "undefined" && init?.body instanceof FormData;

  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: {
        ...(isFormData ? {} : { "Content-Type": "application/json" }),
        ...(init?.headers || {})
      }
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

function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null || !Number.isFinite(bytes)) return "size unknown";
  return bytes < 1024 * 1024
    ? `${Math.max(1, Math.round(bytes / 1024))} KB`
    : `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
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
    if (job.hasCv) {
      bottom.append(createTextElement("span", "cv-attached-tag", "CV attached"));
    }

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
    cvFileInput.value = "";
    const hasCv = Boolean(job.cvFileName);
    downloadCvButton.hidden = !hasCv;
    deleteCvButton.hidden = !hasCv;
    cvCurrentInfo.textContent = hasCv
      ? `${job.cvFileName} · ${formatBytes(job.cvSizeBytes)} · attached ${job.cvUploadedAtUtc ? formatDate(job.cvUploadedAtUtc) : ""}`
      : "No CV attached to this job.";
    cvCurrentInfo.classList.toggle("has-cv", hasCv);
    setMessage(cvMessage, "", "info");
    detailsPanel.hidden = false;
    setMessage(saveMessage, "", "info");
    if (scroll) detailsPanel.scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (error) {
    setMessage(saveMessage, error instanceof Error ? error.message : "Could not load this saved job.", "error");
  }
}

function parseLines(value: string): string[] {
  return value.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
}

function formatLines(values: string[] | null | undefined): string {
  return (values ?? []).join("\n");
}

function parseLanguages(value: string): LanguageEntry[] {
  return parseLines(value).map(line => {
    const match = line.match(/^(.+?)\s+[—–-]\s+(.+)$/);
    if (match) return { language: match[1].trim(), proficiency: match[2].trim() };
    const colon = line.match(/^(.+?):\s*(.+)$/);
    if (colon) return { language: colon[1].trim(), proficiency: colon[2].trim() };
    return { language: line, proficiency: "" };
  });
}

function createProfileChip(text: string, isEmpty = false): HTMLElement {
  return createTextElement("span", isEmpty ? "skill-chip empty-chip" : "skill-chip", text);
}

function renderChips(target: HTMLElement, values: string[], emptyText: string): void {
  target.replaceChildren();
  if (values.length === 0) {
    target.append(createProfileChip(emptyText, true));
    return;
  }
  values.forEach(value => target.append(createProfileChip(value)));
}

function renderProfileStory(profile: CandidateProfile, updatedAtUtc: string): void {
  element<HTMLElement>("#profile-stat-roles").textContent = String(profile.experience.length);
  element<HTMLElement>("#profile-stat-skills").textContent = String(profile.professionalSkills.length);
  element<HTMLElement>("#profile-stat-education").textContent = String(profile.education.length);
  element<HTMLElement>("#profile-stat-languages").textContent = String(profile.languages.length);

  element<HTMLElement>("#profile-summary-view").textContent =
    profile.professionalSummary || "Your career summary is empty. Choose Edit profile to tell your story.";
  renderChips(element<HTMLElement>("#profile-roles-view"), profile.targetRoles, "Add the roles you are aiming for.");
  renderChips(element<HTMLElement>("#profile-skills-view"), profile.professionalSkills, "Add your professional skills.");
  renderChips(element<HTMLElement>("#profile-project-skills-view"), profile.projectAndAcademicSkills, "Add project and academic skills.");

  const timeline = element<HTMLElement>("#career-timeline-view");
  timeline.replaceChildren();
  if (profile.experience.length === 0) {
    timeline.append(createTextElement("div", "career-story-empty", "Your career timeline is empty. Add a role to start building your story."));
  } else {
    profile.experience.forEach((entry, index) => {
      const row = document.createElement("article");
      row.className = "career-story-item";
      const rail = document.createElement("div");
      rail.className = "career-timeline-rail";
      rail.append(createTextElement("span", "career-timeline-dot", ""));
      const body = document.createElement("div");
      body.className = "career-story-content";
      body.append(createTextElement("h3", "", entry.role || `Career entry ${index + 1}`));
      body.append(createTextElement("span", "career-story-period", entry.period || "Dates not specified"));
      if (entry.evidence.length > 0) {
        const list = document.createElement("ul");
        entry.evidence.forEach(evidence => list.append(createTextElement("li", "", evidence)));
        body.append(list);
      } else {
        body.append(createTextElement("p", "muted", "Add achievements or responsibilities to this role."));
      }
      row.append(rail, body);
      timeline.append(row);
    });
  }

  renderStrings(element<HTMLElement>("#profile-education-view"), profile.education, "Education not added yet.");
  renderStrings(
    element<HTMLElement>("#profile-language-view"),
    profile.languages.map(item => item.proficiency ? `${item.language} — ${item.proficiency}` : item.language),
    "Languages not added yet."
  );
  renderStrings(element<HTMLElement>("#profile-certifications-view"), profile.certifications, "No certifications added yet.");
  renderStrings(element<HTMLElement>("#profile-constraints-view"), profile.constraints, "No extra matching notes.");
  element<HTMLElement>("#profile-work-auth-view").textContent =
    profile.workAuthorization || "Not specified. Keep this blank until you have verified the details.";
  element<HTMLElement>("#profile-updated-at").textContent =
    updatedAtUtc ? `Last saved ${formatDate(updatedAtUtc)}` : "Not saved yet.";
}

async function loadProfile(): Promise<void> {
  profileLoading.hidden = false;
  profileStoryView.hidden = true;
  setMessage(profileMessage, "", "info");
  try {
    const response = await api<CandidateProfileResponse>("/api/profile");
    currentProfile = response.profile;
    renderProfileStory(response.profile, response.updatedAtUtc);
    profileStoryView.hidden = false;
    profileLoading.hidden = true;
  } catch (error) {
    profileLoading.hidden = true;
    setMessage(profileMessage, error instanceof Error ? error.message : "Could not load your profile.", "error");
  }
}

function createExperienceEditorCard(entry: ExperienceEntry, index: number): HTMLElement {
  const card = document.createElement("article");
  card.className = "experience-edit-card";
  card.dataset.experienceIndex = String(index);

  const heading = document.createElement("div");
  heading.className = "experience-card-heading";
  heading.append(createTextElement("strong", "", `Role ${index + 1}`));
  const removeButton = createTextElement("button", "", "Remove role") as HTMLButtonElement;
  removeButton.type = "button";
  removeButton.dataset.removeExperience = String(index);
  heading.append(removeButton);
  card.append(heading);

  const grid = document.createElement("div");
  grid.className = "profile-edit-grid";
  const roleField = document.createElement("div");
  roleField.className = "field";
  roleField.append(createTextElement("label", "", "Role title"));
  const role = document.createElement("input");
  role.type = "text";
  role.maxLength = 160;
  role.placeholder = "e.g. Software Engineer";
  role.value = entry.role;
  role.dataset.experienceField = "role";
  roleField.append(role);
  const periodField = document.createElement("div");
  periodField.className = "field";
  periodField.append(createTextElement("label", "", "Period"));
  const period = document.createElement("input");
  period.type = "text";
  period.maxLength = 120;
  period.placeholder = "e.g. Aug 2023 – Feb 2026";
  period.value = entry.period;
  period.dataset.experienceField = "period";
  periodField.append(period);
  grid.append(roleField, periodField);
  card.append(grid);

  const achievements = document.createElement("div");
  achievements.className = "field";
  achievements.append(createTextElement("label", "", "Achievements and responsibilities · one per line"));
  const evidence = document.createElement("textarea");
  evidence.rows = 4;
  evidence.maxLength = 12000;
  evidence.placeholder = "Modernised legacy applications…\nBuilt and maintained REST APIs…";
  evidence.value = formatLines(entry.evidence);
  evidence.dataset.experienceField = "evidence";
  achievements.append(evidence);
  card.append(achievements);
  return card;
}

function readExperienceEditor(): ExperienceEntry[] {
  return Array.from(experienceEditor.querySelectorAll<HTMLElement>(".experience-edit-card"))
    .map(card => ({
      role: card.querySelector<HTMLInputElement>('[data-experience-field="role"]')?.value.trim() ?? "",
      period: card.querySelector<HTMLInputElement>('[data-experience-field="period"]')?.value.trim() ?? "",
      evidence: parseLines(card.querySelector<HTMLTextAreaElement>('[data-experience-field="evidence"]')?.value ?? "")
    }))
    .filter(entry => entry.role || entry.period || entry.evidence.length > 0);
}

function renderExperienceEditor(entries: ExperienceEntry[]): void {
  experienceEditor.replaceChildren();
  if (entries.length === 0) {
    experienceEditor.append(createTextElement("p", "career-story-empty", "No roles added yet. Choose Add role to create the first timeline entry."));
    return;
  }
  entries.forEach((entry, index) => experienceEditor.append(createExperienceEditorCard(entry, index)));
}

function fillProfileEditor(profile: CandidateProfile): void {
  profileSummaryInput.value = profile.professionalSummary;
  profileTargetRolesInput.value = formatLines(profile.targetRoles);
  profileSkillsInput.value = formatLines(profile.professionalSkills);
  profileProjectSkillsInput.value = formatLines(profile.projectAndAcademicSkills);
  profileEducationInput.value = formatLines(profile.education);
  profileCertificationsInput.value = formatLines(profile.certifications);
  profileLanguagesInput.value = profile.languages
    .map(item => item.proficiency ? `${item.language} — ${item.proficiency}` : item.language)
    .join("\n");
  profileWorkAuthInput.value = profile.workAuthorization;
  profileConstraintsInput.value = formatLines(profile.constraints);
  renderExperienceEditor(profile.experience);
}

function buildProfileFromForm(): CandidateProfile {
  return {
    professionalSummary: profileSummaryInput.value.trim(),
    targetRoles: parseLines(profileTargetRolesInput.value),
    professionalSkills: parseLines(profileSkillsInput.value),
    projectAndAcademicSkills: parseLines(profileProjectSkillsInput.value),
    experience: readExperienceEditor(),
    education: parseLines(profileEducationInput.value),
    languages: parseLanguages(profileLanguagesInput.value),
    workAuthorization: profileWorkAuthInput.value.trim(),
    certifications: parseLines(profileCertificationsInput.value),
    constraints: parseLines(profileConstraintsInput.value)
  };
}

function setProfileEditMode(editing: boolean): void {
  profileEditForm.hidden = !editing;
  profileStoryView.hidden = editing || currentProfile === null;
  editProfileButton.hidden = editing;
  if (editing && currentProfile) {
    fillProfileEditor(currentProfile);
    setMessage(profileEditMessage, "", "info");
    profileSummaryInput.focus();
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

cvUploadForm.addEventListener("submit", async (event: SubmitEvent) => {
  event.preventDefault();
  if (!selectedJobId) return;
  const file = cvFileInput.files?.[0];
  if (!file) {
    setMessage(cvMessage, "Choose the CV file you used or plan to use for this job.", "error");
    return;
  }
  if (file.size > maxCvBytes) {
    setMessage(cvMessage, "The maximum CV file size is 10 MB.", "error");
    return;
  }
  const name = file.name.toLocaleLowerCase();
  if (!name.endsWith(".pdf") && !name.endsWith(".docx")) {
    setMessage(cvMessage, "Choose a PDF or DOCX file.", "error");
    return;
  }

  uploadCvButton.disabled = true;
  setMessage(cvMessage, "Uploading CV to this saved job…", "info");
  try {
    const formData = new FormData();
    formData.append("file", file);
    await api(`/api/jobs/${encodeURIComponent(selectedJobId)}/cv`, {
      method: "POST",
      body: formData
    });
    cvFileInput.value = "";
    setMessage(cvMessage, "CV attached to this job.", "success");
    await loadJobs();
  } catch (error) {
    setMessage(cvMessage, error instanceof Error ? error.message : "The CV could not be uploaded.", "error");
  } finally {
    uploadCvButton.disabled = false;
  }
});

downloadCvButton.addEventListener("click", async () => {
  if (!selectedJobId) return;
  downloadCvButton.disabled = true;
  try {
    const response = await fetch(`${API_BASE_URL}/api/jobs/${encodeURIComponent(selectedJobId)}/cv`);
    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as ApiProblem | null;
      throw new Error(problem?.detail || problem?.title || `CV download failed with HTTP ${response.status}.`);
    }
    const fileBlob = await response.blob();
    const objectUrl = URL.createObjectURL(fileBlob);
    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = cvCurrentInfo.textContent?.split(" · ")[0] || "job-cv";
    document.body.append(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
  } catch (error) {
    setMessage(cvMessage, error instanceof Error ? error.message : "The CV could not be downloaded.", "error");
  } finally {
    downloadCvButton.disabled = false;
  }
});

deleteCvButton.addEventListener("click", async () => {
  if (!selectedJobId || !window.confirm("Remove the CV attached to this job?")) return;
  deleteCvButton.disabled = true;
  try {
    await api<void>(`/api/jobs/${encodeURIComponent(selectedJobId)}/cv`, { method: "DELETE" });
    setMessage(cvMessage, "CV removed from this job.", "success");
    await loadJobs();
  } catch (error) {
    setMessage(cvMessage, error instanceof Error ? error.message : "The CV could not be removed.", "error");
  } finally {
    deleteCvButton.disabled = false;
  }
});

element<HTMLButtonElement>("#close-details").addEventListener("click", () => {
  detailsPanel.hidden = true;
  selectedJobId = null;
  renderJobs();
});

editProfileButton.addEventListener("click", () => setProfileEditMode(true));
element<HTMLButtonElement>("#cancel-profile-edit-button").addEventListener("click", () => setProfileEditMode(false));

element<HTMLButtonElement>("#add-experience-button").addEventListener("click", () => {
  const entries = readExperienceEditor();
  entries.push({ role: "", period: "", evidence: [] });
  renderExperienceEditor(entries);
  const lastCard = experienceEditor.lastElementChild;
  lastCard?.querySelector<HTMLInputElement>('[data-experience-field="role"]')?.focus();
});

experienceEditor.addEventListener("click", event => {
  const target = event.target;
  if (!(target instanceof HTMLButtonElement) || target.dataset.removeExperience === undefined) return;
  const index = Number(target.dataset.removeExperience);
  const entries = readExperienceEditor();
  if (!Number.isInteger(index) || index < 0 || index >= entries.length) return;
  entries.splice(index, 1);
  renderExperienceEditor(entries);
});

profileEditForm.addEventListener("submit", async (event: SubmitEvent) => {
  event.preventDefault();
  saveProfileButton.disabled = true;
  setMessage(profileEditMessage, "Saving your career story…", "info");
  try {
    const response = await api<CandidateProfileResponse>("/api/profile", {
      method: "PUT",
      body: JSON.stringify(buildProfileFromForm())
    });
    currentProfile = response.profile;
    renderProfileStory(response.profile, response.updatedAtUtc);
    profileLoading.hidden = true;
    profileStoryView.hidden = false;
    profileEditForm.hidden = true;
    editProfileButton.hidden = false;
    setMessage(profileMessage, "Your profile has been saved. New job analyses will use the updated story.", "success");
    setMessage(profileEditMessage, "", "info");
  } catch (error) {
    setMessage(profileEditMessage, error instanceof Error ? error.message : "The profile could not be saved.", "error");
  } finally {
    saveProfileButton.disabled = false;
  }
});

for (const link of navLinks) {
  link.addEventListener("click", event => {
    event.preventDefault();
    const page = link.dataset.page === "profile" ? "profile" : "dashboard";
    dashboardPage.hidden = page !== "dashboard";
    profilePage.hidden = page !== "profile";
    navLinks.forEach(item => item.classList.toggle("active", item === link));

    if (page === "profile") {
      void loadProfile();
    } else {
      const target = document.querySelector<HTMLElement>(link.getAttribute("href") || "#overview");
      target?.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  });
}

jobSearch.addEventListener("input", renderJobs);
statusFilter.addEventListener("change", renderJobs);
refreshButton.addEventListener("click", () => void loadJobs());

void loadJobs();
