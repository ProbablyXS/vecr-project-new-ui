document.addEventListener("DOMContentLoaded", () => {
  const year = new Date().getFullYear();
  const text = `© ${year} VECR PROJECT. All rights reserved.`;
  const copyrightEl = document.getElementById("copyright");
  if (copyrightEl) {
    copyrightEl.textContent = text;
  }
});
