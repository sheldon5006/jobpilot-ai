interface AuthConfig {
  enabled: boolean;
  googleClientId: string | null;
}

interface Session {
  token: string;
  email: string;
  expiresAtUtc: string;
}

interface GoogleCredentialResponse {
  credential: string;
}

declare const google: {
  accounts: {
    id: {
      initialize(options: { client_id: string; callback: (response: GoogleCredentialResponse) => void; auto_select?: boolean }): void;
      renderButton(parent: HTMLElement, options: Record<string, string | number>): void;
      disableAutoSelect(): void;
    };
  };
};

const SESSION_KEY = "jobpilotSession";
let session: Session | null = readSession();

function readSession(): Session | null {
  try {
    const stored = JSON.parse(localStorage.getItem(SESSION_KEY) || "null") as Session | null;
    return stored?.token && new Date(stored.expiresAtUtc).getTime() > Date.now() ? stored : null;
  } catch {
    return null;
  }
}

function storeSession(value: Session | null): void {
  session = value;
  try {
    if (value) localStorage.setItem(SESSION_KEY, JSON.stringify(value));
    else localStorage.removeItem(SESSION_KEY);
  } catch {
    // Storage can be blocked; the session then lasts until the page is closed.
  }
}

/** Headers for authenticated API requests. */
export function authHeaders(): Record<string, string> {
  return session ? { Authorization: `Bearer ${session.token}` } : {};
}

let onSignedIn: () => void = () => undefined;

function signInScreen(): HTMLElement {
  return document.querySelector<HTMLElement>("#signin-screen")!;
}

function setSignInMessage(message: string): void {
  const target = document.querySelector<HTMLElement>("#signin-message")!;
  target.textContent = message;
  target.hidden = !message;
}

function showSignedIn(email: string | null): void {
  signInScreen().hidden = true;
  document.body.classList.remove("signed-out");
  const account = document.querySelector<HTMLElement>("#account-info")!;
  account.hidden = !email;
  document.querySelector<HTMLElement>("#account-email")!.textContent = email ?? "";
}

/** Called when the API rejects the session token. */
export function requireSignIn(message = "Your session has ended. Sign in again to continue."): void {
  storeSession(null);
  document.body.classList.add("signed-out");
  signInScreen().hidden = false;
  setSignInMessage(message);
}

function loadGoogleScript(): Promise<void> {
  return new Promise((resolve, reject) => {
    if (typeof google !== "undefined") return resolve();
    const script = document.createElement("script");
    script.src = "https://accounts.google.com/gsi/client";
    script.async = true;
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Google sign-in could not be loaded. Check your connection."));
    document.head.append(script);
  });
}

async function renderGoogleButton(apiBaseUrl: string, clientId: string): Promise<void> {
  await loadGoogleScript();
  google.accounts.id.initialize({
    client_id: clientId,
    callback: response => {
      void (async () => {
        setSignInMessage("Signing in…");
        try {
          const result = await fetch(`${apiBaseUrl}/api/auth/google`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ idToken: response.credential })
          });
          const payload = (await result.json().catch(() => null)) as (Session & { title?: string; detail?: string }) | null;
          if (!result.ok || !payload?.token) {
            throw new Error(payload?.detail || payload?.title || `Sign-in failed with HTTP ${result.status}.`);
          }
          storeSession(payload);
          setSignInMessage("");
          showSignedIn(payload.email);
          onSignedIn();
        } catch (error) {
          setSignInMessage(error instanceof Error ? error.message : "Sign-in failed.");
        }
      })();
    }
  });
  google.accounts.id.renderButton(document.querySelector<HTMLElement>("#google-signin-button")!, {
    theme: "outline",
    size: "large",
    shape: "pill",
    text: "signin_with",
    width: 260
  });
}

/**
 * Shows the sign-in screen when the API requires it. `start` runs once the dashboard may call the API.
 */
export async function initAuth(apiBaseUrl: string, start: () => void): Promise<void> {
  onSignedIn = start;
  document.querySelector<HTMLButtonElement>("#sign-out-button")?.addEventListener("click", () => {
    if (typeof google !== "undefined") google.accounts.id.disableAutoSelect();
    requireSignIn("You have signed out.");
  });

  let config: AuthConfig;
  try {
    const response = await fetch(`${apiBaseUrl}/api/auth/config`);
    if (!response.ok) throw new Error();
    config = (await response.json()) as AuthConfig;
  } catch {
    // API offline or asleep: start anyway so the dashboard shows its connection error.
    showSignedIn(null);
    start();
    return;
  }

  if (!config.enabled) {
    showSignedIn(null);
    start();
    return;
  }

  if (session) {
    showSignedIn(session.email);
    start();
  } else {
    requireSignIn("");
  }

  if (config.googleClientId) {
    await renderGoogleButton(apiBaseUrl, config.googleClientId).catch(error =>
      setSignInMessage(error instanceof Error ? error.message : "Google sign-in could not be loaded."));
  }
}
