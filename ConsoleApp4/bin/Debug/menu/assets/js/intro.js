window.addEventListener('load', () => {
  setTimeout(() => {
    const intro = document.getElementById('intro-overlay');
    if (intro) intro.remove();
  }, 4000); // after animation completes
});