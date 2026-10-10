import { apiRequest, copyText, element, setStatus } from "./shared";

export interface AskJobContext {
  jobId: string | null;
  jobTitle: string;
  company: string;
  jobDescription: string;
}

const askForm = element<HTMLFormElement>("#ask-form");
const questionInput = element<HTMLTextAreaElement>("#ask-question");
const useJobToggle = element<HTMLInputElement>("#ask-use-job");
const jobContextLabel = element<HTMLElement>("#ask-job-context");
const lengthSelect = element<HTMLSelectElement>("#ask-length");
const instructionsInput = element<HTMLInputElement>("#ask-instructions");
const askButton = element<HTMLButtonElement>("#ask-button");
const askButtonLabel = element<HTMLElement>("#ask-button-label");
const askStatus = element<HTMLElement>("#ask-status");
const answerArea = element<HTMLElement>("#ask-answer-area");
const answerInput = element<HTMLTextAreaElement>("#ask-answer");
const copyAnswerButton = element<HTMLButtonElement>("#copy-answer-button");

/** Refreshes the "Use the current job" description when the Ask tab is opened. */
export function refreshAskJobContext(context: AskJobContext): void {
  const hasJob = context.jobDescription.trim().length >= 40;
  useJobToggle.disabled = !hasJob;
  if (!hasJob) useJobToggle.checked = false;
  jobContextLabel.textContent = hasJob
    ? [context.jobTitle || "Untitled role", context.company].filter(Boolean).join(" · ")
    : "No job loaded in the Job tab yet. The answer will use your profile only.";
}

export function initAskTab(getJobContext: () => AskJobContext): void {
  askForm.addEventListener("submit", event => {
    event.preventDefault();
    void (async () => {
      const question = questionInput.value.trim();
      if (question.length < 5) {
        questionInput.setCustomValidity("Paste the question from the application form.");
        questionInput.reportValidity();
        return;
      }
      questionInput.setCustomValidity("");

      const context = getJobContext();
      const useJob = useJobToggle.checked && context.jobDescription.trim().length >= 40;
      askButton.disabled = true;
      askButtonLabel.textContent = "Writing answer…";
      setStatus(askStatus, "Drafting an answer from your profile…");
      try {
        const response = await apiRequest<{ answer: string }>("/api/assistant/answer", {
          method: "POST",
          body: JSON.stringify({
            question,
            length: lengthSelect.value,
            customInstructions: instructionsInput.value.trim() || null,
            ...(useJob
              ? {
                  jobId: context.jobId,
                  jobTitle: context.jobTitle,
                  company: context.company,
                  jobDescription: context.jobDescription
                }
              : {})
          })
        });
        answerInput.value = response.answer;
        answerArea.hidden = false;
        setStatus(askStatus, "Answer ready. Review it, then copy it into the form.", "success");
      } catch (error) {
        setStatus(askStatus, error instanceof Error ? error.message : "The answer could not be generated.", "error");
      } finally {
        askButton.disabled = false;
        askButtonLabel.textContent = "Generate answer";
      }
    })();
  });

  questionInput.addEventListener("input", () => questionInput.setCustomValidity(""));

  copyAnswerButton.addEventListener("click", () => {
    void copyText(answerInput.value)
      .then(() => {
        copyAnswerButton.textContent = "Copied ✓";
        window.setTimeout(() => { copyAnswerButton.textContent = "⧉ Copy answer"; }, 1400);
      })
      .catch(error => setStatus(askStatus, error instanceof Error ? error.message : "Copy failed.", "error"));
  });
}
