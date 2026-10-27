const NATIVE_HOST = "com.ydm.downloader";

document.addEventListener("DOMContentLoaded", () => {
  const statusEl = document.getElementById("status");
  const interceptToggle = document.getElementById("interceptToggle");
  const cookieToggle = document.getElementById("cookieToggle");

  // Load saved preferences
  chrome.storage.sync.get(["interceptionEnabled", "sendDomainCookies"], (res) => {
    interceptToggle.checked = res.interceptionEnabled ?? true;
    cookieToggle.checked = res.sendDomainCookies ?? false;
  });

  // Toggle interception
  interceptToggle.addEventListener("change", () => {
    chrome.storage.sync.set({ interceptionEnabled: interceptToggle.checked });
  });

  // Toggle domain cookies (requests optional permission if enabled)
  cookieToggle.addEventListener("change", async () => {
    if (cookieToggle.checked) {
      const granted = await chrome.permissions.request({ permissions: ["cookies"] });
      if (!granted) {
        cookieToggle.checked = false;
        return;
      }
    }
    chrome.storage.sync.set({ sendDomainCookies: cookieToggle.checked });
  });

  // Ping native host to check if YDM is online
  try {
    chrome.runtime.sendNativeMessage(NATIVE_HOST, { action: "ping" }, (response) => {
      if (chrome.runtime.lastError || !response || response.status !== "ok") {
        statusEl.className = "status-badge status-offline";
        statusEl.textContent = "YDM Desktop App: Disconnected";
      } else {
        statusEl.className = "status-badge status-online";
        statusEl.textContent = "YDM Desktop App: Active";
      }
    });
  } catch (err) {
    statusEl.className = "status-badge status-offline";
    statusEl.textContent = "Native Host Missing";
  }
});
