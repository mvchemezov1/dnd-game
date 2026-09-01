// js/views/admin.js
// Панель администратора: глобальный поиск персонажей и доступ к мастерским инструментам.
// Администратор обходит проверки владения на сервере, поэтому видит всех персонажей.

'use strict';

(function () {
    const g = UI.g; // безопасное получение значения с учётом camelCase/PascalCase

    /**
     * Рендерит представление администратора.
     * @param {HTMLElement} root - корневой контейнер
     */
    async function renderAdminView(root) {
        root.innerHTML = `
            <div class="note">
                В бэкенде сейчас нет эндпоинтов для управления пользователями, назначения ролей
                (Player / GameMaster / Admin) или создания кампаний — только неограниченный доступ
                к существующим персонажам и боям. Ниже — то, что реально работает: глобальный поиск
                и правка любого персонажа, плюс все инструменты мастера.
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

            <div class="card">
                <h2>Инструменты мастера</h2>
                <p class="muted small">
                    Как у GameMaster: обзор пати, быстрые действия, спавн NPC, трекер боя,
                    кампания/квесты — доступны в соответствующих вкладках слева,
                    без ограничений по владению.
                </p>
            </div>
        `;

        // Привязка действий
        UI.bindActions(root, {
            search: search,
            reset: resetFilters
        });

        // Функция сброса полей фильтрации
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

        /**
         * Выполняет поиск персонажей по заданным фильтрам.
         */
        async function search() {
            const resultsEl = root.querySelector('#admin-results');
            if (!resultsEl) return;
            resultsEl.innerHTML = UI.loadingBlock('Поиск…');

            // Сбор параметров
            const name = root.querySelector('#f-name').value.trim();
            const className = root.querySelector('#f-class').value.trim();
            const race = root.querySelector('#f-race').value.trim();
            const minLvl = root.querySelector('#f-minlvl').value.trim();
            const maxLvl = root.querySelector('#f-maxlvl').value.trim();
            const alive = root.querySelector('#f-alive').value;

            // Валидация уровней
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

            // Формирование query-параметров
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

        /**
         * Генерирует HTML-строку для одного найденного персонажа.
         */
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
    }

    // Экспорт
    window.Views = window.Views || {};
    window.Views.renderAdminView = renderAdminView;
})();