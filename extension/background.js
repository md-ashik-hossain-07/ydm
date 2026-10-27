// YDM Chromium Manifest V3 Background Service Worker
const NATIVE_HOST = "com.ydm.downloader";

// Initialize extension defaults
chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.sync.get(["interceptionEnabled", "sendDomainCookies"], (res) => {
    if (res.interceptionEnabled === undefined) {
      chrome.storage.sync.set({ interceptionEnabled: true });
    }
    if (res.sendDomainCookies === undefined) {
      chrome.storage.sync.set({ sendDomainCookies: false });
    }
  });

  // Create Context Menu item
  chrome.contextMenus.create({
    id: "ydm-download-link",
    title: "Download with YDM",
    contexts: ["link", "video", "audio"]
  });
});

// Handle Context Menu clicks
chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "ydm-download-link") {
    const targetUrl = info.linkUrl || info.srcUrl;
    if (targetUrl) {
      sendDownloadToYdm({
        action: "download",
        url: targetUrl,
        referrer: tab ? tab.url : undefined
      });
    }
  }
});

// Intercept browser downloads via chrome.downloads API
chrome.downloads.onDeterminingFilename.addListener((downloadItem, suggest) => {
  chrome.storage.sync.get(["interceptionEnabled", "sendDomainCookies"], async (settings) => {
    if (!settings.interceptionEnabled) {
      suggest(); // Leave download to Chrome
      return;
    }

    // Do NOT intercept blob:, data:, or chrome:// URLs
    if (!downloadItem.url.startsWith("http://") && !downloadItem.url.startsWith("https://")) {
      suggest();
      return;
    }

    try {
      let cookies = undefined;
      // Scoped cookie transmission: Only if explicitly enabled by user and granted
      if (settings.sendDomainCookies) {
        const hasCookiePermission = await chrome.permissions.contains({ permissions: ["cookies"] });
        if (hasCookiePermission) {
          const cookieList = await chrome.cookies.getAll({ url: downloadItem.url });
          if (cookieList && cookieList.length > 0) {
            cookies = cookieList.map(c => `${c.name}=${c.value}`).join("; ");
          }
        }
      }

      const requestPayload = {
        action: "download",
        url: downloadItem.url,
        referrer: downloadItem.referrer || undefined,
        fileName: downloadItem.filename || undefined,
        cookies: cookies
      };

      // Contact native messaging host
      chrome.runtime.sendNativeMessage(NATIVE_HOST, requestPayload, (response) => {
        if (chrome.runtime.lastError) {
          console.warn("[YDM] Native host unreachable:", chrome.runtime.lastError.message);
          suggest(); // Let browser download proceed safely
          return;
        }

        if (response && response.status === "accepted") {
          // Cancel browser download only after YDM successfully accepted the job
          chrome.downloads.cancel(downloadItem.id, () => {
            // Clear from browser download shelf
            chrome.downloads.erase({ id: downloadItem.id });
          });
        } else {
          suggest();
        }
      });
    } catch (err) {
      console.error("[YDM] Interception error:", err);
      suggest();
    }
  });

  return true; // Keep suggest callback active asynchronously
});

function sendDownloadToYdm(payload) {
  chrome.runtime.sendNativeMessage(NATIVE_HOST, payload, (response) => {
    if (chrome.runtime.lastError) {
      console.warn("[YDM] Failed to dispatch download:", chrome.runtime.lastError.message);
    }
  });
}
