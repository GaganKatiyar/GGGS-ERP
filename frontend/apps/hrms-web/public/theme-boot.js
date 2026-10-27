// Applies the saved theme before the app boots, so a dark-theme user never sees a light flash
// while Angular loads. Mirrors readInitialTheme() in libs/core/layout/src/lib/theme.ts, which
// takes over once the shell starts. A file rather than an inline script: the CSP allows 'self' only.
(function () {
  var theme;
  try {
    theme = localStorage.getItem('gggs-theme');
  } catch (e) {
    // Blocked storage: fall through to the OS preference.
  }
  if (theme !== 'light' && theme !== 'dark') {
    theme = window.matchMedia('(prefers-color-scheme: dark)').matches
      ? 'dark'
      : 'light';
  }
  if (theme === 'dark') {
    document.documentElement.classList.add('gggs-dark');
  }
})();
