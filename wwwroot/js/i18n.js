'use strict';

(function () {
    const DEFAULT_LOCALE = 'ru';
    let currentLocale = localStorage.getItem('dnd.locale') || DEFAULT_LOCALE;
    let translations = {};

    async function loadTranslations(locale) {
        try {
            const response = await fetch(`/locales/${locale}.json`);
            if (!response.ok) throw new Error(`Failed to load ${locale}`);
            translations = await response.json();
        } catch (e) {
            console.warn(`Не удалось загрузить переводы для ${locale}, используется пустой словарь.`);
            translations = {};
        }
    }

    async function init() {
        await loadTranslations(currentLocale);
        // Уведомляем приложение о готовности переводов
        document.dispatchEvent(new CustomEvent('i18n:loaded', { detail: { locale: currentLocale } }));
    }

    function t(key, fallback) {
        return translations[key] ?? fallback ?? key;
    }

    function setLocale(locale) {
        currentLocale = locale;
        localStorage.setItem('dnd.locale', locale);
        loadTranslations(locale).then(() => {
            document.dispatchEvent(new CustomEvent('i18n:changed', { detail: { locale } }));
        });
    }

    window.I18n = {
        t,
        setLocale,
        get currentLocale() { return currentLocale; },
        init
    };
})();