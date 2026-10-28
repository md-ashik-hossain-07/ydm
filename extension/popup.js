const NATIVE_HOST = "com.ydm.downloader";

document.addEventListener("DOMContentLoaded", async () => {
  const statusEl = document.getElementById("status");
  const troubleshootEl = document.getElementById("troubleshoot");
  const modeSelect = document.getElementById("modeSelect");
  const cookieToggle = document.getElementById("cookieToggle");
  const mediaContainer = document.getElementById("mediaContainer");

  // Load preferences
  chrome.storage.sync.get(["interceptionMode", "interceptionEnabled", "sendDomainCookies"], (res) => {
    let mode = res.interceptionMode;
    if (!mode) {
      mode = res.interceptionEnabled === false ? "manual" : "automatic";
    }
    modeSelect.value = mode;
    cookieToggle.checked = res.sendDomainCookies ?? false;
  });

  modeSelect.addEventListener("change", () => {
    chrome.storage.sync.set({ 
      interceptionMode: modeSelect.value,
      interceptionEnabled: modeSelect.value !== "manual"
    });
  });

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

  // Check connection to native host
  try {
    chrome.runtime.sendNativeMessage(NATIVE_HOST, { action: "ping" }, (response) => {
      if (chrome.runtime.lastError || !response || response.status !== "ok") {
        statusEl.className = "status-badge status-offline";
        statusEl.textContent = "YDM Desktop: Disconnected";
        troubleshootEl.style.display = "block";
      } else {
        statusEl.className = "status-badge status-online";
        statusEl.textContent = "YDM Desktop: Active";
        troubleshootEl.style.display = "none";
      }
    });
  } catch (err) {
    statusEl.className = "status-badge status-offline";
    statusEl.textContent = "Native Host Missing";
    troubleshootEl.style.display = "block";
  }

  // Query active tab media
  const [activeTab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!activeTab || !activeTab.id) {
    mediaContainer.innerHTML = `<div style="font-size:11px;color:#888;">No active browser tab found.</div>`;
    return;
  }

  const tabUrl = activeTab.url || "";

  // Check for YouTube / DRM platforms
  if (tabUrl.includes("youtube.com/watch") || tabUrl.includes("youtu.be/")) {
    mediaContainer.innerHTML = `
      <div class="notice-box">
        <strong>YouTube Notice:</strong> Media streams on YouTube utilize dynamic client-side cipher signatures. Direct media interception is not supported. Use direct media download pages.
      </div>`;
    return;
  }

  // Request detected media from background
  chrome.runtime.sendMessage({ type: "GET_MEDIA", tabId: activeTab.id }, (response) => {
    const list = response && response.media ? response.media : [];

    if (list.length === 0) {
      mediaContainer.innerHTML = `<div style="font-size:11px;color:#888;padding:4px 0;">No media elements detected on this page.</div>`;
      return;
    }

    mediaContainer.innerHTML = "";
    list.forEach((item, idx) => {
      const card = document.createElement("div");
      card.className = "media-card";

      const titleEl = document.createElement("div");
      titleEl.className = "media-title";
      titleEl.textContent = item.title || `Media Stream ${idx + 1}`;
      titleEl.title = item.url;

      const typeEl = document.createElement("div");
      typeEl.className = "media-type";
      typeEl.textContent = item.type || "Media";

      const btn = document.createElement("button");
      btn.className = "download-btn";
      btn.textContent = "⬇ Download with YDM";
      btn.addEventListener("click", () => {
        btn.textContent = "✔ Dispatched to YDM";
        btn.disabled = true;
        chrome.runtime.sendMessage({
          type: "DOWNLOAD_MEDIA",
          url: item.url,
          referrer: activeTab.url,
          fileName: item.title ? `${item.title.substring(0, 40)}` : undefined
        });
      });

      card.appendChild(titleEl);
      card.appendChild(typeEl);
      card.appendChild(btn);
      mediaContainer.appendChild(card);
    });
  });
});