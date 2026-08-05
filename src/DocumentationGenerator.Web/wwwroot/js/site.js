document.querySelectorAll("[data-loading-form]").forEach((form) => {
  form.addEventListener("submit", () => {
    if (!form.checkValidity()) return;
    const overlay = document.getElementById("loadingOverlay");
    const message = document.getElementById("loadingMessage");
    if (message) message.textContent = form.dataset.loadingMessage || "Working…";
    if (overlay) {
      overlay.classList.add("is-visible");
      overlay.setAttribute("aria-hidden", "false");
    }
  });
});
