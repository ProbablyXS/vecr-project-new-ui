document.addEventListener("DOMContentLoaded", () => {
  const toggle = document.getElementById("theme-toggle");
  const themeToggle = document.getElementById('theme-toggle');
  const icon = themeToggle.querySelector('svg');
  const body = document.body;

  const savedTheme = localStorage.getItem("theme");
  if (savedTheme === "light") {
    body.classList.add("light-theme");
    icon.classList.toggle('active');
  }

  toggle.addEventListener("click", () => {
    body.classList.toggle("light-theme");

    // Sauvegarder
    if (body.classList.contains("light-theme")) {
      localStorage.setItem("theme", "light");
    } else {
      localStorage.setItem("theme", "dark");
    }
  });
});