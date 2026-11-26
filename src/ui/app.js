(() => {
  const webview = window.chrome?.webview;

  const state = {
    launcherVersion: "0.2.0",
    runtimeVersion: "",
    settings: {
      gameExe: "",
      officialLauncher: "",
      runtimeDirectory: "",
      enableMcp: false,
      staticBackground: false,
      wallpaperSource: "video"
    },
    wallpaper: {
      hasVideo: false,
      hasImage: false,
      videoUrl: "",
      imageUrl: ""
    },
    busy: false,
    canCancel: false,
    menuOpen: false,
    modalOpen: false,
    draftWallpaperSource: "video",
    toastTimer: null
  };

  const els = {
    app: document.getElementById("app"),
    bgVideo: document.getElementById("bg-video"),
    bgImage: document.getElementById("bg-image"),
    bgToggleBtn: document.getElementById("bg-toggle-btn"),
    bgTooltip: document.getElementById("bg-tooltip"),
    iconPause: document.getElementById("icon-pause"),
    iconPlay: document.getElementById("icon-play"),
    settingsBtn: document.getElementById("settings-btn"),
    minimizeBtn: document.getElementById("minimize-btn"),
    closeBtn: document.getElementById("close-btn"),
    startBtn: document.getElementById("start-btn"),
    startLetter: document.getElementById("start-letter"),
    startSpinner: document.getElementById("start-spinner"),
    startLabel: document.getElementById("start-label"),
    cancelBtn: document.getElementById("cancel-btn"),
    launcherVersion: document.getElementById("launcher-version"),
    menuBtn: document.getElementById("menu-btn"),
    moreMenu: document.getElementById("more-menu"),
    openModsBtn: document.getElementById("open-mods-btn"),
    openLogsBtn: document.getElementById("open-logs-btn"),
    statusToast: document.getElementById("status-toast"),
    statusStage: document.getElementById("status-stage"),
    statusDetail: document.getElementById("status-detail"),
    statusMessage: document.getElementById("status-message"),
    toastActionBtn: document.getElementById("toast-action-btn"),
    toastDismissBtn: document.getElementById("toast-dismiss-btn"),
    settingsModal: document.getElementById("settings-modal"),
    settingsCloseBtn: document.getElementById("settings-close-btn"),
    settingsCancelBtn: document.getElementById("settings-cancel-btn"),
    settingsSaveBtn: document.getElementById("settings-save-btn"),
    modalRuntimeBadge: document.getElementById("modal-runtime-badge"),
    inputGamePath: document.getElementById("input-game-path"),
    inputOfficialPath: document.getElementById("input-official-path"),
    inputRuntimePath: document.getElementById("input-runtime-path"),
    inputEnableMcp: document.getElementById("input-enable-mcp"),
    wallpaperSourceGroup: document.getElementById("wallpaper-source-group"),
    wpVideoBtn: document.getElementById("wp-video-btn"),
    wpImageBtn: document.getElementById("wp-image-btn"),
    settingsError: document.getElementById("settings-error")
  };

  function post(payload) {
    webview?.postMessage(payload);
  }

  function applyWallpaper() {
    const { hasVideo, hasImage, videoUrl, imageUrl } = state.wallpaper;
    const preferImage = state.settings.wallpaperSource === "image";
    const useVideo = hasVideo && (!preferImage || !hasImage);
    const useImage = !useVideo && hasImage;

    if (useVideo) {
      els.bgImage.classList.add("hidden");
      els.bgVideo.classList.remove("hidden");
      if (els.bgVideo.getAttribute("src") !== videoUrl) {
        els.bgVideo.setAttribute("src", videoUrl);
        els.bgVideo.load();
      }
      if (state.settings.staticBackground) {
        if (els.bgVideo.readyState >= 2) {
          els.bgVideo.pause();
        } else {
          const freezeFirstFrame = () => {
            els.bgVideo.pause();
            els.bgVideo.removeEventListener("loadeddata", freezeFirstFrame);
          };
          els.bgVideo.addEventListener("loadeddata", freezeFirstFrame);
        }
      } else {
        els.bgVideo.play().catch(() => {});
      }
    } else if (useImage) {
      els.bgVideo.pause();
      els.bgVideo.classList.add("hidden");
      if (els.bgImage.getAttribute("src") !== imageUrl) {
        els.bgImage.setAttribute("src", imageUrl);
      }
      els.bgImage.classList.remove("hidden");
    } else {
      els.bgVideo.pause();
      els.bgVideo.classList.add("hidden");
      els.bgImage.classList.add("hidden");
    }

    const isStatic = !useVideo || state.settings.staticBackground;
    els.bgToggleBtn.classList.toggle("hidden", !hasVideo);
    els.iconPause.classList.toggle("hidden", isStatic);
    els.iconPlay.classList.toggle("hidden", !isStatic);
    els.bgTooltip.textContent = isStatic ? "Switch to Live Background" : "Switch to Static Background";
  }

  function setMenuOpen(open) {
    state.menuOpen = open;
    els.moreMenu.classList.toggle("hidden", !open);
    els.menuBtn.classList.toggle("active", open);
    els.menuBtn.setAttribute("aria-expanded", String(open));
  }

  function updateDraftWallpaperButtons() {
    els.wpVideoBtn.classList.toggle("active", state.draftWallpaperSource === "video");
    els.wpImageBtn.classList.toggle("active", state.draftWallpaperSource === "image");
  }

  function openSettingsModal() {
    if (state.busy) return;
    setMenuOpen(false);
    state.modalOpen = true;
    els.inputGamePath.value = state.settings.gameExe || "";
    els.inputOfficialPath.value = state.settings.officialLauncher || "";
    els.inputRuntimePath.value = state.settings.runtimeDirectory || "";
    els.inputEnableMcp.checked = Boolean(state.settings.enableMcp);
    state.draftWallpaperSource = state.settings.wallpaperSource === "image" ? "image" : "video";
    updateDraftWallpaperButtons();

    const canSwitchAsset = state.wallpaper.hasVideo && state.wallpaper.hasImage;
    els.wallpaperSourceGroup.classList.toggle("hidden", !canSwitchAsset);

    if (state.runtimeVersion) {
      els.modalRuntimeBadge.textContent = state.runtimeVersion;
      els.modalRuntimeBadge.classList.remove("hidden");
    } else {
      els.modalRuntimeBadge.classList.add("hidden");
    }

    els.settingsError.textContent = "";
    els.settingsError.classList.add("hidden");
    els.settingsModal.classList.remove("hidden");
  }

  function closeSettingsModal() {
    state.modalOpen = false;
    els.settingsModal.classList.add("hidden");
  }

  function applyStatus(status) {
    if (!status) return;
    clearTimeout(state.toastTimer);
    state.busy = Boolean(status.busy);
    state.canCancel = Boolean(status.canCancel);

    els.startBtn.disabled = state.busy;
    els.settingsBtn.disabled = state.busy;
    els.startLetter.classList.toggle("hidden", state.busy);
    els.startSpinner.classList.toggle("hidden", !state.busy);

    if (state.busy) {
      els.startLabel.textContent = status.stage ? `${status.stage}…` : "Starting…";
    } else {
      els.startLabel.textContent = "Start Game";
    }

    els.cancelBtn.classList.toggle("hidden", !state.busy);
    els.cancelBtn.disabled = !state.canCancel;

    const kind = status.kind || "idle";
    if (kind === "idle" || (!status.stage && !status.message)) {
      els.statusToast.classList.add("hidden");
      return;
    }

    els.statusToast.className = `status-toast kind-${kind}`;
    els.statusStage.textContent = status.stage || "";
    els.statusMessage.textContent = status.message || "";

    if (status.detail) {
      els.statusDetail.textContent = status.detail;
      els.statusDetail.classList.remove("hidden");
    } else {
      els.statusDetail.classList.add("hidden");
    }

    els.toastDismissBtn.classList.toggle("hidden", state.busy);

    if (!state.busy && status.configError) {
      els.toastActionBtn.textContent = "Settings";
      els.toastActionBtn.dataset.action = "settings";
      els.toastActionBtn.classList.remove("hidden");
    } else if (!state.busy && kind === "error") {
      els.toastActionBtn.textContent = "Open Logs";
      els.toastActionBtn.dataset.action = "logs";
      els.toastActionBtn.classList.remove("hidden");
    } else {
      els.toastActionBtn.classList.add("hidden");
      delete els.toastActionBtn.dataset.action;
    }

    if (!state.busy && kind === "success") {
      state.toastTimer = setTimeout(() => {
        if (!state.busy) {
          els.statusToast.classList.add("hidden");
        }
      }, 6500);
    }
  }

  // Native window drag on empty/background areas
  document.addEventListener("mousedown", (event) => {
    if (event.button !== 0) return;
    if (state.modalOpen) return;
    const interactive = event.target.closest(
      "button, input, a, .more-menu, .modal-card, .status-toast, .floating-bubble"
    );
    if (interactive) return;
    if (state.menuOpen) {
      setMenuOpen(false);
    }
    post({ type: "dragWindow" });
  });

  // Close popover menu when clicking outside
  document.addEventListener("click", (event) => {
    if (!state.menuOpen) return;
    if (!event.target.closest(".menu-anchor")) {
      setMenuOpen(false);
    }
  });

  // Escape key closes modal or popover menu
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") return;
    if (state.modalOpen) {
      closeSettingsModal();
    } else if (state.menuOpen) {
      setMenuOpen(false);
    }
  });

  // Top-right controls
  els.bgToggleBtn.addEventListener("click", () => {
    if (!state.wallpaper.hasVideo) return;
    if (state.settings.wallpaperSource === "image") {
      state.settings.wallpaperSource = "video";
      state.settings.staticBackground = false;
    } else {
      state.settings.staticBackground = !state.settings.staticBackground;
    }
    applyWallpaper();
    post({
      type: "setBackgroundPreference",
      staticBackground: state.settings.staticBackground,
      wallpaperSource: state.settings.wallpaperSource
    });
  });

  els.settingsBtn.addEventListener("click", openSettingsModal);
  els.minimizeBtn.addEventListener("click", () => post({ type: "minimize" }));
  els.closeBtn.addEventListener("click", () => post({ type: "close" }));

  // Launch controls
  els.startBtn.addEventListener("click", () => {
    if (state.busy) return;
    setMenuOpen(false);
    post({ type: "start" });
  });

  els.cancelBtn.addEventListener("click", () => {
    if (!state.canCancel) return;
    post({ type: "cancel" });
  });

  // Three-bar menu
  els.menuBtn.addEventListener("click", (event) => {
    event.stopPropagation();
    setMenuOpen(!state.menuOpen);
  });

  els.openModsBtn.addEventListener("click", () => {
    setMenuOpen(false);
    post({ type: "openMods" });
  });

  els.openLogsBtn.addEventListener("click", () => {
    setMenuOpen(false);
    post({ type: "openLogs" });
  });

  // Toast actions
  els.toastActionBtn.addEventListener("click", () => {
    const action = els.toastActionBtn.dataset.action;
    if (action === "settings") {
      openSettingsModal();
    } else if (action === "logs") {
      post({ type: "openLogs" });
    }
  });

  els.toastDismissBtn.addEventListener("click", () => {
    if (!state.busy) {
      els.statusToast.classList.add("hidden");
    }
  });

  // Settings modal events
  els.settingsCloseBtn.addEventListener("click", closeSettingsModal);
  els.settingsCancelBtn.addEventListener("click", closeSettingsModal);
  els.settingsModal.addEventListener("click", (event) => {
    if (event.target === els.settingsModal) {
      closeSettingsModal();
    }
  });

  document.querySelectorAll("[data-browse]").forEach((btn) => {
    btn.addEventListener("click", () => {
      post({ type: "browse", field: btn.dataset.browse });
    });
  });

  [els.wpVideoBtn, els.wpImageBtn].forEach((btn) => {
    btn.addEventListener("click", () => {
      state.draftWallpaperSource = btn.dataset.wallpaper;
      updateDraftWallpaperButtons();
    });
  });

  els.settingsSaveBtn.addEventListener("click", () => {
    const switchingToVideo = state.settings.wallpaperSource !== "video" && state.draftWallpaperSource === "video";
    const nextStatic = state.draftWallpaperSource === "image"
      ? true
      : (switchingToVideo ? false : state.settings.staticBackground);
    post({
      type: "saveSettings",
      gameExe: els.inputGamePath.value.trim(),
      officialLauncher: els.inputOfficialPath.value.trim(),
      runtimeDirectory: els.inputRuntimePath.value.trim(),
      enableMcp: els.inputEnableMcp.checked,
      wallpaperSource: state.draftWallpaperSource,
      staticBackground: nextStatic
    });
  });

  // Host -> UI messages
  webview?.addEventListener("message", (event) => {
    const msg = event.data;
    if (!msg || !msg.type) return;

    switch (msg.type) {
      case "init":
        state.launcherVersion = msg.launcherVersion || "0.2.0";
        state.runtimeVersion = msg.runtimeVersion || "";
        if (msg.settings) state.settings = { ...state.settings, ...msg.settings };
        if (msg.wallpaper) state.wallpaper = { ...state.wallpaper, ...msg.wallpaper };
        els.launcherVersion.textContent = `Launcher v${state.launcherVersion}`;
        applyWallpaper();
        applyStatus(msg.status);
        break;

      case "status":
        applyStatus(msg);
        break;

      case "browseResult":
        if (msg.field === "game") els.inputGamePath.value = msg.path || "";
        if (msg.field === "official") els.inputOfficialPath.value = msg.path || "";
        if (msg.field === "runtime") els.inputRuntimePath.value = msg.path || "";
        break;

      case "settingsResult":
        if (msg.runtimeVersion !== undefined) state.runtimeVersion = msg.runtimeVersion;
        if (msg.ok) {
          if (msg.settings) state.settings = { ...state.settings, ...msg.settings };
          applyWallpaper();
          closeSettingsModal();
        } else {
          els.settingsError.textContent = msg.error || "Failed to save settings. Please verify the paths.";
          els.settingsError.classList.remove("hidden");
        }
        break;
    }
  });

  // Prevent browser drag-and-drop navigation
  window.addEventListener("dragover", (e) => e.preventDefault());
  window.addEventListener("drop", (e) => e.preventDefault());

  // Signal host that DOM is ready
  post({ type: "ready" });
})();
