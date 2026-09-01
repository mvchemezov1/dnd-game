// js/views/auth.js
// Модуль экрана аутентификации: вход, регистрация, восстановление и смена пароля.
'use strict';

(function () {
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

        // Добавляем ссылку "Забыли пароль?" под формой входа
        if (!loginForm.querySelector('#forgot-password-link')) {
            const link = document.createElement('a');
            link.id = 'forgot-password-link';
            link.href = '#';
            link.textContent = 'Забыли пароль?';
            link.style.marginTop = '8px';
            link.addEventListener('click', (e) => {
                e.preventDefault();
                openForgotPasswordModal();
            });
            loginForm.appendChild(link);
        }

        let isSubmitting = false;

        tabs.forEach(tab => {
            tab.addEventListener('click', () => switchTab(tab));
        });

        function switchTab(activeTab) {
            tabs.forEach(tab => {
                const isActive = tab === activeTab;
                tab.classList.toggle('active', isActive);
                tab.setAttribute('aria-selected', isActive ? 'true' : 'false');
            });

            const which = activeTab.dataset.tab;
            const showLogin = which === 'login';
            loginForm.classList.toggle('hidden', !showLogin);
            registerForm.classList.toggle('hidden', showLogin);

            clearErrors();
            if (showLogin) {
                const firstInput = loginForm.querySelector('input');
                if (firstInput) firstInput.focus();
            } else {
                const firstInput = registerForm.querySelector('input');
                if (firstInput) firstInput.focus();
            }
        }

        loginForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (isSubmitting) return;
            clearErrors();

            const fd = new FormData(loginForm);
            const username = (fd.get('username') || '').trim();
            const password = fd.get('password') || '';

            if (!username || !password) {
                showError(loginError, 'Введите имя пользователя и пароль');
                return;
            }

            await submitWithLoading(loginForm, async () => {
                await Api.login(username, password);
            }, onSuccess, loginError);
        });

        registerForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (isSubmitting) return;
            clearErrors();

            const fd = new FormData(registerForm);
            const username = (fd.get('username') || '').trim();
            const email = (fd.get('email') || '').trim();
            const password = fd.get('password') || '';
            const confirmPassword = fd.get('confirmPassword') || '';

            const validationError = validateRegistration(username, email, password, confirmPassword);
            if (validationError) {
                showError(registerError, validationError);
                return;
            }

            await submitWithLoading(registerForm, async () => {
                await Api.register(username, email, password);
            }, onSuccess, registerError);
        });

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
            const submitBtn = form.querySelector('button[type="submit"]');
            form.insertBefore(label, submitBtn);
        }

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

        async function submitWithLoading(form, action, onSuccess, errorEl) {
            isSubmitting = true;
            const submitBtn = form.querySelector('button[type="submit"]');
            const originalText = submitBtn.textContent;
            submitBtn.disabled = true;
            submitBtn.textContent = 'Подождите…';

            try {
                await action();
                onSuccess();
            } catch (err) {
                showError(errorEl, err.message || 'Не удалось выполнить операцию');
            } finally {
                isSubmitting = false;
                submitBtn.disabled = false;
                submitBtn.textContent = originalText;
            }
        }

        function showError(el, message) {
            el.textContent = message;
            el.classList.add('visible');
        }

        function clearErrors() {
            loginError.textContent = '';
            loginError.classList.remove('visible');
            registerError.textContent = '';
            registerError.classList.remove('visible');
        }

        const defaultTab = document.querySelector('#auth-tabs .tab[data-tab="login"]');
        if (defaultTab) switchTab(defaultTab);
    }

    // Функция восстановления пароля (forgot-password)
    function openForgotPasswordModal() {
        openModal({
            title: 'Восстановление пароля',
            bodyHtml: `
                <div class="stack">
                    ${UI.field('Email', '<input data-field="email" type="email">')}
                </div>`,
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Отправить', className: 'btn btn-primary',
                    onClick: async (ev) => {
                        const box = ev.target.closest('.modal-box');
                        const data = UI.collectFields(box);
                        if (!data.email) { toast('Введите email', 'error'); return; }
                        try {
                            await Api.post('/api/auth/forgot-password', { email: data.email });
                            toast('Инструкция отправлена на email', 'success');
                            closeModal();
                        } catch (e) { notifyError(e); }
                    }
                }
            ]
        });
    }

    // Функция сброса пароля по токену (reset-password)
    function openResetPasswordModal(token) {
        openModal({
            title: 'Сброс пароля',
            bodyHtml: `
                <div class="stack">
                    ${UI.field('Новый пароль', '<input data-field="newPassword" type="password">')}
                    <input type="hidden" data-field="token" value="${token}">
                    <div class="hint">Минимум 8 символов, заглавная и строчная буква, цифра и спецсимвол.</div>
                </div>`,
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Сбросить', className: 'btn btn-primary',
                    onClick: async (ev) => {
                        const box = ev.target.closest('.modal-box');
                        const data = UI.collectFields(box);
                        if (!data.newPassword) { toast('Введите пароль', 'error'); return; }
                        try {
                            await Api.post('/api/auth/reset-password', { token: data.token, newPassword: data.newPassword });
                            toast('Пароль изменён', 'success');
                            closeModal();
                            Store.setRoute('login');
                        } catch (e) { notifyError(e); }
                    }
                }
            ]
        });
    }

    // Функция смены пароля (change-password)
    function openChangePasswordModal() {
        openModal({
            title: 'Смена пароля',
            bodyHtml: `
                <div class="stack">
                    ${UI.field('Текущий пароль', '<input data-field="currentPassword" type="password">')}
                    ${UI.field('Новый пароль', '<input data-field="newPassword" type="password">')}
                </div>`,
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Сменить', className: 'btn btn-primary',
                    onClick: async (ev) => {
                        const box = ev.target.closest('.modal-box');
                        const data = UI.collectFields(box);
                        if (!data.currentPassword || !data.newPassword) { toast('Заполните все поля', 'error'); return; }
                        try {
                            await Api.post('/api/auth/change-password', data);
                            toast('Пароль изменён', 'success');
                            closeModal();
                            Api.logout();
                            Store.setRoute('login');
                        } catch (e) { notifyError(e); }
                    }
                }
            ]
        });
    }

    window.Views = window.Views || {};
    window.Views.initAuthScreen = initAuthScreen;
    window.Views.openChangePasswordModal = openChangePasswordModal;
    window.Views.openForgotPasswordModal = openForgotPasswordModal;
    window.Views.openResetPasswordModal = openResetPasswordModal;
})();