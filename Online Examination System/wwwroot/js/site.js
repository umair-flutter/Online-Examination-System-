// ---------- Theme (light / dark) ----------
(function () {
    function apply(theme) {
        document.documentElement.setAttribute('data-bs-theme', theme);
        document.querySelectorAll('#themeToggle i').forEach(function (i) {
            i.className = theme === 'dark' ? 'bi bi-sun' : 'bi bi-moon-stars';
        });
    }
    var current = document.documentElement.getAttribute('data-bs-theme') || 'light';
    apply(current);
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('#themeToggle');
        if (!btn) return;
        current = current === 'dark' ? 'light' : 'dark';
        try { localStorage.setItem('theme', current); } catch (err) { }
        apply(current);
        document.dispatchEvent(new CustomEvent('themechange', { detail: current }));
    });
})();

// ---------- Confirm dialogs: <button data-confirm="Are you sure?"> ----------
document.addEventListener('click', function (e) {
    var el = e.target.closest('[data-confirm]');
    if (el && !confirm(el.getAttribute('data-confirm'))) { e.preventDefault(); e.stopPropagation(); }
}, true);

// ---------- Notification badge ----------
(function () {
    var badge = document.getElementById('notifBadge');
    if (!badge) return;
    function refresh() {
        fetch('/api/notifications/unread', { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) {
                if (!d) return;
                badge.textContent = d.count > 99 ? '99+' : d.count;
                badge.classList.toggle('d-none', d.count === 0);
            }).catch(function () { });
    }
    refresh();
    setInterval(refresh, 60000);
})();

// ---------- Auto-submit selects in filter bars: <select data-autosubmit> ----------
document.addEventListener('change', function (e) {
    if (e.target.matches('[data-autosubmit]')) e.target.form.submit();
});

// ---------- Chart.js defaults helper ----------
window.chartColors = ['#4f46e5', '#22c55e', '#f59e0b', '#ef4444', '#06b6d4', '#a855f7', '#ec4899', '#84cc16'];
window.applyChartTheme = function () {
    if (!window.Chart) return;
    var dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
    Chart.defaults.color = dark ? '#cbd5e1' : '#475569';
    Chart.defaults.borderColor = dark ? 'rgba(255,255,255,.08)' : 'rgba(0,0,0,.08)';
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
};

// ---------- Show / hide password: <button data-toggle-password="inputId"> ----------
document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-toggle-password]');
    if (!b) return;
    var input = document.getElementById(b.getAttribute('data-toggle-password'));
    if (input) input.type = input.type === 'password' ? 'text' : 'password';
});
