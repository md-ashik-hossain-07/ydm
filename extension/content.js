// YDM Video & Media Detector Content Script
(function () {
  function scanMedia() {
    const media = [];
    const elements = document.querySelectorAll("video, audio");
    elements.forEach((el, index) => {
      let src = el.currentSrc || el.src;
      if (!src) {
        const source = el.querySelector("source");
        if (source) src = source.src;
      }

      if (src && (src.startsWith("http://") || src.startsWith("https://"))) {
        const ext = src.split("?")[0].split(".").pop().toLowerCase();
        const type = el.tagName.toLowerCase() === "video" ? "Video" : "Audio";
        const title = (document.title || "").trim() || `${type} ${index + 1}`;
        media.push({
          url: src,
          type: ext ? `${type} (.${ext})` : type,
          title: title
        });
      }
    });

    // Detect direct media links on page
    const links = document.querySelectorAll("a[href]");
    links.forEach(a => {
      const href = a.href;
      if (href && /\.(mp4|mkv|webm|mp3|wav|m4a|m3u8)(\?.*)?$/i.test(href)) {
        if (!media.some(m => m.url === href)) {
          media.push({
            url: href,
            type: "Direct Media Link",
            title: (a.innerText || "").trim() || a.getAttribute("download") || document.title
          });
        }
      }
    });

    if (media.length > 0) {
      try {
        chrome.runtime.sendMessage({
          type: "MEDIA_DETECTED",
          media: media
        }).catch(() => {});
      } catch {}
    }
  }

  // Scan immediately and on media events
  scanMedia();
  document.addEventListener("play", scanMedia, true);
  document.addEventListener("loadeddata", scanMedia, true);

  // Periodic scan for dynamically rendered HTML5 players
  setInterval(scanMedia, 4000);
})();