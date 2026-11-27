// Thème clair/sombre : suit le système, ou le choix mémorisé (appliqué avant l'affichage).
(function () {
  var root = document.documentElement;
  function stored() { try { return localStorage.getItem('stackr-sql-theme'); } catch (e) { return null; } }
  function system() { return window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'; }
  function apply(t) { root.dataset.theme = t; }
  apply(stored() || system());
  if (window.matchMedia) {
    matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () { if (!stored()) apply(system()); });
  }
  document.addEventListener('click', function (ev) {
    var btn = ev.target.closest && ev.target.closest('[data-theme-toggle]');
    if (!btn) return;
    var next = root.dataset.theme === 'dark' ? 'light' : 'dark';
    root.classList.add('theming');
    apply(next);
    try { localStorage.setItem('stackr-sql-theme', next); } catch (e) { /* stockage indisponible */ }
    setTimeout(function () { root.classList.remove('theming'); }, 260);
  });
})();
