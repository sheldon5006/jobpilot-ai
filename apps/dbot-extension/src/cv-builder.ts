import { API_BASE_URL, apiRequest, element, parseLines, setStatus } from "./shared";

interface CvSkillGroup {
  category: string;
  skills: string[];
}

interface CvExperience {
  sourceIndex: number;
  role: string;
  period: string;
  bullets: string[];
}

interface CvDocument {
  headline: string;
  summary: string;
  skillGroups: CvSkillGroup[];
  experience: CvExperience[];
  education: string[];
  languages: string[];
  certifications: string[];
}

interface GeneratedCvResponse {
  jobId: string;
  jobTitle: string;
  company: string;
  applicationStatus: string;
  cv: CvDocument;
  updatedAtUtc: string;
}

export interface CvBuilderDependencies {
  /** Returns the saved job ID for the job in the form, analysing it first if necessary. */
  ensureSavedJob(): Promise<string | null>;
}

const INSTRUCTIONS_KEY = "dbotCvInstructions";

const instructionsInput = element<HTMLTextAreaElement>("#cv-instructions");
const jobInstructionsInput = element<HTMLTextAreaElement>("#cv-job-instructions");
const resetInstructionsButton = element<HTMLButtonElement>("#reset-cv-instructions-button");
const instructionsSaved = element<HTMLElement>("#cv-instructions-saved");
const makeCvButton = element<HTMLButtonElement>("#make-cv-button");
const makeCvLabel = element<HTMLElement>("#make-cv-label");
const cvStatus = element<HTMLElement>("#cv-status");
const cvEditor = element<HTMLElement>("#cv-editor");
const headlineInput = element<HTMLInputElement>("#cv-headline");
const summaryInput = element<HTMLTextAreaElement>("#cv-summary");
const skillsInput = element<HTMLTextAreaElement>("#cv-skills");
const experienceEditor = element<HTMLElement>("#cv-experience-editor");
const educationInput = element<HTMLTextAreaElement>("#cv-education");
const certificationsInput = element<HTMLTextAreaElement>("#cv-certifications");
const languagesInput = element<HTMLTextAreaElement>("#cv-languages");
const saveCvButton = element<HTMLButtonElement>("#save-cv-button");
const downloadCvButton = element<HTMLButtonElement>("#download-cv-button");

let defaultInstructions = "";
let currentCvJobId: string | null = null;
let saveTimer: number | undefined;

function fillEditor(cv: CvDocument): void {
  headlineInput.value = cv.headline;
  summaryInput.value = cv.summary;
  skillsInput.value = cv.skillGroups
    .map(group => group.category ? `${group.category}: ${group.skills.join(", ")}` : group.skills.join(", "))
    .join("\n");
  educationInput.value = cv.education.join("\n");
  certificationsInput.value = cv.certifications.join("\n");
  languagesInput.value = cv.languages.join("\n");

  experienceEditor.replaceChildren();
  for (const entry of cv.experience) {
    const card = document.createElement("div");
    card.className = "cv-role-card";
    card.dataset.sourceIndex = String(entry.sourceIndex);

    const role = document.createElement("input");
    role.type = "text";
    role.maxLength = 160;
    role.value = entry.role;
    role.dataset.field = "role";
    role.setAttribute("aria-label", "Role title");

    const period = document.createElement("input");
    period.type = "text";
    period.maxLength = 120;
    period.value = entry.period;
    period.dataset.field = "period";
    period.setAttribute("aria-label", "Period");

    const bullets = document.createElement("textarea");
    bullets.rows = Math.min(8, Math.max(3, entry.bullets.length + 1));
    bullets.value = entry.bullets.join("\n");
    bullets.dataset.field = "bullets";
    bullets.setAttribute("aria-label", "Bullet points, one per line");

    const header = document.createElement("div");
    header.className = "cv-role-header";
    header.append(role, period);
    card.append(header, bullets);
    experienceEditor.append(card);
  }
}

function readEditor(): CvDocument {
  return {
    headline: headlineInput.value.trim(),
    summary: summaryInput.value.trim(),
    skillGroups: parseLines(skillsInput.value).map(line => {
      const separator = line.indexOf(":");
      const category = separator > 0 ? line.slice(0, separator).trim() : "";
      const skills = (separator > 0 ? line.slice(separator + 1) : line).split(",").map(skill => skill.trim()).filter(Boolean);
      return { category, skills };
    }),
    experience: Array.from(experienceEditor.querySelectorAll<HTMLElement>(".cv-role-card")).map(card => ({
      sourceIndex: Number(card.dataset.sourceIndex) || 0,
      role: card.querySelector<HTMLInputElement>('[data-field="role"]')?.value.trim() ?? "",
      period: card.querySelector<HTMLInputElement>('[data-field="period"]')?.value.trim() ?? "",
      bullets: parseLines(card.querySelector<HTMLTextAreaElement>('[data-field="bullets"]')?.value ?? "")
        .map(line => line.replace(/^[•*-]\s*/, ""))
    })),
    education: parseLines(educationInput.value),
    languages: parseLines(languagesInput.value),
    certifications: parseLines(certificationsInput.value)
  };
}

function buildInstructions(): string {
  const standing = instructionsInput.value.trim() || defaultInstructions;
  const forThisJob = jobInstructionsInput.value.trim();
  return forThisJob ? `${standing}\n\nFor this job specifically: ${forThisJob}` : standing;
}

