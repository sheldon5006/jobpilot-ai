export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL || "http://127.0.0.1:5080").replace(/\/$/, "");

const SESSION_KEY = "dbotSession";

interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

interface StoredSession {
  token: string;
  email: string;
  expiresAtUtc: string;
}

/** Raised when the API needs a (new) Google sign-in. */
export class SignInRequiredError extends Error {
  constructor() {
    super("Sign in with Google to use DBot.");
  }
}

let session: StoredSession | null = null;
const sessionReady: Promise<void> = chrome.storage.local.get(SESSION_KEY)
  .then(values => {
    const stored = values[SESSION_KEY] as StoredSession | undefined;
    if (stored?.token && new Date(stored.expiresAtUtc).getTime() > Date.now()) session = stored;
  })
  .catch(() => undefined);

const signInListeners: Array<() => void> = [];

/** Registers a callback for when the API rejects the session (expired or missing). */
export function onSignInRequired(listener: () => void): void {
  signInListeners.push(listener);
}

export async function getSession(): Promise<StoredSession | null> {
  await sessionReady;
  return session;
}

export async function saveSession(value: StoredSession | null): Promise<void> {
  session = value;
  await (value ? chrome.storage.local.set({ [SESSION_KEY]: value }) : chrome.storage.local.remove(SESSION_KEY)).catch(() => undefined);
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

/** fetch() against the JobPilot API with the session token attached. */
export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  await sessionReady;
  const headers = new Headers(init?.headers);
  if (session) headers.set("Authorization", `Bearer ${session.token}`);

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, { ...init, headers });
  } catch {
    throw new Error(API_BASE_URL.includes("127.0.0.1")
      ? `Cannot reach the local API. Start the ASP.NET Core API at ${API_BASE_URL}, then try again.`
      : "Cannot reach the JobPilot API. A free server can take up to a minute to wake up; try again shortly.");
  }

  if (response.status === 401) {
    await saveSession(null);
    signInListeners.forEach(listener => listener());
    throw new SignInRequiredError();
  }
  return response;
}

/** Calls the JobPilot API with JSON and turns problem responses into readable errors. */
export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers);
  if (!headers.has("Content-Type")) headers.set("Content-Type", "application/json");
  const response = await apiFetch(path, { ...init, headers });

  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    const problem = (payload ?? {}) as ApiProblem;
    const validation = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
    throw new Error(validation || problem.detail || problem.title || `The API returned HTTP ${response.status}.`);
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
