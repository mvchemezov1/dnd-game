// js/views/campaign.js
// Представление кампании: общее состояние, квесты и действия над ними.

'use strict';

(function () {
    const g = UI.g; // безопасный доступ к полям (camelCase/PascalCase)

    /**
     * Рендерит представление кампании.
     * @param {HTMLElement} root - корневой контейнер
     */
    function normalizeQuests(data) {
        if (Array.isArray(data)) return data;
        if (data && Array.isArray(data.quests)) return data.quests;
        if (data && typeof data === 'object') {
            // Возможно, сервер вернул объект с ключом 'items' или 'data'
            for (const key of ['items', 'data', 'quests']) {
                if (Array.isArray(data[key])) return data[key];
            }
        }
        return [];
    }

    async function renderCampaignView(root) {
        const campaignId = Api.sessionId;
        if (!campaignId) {
            root.innerHTML = `
                <div class="note">
                    Укажите ID кампании (сессии) в верхней панели — он используется
                    и как заголовок X-Session-Id для всех запросов, и как ID кампании здесь.
                </div>`;
            return;
        }

        // Функция загрузки и отрисовки
        async function load() {
            root.innerHTML = UI.loadingBlock('Загрузка кампании…');
            try {
                const [state, questsRaw] = await Promise.all([
                    Api.get(`/api/campaign/${campaignId}`).catch(() => null),
                    Api.get(`/api/campaign/${campaignId}/quests`).catch(() => [])
                ]);
                const quests = normalizeQuests(questsRaw);
                draw(state, quests);
            } catch (e) {
                root.innerHTML = UI.emptyState('Не удалось загрузить кампанию.');
                notifyError(e);
            }
        }

        /**
         * Отрисовывает данные кампании и квесты.
         * @param {Object|null} state - состояние кампании
         * @param {Array} quests - массив квестов
         */
        function draw(state, quests) {
            const campaignName = UI.esc(g(state, 'campaignName') || 'Кампания');
            const day = g(state, 'day', '?');
            const hour = g(state, 'hour', '?');
            const minute = String(g(state, 'minute', 0)).padStart(2, '0');
            const weather = UI.esc(g(state, 'weather') || '—');
            const currentAct = g(state, 'currentAct', '?');
            const regions = (g(state, 'discoveredRegions', []) || []).join(', ') || '—';

            let stateHtml = '';
            if (state) {
                stateHtml = `
                    <div class="card">
                        <h2>${campaignName}</h2>
                        <div class="row small muted">
                            День ${day}, ${hour}:${minute} ·
                            Погода: ${weather} · Акт ${currentAct}
                        </div>
                        <div class="small muted" style="margin-top:8px">
                            Открытые регионы: ${regions}
                        </div>
                    </div>`;
            } else {
                stateHtml = `
                    <div class="card">
                        <div class="note">
                            Кампания с таким ID не найдена, но квесты ниже всё равно можно попробовать загрузить по тому же ID.
                        </div>
                    </div>`;
            }

            let questsHtml = '';
            if (quests.length) {
                questsHtml = `
                    <div class="card">
                        <h3>Квесты</h3>
                        <table>
                            <thead>
                                <tr><th>Название</th><th>Статус</th><th>Прогресс</th><th></th></tr>
                            </thead>
                            <tbody>
                                ${quests.map(q => questRowHtml(q)).join('')}
                            </tbody>
                        </table>
                    </div>`;
            } else {
                questsHtml = `
                    <div class="card">
                        <h3>Квесты</h3>
                        ${UI.emptyState('Квестов нет.')}
                    </div>`;
            }

            root.innerHTML = stateHtml + questsHtml;

            // Привязка действий
            UI.bindActions(root, {
                accept: (el) => act(() => Api.post(`/api/campaign/${campaignId}/quests/${el.dataset.id}/accept`)),
                complete: (el) => act(() => Api.post(`/api/campaign/${campaignId}/quests/${el.dataset.id}/complete`)),
                fail: (el) => act(() => Api.post(`/api/campaign/${campaignId}/quests/${el.dataset.id}/fail`))
            });
        }

        /**
         * Генерирует HTML строку для одного квеста.
         * @param {Object} q - данные квеста
         * @returns {string} HTML-разметка строки
         */
        function questRowHtml(q) {
            const questId = g(q, 'questId');
            const title = UI.esc(g(q, 'title', '—'));
            const status = g(q, 'status', 'Unknown');
            const statusPill = UI.pill(status, statusKind(status));

            // Прогресс целей
            const objectives = g(q, 'objectives', []) || [];
            let progressText = '—';
            if (objectives.length) {
                progressText = objectives.map(o => {
                    const current = g(o, 'currentProgress', 0);
                    const required = g(o, 'requiredProgress', '?');
                    return `${current}/${required}`;
                }).join(', ');
            }

            return `
                <tr>
                    <td>${title}</td>
                    <td>${statusPill}</td>
                    <td>${progressText}</td>
                    <td class="row">
                        <button class="btn btn-sm" data-action="accept" data-id="${UI.esc(questId)}">Принять</button>
                        <button class="btn btn-sm btn-success" data-action="complete" data-id="${UI.esc(questId)}">Завершить</button>
                        <button class="btn btn-sm btn-danger" data-action="fail" data-id="${UI.esc(questId)}">Провалить</button>
                    </td>
                </tr>`;
        }

        /**
         * Возвращает CSS-класс для статуса квеста.
         * @param {string} status - статус
         * @returns {string} класс пилюли
         */
        function statusKind(status) {
            switch (String(status).toLowerCase()) {
                case 'completed': return 'success';
                case 'failed': return 'danger';
                case 'active': return 'warn';
                default: return 'info';
            }
        }

        /**
         * Выполняет действие и перезагружает данные.
         * @param {Function} fn - функция-действие
         */
        async function act(fn) {
            try {
                await fn();
                toast('Готово', 'success');
                await load();
            } catch (e) {
                notifyError(e, 'Не удалось выполнить действие');
            }
        }

        // Первичная загрузка
        await load();
    }

    // Экспорт
    window.Views = window.Views || {};
    window.Views.renderCampaignView = renderCampaignView;
})();