async function makeCv(dependencies: CvBuilderDependencies): Promise<void> {
  if (makeCvButton.disabled) return;
  makeCvButton.disabled = true;
  makeCvButton.setAttribute("aria-busy", "true");
  try {
    makeCvLabel.textContent = "Preparing job…";
    setStatus(cvStatus, "Making sure this job is analysed and saved…");
    const jobId = await dependencies.ensureSavedJob();
    if (!jobId) {
      setStatus(cvStatus, "The job could not be analysed, so no CV was generated. Check the job description above.", "error");
      return;
    }

    makeCvLabel.textContent = "Writing your CV…";
    setStatus(cvStatus, "Tailoring your CV to this job. This can take up to a minute.");
    const response = await apiRequest<GeneratedCvResponse>(`/api/jobs/${encodeURIComponent(jobId)}/generated-cv`, {
      method: "POST",
      body: JSON.stringify({ customInstructions: buildInstructions() })
    });
    currentCvJobId = response.jobId;
    fillEditor(response.cv);
    cvEditor.hidden = false;
    setStatus(
      cvStatus,
      `CV ready and saved to the dashboard · status: ${response.applicationStatus}. Review and edit it below, then download it.`,
      "success"
    );
  } catch (error) {
    setStatus(cvStatus, error instanceof Error ? error.message : "The CV could not be generated.", "error");
  } finally {
    makeCvButton.disabled = false;
    makeCvButton.removeAttribute("aria-busy");
    makeCvLabel.textContent = currentCvJobId ? "Regenerate CV" : "Make CV";
  }
}

async function saveEdits(): Promise<boolean> {
  if (!currentCvJobId) return false;
  saveCvButton.disabled = true;
  try {
    await apiRequest<GeneratedCvResponse>(`/api/jobs/${encodeURIComponent(currentCvJobId)}/generated-cv`, {
      method: "PUT",
      body: JSON.stringify({ cv: readEditor() })
    });
    setStatus(cvStatus, "Edits saved to the dashboard.", "success");
    return true;
  } catch (error) {
    setStatus(cvStatus, error instanceof Error ? error.message : "Edits could not be saved.", "error");
    return false;
  } finally {
    saveCvButton.disabled = false;
  }
}

async function downloadDocx(): Promise<void> {
  if (!currentCvJobId) return;
  downloadCvButton.disabled = true;
  try {
    // Save first so the downloaded file includes any manual edits.
    if (!(await saveEdits())) return;
    const response = await fetch(`${API_BASE_URL}/api/jobs/${encodeURIComponent(currentCvJobId)}/generated-cv/docx`);
    if (!response.ok) throw new Error(`CV download failed with HTTP ${response.status}.`);
    const disposition = response.headers.get("Content-Disposition") || "";
    const fileName = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1]
      ?? /filename="?([^";]+)"?/i.exec(disposition)?.[1]
      ?? "CV.docx";
    const objectUrl = URL.createObjectURL(await response.blob());
    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = decodeURIComponent(fileName);
    document.body.append(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
    setStatus(cvStatus, "CV downloaded. You can also find it under this job in the dashboard.", "success");
  } catch (error) {
    setStatus(cvStatus, error instanceof Error ? error.message : "The CV could not be downloaded.", "error");
  } finally {
    downloadCvButton.disabled = false;
  }
}

/** Clears the editor when a different job is loaded into the form. */
export function resetCvBuilder(): void {
  currentCvJobId = null;
  cvEditor.hidden = true;
  experienceEditor.replaceChildren();
  jobInstructionsInput.value = "";
  makeCvLabel.textContent = "Make CV";
  setStatus(cvStatus, "");
}

export function initCvBuilder(dependencies: CvBuilderDependencies): void {
  void (async () => {
    try {
      const response = await apiRequest<{ instructions: string }>("/api/cv/default-instructions");
      defaultInstructions = response.instructions.trim();
    } catch {
      // The API may be offline; the backend default still applies when instructions are empty.
    }
    const stored = await chrome.storage.local.get(INSTRUCTIONS_KEY).catch(() => ({} as Record<string, unknown>));
    const saved = stored[INSTRUCTIONS_KEY];
    instructionsInput.value = typeof saved === "string" && saved.trim() ? saved : defaultInstructions;
    instructionsInput.placeholder = "Describe how your CV should be written…";
  })();

  instructionsInput.addEventListener("input", () => {
    window.clearTimeout(saveTimer);
    saveTimer = window.setTimeout(() => {
      void chrome.storage.local.set({ [INSTRUCTIONS_KEY]: instructionsInput.value })
        .then(() => {
          instructionsSaved.textContent = "Saved";
          window.setTimeout(() => { instructionsSaved.textContent = ""; }, 1500);
        })
        .catch(() => { instructionsSaved.textContent = "Could not save"; });
    }, 500);
  });

  resetInstructionsButton.addEventListener("click", () => {
    instructionsInput.value = defaultInstructions;
    void chrome.storage.local.remove(INSTRUCTIONS_KEY).catch(() => undefined);
    instructionsSaved.textContent = "Reset to default";
    window.setTimeout(() => { instructionsSaved.textContent = ""; }, 1500);
  });

  makeCvButton.addEventListener("click", () => void makeCv(dependencies));
  saveCvButton.addEventListener("click", () => void saveEdits());
  downloadCvButton.addEventListener("click", () => void downloadDocx());
}
