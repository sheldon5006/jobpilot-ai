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

  // This LinkedIn layout exposes a job-specific section ID, the job URL link,
  // and company links. Avoid hashed CSS classes and broad main/article scraping.
  const aboutSection = document.querySelector('[id^="JobDetails_AboutTheJob_"]');
  const aboutId = String(aboutSection?.id || "").match(/^JobDetails_AboutTheJob_(\d+)$/);
  const url = new URL(location.href);
  const selectedJobId = aboutId?.[1]
    || url.searchParams.get("currentJobId")
    || url.searchParams.get("jobPostingId")
    || url.pathname.match(/\/jobs\/view\/(\d+)/)?.[1]
    || "";

  const jobLinks = Array.from(document.querySelectorAll('a[href*="/jobs/view/"]'));
  const jobLink = (selectedJobId
    ? jobLinks.find(link => {
        try {
          return new URL(link.href, location.href).pathname.includes("/jobs/view/" + selectedJobId);
        } catch {
          return false;
        }
      })
    : null)
    || document.querySelector(".jobs-unified-top-card__job-title a")
    || jobLinks[0]
    || null;

  const descriptionRoot = aboutSection?.querySelector('[data-testid="expandable-text-box"]')
    || aboutSection
    || null;
  let title = cleanText(
    jobLink?.textContent
    || descriptionRoot?.querySelector("strong")?.textContent
    || document.querySelector(".jobs-unified-top-card__job-title")?.textContent
    || ""
  ).slice(0, 160);

  // Find the company link closest to the selected job's header, using its href
  // pattern instead of LinkedIn's generated gfl* CSS classes.
  let companyLink = null;
  let header = jobLink?.parentElement || null;
  for (let depth = 0; header && depth < 10; depth += 1, header = header.parentElement) {
    const links = Array.from(header.querySelectorAll('a[href*="/company/"]'));
    companyLink = links.find(link => cleanText(link.textContent || link.getAttribute("aria-label"))) || null;
    if (companyLink) break;
  }
  const ariaCompany = cleanText(companyLink?.getAttribute("aria-label") || "")
    .replace(/^Company,\s*/i, "")
    .replace(/\.$/, "");
  const company = cleanText(
    companyLink?.querySelector('a[href*="/company/"]')?.textContent
    || companyLink?.textContent
    || ariaCompany
  ).slice(0, 160);

  let description = "";
  if (descriptionRoot) {
    const clone = descriptionRoot.cloneNode(true);
    clone.querySelectorAll("button, script, style, noscript, svg, [data-testid='expandable-text-button']").forEach(node => node.remove());
    description = cleanText(clone.innerText || clone.textContent || "");
    // The first line of this LinkedIn container repeats the job title.
    if (title && description.toLowerCase().startsWith(title.toLowerCase())) {
      description = description.slice(title.length).trim();
    }
    description = description.replace(/^[\s:–—-]+/, "").trim().slice(0, 20000);
  }

  const found = description.length >= 40 && Boolean(title || company);
  let reason = "Job details were read from LinkedIn's selected job-detail HTML.";
  if (!aboutSection) {
    reason = "Couldn't find LinkedIn's [id^='JobDetails_AboutTheJob_'] section. Select a job and wait for its details to load, then retry.";
  } else if (!description || description.length < 40) {
    reason = "Found the selected job panel, but its About the job text is missing or too short. Wait for the description to load, then retry.";
  }

  return {
    found,
    title,
    company,
    description,
    url: location.href,
    pageTitle: cleanText(document.title).slice(0, 240),
    reason,
    extractionSource: "LinkedIn selected job-detail DOM"
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
