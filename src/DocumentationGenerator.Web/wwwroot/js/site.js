(() => {
  const input = document.querySelector("[data-screenshot-input]");
  if (!input) return;

  const form = input.closest("form");
  const gallery = form?.querySelector("[data-screenshot-gallery]");
  const list = form?.querySelector("[data-screenshot-list]");
  const template = form?.querySelector("[data-screenshot-card-template]");
  const count = form?.querySelector("[data-screenshot-count]");
  const status = form?.querySelector("[data-screenshot-status]");
  const replacementInput = form?.querySelector("[data-screenshot-replacement]");
  if (!form || !gallery || !list || !template || !count || !replacementInput) return;

  const maxFiles = Number.parseInt(input.dataset.maxFiles || "10", 10);
  let items = [];
  let nextId = 1;
  let replacementId = null;
  let draggedId = null;

  try {
    new DataTransfer();
  } catch {
    setStatus("This browser supports basic multiple upload, but gallery organization is unavailable.", true);
    return;
  }

  function friendlyCaption(fileName) {
    const lastDot = fileName.lastIndexOf(".");
    const baseName = lastDot > 0 ? fileName.slice(0, lastDot) : fileName;
    const cleaned = baseName.replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim();
    return cleaned ? cleaned.charAt(0).toUpperCase() + cleaned.slice(1) : "Screenshot";
  }

  function formatFileSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  function setStatus(message, isError = false) {
    if (!status) return;
    status.textContent = message;
    status.classList.toggle("is-error", isError);
  }

  function createItem(file) {
    return {
      id: `screenshot-${nextId++}`,
      file,
      caption: friendlyCaption(file.name),
      captionWasEdited: false,
      previewUrl: URL.createObjectURL(file)
    };
  }

  function syncFiles() {
    const transfer = new DataTransfer();
    items.forEach((item) => transfer.items.add(item.file));
    input.files = transfer.files;
  }

  function focusControl(itemId, selector) {
    window.requestAnimationFrame(() => {
      const card = list.querySelector(`[data-screenshot-id="${itemId}"]`);
      const preferred = card?.querySelector(selector);
      if (preferred && !preferred.disabled) preferred.focus();
      else card?.querySelector("[data-screenshot-caption]")?.focus();
    });
  }

  function moveItem(itemId, targetIndex, focusSelector) {
    const currentIndex = items.findIndex((item) => item.id === itemId);
    if (currentIndex < 0 || targetIndex < 0 || targetIndex >= items.length || currentIndex === targetIndex) return;
    const [item] = items.splice(currentIndex, 1);
    items.splice(targetIndex, 0, item);
    syncFiles();
    render();
    setStatus(`Moved ${item.caption} to position ${targetIndex + 1}.`);
    if (focusSelector) focusControl(item.id, focusSelector);
  }

  function removeItem(itemId) {
    const index = items.findIndex((item) => item.id === itemId);
    if (index < 0) return;
    const [removed] = items.splice(index, 1);
    URL.revokeObjectURL(removed.previewUrl);
    syncFiles();
    render();
    setStatus(`Removed ${removed.file.name}.`);
    const nearest = items[Math.min(index, items.length - 1)];
    if (nearest) focusControl(nearest.id, "[data-screenshot-caption]");
    else window.requestAnimationFrame(() => input.focus());
  }

  function clearDropState() {
    list.querySelectorAll(".is-dragging, .is-drop-target").forEach((card) => {
      card.classList.remove("is-dragging", "is-drop-target");
    });
  }

  function render() {
    list.replaceChildren();
    count.textContent = String(items.length);
    gallery.hidden = items.length === 0;

    items.forEach((item, index) => {
      const card = template.content.firstElementChild.cloneNode(true);
      card.dataset.screenshotId = item.id;

      const preview = card.querySelector("[data-screenshot-preview]");
      preview.src = item.previewUrl;
      preview.alt = `Preview of ${item.file.name}`;
      card.querySelector("[data-screenshot-order]").textContent = String(index + 1);
      card.querySelector("[data-screenshot-filename]").textContent = item.file.name;
      card.querySelector("[data-screenshot-size]").textContent = formatFileSize(item.file.size);

      const captionInput = card.querySelector("[data-screenshot-caption]");
      captionInput.name = `ScreenshotCaptions[${index}]`;
      captionInput.value = item.caption;
      captionInput.setAttribute("aria-label", `Caption for ${item.file.name}`);
      captionInput.addEventListener("input", (event) => {
        item.caption = event.target.value;
        item.captionWasEdited = true;
      });

      const moveUp = card.querySelector("[data-screenshot-up]");
      const moveDown = card.querySelector("[data-screenshot-down]");
      moveUp.disabled = index === 0;
      moveDown.disabled = index === items.length - 1;
      moveUp.setAttribute("aria-label", `Move ${item.file.name} up`);
      moveDown.setAttribute("aria-label", `Move ${item.file.name} down`);
      moveUp.addEventListener("click", () => moveItem(item.id, index - 1, "[data-screenshot-up]"));
      moveDown.addEventListener("click", () => moveItem(item.id, index + 1, "[data-screenshot-down]"));

      const replace = card.querySelector("[data-screenshot-replace]");
      replace.setAttribute("aria-label", `Replace ${item.file.name}`);
      replace.addEventListener("click", () => {
        replacementId = item.id;
        replacementInput.value = "";
        replacementInput.click();
      });

      const remove = card.querySelector("[data-screenshot-remove]");
      remove.setAttribute("aria-label", `Remove ${item.file.name}`);
      remove.addEventListener("click", () => removeItem(item.id));

      const dragHandle = card.querySelector("[data-screenshot-drag-handle]");
      dragHandle.addEventListener("dragstart", (event) => {
        draggedId = item.id;
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", item.id);
        window.requestAnimationFrame(() => card.classList.add("is-dragging"));
      });
      card.addEventListener("dragover", (event) => {
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
        if (draggedId && draggedId !== item.id) card.classList.add("is-drop-target");
      });
      card.addEventListener("dragleave", () => card.classList.remove("is-drop-target"));
      card.addEventListener("drop", (event) => {
        event.preventDefault();
        const sourceId = draggedId || event.dataTransfer.getData("text/plain");
        clearDropState();
        if (!sourceId || sourceId === item.id) return;
        moveItem(sourceId, index);
      });
      dragHandle.addEventListener("dragend", () => {
        draggedId = null;
        clearDropState();
      });

      list.appendChild(card);
    });
  }

  input.addEventListener("change", () => {
    const selectedFiles = Array.from(input.files || []);
    if (selectedFiles.length === 0) return;

    const availableSlots = Math.max(0, maxFiles - items.length);
    selectedFiles.slice(0, availableSlots).forEach((file) => items.push(createItem(file)));
    syncFiles();
    render();

    const skipped = Math.max(0, selectedFiles.length - availableSlots);
    if (skipped > 0) {
      setStatus(`Only ${maxFiles} screenshots are allowed. ${skipped} file${skipped === 1 ? " was" : "s were"} not added.`, true);
    } else {
      setStatus(`${items.length} screenshot${items.length === 1 ? " is" : "s are"} ready. The order below will be used in the document.`);
    }
  });

  input.addEventListener("click", () => {
    input.value = "";
  });

  input.addEventListener("cancel", syncFiles);

  replacementInput.addEventListener("change", () => {
    const replacement = replacementInput.files?.[0];
    const item = items.find((candidate) => candidate.id === replacementId);
    if (!replacement || !item) return;

    const previousName = item.file.name;
    URL.revokeObjectURL(item.previewUrl);
    item.file = replacement;
    item.previewUrl = URL.createObjectURL(replacement);
    if (!item.captionWasEdited) item.caption = friendlyCaption(replacement.name);
    replacementId = null;
    replacementInput.value = "";
    syncFiles();
    render();
    setStatus(`Replaced ${previousName} with ${replacement.name}.`);
    focusControl(item.id, "[data-screenshot-replace]");
  });

  form.addEventListener("submit", syncFiles);
  window.addEventListener("beforeunload", () => {
    items.forEach((item) => URL.revokeObjectURL(item.previewUrl));
  }, { once: true });
})();

document.querySelectorAll("[data-loading-form]").forEach((form) => {
  form.addEventListener("submit", () => {
    if (!form.checkValidity()) return;
    const overlay = document.getElementById("loadingOverlay");
    const message = document.getElementById("loadingMessage");
    if (message) message.textContent = form.dataset.loadingMessage || "Working...";
    if (overlay) {
      overlay.classList.add("is-visible");
      overlay.setAttribute("aria-hidden", "false");
    }
  });
});
