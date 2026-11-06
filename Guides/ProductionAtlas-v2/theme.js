/* Works on file URLs; unavailable browser storage never blocks the interface. */
(function () {
  'use strict';
  const key = 'production-atlas-v2-theme';
  let theme = 'light';
  try { if (localStorage.getItem(key) === 'dark') theme = 'dark'; } catch (_) {}
  document.documentElement.dataset.theme = theme;
  document.addEventListener('DOMContentLoaded', function () {
    const button = document.getElementById('theme-toggle');
    function sync() { button.setAttribute('aria-pressed', String(document.documentElement.dataset.theme === 'dark')); }
    button.addEventListener('click', function () {
      const next = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
      document.documentElement.dataset.theme = next;
      try { localStorage.setItem(key, next); } catch (_) {}
      sync();
    });
    sync();
  });
})();
