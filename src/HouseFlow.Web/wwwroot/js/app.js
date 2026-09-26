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

})();
