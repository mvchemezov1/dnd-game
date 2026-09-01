// js/views/campaign.js
'use strict';

(function () {
    const g = UI.g;

    async function renderCampaignView(root) {
        const campaignId = Api.sessionId;
        if (!campaignId) {
            root.innerHTML = `
                <div class="note">
                    Укажите ID кампании в верхней панели или создайте новую.
                    <button class="btn btn-primary" data-action="create-campaign">Создать кампанию</button>
                </div>`;
            UI.bindActions(root, {
                'create-campaign': () => openCreateCampaignModal()
            });
            return;
        }

        async function load() {
            root.innerHTML = UI.loadingBlock('Загрузка кампании…');
            try {
                const [state, questsRaw, playersRaw] = await Promise.all([
                    Api.get(`/api/campaign/${campaignId}`).catch(() => null),
                    Api.get(`/api/campaign/${campaignId}/quests`).catch(() => []),
                    Api.get(`/api/campaign/${campaignId}/players`).catch(() => [])
                ]);

                const quests = normalizeQuests(questsRaw);
                // Гарантируем, что players — массив
                const players = Array.isArray(playersRaw) ? playersRaw : [];
                draw(state, quests, players);
            } catch (e) {
                root.innerHTML = UI.emptyState('Не удалось загрузить кампанию.');
                notifyError(e);
            }
        }

        function normalizeQuests(data) {
            if (Array.isArray(data)) return data;
            if (data && Array.isArray(data.quests)) return data.quests;
            if (data && typeof data === 'object') {
                for (const key of ['items', 'data', 'quests']) {
                    if (Array.isArray(data[key])) return data[key];
                }
            }
            return [];
        }

        function draw(state, quests, players) {
            const campaignName = UI.esc(g(state, 'campaignName') || 'Кампания');
            const day = g(state, 'day', '?');
            const hour = g(state, 'hour', '?');
            const minute = String(g(state, 'minute', 0)).padStart(2, '0');
            const weather = UI.esc(g(state, 'weather') || '—');
            const currentAct = g(state, 'currentAct', '?');

            let stateHtml = '';
            if (state) {
                stateHtml = `
                    <div class="card">
                        <h2>${campaignName}</h2>
                        <div class="row small muted">
                            День ${day}, ${hour}:${minute} ·
                            Погода: ${weather} · Акт ${currentAct}
                        </div>
                    </div>`;
            } else {
                stateHtml = `
                    <div class="card">
                        <div class="note">Кампания с таким ID не найдена.</div>
                    </div>`;
            }

            // Игроки (безопасно)
            let playersHtml = '';
            if (players.length > 0) {
                playersHtml = `
                    <div class="card">
                        <h3>Игроки (${players.length})</h3>
                        <div class="tag-list">
                            ${players.map(p => `<span class="tag">${UI.esc(p.username || p)}</span>`).join('')}
                        </div>
                    </div>`;
            }

            // Квесты
            let questsHtml = '';
            if (quests.length > 0) {
                questsHtml = `
                    <div class="card">
                        <h3>Квесты</h3>
                        <table>
                            <thead><tr><th>Название</th><th>Статус</th><th>Прогресс</th><th></th></tr></thead>
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

            root.innerHTML = stateHtml + playersHtml + questsHtml;

            UI.bindActions(root, {
                accept: (el) => act(() => Api.post(`/api/campaign/${campaignId}/quests/${el.dataset.id}/accept`)),
                complete: (el) => act(() => Api.post(`/api/campaign/${campaignId}/quests/${el.dataset.id}/complete`)),
                fail: (el) => act(() => Api.post(`/api/campaign/${campaignId}/quests/${el.dataset.id}/fail`))
            });
        }

        function questRowHtml(q) {
            const questId = g(q, 'questId');
            const title = UI.esc(g(q, 'title', '—'));
            const status = g(q, 'status', 'Unknown');
            const statusPill = UI.pill(status, statusKind(status));
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

        function statusKind(status) {
            switch (String(status).toLowerCase()) {
                case 'completed': return 'success';
                case 'failed': return 'danger';
                case 'active': return 'warn';
                default: return 'info';
            }
        }

        async function act(fn) {
            try {
                await fn();
                toast('Готово', 'success');
                await load();
            } catch (e) {
                notifyError(e, 'Не удалось выполнить действие');
            }
        }

        await load();
    }

    function openCreateCampaignModal() {
        openModal({
            title: 'Новая кампания',
            bodyHtml: `
                <div class="stack">
                    ${UI.field('ID кампании (GUID)', '<input data-field="campaignId" value="' + UI.uuid() + '" readonly>')}
                    ${UI.field('Название', '<input data-field="name">')}
                    ${UI.field('ID мастера', '<input data-field="gameMasterId" value="' + Api.currentUser.userId + '" readonly>')}
                </div>`,
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Создать', className: 'btn btn-primary',
                    onClick: async (ev) => {
                        const box = ev.target.closest('.modal-box');
                        const data = UI.collectFields(box);
                        if (!data.name) { toast('Введите название', 'error'); return; }
                        try {
                            await Api.post('/api/campaign', {
                                campaignId: data.campaignId,
                                name: data.name,
                                gameMasterId: data.gameMasterId
                            });
                            closeModal();
                            Api.sessionId = data.campaignId;
                            toast('Кампания создана', 'success');
                            Store.setRoute('campaign');
                        } catch (e) { notifyError(e); }
                    }
                }
            ]
        });
    }

    window.Views = window.Views || {};
    window.Views.renderCampaignView = renderCampaignView;
})();