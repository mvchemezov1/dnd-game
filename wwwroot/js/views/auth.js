// js/views/auth.js
// Модуль экрана аутентификации: вход и регистрация.
// Обеспечивает переключение вкладок, валидацию форм, обработку ошибок и доступность.

'use strict';

(function () {
    /**
     * Инициализирует экран авторизации.
     * @param {Function} onSuccess - колбэк, вызываемый после успешного входа/регистрации
     */
    function initAuthScreen(onSuccess) {
        const tabs = document.querySelectorAll('#auth-tabs .tab');
        const loginForm = document.getElementById('login-form');
        const registerForm = document.getElementById('register-form');
        const loginError = document.getElementById('login-error');
        const registerError = document.getElementById('register-error');

        if (!tabs.length || !loginForm || !registerForm || !loginError || !registerError) {
            console.warn('Не найдены все элементы экрана аутентификации.');
            return;
        }

        // Добавляем поле подтверждения пароля в форму регистрации, если его нет
        ensureConfirmPasswordField(registerForm);

        // Переменная для предотвращения двойной отправки
        let isSubmitting = false;

        // ===================== Переключение вкладок =====================
        tabs.forEach(tab => {
            tab.addEventListener('click', () => switchTab(tab));
        });

        function switchTab(activeTab) {
            // Обновляем классы и ARIA-атрибуты
            tabs.forEach(tab => {
                const isActive = tab === activeTab;
                tab.classList.toggle('active', isActive);
                tab.setAttribute('aria-selected', isActive ? 'true' : 'false');
            });

            const which = activeTab.dataset.tab;
            const showLogin = which === 'login';
            loginForm.classList.toggle('hidden', !showLogin);
            registerForm.classList.toggle('hidden', showLogin);

            // Сбрасываем ошибки и фокус
            clearErrors();
            if (showLogin) {
                const firstInput = loginForm.querySelector('input');
                if (firstInput) firstInput.focus();
            } else {
                const firstInput = registerForm.querySelector('input');
                if (firstInput) firstInput.focus();
            }
        }

        // ===================== Обработка формы входа =====================
        loginForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (isSubmitting) return;
            clearErrors();

            const fd = new FormData(loginForm);
            const username = (fd.get('username') || '').trim();
            const password = fd.get('password') || '';

            // Клиентская валидация
            if (!username || !password) {
                showError(loginError, 'Введите имя пользователя и пароль');
                return;
            }

            await submitWithLoading(loginForm, async () => {
                await Api.login(username, password);
            }, onSuccess, loginError);
        });

        // ===================== Обработка формы регистрации =====================
        registerForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (isSubmitting) return;
            clearErrors();

            const fd = new FormData(registerForm);
            const username = (fd.get('username') || '').trim();
            const email = (fd.get('email') || '').trim();
            const password = fd.get('password') || '';
            const confirmPassword = fd.get('confirmPassword') || '';

            // Валидация
            const validationError = validateRegistration(username, email, password, confirmPassword);
            if (validationError) {
                showError(registerError, validationError);
                return;
            }

            await submitWithLoading(registerForm, async () => {
                await Api.register(username, email, password);
            }, onSuccess, registerError);
        });

        // ===================== Вспомогательные функции =====================

        /**
         * Добавляет поле подтверждения пароля в форму регистрации.
         */
        function ensureConfirmPasswordField(form) {
            if (form.querySelector('input[name="confirmPassword"]')) return;

            const label = document.createElement('label');
            label.htmlFor = 'register-confirm-password';
            label.textContent = 'Подтверждение пароля';

            const input = document.createElement('input');
            input.type = 'password';
            input.id = 'register-confirm-password';
            input.name = 'confirmPassword';
            input.autocomplete = 'new-password';
            input.required = true;

            label.appendChild(input);
            // Вставляем перед кнопкой отправки
            const submitBtn = form.querySelector('button[type="submit"]');
            form.insertBefore(label, submitBtn);
        }

        /**
         * Проверяет данные регистрации.
         * @returns {string|null} сообщение об ошибке или null, если всё корректно
         */
        function validateRegistration(username, email, password, confirmPassword) {
            if (!username) return 'Введите имя пользователя';
            if (username.length < 3) return 'Имя пользователя должно содержать минимум 3 символа';
            if (!email) return 'Введите email';
            if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return 'Некорректный формат email';

            if (!password) return 'Введите пароль';
            if (password.length < 8) return 'Пароль должен быть не короче 8 символов';
            if (!/[A-Z]/.test(password)) return 'Пароль должен содержать хотя бы одну заглавную букву';
            if (!/[a-z]/.test(password)) return 'Пароль должен содержать хотя бы одну строчную букву';
            if (!/\d/.test(password)) return 'Пароль должен содержать хотя бы одну цифру';
            if (!/[^A-Za-z0-9]/.test(password)) return 'Пароль должен содержать хотя бы один специальный символ';

            if (password !== confirmPassword) return 'Пароли не совпадают';

            return null;
        }

        /**
         * Выполняет асинхронное действие с индикацией загрузки и блокировкой формы.
         * @param {HTMLFormElement} form - форма, которая отправляется
         * @param {Function} action - асинхронная функция действия (логин/регистрация)
         * @param {Function} onSuccess - колбэк успеха
         * @param {HTMLElement} errorEl - элемент для вывода ошибки
         */
        async function submitWithLoading(form, action, onSuccess, errorEl) {
            isSubmitting = true;
            const submitBtn = form.querySelector('button[type="submit"]');
            const originalText = submitBtn.textContent;
            submitBtn.disabled = true;
            submitBtn.textContent = 'Подождите…';

            try {
                await action();
                // Успех — вызываем общий колбэк
                onSuccess();
            } catch (err) {
                showError(errorEl, err.message || 'Не удалось выполнить операцию');
            } finally {
                isSubmitting = false;
                submitBtn.disabled = false;
                submitBtn.textContent = originalText;
            }
        }

        /**
         * Отображает сообщение об ошибке.
         */
        function showError(el, message) {
            el.textContent = message;
            el.classList.add('visible');
        }

        /**
         * Очищает сообщения об ошибках.
         */
        function clearErrors() {
            loginError.textContent = '';
            loginError.classList.remove('visible');
            registerError.textContent = '';
            registerError.classList.remove('visible');
        }

        // Инициализация активной вкладки по умолчанию (вход)
        const defaultTab = document.querySelector('#auth-tabs .tab[data-tab="login"]');
        if (defaultTab) switchTab(defaultTab);
    }

    window.Views = window.Views || {};
    window.Views.initAuthScreen = initAuthScreen;
})();