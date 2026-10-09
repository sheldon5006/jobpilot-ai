const DEFAULT_SETTINGS = {
  dbotAutoFetchEnabled: false,
  dbotAutoAnalyzeEnabled: false
};

const ALL_HTTP_ORIGINS = ["http://*/*", "https://*/*"];
const recentlyProcessed = new Map();
const inProgressTabs = new Set();

async function configureSidePanel() {
  try {
    await chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true });
  } catch (error) {
    console.error("DBot could not configure its side panel.", error);
  }
}

async function initialiseSettings() {
  const current = await chrome.storage.local.get(Object.keys(DEFAULT_SETTINGS));
  const missing = {};
  for (const [key, value] of Object.entries(DEFAULT_SETTINGS)) {
    if (current[key] === undefined) missing[key] = value;
  }
  if (Object.keys(missing).length > 0) {
    await chrome.storage.local.set(missing);
  }
}

chrome.runtime.onInstalled.addListener(() => {
  void configureSidePanel();
  void initialiseSettings();
});

chrome.runtime.onStartup.addListener(() => {
  void configureSidePanel();
  void initialiseSettings();
});

void configureSidePanel();
void initialiseSettings();

function normaliseText(value) {
  return String(value || "")
    .replace(/\u00a0/g, " ")
    .replace(/[ \t]+/g, " ")
    .replace(/\n[ \t]+/g, "\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

// This function is serialised and injected into the active page; it must be self-contained.
function extractJobDetailsFromPage() {
  const cleanText = (value) => String(value || "")
    .replace(/\u00a0/g, " ")
    .replace(/[ \t]+/g, " ")
    .replace(/\n[ \t]+/g, "\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();

  const htmlToText = (html) => {
    const parser = new DOMParser();
    const doc = parser.parseFromString(String(html || ""), "text/html");
    doc.querySelectorAll("script,style,noscript,svg,button,form").forEach(node => node.remove());
    return cleanText(doc.body.innerText || doc.body.textContent || "");
  };

  const unwrapTypes = (value) => {
    if (!value || typeof value !== "object") return [];
    const results = [];
    const types = Array.isArray(value["@type"]) ? value["@type"] : [value["@type"]];
    if (types.some(type => String(type || "").toLowerCase() === "jobposting")) {
      results.push(value);
    }
    for (const [key, child] of Object.entries(value)) {
      if (key === "@type") continue;
      if (Array.isArray(child)) {
        child.forEach(item => results.push(...unwrapTypes(item)));
      } else if (child && typeof child === "object") {
        results.push(...unwrapTypes(child));
      }
    }
    return results;
  };

  const structuredJobs = [];
  document.querySelectorAll('script[type="application/ld+json"]').forEach(script => {
    try {
      structuredJobs.push(...unwrapTypes(JSON.parse(script.textContent || "null")));
    } catch {
      // Ignore malformed structured data; use visible page content instead.
    }
  });

  const structuredJob = structuredJobs.find(job =>
    typeof job.description === "string" && job.description.trim().length >= 80
  ) || structuredJobs[0] || null;

  const companyFromStructured = structuredJob?.hiringOrganization;
  const structuredCompany = typeof companyFromStructured === "string"
    ? companyFromStructured
    : companyFromStructured?.name || "";

  // Prefer known job-description containers first, including LinkedIn's
  // dynamically rendered job detail pane, then fall back to common page markup.
  const selectors = [
    "#job-details",
    ".jobs-description__content",
    ".jobs-description-content__text",
    ".jobs-box__html-content",
    '[class*="jobs-description-content" i]',
    '[data-testid*="job-description" i]',
    '[data-automation-id*="jobPostingDescription" i]',
    '[itemprop="description"]',
    '[data-job-description]',
    '[class*="job-description" i]',
    '[id*="job-description" i]',
    '[class*="jobDescription" i]',
    '[id*="jobDescription" i]',
    '[class*="description__" i]',
    "article",
    "main",
    '[role="main"]'
  ];

  const candidates = [];
  selectors.forEach((selector, priority) => {
    try {
      document.querySelectorAll(selector).forEach(node => {
        if (!(node instanceof HTMLElement)) return;
        const clone = node.cloneNode(true);
        if (!(clone instanceof HTMLElement)) return;
        clone.querySelectorAll("script,style,noscript,svg,button,form,nav,footer,header,aside,[aria-hidden='true']").forEach(child => child.remove());
        const text = cleanText(clone.innerText || clone.textContent || "");
        if (text.length < 100) return;
        const hasJobTerms = /responsibilit|qualification|requirement|what you('ll| will) do|what we('re| are) looking|experience|skills|your profile|about the role|job description|stellenbeschreibung|aufgaben|anforderungen|qualifikation|berufserfahrung|kenntnisse/i.test(text);
        const lengthScore = Math.min(text.length, 14000) / 100;
        const score = (selectors.length - priority) * 12 + lengthScore + (hasJobTerms ? 30 : 0);
        candidates.push({ text, score });
      });
    } catch {
      // Some websites use custom selector behaviour; continue with other selectors.
    }
  });

  candidates.sort((a, b) => b.score - a.score);
  const structuredDescription = typeof structuredJob?.description === "string"
    ? htmlToText(structuredJob.description)
    : "";
  const visibleDescription = candidates.find(item => item.text.length >= 180)?.text || "";
  const description = (structuredDescription.length >= 120 ? structuredDescription : visibleDescription)
    .slice(0, 20000)
    .trim();

  const title = cleanText(
    structuredJob?.title ||
    document.querySelector(".jobs-unified-top-card__job-title")?.textContent ||
    document.querySelector("#job-details h1")?.textContent ||
    document.querySelector("main h1")?.textContent ||
    document.querySelector("h1")?.textContent ||
    document.querySelector('meta[property="og:title"]')?.getAttribute("content") ||
    document.title
  ).slice(0, 160);

  const companyElement = document.querySelector(
    '[data-testid*="company" i], [data-automation-id*="company" i], [class*="company-name" i], [class*="employer" i], [itemprop="hiringOrganization"]'
  );
  const companyFromPage = cleanText(companyElement?.textContent || "").slice(0, 160);
  const company = cleanText(structuredCompany || companyFromPage).slice(0, 160);

  const jobTextSignal = /job|career|vacancy|position|recruit|employment|stellenangebot|karriere|stelle|bewerbung/i.test(
    [document.title, title, structuredJob?.title || "", description.slice(0, 2500)].join(" ")
  );
  const found = description.length >= 100 && (Boolean(structuredJob) || jobTextSignal);

  return {
    found,
    title,
    company,
    description,
    url: location.href,
    pageTitle: cleanText(document.title).slice(0, 240),
    reason: found
      ? "Job details were detected from the page HTML."
      : "A full job description was not detected. Try another job page or paste the description manually.",
    extractionSource: structuredDescription.length >= 120 ? "JobPosting structured data" : "visible page content"
  };
}

async function readSettings() {
  const settings = await chrome.storage.local.get(DEFAULT_SETTINGS);
  return {
    autoFetchEnabled: Boolean(settings.dbotAutoFetchEnabled),
    autoAnalyzeEnabled: Boolean(settings.dbotAutoAnalyzeEnabled)
  };
}

async function hasAllSitesPermission() {
  try {
    return await chrome.permissions.contains({ origins: ALL_HTTP_ORIGINS });
  } catch {
    return false;
  }
}

async function extractFromTab(tabId) {
  const results = await chrome.scripting.executeScript({
    target: { tabId },
    func: extractJobDetailsFromPage
  });
  if (!results || results.length === 0 || !results[0].result) {
    throw new Error("No page content was returned. The current page may block script access.");
  }
  return results[0].result;
}

async function notifyPanel(message) {
  try {
    await chrome.runtime.sendMessage(message);
  } catch {
    // It is normal for the side panel to be closed while the user browses.
  }
}

async function maybeAutoFetch(tabId, tabUrl) {
  const settings = await readSettings();
  if (!settings.autoFetchEnabled || inProgressTabs.has(tabId)) return;
  if (!(await hasAllSitesPermission())) return;

  const url = tabUrl || (await chrome.tabs.get(tabId)).url || "";
  if (!/^https?:\/\//i.test(url)) return;
  const key = `${tabId}:${url}`;
  if (recentlyProcessed.get(tabId) === key) return;

  inProgressTabs.add(tabId);
  try {
    const job = await extractFromTab(tabId);
    recentlyProcessed.set(tabId, key);
    await notifyPanel({
      type: "DBOT_JOB_DETAILS_DETECTED",
      tabId,
      job,
      source: "auto",
      autoAnalyze: settings.autoAnalyzeEnabled
    });
  } catch (error) {
    await notifyPanel({
      type: "DBOT_PAGE_SCAN_FAILED",
      tabId,
      url,
      message: error instanceof Error ? error.message : "Could not inspect this page."
    });
  } finally {
    inProgressTabs.delete(tabId);
  }
}

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status === "complete" || changeInfo.url) {
    void maybeAutoFetch(tabId, tab.url);
  }
});

chrome.tabs.onActivated.addListener(({ tabId }) => {
  void maybeAutoFetch(tabId);
});

chrome.permissions.onRemoved.addListener(() => {
  void chrome.storage.local.set({
    dbotAutoFetchEnabled: false,
    dbotAutoAnalyzeEnabled: false
  });
  void notifyPanel({ type: "DBOT_SETTINGS_CHANGED" });
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (!message || typeof message.type !== "string") return false;

  if (message.type === "DBOT_GET_SETTINGS") {
    Promise.all([readSettings(), hasAllSitesPermission()])
      .then(([settings, allSitesPermission]) => sendResponse({
        ok: true,
        settings,
        allSitesPermission
      }))
      .catch(error => sendResponse({
        ok: false,
        error: error instanceof Error ? error.message : "Could not load DBot settings."
      }));
    return true;
  }

  if (message.type === "DBOT_FETCH_CURRENT_PAGE") {
    (async () => {
      // The side panel supplies the active tab ID from its own browser window.
      // A service worker's lastFocusedWindow query can point at a different
      // window (or return no tab), so don't guess which page the user meant.
      const requestedTabId = Number.isInteger(message.tabId) ? message.tabId : undefined;
      if (requestedTabId === undefined) {
        sendResponse({
          ok: false,
          error: "DBot could not identify the active tab. Close and reopen the side panel, then try fetching again."
        });
        return;
      }

      const tab = await chrome.tabs.get(requestedTabId);
      if (tab.id === undefined) {
        sendResponse({
          ok: false,
          error: "DBot could not access the selected browser tab. Please retry from the job page."
        });
        return;
      }
      try {
        // Don't reject the tab just because Chrome omitted tab.url from metadata.
        // Read location.href from the page itself after script injection.
        const job = await extractFromTab(tab.id);
        if (typeof job.url !== "string" || !/^https?:\/\//i.test(job.url)) {
          sendResponse({
            ok: false,
            error: "Open a normal http/https job webpage before fetching details."
          });
          return;
        }
        recentlyProcessed.set(tab.id, `${tab.id}:${job.url}`);
        sendResponse({ ok: true, tabId: tab.id, job, source: "manual" });
      } catch (error) {
        sendResponse({
          ok: false,
          error: error instanceof Error
            ? `${error.message} Click DBot's toolbar icon while this page is active to grant temporary page access, or enable Auto-fetch for ongoing access.`
            : "Could not inspect this page."
        });
      }
    })().catch(error => sendResponse({
      ok: false,
      error: error instanceof Error ? error.message : "Could not fetch page details."
    }));
    return true;
  }

  if (message.type === "DBOT_SET_SETTINGS") {
    (async () => {
      const autoFetchEnabled = Boolean(message.autoFetchEnabled);
      const autoAnalyzeEnabled = Boolean(message.autoAnalyzeEnabled) && autoFetchEnabled;
      await chrome.storage.local.set({
        dbotAutoFetchEnabled: autoFetchEnabled,
        dbotAutoAnalyzeEnabled: autoAnalyzeEnabled
      });
      sendResponse({ ok: true, settings: { autoFetchEnabled, autoAnalyzeEnabled } });
    })().catch(error => sendResponse({
      ok: false,
      error: error instanceof Error ? error.message : "Could not save DBot settings."
    }));
    return true;
  }

  return false;
});
