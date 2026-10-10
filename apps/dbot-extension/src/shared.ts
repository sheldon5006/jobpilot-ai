export const API_BASE_URL = "http://127.0.0.1:5080";

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

export function element<T extends HTMLElement>(selector: string): T {
  const found = document.querySelector<T>(selector);
  if (!found) {
    throw new Error(`DBot could not initialise: missing element ${selector}`);
  }
  return found;
}

export function setStatus(target: HTMLElement, message: string, kind: "info" | "success" | "error" = "info"): void {
  target.textContent = message;
  target.className = `page-fetch-status ${kind}`;
}

/** Calls the local JobPilot API and turns problem responses into readable errors. */
export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: { "Content-Type": "application/json", ...(init?.headers || {}) }
    });
  } catch {
    throw new Error(`Cannot reach the local API. Start the ASP.NET Core API at ${API_BASE_URL}, then try again.`);
  }

  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    const problem = (payload ?? {}) as ApiProblem;
    const validation = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
    throw new Error(validation || problem.detail || problem.title || `Local API returned HTTP ${response.status}.`);
  }
  return payload as T;
}

export async function copyText(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    // Fallback for when the side panel document is not focused.
    const helper = document.createElement("textarea");
    helper.value = text;
    helper.style.position = "fixed";
    helper.style.opacity = "0";
    document.body.append(helper);
    helper.select();
    const copied = document.execCommand("copy");
    helper.remove();
    if (!copied) throw new Error("Copy failed. Select the text and copy it manually.");
  }
}

export function parseLines(value: string): string[] {
  return value.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
}
