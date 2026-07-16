// Language preference helpers used by LanguageToggle.razor via JS interop.
// Cookie format follows ASP.NET Core's CookieRequestCultureProvider: c=<lang>|uic=<lang>
const COOKIE_NAME = ".AspNetCore.Culture";
const STORAGE_PREFIX = "lang:";

// Session storage keys — token lives only for the browser tab session.
// This lets the language toggle (forceLoad reload) survive without logging the user out.
const SESSION_TOKEN_KEY = "shelterstack:token";
const SESSION_EMAIL_KEY = "shelterstack:email";

window.languageInterop = {
    setCultureCookie(lang) {
        const value = `c=${lang}|uic=${lang}`;
        const expires = new Date();
        expires.setFullYear(expires.getFullYear() + 1);
        document.cookie = `${COOKIE_NAME}=${encodeURIComponent(value)};expires=${expires.toUTCString()};path=/;SameSite=Lax`;
    },

    getUserLanguage(email) {
        return localStorage.getItem(STORAGE_PREFIX + email);
    },

    setUserLanguage(email, lang) {
        localStorage.setItem(STORAGE_PREFIX + email, lang);
    },

    saveSession(token, email) {
        sessionStorage.setItem(SESSION_TOKEN_KEY, token);
        sessionStorage.setItem(SESSION_EMAIL_KEY, email);
    },

    clearSession() {
        sessionStorage.removeItem(SESSION_TOKEN_KEY);
        sessionStorage.removeItem(SESSION_EMAIL_KEY);
    },

    getSession() {
        const token = sessionStorage.getItem(SESSION_TOKEN_KEY);
        const email = sessionStorage.getItem(SESSION_EMAIL_KEY);
        return token && email ? { token, email } : null;
    },
};
