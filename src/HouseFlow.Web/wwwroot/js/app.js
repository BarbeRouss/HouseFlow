// HouseFlow Blazor interop helpers: theme handling and small browser shims.
// Auth tokens are never stored here: the access token lives in memory
// (Auth/TokenStore.cs) and the session survives through the HttpOnly cookie.
(function () {
    const THEME_KEY = 'houseflow_theme';
    const darkQuery = window.matchMedia('(prefers-color-scheme: dark)');

    function storedTheme() {
        try {
            return localStorage.getItem(THEME_KEY) || 'system';
        } catch (e) {
            return 'system';
        }
    }

    // Resolves light | dark | system to the .dark / .light class on <html> (+ the browser UI colour).
    function paintTheme(theme) {
        const isDark = theme === 'dark' || (theme === 'system' && darkQuery.matches);
        const el = document.documentElement;
        el.classList.remove('light', 'dark');
        el.classList.add(isDark ? 'dark' : 'light');
        const meta = document.getElementById('hf-theme-color');
        if (meta) meta.content = isDark ? '#121117' : '#f7f5f1';
    }

    // « Système » follows the OS setting live (no reload needed).
    const onSchemeChange = function () {
        if (storedTheme() === 'system') paintTheme('system');
    };
    if (darkQuery.addEventListener) darkQuery.addEventListener('change', onSchemeChange);
    else if (darkQuery.addListener) darkQuery.addListener(onSchemeChange);

    window.hf = {
        // --- theme ---
        applyTheme: function (theme) {
            try {
                localStorage.setItem(THEME_KEY, theme);
            } catch (e) { }
            paintTheme(theme);
        },
        getTheme: function () {
            return storedTheme();
        },

        // --- app icon (favicon) by global status: ok | due | late | none (Services/AppIconService.cs) ---
        setAppIcon: function (variant) {
            const link = document.getElementById('hf-favicon');
            if (!link) return;
            const href = 'icons/favicon-' + variant + '.svg';
            if (link.getAttribute('href') !== href) link.setAttribute('href', href);
        },

        // --- language ---
        setLang: function (lang) {
            document.documentElement.lang = lang;
        },

        // --- misc ---
        copyToClipboard: function (text) {
            try {
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(text);
                }
            } catch (e) { }
        },
        confirm: function (message) {
            return window.confirm(message);
        },
        // Below the sm breakpoint (640 px): mobile variants decided in C# (e.g. C5 toast actions).
        isMobile: function () {
            return window.matchMedia('(max-width: 639px)').matches;
        },

        // Triggers a browser download from base64 bytes produced by .NET
        // (used by the GDPR data export, Art. 15/20).
        // Returns true only if the download was actually handed to the browser: the caller
        // must not claim success otherwise. An export announced but never delivered is an
        // Art. 15 request treated as served when it was not, and the hourly quota is spent.
        downloadFile: function (fileName, base64, contentType) {
            try {
                const binary = atob(base64);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

                const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
                const url = URL.createObjectURL(blob);

                const link = document.createElement('a');
                link.href = url;
                link.download = fileName || 'download';
                document.body.appendChild(link);
                link.click();
                document.body.removeChild(link);

                setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
                return true;
            } catch (e) {
                console.error('houseflow: download failed', e);
                return false;
            }
        }
    };

    // --- modal (Components/Modal.razor): initial focus, focus trap, focus restore ---
    const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), ' +
        'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
    const FIELDS = 'input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled])';
    const modalStack = [];

    function visible(el) {
        return !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length);
    }

    function focusables(root) {
        return Array.prototype.filter.call(root.querySelectorAll(FOCUSABLE), visible);
    }

    window.hf.modal = {
        // initialSelector: CSS selector of the element to focus first (e.g. "[data-modal-cancel]");
        // default = first form field, else first focusable element, else the panel itself.
        open: function (panel, initialSelector) {
            if (!panel) return;
            const entry = { panel: panel, previous: document.activeElement };
            entry.onKeyDown = function (e) {
                if (e.key !== 'Tab' || modalStack[modalStack.length - 1] !== entry) return;
                const items = focusables(panel);
                if (items.length === 0) { e.preventDefault(); panel.focus(); return; }
                const first = items[0], last = items[items.length - 1];
                if (e.shiftKey && (document.activeElement === first || !panel.contains(document.activeElement))) {
                    e.preventDefault(); last.focus();
                } else if (!e.shiftKey && (document.activeElement === last || !panel.contains(document.activeElement))) {
                    e.preventDefault(); first.focus();
                }
            };
            document.addEventListener('keydown', entry.onKeyDown, true);
            modalStack.push(entry);
            document.body.style.overflow = 'hidden';

            let target = initialSelector ? panel.querySelector(initialSelector) : null;
            if (!target) target = Array.prototype.filter.call(panel.querySelectorAll(FIELDS), visible)[0];
            if (!target) target = focusables(panel)[0];
            (target || panel).focus();
        },
        close: function () {
            const entry = modalStack.pop();
            if (!entry) return;
            document.removeEventListener('keydown', entry.onKeyDown, true);
            if (modalStack.length === 0) document.body.style.overflow = '';
            if (entry.previous && typeof entry.previous.focus === 'function' && document.contains(entry.previous)) {
                entry.previous.focus();
            }
        }
    };

    // --- menus (Components/MenuButton.razor): arrow keys / Home / End / Escape ---
    window.hf.menu = {
        attach: function (menu, trigger, dotnet, focusFirst) {
            if (!menu) return;
            const items = function () {
                return Array.prototype.filter.call(menu.querySelectorAll('a[href], button:not([disabled])'), visible);
            };
            menu._hfKeyDown = function (e) {
                const list = items();
                const i = list.indexOf(document.activeElement);
                if (e.key === 'ArrowDown') { e.preventDefault(); (list[(i + 1) % list.length] || list[0]).focus(); }
                else if (e.key === 'ArrowUp') { e.preventDefault(); (list[(i - 1 + list.length) % list.length] || list[0]).focus(); }
                else if (e.key === 'Home') { e.preventDefault(); list[0] && list[0].focus(); }
                else if (e.key === 'End') { e.preventDefault(); list[list.length - 1] && list[list.length - 1].focus(); }
                else if (e.key === 'Escape') { e.preventDefault(); dotnet.invokeMethodAsync('CloseFromJs'); trigger && trigger.focus(); }
                else if (e.key === 'Tab') { dotnet.invokeMethodAsync('CloseFromJs'); }
            };
            menu.addEventListener('keydown', menu._hfKeyDown);
            if (focusFirst) { const list = items(); list[0] && list[0].focus(); }
        },
        detach: function (menu) {
            if (menu && menu._hfKeyDown) menu.removeEventListener('keydown', menu._hfKeyDown);
        }
    };

    // --- legal pages (Features/Legal/LegalPage.razor): section headings become anchors ---
    // The legal texts are frozen, so ids are derived at runtime from the "N." numbering of each
    // h2 ("section-3") instead of being written into the content components.
    window.hf.legal = {
        anchorHeadings: function (article) {
            if (!article) return;
            Array.prototype.forEach.call(article.querySelectorAll('h2'), function (h, i) {
                if (h.id) return;
                const m = /^\s*(\d+)\./.exec(h.textContent || '');
                h.id = 'section-' + (m ? m[1] : String(i + 1));
            });
            const hash = decodeURIComponent((location.hash || '').slice(1));
            const target = hash && document.getElementById(hash);
            if (target && article.contains(target)) target.scrollIntoView();
        }
    };

    // --- account page (Features/Settings/Settings.razor): one scrolling page with anchors ---
    // The left menu scrolls to a section and updates the #hash without a Blazor navigation.
    window.hf.sections = {
        // Current #hash without the '#', or '' (the page reads it once rendered to scroll there).
        hash: function () {
            return decodeURIComponent((location.hash || '').slice(1));
        },
        scrollTo: function (id, updateHash) {
            const el = id && document.getElementById(id);
            if (!el) return false;
            const reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            el.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'start' });
            if (updateHash) history.replaceState(history.state, '', '#' + id);
            return true;
        }
    };

})();
