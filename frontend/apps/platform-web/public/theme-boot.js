// Applies the saved theme and colour palette before the app boots, so a reload never flashes the
// default first. Mirrors ThemeStore in libs/core/layout/src/lib/theme.ts, which takes over once
// the shell starts. A file rather than an inline script: the CSP allows 'self' only.
(function () {
  let theme = null;
  let palette = null;
  try {
    theme = localStorage.getItem('gggs-theme');
    palette = localStorage.getItem('gggs-palette');
  } catch {
    // Blocked storage: fall through to the OS preference and the default palette.
  }
  if (theme !== 'light' && theme !== 'dark') {
    theme = window.matchMedia('(prefers-color-scheme: dark)').matches
      ? 'dark'
      : 'light';
  }
  if (theme === 'dark') {
    document.documentElement.classList.add('gggs-dark');
  }
  if (palette === 'brand-light-menu' || palette === 'classic') {
    document.documentElement.classList.add('gggs-palette-' + palette);
  }
})();
