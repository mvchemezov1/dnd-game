// js/views/dev.js
(function () {
    async function renderDevView(root) {
        root.innerHTML = `
            <div class="card">
                <h2>Инструменты разработчика</h2>
                <div class="row">
                    <button class="btn" data-action="health">Проверить здоровье</button>
                    <button class="btn" data-action="scripts">Список скриптов</button>
                    <button class="btn" data-action="webhooks">Webhook'и</button>
                </div>
            </div>
            <div id="dev-output"></div>
        `;

        UI.bindActions(root, {
            health: () => loadData('/api/dev/health', 'Здоровье'),
            scripts: () => loadData('/api/dev/scripts', 'Скрипты'),
            webhooks: () => loadData('/api/dev/webhooks', 'Webhook\'и')
        });
    }

    async function loadData(url, title) {
        const out = document.querySelector('#dev-output');
        out.innerHTML = UI.loadingBlock();
        try {
            const data = await Api.get(url);
            out.innerHTML = `<div class="card"><h3>${title}</h3><pre>${JSON.stringify(data, null, 2)}</pre></div>`;
        } catch (e) {
            out.innerHTML = UI.emptyState('Ошибка загрузки');
            notifyError(e);
        }
    }

    window.Views = window.Views || {};
    window.Views.renderDevView = renderDevView;
})();