// HouseFlow Blazor interop helpers: theme handling and small browser shims.
// Auth tokens are never stored here: the access token lives in memory
// (Auth/TokenStore.cs) and the session survives through the HttpOnly cookie.
(function () {
    const THEME_KEY = 'houseflow_theme';

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

})();
