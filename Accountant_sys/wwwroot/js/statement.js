/* ==========================================================================
   Accountant_sys — Account Statement screen behaviour
   Sticky toolbar, mode switch, expand/collapse, live filters, copy, theme.
   Print/PDF is handled by the browser print dialog and statement.css.
   ========================================================================== */
(function () {
    'use strict';

    var THEME_KEY = 'st-statement-theme';
    var MODE_KEY = 'st-statement-mode';
    var toastTimer = null;

    function showError() {
        var panel = document.getElementById('stErrorPanel');
        if (panel) panel.hidden = false;
    }

    function init() {
        var page = document.getElementById('statementPage');
        if (!page) return;

        var toolbar = document.getElementById('stToolbar');
        var search = document.getElementById('stSearch');
        var kindSelect = document.getElementById('stKind');
        var countEl = document.getElementById('stVisibleCount');
        var emptyState = document.getElementById('stEmptyState');
        var clearFilters = document.getElementById('stClearFilters');
        var toast = document.getElementById('stToast');
        var loading = document.getElementById('stLoading');
        var modeField = document.getElementById('stModeField');
        var groups = Array.prototype.slice.call(page.querySelectorAll('tbody.st-mv-group'));
        var dayHeads = Array.prototype.slice.call(page.querySelectorAll('tbody.st-day-head'));

        /* ----- Sticky offset below the app navbar ----- */
        function syncNavbarHeight() {
            var navbar = document.querySelector('.top-navbar');
            var height = navbar ? navbar.getBoundingClientRect().height : 64;
            document.documentElement.style.setProperty('--st-navbar-h', Math.round(height + 6) + 'px');
        }
        syncNavbarHeight();
        window.addEventListener('resize', syncNavbarHeight);

        /* ----- Toast ----- */
        function showToast(message) {
            if (!toast) return;
            toast.textContent = message;
            toast.classList.add('is-visible');
            if (toastTimer) window.clearTimeout(toastTimer);
            toastTimer = window.setTimeout(function () {
                toast.classList.remove('is-visible');
            }, 2200);
        }

        /* ----- Detailed / summary mode ----- */
        function applyMode(mode, persist) {
            var summary = mode === 'summary';
            page.classList.toggle('is-summary', summary);
            page.classList.toggle('is-detailed', !summary);
            page.setAttribute('data-mode', summary ? 'summary' : 'detailed');
            if (modeField) modeField.value = summary ? 'summary' : 'detailed';
            page.querySelectorAll('[data-st-mode]').forEach(function (button) {
                button.setAttribute('aria-pressed', String(button.getAttribute('data-st-mode') === mode));
            });
            if (persist) {
                try { window.localStorage.setItem(MODE_KEY, mode); } catch (e) { /* private mode */ }
            }
        }

        var initialMode = page.getAttribute('data-mode') === 'summary' ? 'summary' : 'detailed';
        var urlHasMode = false;
        try {
            urlHasMode = new URLSearchParams(window.location.search).has('mode');
        } catch (e) { /* ignore */ }

        if (!urlHasMode) {
            try {
                var storedMode = window.localStorage.getItem(MODE_KEY);
                if (storedMode === 'summary' || storedMode === 'detailed') initialMode = storedMode;
            } catch (e) { /* ignore */ }
        }
        applyMode(initialMode, false);

        page.querySelectorAll('[data-st-mode]').forEach(function (button) {
            button.addEventListener('click', function () {
                applyMode(button.getAttribute('data-st-mode'), true);
            });
        });

        /* ----- Expand / collapse individual movements ----- */
        page.addEventListener('click', function (event) {
            var toggle = event.target.closest('.st-toggle');
            if (!toggle) return;
            var group = toggle.closest('tbody.st-mv-group');
            if (!group) return;
            var collapsed = group.classList.toggle('is-collapsed');
            toggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
            toggle.textContent = collapsed ? '◂' : '▾';
        });

        function setAllDetails(expand) {
            groups.forEach(function (group) {
                if (!group.querySelector('.st-sub')) return;
                group.classList.toggle('is-collapsed', !expand);
                var toggle = group.querySelector('.st-toggle');
                if (toggle) {
                    toggle.setAttribute('aria-expanded', expand ? 'true' : 'false');
                    toggle.textContent = expand ? '▾' : '◂';
                }
            });
        }

        var expandAll = document.getElementById('stExpandAll');
        var collapseAll = document.getElementById('stCollapseAll');
        if (expandAll) expandAll.addEventListener('click', function () { setAllDetails(true); });
        if (collapseAll) collapseAll.addEventListener('click', function () { setAllDetails(false); });

        /* ----- Live filters ----- */
        function applyFilters() {
            var query = (search && search.value ? search.value : '').trim().toLowerCase();
            var kind = kindSelect ? kindSelect.value : 'all';
            var visible = 0;

            groups.forEach(function (group) {
                var text = (group.getAttribute('data-search') || '').toLowerCase();
                var groupKind = group.getAttribute('data-kind') || '';
                var matches = (kind === 'all' || groupKind === kind)
                    && (query === '' || text.indexOf(query) !== -1);
                group.hidden = !matches;
                if (matches) visible++;
            });

            dayHeads.forEach(function (head) {
                var day = head.getAttribute('data-day');
                var dayHasVisible = groups.some(function (group) {
                    return !group.hidden && group.getAttribute('data-day') === day;
                });
                head.hidden = !dayHasVisible;
            });

            if (countEl) {
                countEl.textContent = 'عرض ' + visible + ' من إجمالي ' + groups.length + ' حركة';
            }
            if (emptyState) {
                emptyState.hidden = !(visible === 0 && groups.length > 0);
            }
        }

        if (search) search.addEventListener('input', applyFilters);
        if (kindSelect) kindSelect.addEventListener('change', applyFilters);

        if (clearFilters) {
            clearFilters.addEventListener('click', function () {
                if (search) search.value = '';
                if (kindSelect) kindSelect.value = 'all';
                applyFilters();
                if (search) search.focus();
            });
        }

        applyFilters();

        /* ----- Copy document numbers ----- */
        function fallbackCopy(text) {
            var area = document.createElement('textarea');
            area.value = text;
            area.setAttribute('readonly', '');
            area.style.position = 'fixed';
            area.style.opacity = '0';
            document.body.appendChild(area);
            area.select();
            var ok = false;
            try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
            document.body.removeChild(area);
            return ok;
        }

        page.addEventListener('click', function (event) {
            var copyButton = event.target.closest('[data-copy]');
            if (!copyButton) return;
            var text = copyButton.getAttribute('data-copy') || '';
            if (!text) return;

            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text)
                    .then(function () { showToast('تم نسخ: ' + text); })
                    .catch(function () {
                        showToast(fallbackCopy(text) ? 'تم نسخ: ' + text : 'تعذّر النسخ');
                    });
            } else {
                showToast(fallbackCopy(text) ? 'تم نسخ: ' + text : 'تعذّر النسخ');
            }
        });

        /* ----- Insight chips ----- */
        page.addEventListener('click', function (event) {
            var chip = event.target.closest('[data-scroll-to]');
            if (!chip) return;
            var targetId = chip.getAttribute('data-scroll-to');
            var target = document.getElementById(targetId);
            if (!target && targetId === 'lastPaymentRow') {
                target = page.querySelector('[data-last-payment="true"]');
            }
            if (!target) return;

            var group = target.closest ? target.closest('tbody.st-mv-group') : null;
            if (group) {
                group.classList.remove('is-collapsed');
                var toggle = group.querySelector('.st-toggle');
                if (toggle) {
                    toggle.setAttribute('aria-expanded', 'true');
                    toggle.textContent = '▾';
                }
            }

            target.scrollIntoView({ behavior: 'smooth', block: 'center' });
            target.setAttribute('tabindex', '-1');
            target.focus({ preventScroll: true });
        });

        /* ----- Jump from an unpaid invoice to its movement row ----- */
        page.addEventListener('click', function (event) {
            var jump = event.target.closest('[data-jump-invoice]');
            if (!jump) return;
            var id = jump.getAttribute('data-jump-invoice');
            var row = document.getElementById('inv-row-' + id);
            if (!row) return;

            var group = row.closest('tbody.st-mv-group');
            if (group) {
                group.classList.remove('is-collapsed');
                group.hidden = false;
                if (kindSelect) kindSelect.value = 'all';
                if (search) search.value = '';
                applyFilters();
            }

            row.scrollIntoView({ behavior: 'smooth', block: 'center' });
            row.classList.add('st-highlight');
            window.setTimeout(function () { row.classList.remove('st-highlight'); }, 1600);
        });

        /* ----- Theme toggle (persisted, print always light) ----- */
        var themeButton = document.getElementById('stThemeToggle');
        var media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

        function effectiveTheme() {
            var explicit = document.documentElement.getAttribute('data-st-theme');
            if (explicit === 'dark' || explicit === 'light') return explicit;
            return media && media.matches ? 'dark' : 'light';
        }

        function applyTheme(theme, persist) {
            document.documentElement.setAttribute('data-st-theme', theme);
            document.documentElement.classList.toggle('st-dark-host', theme === 'dark');
            if (themeButton) {
                themeButton.setAttribute('aria-pressed', String(theme === 'dark'));
                themeButton.setAttribute('title', theme === 'dark' ? 'الوضع النهاري' : 'الوضع الليلي');
                var icon = themeButton.querySelector('i');
                if (icon) icon.className = theme === 'dark' ? 'bi bi-sun' : 'bi bi-moon-stars';
            }
            if (persist) {
                try { window.localStorage.setItem(THEME_KEY, theme); } catch (e) { /* ignore */ }
            }
        }

        var storedTheme = null;
        try { storedTheme = window.localStorage.getItem(THEME_KEY); } catch (e) { /* ignore */ }
        if (storedTheme === 'dark' || storedTheme === 'light') {
            applyTheme(storedTheme, false);
        } else {
            applyTheme(effectiveTheme(), false);
        }

        if (themeButton) {
            themeButton.addEventListener('click', function () {
                applyTheme(effectiveTheme() === 'dark' ? 'light' : 'dark', true);
            });
        }

        /* ----- Print / export ----- */
        var printButton = document.getElementById('stPrint');
        if (printButton) {
            printButton.addEventListener('click', function () { window.print(); });
        }

        var exportButton = document.getElementById('stExport');
        if (exportButton) {
            exportButton.addEventListener('click', function () {
                var tableId = exportButton.getAttribute('data-export-table');
                var filename = exportButton.getAttribute('data-export-filename') || 'statement';
                if (typeof window.exportTableToCSV === 'function') {
                    window.exportTableToCSV(tableId, filename);
                }
            });
        }

        /* ----- Loading state on server navigation ----- */
        var filterForm = document.getElementById('stFilterForm');
        if (filterForm) {
            filterForm.addEventListener('submit', function () {
                if (loading) loading.hidden = false;
            });
        }
        page.querySelectorAll('.st-date-chip, .st-toolbar a[href]').forEach(function (link) {
            link.addEventListener('click', function () {
                if (loading && link.getAttribute('aria-disabled') !== 'true') loading.hidden = false;
            });
        });

        var retry = document.getElementById('stRetry');
        if (retry) retry.addEventListener('click', function () { window.location.reload(); });
    }

    document.addEventListener('DOMContentLoaded', function () {
        try {
            init();
        } catch (error) {
            showError();
            if (window.console && window.console.error) window.console.error(error);
        }
    });
})();
