import { apiRequest, copyText, element, setStatus } from "./shared";

interface ContactDetails {
  fullName: string;
  email: string;
  phone: string;
  location: string;
  linkedIn: string;
  website: string;
}

interface CandidateProfile {
  professionalSummary: string;
  targetRoles: string[];
  professionalSkills: string[];
  projectAndAcademicSkills: string[];
  experience: Array<{ role: string; period: string; evidence: string[] }>;
  projects?: Array<{ name: string; context: string; url: string; description: string; highlights: string[]; technologies: string[] }>;
  education: string[];
  languages: Array<{ language: string; proficiency: string }>;
  workAuthorization: string;
  certifications: string[];
  contact?: ContactDetails | null;
}

const profileContent = element<HTMLElement>("#profile-content");
const profileStatus = element<HTMLElement>("#profile-status");
const refreshButton = element<HTMLButtonElement>("#refresh-profile-button");
let loaded = false;

function copyRow(label: string, value: string, multiline = false): HTMLElement {
  const row = document.createElement("button");
  row.type = "button";
  row.className = `copy-row${multiline ? " multiline" : ""}`;
  row.title = "Click to copy";

  const labelElement = document.createElement("span");
  labelElement.className = "copy-row-label";
  labelElement.textContent = label;
  const valueElement = document.createElement("span");
  valueElement.className = "copy-row-value";
  valueElement.textContent = value;
  const hint = document.createElement("span");
  hint.className = "copy-row-hint";
  hint.textContent = "Copy";
  row.append(labelElement, valueElement, hint);

  row.addEventListener("click", () => {
    void copyText(value)
      .then(() => {
        hint.textContent = "Copied ✓";
        row.classList.add("copied");
        window.setTimeout(() => {
          hint.textContent = "Copy";
          row.classList.remove("copied");
        }, 1400);
      })
      .catch(error => setStatus(profileStatus, error instanceof Error ? error.message : "Copy failed.", "error"));
  });
  return row;
}

function section(title: string, rows: HTMLElement[]): HTMLElement | null {
  if (rows.length === 0) return null;
  const wrapper = document.createElement("section");
  wrapper.className = "profile-section";
  const heading = document.createElement("h3");
  heading.textContent = title;
  wrapper.append(heading, ...rows);
  return wrapper;
}

function renderProfile(profile: CandidateProfile): void {
  const contact = profile.contact ?? { fullName: "", email: "", phone: "", location: "", linkedIn: "", website: "" };
  const contactRows = [
    ["Full name", contact.fullName],
    ["Email", contact.email],
    ["Phone", contact.phone],
    ["Location", contact.location],
    ["LinkedIn", contact.linkedIn],
    ["Website / GitHub", contact.website]
  ]
    .filter(([, value]) => value?.trim())
    .map(([label, value]) => copyRow(label, value.trim()));

  const experienceRows = profile.experience.flatMap(entry => {
    const title = [entry.role, entry.period].filter(Boolean).join(" · ");
    const rows = [copyRow("Role", title)];
    if (entry.evidence.length > 0) {
      rows.push(copyRow("Responsibilities", entry.evidence.map(line => `• ${line}`).join("\n"), true));
    }
    return rows;
  });

  const projectRows = (profile.projects ?? []).flatMap(project => {
    const rows = [copyRow(project.context || "Project", [project.name, project.url].filter(Boolean).join(" · "))];
    const details = [project.description, ...project.highlights.map(line => `• ${line}`)].filter(Boolean).join("\n");
    if (details) rows.push(copyRow("Description", details, true));
    if (project.technologies.length > 0) rows.push(copyRow("Technologies", project.technologies.join(", ")));
    return rows;
  });

  const skills = [...profile.professionalSkills];
  const languages = profile.languages.map(item => item.proficiency ? `${item.language} — ${item.proficiency}` : item.language);

  const sections = [
    section("Contact", contactRows.length > 0
      ? contactRows
      : [emptyNote("Add your name, email and phone under My Profile in the dashboard to copy them here.")]),
    section("Summary", profile.professionalSummary ? [copyRow("Professional summary", profile.professionalSummary, true)] : []),
    section("Work authorisation", profile.workAuthorization ? [copyRow("Right to work", profile.workAuthorization, true)] : []),
    section("Languages", languages.map(value => copyRow("Language", value))),
    section("Experience", experienceRows),
    section("Projects", projectRows),
    section("Skills", [
      ...(skills.length > 0 ? [copyRow("Professional skills", skills.join(", "), true)] : []),
      ...(profile.projectAndAcademicSkills.length > 0
        ? [copyRow("Project & academic skills", profile.projectAndAcademicSkills.join(", "), true)]
        : [])
    ]),
    section("Education", profile.education.map(value => copyRow("Education", value))),
    section("Certifications", profile.certifications.map(value => copyRow("Certification", value))),
    section("Target roles", profile.targetRoles.length > 0 ? [copyRow("Target roles", profile.targetRoles.join(", "))] : [])
  ].filter((item): item is HTMLElement => item !== null);

  profileContent.replaceChildren(...sections);
}

function emptyNote(text: string): HTMLElement {
  const note = document.createElement("p");
  note.className = "field-hint";
  note.textContent = text;
  return note;
}

export async function loadProfileTab(force = false): Promise<void> {
  if (loaded && !force) return;
  refreshButton.disabled = true;
  setStatus(profileStatus, "Loading your profile…");
  try {
    const response = await apiRequest<{ profile: CandidateProfile; updatedAtUtc: string }>("/api/profile");
    renderProfile(response.profile);
    loaded = true;
    setStatus(profileStatus, "");
  } catch (error) {
    setStatus(profileStatus, error instanceof Error ? error.message : "Could not load your profile.", "error");
  } finally {
    refreshButton.disabled = false;
  }
}

refreshButton.addEventListener("click", () => void loadProfileTab(true));
