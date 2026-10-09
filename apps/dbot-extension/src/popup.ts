const form = document.querySelector<HTMLFormElement>("#job-form");
const jobTitleInput = document.querySelector<HTMLInputElement>("#job-title");
const companyInput = document.querySelector<HTMLInputElement>("#company");
const descriptionInput = document.querySelector<HTMLTextAreaElement>("#job-description");
const resultPanel = document.querySelector<HTMLElement>("#result");
const resultTitle = document.querySelector<HTMLElement>("#result-title");
const resultCopy = document.querySelector<HTMLElement>("#result-copy");
const resultMeta = document.querySelector<HTMLElement>("#result-meta");

if (
  !form ||
  !jobTitleInput ||
  !companyInput ||
  !descriptionInput ||
  !resultPanel ||
  !resultTitle ||
  !resultCopy ||
  !resultMeta
) {
  throw new Error("DBot popup could not initialise because a required UI element is missing.");
}

const hideResult = (): void => {
  resultPanel.hidden = true;
};

form.addEventListener("input", hideResult);

form.addEventListener("submit", (event: SubmitEvent) => {
  event.preventDefault();

  const description = descriptionInput.value.trim();
  if (description.length < 40) {
    descriptionInput.setCustomValidity("Please paste at least 40 characters from the job description.");
    descriptionInput.reportValidity();
    return;
  }

  descriptionInput.setCustomValidity("");

  const jobTitle = jobTitleInput.value.trim() || "Untitled role";
  const company = companyInput.value.trim() || "Company not specified";

  resultTitle.textContent = "Description captured";
  resultCopy.textContent =
    "The input is valid. AI translation, profile matching, and the Apply / Review / Skip recommendation are not connected yet; we will add them in the next milestone.";
  resultMeta.textContent =
    `${jobTitle} · ${company} · ${description.length.toLocaleString()} characters`;

  resultPanel.hidden = false;
});
