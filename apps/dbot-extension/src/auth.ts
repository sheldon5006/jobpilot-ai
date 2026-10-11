import { API_BASE_URL, apiRequest, element, getSession, onSignInRequired, saveSession, setStatus } from "./shared";

interface AuthConfig {
  enabled: boolean;
  googleClientId: string | null;
}

const signInPanel = element<HTMLElement>("#signin-panel");
const signInButton = element<HTMLButtonElement>("#google-signin-button");
const signInStatus = element<HTMLElement>("#signin-status");
const redirectHint = element<HTMLElement>("#signin-redirect-uri");
const appContent = element<HTMLElement>("#app-content");
const accountRow = element<HTMLElement>("#account-row");
const accountEmail = element<HTMLElement>("#account-email");
const signOutButton = element<HTMLButtonElement>("#sign-out-button");
const signInApiUrl = element<HTMLElement>("#signin-api-url");

let config: AuthConfig | null = null;

function showSignIn(message = ""): void {
  appContent.hidden = true;
  signInPanel.hidden = false;
  accountRow.hidden = true;
  setStatus(signInStatus, message, message ? "error" : "info");
}

function showApp(email: string | null): void {
  signInPanel.hidden = true;
  appContent.hidden = false;
  accountRow.hidden = !email;
  accountEmail.textContent = email ?? "";
}

const isLocalApi = /^http:\/\/(127\.0\.0\.1|localhost)(:|\/|$)/i.test(API_BASE_URL);

async function loadConfig(): Promise<AuthConfig> {
  if (!config) {
    // A sleeping free server can take about a minute to wake; don't wait forever.
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 90_000);
    let response: Response;
    try {
      response = await fetch(`${API_BASE_URL}/api/auth/config`, { signal: controller.signal });
    } catch {
      throw new Error(`Can't reach the JobPilot API at ${API_BASE_URL}. Check that this is your API's exact address (Render → jobpilot-api → URL), then try again.`);
    } finally {
      window.clearTimeout(timeout);
    }
    if (!response.ok) throw new Error(`The API at ${API_BASE_URL} returned HTTP ${response.status}. Check the Render logs for jobpilot-api.`);
    config = (await response.json()) as AuthConfig;
  }
  return config;
}

function decodeJwtPayload(token: string): Record<string, unknown> {
  const payload = token.split(".")[1] ?? "";
  const bytes = Uint8Array.from(atob(payload.replace(/-/g, "+").replace(/_/g, "/")), character => character.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;
}

async function signInWithGoogle(): Promise<void> {
  signInButton.disabled = true;
  setStatus(signInStatus, "Opening Google sign-in…");
  try {
    const { enabled, googleClientId } = await loadConfig();
    if (!enabled) {
      showApp(null);
      return;
    }
    if (!googleClientId) throw new Error("The API has no Google client configured.");

    const nonce = crypto.randomUUID();
    const url = new URL("https://accounts.google.com/o/oauth2/v2/auth");
    url.search = new URLSearchParams({
      client_id: googleClientId,
      response_type: "id_token",
      redirect_uri: chrome.identity.getRedirectURL(),
      scope: "openid email",
      nonce,
      prompt: "select_account"
    }).toString();

    const redirect = await chrome.identity.launchWebAuthFlow({ url: url.toString(), interactive: true });
    const idToken = redirect ? new URLSearchParams(new URL(redirect).hash.slice(1)).get("id_token") : null;
    if (!idToken) throw new Error("Google did not return a sign-in token.");
    if (decodeJwtPayload(idToken).nonce !== nonce) throw new Error("The Google sign-in response could not be verified.");

    const session = await apiRequest<{ token: string; email: string; expiresAtUtc: string }>("/api/auth/google", {
      method: "POST",
      body: JSON.stringify({ idToken })
    });
    await saveSession(session);
    showApp(session.email);
    document.dispatchEvent(new CustomEvent("dbot:signed-in"));
  } catch (error) {
    const message = error instanceof Error ? error.message : "Sign-in failed.";
    setStatus(signInStatus, /did not approve|canceled|cancelled/i.test(message) ? "Sign-in was cancelled." : message, "error");
  } finally {
    signInButton.disabled = false;
  }
}

/** Shows the sign-in screen when the API requires it. Resolves true once DBot can call the API. */
export async function initAuth(): Promise<boolean> {
  redirectHint.textContent = chrome.identity.getRedirectURL();
  signInButton.addEventListener("click", () => void signInWithGoogle());
  signOutButton.addEventListener("click", () => {
    void saveSession(null).then(() => showSignIn());
  });
  onSignInRequired(() => showSignIn("Your session has ended. Sign in again to continue."));

  signInApiUrl.textContent = API_BASE_URL;
  showSignIn();
  setStatus(signInStatus, isLocalApi
    ? "Connecting to the local API…"
    : "Connecting to your JobPilot API… a free server can take up to a minute to wake up.");
  signInButton.disabled = true;

  try {
    const { enabled } = await loadConfig();
    if (!enabled) {
      showApp(null);
      return true;
    }
  } catch (error) {
    if (isLocalApi) {
      // Local development: show the app; requests explain how to start the API.
      showApp(null);
      return true;
    }
    showSignIn(`${error instanceof Error ? error.message : "Can't reach the JobPilot API."} Choosing Sign in retries the connection.`);
    return false;
  } finally {
    signInButton.disabled = false;
  }

  const session = await getSession();
  if (session) {
    showApp(session.email);
    return true;
  }

  showSignIn();
  return false;
}
