// js/views/admin.js
// Панель администратора: глобальный поиск персонажей, управление пользователями.
'use strict';

(function () {
    const g = UI.g; // безопасное получение значения с учётом camelCase/PascalCase

    async function renderAdminView(root) {
        root.innerHTML = `
            <div class="card">
                <h2>Управление пользователями</h2>
                <div id="users-list">${UI.loadingBlock('Загрузка пользователей…')}</div>
            </div>

            <div class="card">
                <h2>Глобальный поиск персонажей</h2>
                <div class="grid grid-3">
                    <div class="field">Имя<input id="f-name" placeholder="Частичное совпадение"></div>
                    <div class="field">Класс<input id="f-class" placeholder="Точное совпадение"></div>
                    <div class="field">Раса<input id="f-race" placeholder="Точное совпадение"></div>
                    <div class="field">Мин. уровень<input id="f-minlvl" type="number" min="1" max="20"></div>
                    <div class="field">Макс. уровень<input id="f-maxlvl" type="number" min="1" max="20"></div>
                    <div class="field">Статус
                        <select id="f-alive">
                            <option value="">Все</option>
                            <option value="true">Живые</option>
                            <option value="false">Мёртвые</option>
                        </select>
                    </div>
                </div>
                <div class="row" style="margin-top:10px">
                    <button class="btn btn-primary" data-action="search">Искать</button>
                    <button class="btn" data-action="reset">Сбросить фильтры</button>
                </div>
            </div>

            <div id="admin-results"></div>
        `;

        // Привязка действий
        UI.bindActions(root, {
            search: search,
            reset: resetFilters
        });

        // Загружаем пользователей сразу
        loadUsers();

        function resetFilters() {
            root.querySelector('#f-name').value = '';
            root.querySelector('#f-class').value = '';
            root.querySelector('#f-race').value = '';
            root.querySelector('#f-minlvl').value = '';
            root.querySelector('#f-maxlvl').value = '';
            root.querySelector('#f-alive').value = '';
            const resultsEl = root.querySelector('#admin-results');
            if (resultsEl) resultsEl.innerHTML = '';
        }

        async function search() {
            const resultsEl = root.querySelector('#admin-results');
            if (!resultsEl) return;
            resultsEl.innerHTML = UI.loadingBlock('Поиск…');

            const name = root.querySelector('#f-name').value.trim();
            const className = root.querySelector('#f-class').value.trim();
            const race = root.querySelector('#f-race').value.trim();
            const minLvl = root.querySelector('#f-minlvl').value.trim();
            const maxLvl = root.querySelector('#f-maxlvl').value.trim();
            const alive = root.querySelector('#f-alive').value;

            if (minLvl && (isNaN(+minLvl) || +minLvl < 1 || +minLvl > 20)) {
                toast('Минимальный уровень должен быть от 1 до 20', 'error');
                return;
            }
            if (maxLvl && (isNaN(+maxLvl) || +maxLvl < 1 || +maxLvl > 20)) {
                toast('Максимальный уровень должен быть от 1 до 20', 'error');
                return;
            }
            if (minLvl && maxLvl && +minLvl > +maxLvl) {
                toast('Минимальный уровень не может быть больше максимального', 'error');
                return;
            }

            const params = new URLSearchParams();
            if (name) params.set('name', name);
            if (className) params.set('className', className);
            if (race) params.set('race', race);
            if (minLvl) params.set('minLvl', minLvl);
            if (maxLvl) params.set('maxLvl', maxLvl);
            if (alive) params.set('alive', alive);

            try {
                const list = await Api.get(`/api/characters/search?${params.toString()}`);

                if (!list || !list.length) {
                    resultsEl.innerHTML = UI.emptyState('Ничего не найдено.');
                    return;
                }

                resultsEl.innerHTML = `
                    <div class="card">
                        <table>
                            <thead>
                                <tr><th>Имя</th><th>Ур.</th><th>Класс</th><th>Раса</th><th>HP</th><th></th></tr>
                            </thead>
                            <tbody>
                                ${list.map(c => rowHtml(c)).join('')}
                            </tbody>
                        </table>
                    </div>`;

                UI.bindActions(resultsEl, {
                    open: (el) => Store.setRoute('sheet', { id: el.dataset.id })
                });
            } catch (e) {
                resultsEl.innerHTML = UI.emptyState('Ошибка поиска.');
                notifyError(e);
            }
        }

        function rowHtml(c) {
            const id = g(c, 'id');
            const name = UI.esc(g(c, 'name', '—'));
            const level = g(c, 'level', '?');
            const className = UI.esc(g(c, 'class', ''));
            const race = UI.esc(g(c, 'race', ''));
            const hitPoints = g(c, 'hitPoints', '?');
            const maxHitPoints = g(c, 'maxHitPoints', '?');

            return `
                <tr>
                    <td>${name}</td>
                    <td>${level}</td>
                    <td>${className}</td>
                    <td>${race}</td>
                    <td>${hitPoints}/${maxHitPoints}</td>
                    <td><button class="btn btn-sm" data-action="open" data-id="${UI.esc(id)}">Открыть</button></td>
                </tr>`;
        }

        async function loadUsers() {
            const container = root.querySelector('#users-list');
            if (!container) return;
            container.innerHTML = UI.loadingBlock('Загрузка пользователей…');
            try {
                const users = await Api.get('/api/users');
                if (!users.length) {
                    container.innerHTML = UI.emptyState('Нет пользователей.');
                    return;
                }
                container.innerHTML = `
                    <table>
                        <thead><tr><th>Имя</th><th>Email</th><th>Роль</th><th>Статус</th><th></th></tr></thead>
                        <tbody>
                            ${users.map(u => `
                                <tr>
                                    <td>${UI.esc(u.username)}</td>
                                    <td>${UI.esc(u.email)}</td>
                                    <td>
                                        <select data-action="change-role" data-user-id="${u.id}">
                                            ${['Player', 'GameMaster', 'Admin'].map(role => `
                                                <option value="${role}" ${role === u.globalRole ? 'selected' : ''}>${UI.roleLabel(role)}</option>
                                            `).join('')}
                                        </select>
                                    </td>
                                    <td>${u.isActive ? UI.pill('Активен', 'success') : UI.pill('Заблокирован', 'danger')}</td>
                                    <td class="row">
                                        <button class="btn btn-sm" data-action="toggle-status" data-user-id="${u.id}" data-active="${!u.isActive}">
                                            ${u.isActive ? 'Заблокировать' : 'Разблокировать'}
                                        </button>
                                        <button class="btn btn-sm" data-action="reset-password" data-user-id="${u.id}">Сбросить пароль</button>
                                        <button class="btn btn-sm btn-danger" data-action="delete-user" data-user-id="${u.id}">Удалить</button>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>`;

                UI.bindActions(container, {
                    'change-role': async (el) => {
                        const userId = el.dataset.userId;
                        const newRole = el.value;
                        await Api.put(`/api/users/${userId}/role`, { role: newRole });
                        toast('Роль обновлена', 'success');
                        loadUsers();
                    },
                    'toggle-status': async (el) => {
                        const userId = el.dataset.userId;
                        const isActive = el.dataset.active === 'true';
                        await Api.put(`/api/users/${userId}/status`, { isActive });
                        toast('Статус обновлён', 'success');
                        loadUsers();
                    },
                    'reset-password': (el) => {
                        const userId = el.dataset.userId;
                        openResetPasswordModal(userId);
                    },
                    'delete-user': async (el) => {
                        const userId = el.dataset.userId;
                        if (await confirmDialog('Удалить пользователя?')) {
                            await Api.del(`/api/users/${userId}`);
                            toast('Пользователь удалён', 'success');
                            loadUsers();
                        }
                    }
                });
            } catch (e) {
                container.innerHTML = UI.emptyState('Ошибка загрузки пользователей.');
                notifyError(e);
            }
        }

        function openResetPasswordModal(userId) {
            openModal({
                title: 'Сброс пароля',
                bodyHtml: `
                    <div class="stack">
                        ${UI.field('Новый пароль', '<input data-field="newPassword" type="password">')}
                        <div class="hint">Минимум 8 символов, заглавная и строчная буква, цифра и спецсимвол.</div>
                    </div>`,
                actions: [
                    { label: 'Отмена', className: 'btn', onClick: closeModal },
                    {
                        label: 'Сбросить',
                        className: 'btn btn-primary',
                        onClick: async (ev) => {
                            const box = ev.target.closest('.modal-box');
                            const data = UI.collectFields(box);
                            if (!data.newPassword) { toast('Введите пароль', 'error'); return; }
                            try {
                                await Api.put(`/api/users/${userId}/password`, { newPassword: data.newPassword });
                                closeModal();
                                toast('Пароль сброшен', 'success');
                            } catch (e) { notifyError(e); }
                        }
                    }
                ]
            });
        }
    }

    // Экспорт
    window.Views = window.Views || {};
    window.Views.renderAdminView = renderAdminView;
})();