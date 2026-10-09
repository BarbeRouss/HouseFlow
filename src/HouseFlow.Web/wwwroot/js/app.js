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
        // OAuth pages (#304) refuse to work inside a frame: an « Autoriser » button under a
        // transparent iframe would be clickjacking (RFC 9700 §4.16). Nothing renders without
        // scripts, so a sandboxed frame cannot bypass this check.
        isFramed: function () {
            try {
                return window.self !== window.top;
            } catch (e) {
                return true;
            }
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

    // --- modal (Components/Modal.razor): initial focus, focus trap, Esc, focus restore ---
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

    // Can `el` take the focus back right now (still in the page, shown, enabled)?
    function usable(el) {
        return !!el && el !== document.body && typeof el.focus === 'function' && document.contains(el) &&
            !el.disabled && visible(el);
    }

    // Last element the user focused or pressed. A modal is usually opened by a menu item (⋯ →
    // « Supprimer ») that the same render removes, so document.activeElement is already <body>
    // when hf.modal.open runs: this remembers where the user actually was.
    let lastInteraction = null;
    document.addEventListener('focusin', function (e) { lastInteraction = e.target; }, true);
    document.addEventListener('pointerdown', function (e) {
        const t = e.target && e.target.closest ? e.target.closest(FOCUSABLE) : null;
        if (t) lastInteraction = t;
    }, true);

    // Where focus goes back when a modal closes: the opener if it is still usable, else — opener
    // inside a menu that has closed since — that menu's trigger (a detached item still reaches its
    // detached menu panel through closest(), and the panel remembers its trigger).
    function returnTarget(opener) {
        if (usable(opener)) return opener;
        const menu = opener && opener.closest ? opener.closest('[data-hf-menu]') : null;
        const trigger = menu && menu._hfTrigger;
        return usable(trigger) ? trigger : null;
    }

    function topEntry() {
        return modalStack[modalStack.length - 1];
    }

    function initialTarget(panel, initialSelector) {
        let target = initialSelector ? panel.querySelector(initialSelector) : null;
        if (target && !usable(target)) target = null;
        if (!target) target = Array.prototype.filter.call(panel.querySelectorAll(FIELDS), visible)[0];
        if (!target) target = focusables(panel)[0];
        return target || panel;
    }

    window.hf.modal = {
        // key: unique id of the Modal instance; dotnet: its DotNetObjectReference (Esc → OnEscape).
        // initialSelector: CSS selector of the element to focus first (e.g. "[data-modal-cancel]");
        // default = first form field, else first focusable element, else the panel itself.
        open: function (key, panel, initialSelector, dotnet) {
            if (!panel) return;
            const active = document.activeElement;
            const entry = {
                key: key, panel: panel, dotnet: dotnet, initialSelector: initialSelector,
                opener: active && active !== document.body ? active : lastInteraction,
                lastInside: null
            };
            entry.onKeyDown = function (e) {
                if (topEntry() !== entry) return;
                if (e.key === 'Escape') {
                    // A ⋯ menu open inside the modal, or an expanded combobox (M3 provider
                    // suggestions), closes first — with its own handler.
                    const t = e.target && e.target.closest ? e.target : null;
                    if (e.defaultPrevented || (t && (t.closest('[data-hf-menu]') || t.getAttribute('aria-expanded') === 'true'))) return;
                    // Document level: Esc works even when the focus fell out of the dialog
                    // (e.g. onto <body> when the focused button got disabled).
                    e.preventDefault();
                    if (entry.dotnet) entry.dotnet.invokeMethodAsync('OnEscape');
                    return;
                }
                if (e.key !== 'Tab') return;
                const items = focusables(panel);
                if (items.length === 0) { e.preventDefault(); panel.focus(); return; }
                const first = items[0], last = items[items.length - 1];
                if (e.shiftKey && (document.activeElement === first || !panel.contains(document.activeElement) || document.activeElement === panel)) {
                    e.preventDefault(); last.focus();
                } else if (!e.shiftKey && (document.activeElement === last || !panel.contains(document.activeElement))) {
                    e.preventDefault(); first.focus();
                }
            };
            entry.onFocusIn = function (e) {
                if (e.target !== panel) entry.lastInside = e.target;
            };
            document.addEventListener('keydown', entry.onKeyDown, true);
            panel.addEventListener('focusin', entry.onFocusIn);
            modalStack.push(entry);
            document.body.style.overflow = 'hidden';
            initialTarget(panel, initialSelector).focus();
        },

        // Called after every render of an open modal: when the focused control was disabled
        // (busy button, checkbox saving) or removed (menu item, deleted row), the browser drops the
        // focus onto <body> — outside the trap. Park it on the panel while that control is
        // unusable, give it back as soon as it is usable again, else fall back to the initial target.
        // Deferred to a task of its own: a render can run in the middle of a focus move (the blur of
        // one field re-renders before the next field gets the focus), while <body> is only
        // transiently active — acting then would steal the focus back from the field being entered.
        keepFocus: function (key) {
            setTimeout(function () {
                const entry = topEntry();
                if (!entry || entry.key !== key) return;
                const panel = entry.panel, active = document.activeElement;
                // Parked on the panel by us (not a click on its background): give the control back.
                const parked = entry.parked && active === panel;
                entry.parked = false;
                if (!parked && panel.contains(active)) return;
                const last = entry.lastInside;
                if (usable(last) && panel.contains(last)) { last.focus(); return; }
                if (last && document.contains(last) && panel.contains(last)) {
                    entry.parked = true; // still there but disabled: wait on the panel
                    panel.focus();
                    return;
                }
                // Removed: an item of a ⋯ menu inside the modal → that menu's trigger, if still there.
                const back = returnTarget(last);
                if (back && panel.contains(back)) { back.focus(); return; }
                initialTarget(panel, entry.initialSelector).focus();
            }, 0);
        },

        close: function (key) {
            let i = modalStack.length - 1;
            while (i >= 0 && modalStack[i].key !== key) i--;
            if (i < 0) return;
            const entry = modalStack.splice(i, 1)[0];
            document.removeEventListener('keydown', entry.onKeyDown, true);
            entry.panel.removeEventListener('focusin', entry.onFocusIn);
            if (modalStack.length === 0) document.body.style.overflow = '';
            // Only the top modal hands the focus back (a lower one closing keeps the upper one's).
            if (i !== modalStack.length) return;
            const target = returnTarget(entry.opener);
            if (target) target.focus();
            else if (modalStack.length > 0) initialTarget(topEntry().panel, topEntry().initialSelector).focus();
        }
    };

    // --- menus (Components/MenuButton.razor): arrow keys / Home / End / Escape ---
    window.hf.menu = {
        attach: function (menu, trigger, dotnet, focusFirst) {
            if (!menu) return;
            // Lets a modal opened from one of its items give the focus back to the trigger.
            menu._hfTrigger = trigger;
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
            window.hf.menu.float(menu, trigger);
            if (focusFirst) { const list = items(); list[0] && list[0].focus({ preventScroll: true }); }
        },

        // The panel is `position: fixed` (never cropped by an overflow-hidden list or a scroll
        // area): placed under the trigger (above it when there is no room below, or first when
        // data-open-up), aligned on its end or start edge, kept 8 px inside the viewport, and
        // re-placed on scroll / resize until the panel leaves the DOM.
        float: function (menu, trigger) {
            if (!menu || !trigger) return;
            const gap = 4, pad = 8;
            const place = function () {
                if (!menu.isConnected || !trigger.isConnected) {
                    window.removeEventListener('scroll', place, true);
                    window.removeEventListener('resize', place);
                    return;
                }
                menu.style.top = '0px';
                menu.style.left = '0px';
                // A transformed ancestor (modal panel) becomes the containing block of `fixed`:
                // measure where (0, 0) lands and compensate.
                const origin = menu.getBoundingClientRect();
                const t = trigger.getBoundingClientRect();
                const w = menu.offsetWidth, h = menu.offsetHeight;
                const vw = document.documentElement.clientWidth;
                // The mobile tab bar (C2, fixed at the bottom) is the floor: never open under it.
                const bar = document.querySelector('[data-hf-tabbar]');
                const barTop = bar && getComputedStyle(bar).display !== 'none' ? bar.getBoundingClientRect().top : Infinity;
                const vh = Math.min(window.innerHeight, barTop);
                let left = menu.dataset.align === 'end' ? t.right - w : t.left;
                left = Math.max(pad, Math.min(left, vw - w - pad));
                const below = vh - t.bottom - gap - pad, above = t.top - gap - pad;
                const up = menu.dataset.openUp === 'true' ? (above >= h || above > below) : (below < h && above > below);
                let top = up ? t.top - gap - h : t.bottom + gap;
                top = Math.max(pad, Math.min(top, vh - h - pad));
                menu.style.left = (left - origin.left) + 'px';
                menu.style.top = (top - origin.top) + 'px';
            };
            place();
            window.addEventListener('scroll', place, true);
            window.addEventListener('resize', place);
        },
        detach: function (menu) {
            if (menu && menu._hfKeyDown) menu.removeEventListener('keydown', menu._hfKeyDown);
        },

        // Menu opened by a long press (mobile C3 row): the finger still rests on the screen, and the
        // click the browser synthesizes when it lifts would land on the menu's outside-click catcher
        // (closing it at once) or on an item (triggering it). Swallow that one click — before Blazor
        // sees it (window, capture phase). A new press (pointerdown) disarms it, so the next real tap
        // is never lost, including when the lift produced no click at all.
        swallowNextClick: function () {
            const disarm = function () {
                window.removeEventListener('click', swallow, true);
                window.removeEventListener('pointerdown', disarm, true);
            };
            const swallow = function (e) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                disarm();
            };
            window.addEventListener('click', swallow, true);
            window.addEventListener('pointerdown', disarm, true);
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
