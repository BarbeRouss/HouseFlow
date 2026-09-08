// HouseFlow Blazor interop helpers: theme handling, token/session storage,
// and E2E compatibility shims (window.__setAccessToken / __INITIAL_AUTH_TOKEN).
(function () {
    const ACCESS_TOKEN_KEY = 'houseflow_access_token';
    const AUTH_USER_KEY = 'houseflow_auth_user';
    const THEME_KEY = 'houseflow_theme';

    // Some E2E tests inject an initial token via addInitScript before load.
    try {
        if (window.__INITIAL_AUTH_TOKEN) {
            localStorage.setItem(ACCESS_TOKEN_KEY, window.__INITIAL_AUTH_TOKEN);
            delete window.__INITIAL_AUTH_TOKEN;
        }
    } catch (e) { }

    window.hf = {
        // --- theme ---
        applyTheme: function (theme) {
            try {
                localStorage.setItem(THEME_KEY, theme);
            } catch (e) { }
            const isDark = theme === 'dark' ||
                (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
            const el = document.documentElement;
            el.classList.remove('light', 'dark');
            el.classList.add(isDark ? 'dark' : 'light');
        },
        getTheme: function () {
            try {
                return localStorage.getItem(THEME_KEY) || 'system';
            } catch (e) {
                return 'system';
            }
        },

        // --- storage ---
        localGet: function (key) {
            try { return localStorage.getItem(key); } catch (e) { return null; }
        },
        localSet: function (key, value) {
            try {
                if (value === null || value === undefined) localStorage.removeItem(key);
                else localStorage.setItem(key, value);
            } catch (e) { }
        },
        localRemove: function (key) {
            try { localStorage.removeItem(key); } catch (e) { }
        },
        sessionGet: function (key) {
            try { return sessionStorage.getItem(key); } catch (e) { return null; }
        },
        sessionSet: function (key, value) {
            try {
                if (value === null || value === undefined) sessionStorage.removeItem(key);
                else sessionStorage.setItem(key, value);
            } catch (e) { }
        },
        sessionRemove: function (key) {
            try { sessionStorage.removeItem(key); } catch (e) { }
        },
        sessionClear: function () {
            try { sessionStorage.clear(); } catch (e) { }
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
        }
    };

    // E2E compatibility: allow tests to clear/set the access token directly.
    window.__setAccessToken = function (token) {
        try {
            if (token) localStorage.setItem(ACCESS_TOKEN_KEY, token);
            else localStorage.removeItem(ACCESS_TOKEN_KEY);
        } catch (e) { }
    };
})();
