// js/views/travel.js (дополнить)
(function () {
    async function renderTravelView(root) {
        root.innerHTML = `
            <div class="card">
                <h2>Путешествия</h2>
                <div class="row">
                    <input id="party-id" placeholder="ID группы" style="width:300px">
                    <input id="route-id" placeholder="ID маршрута" style="width:300px">
                    <button class="btn" data-action="start-journey">Начать</button>
                </div>
                <div class="row" style="margin-top:10px">
                    <input id="pace" value="Normal" style="width:150px">
                </div>
            </div>
            <div id="journey-status"></div>
        `;

        UI.bindActions(root, {
            'start-journey': () => {
                const partyId = root.querySelector('#party-id').value.trim();
                const routeId = root.querySelector('#route-id').value.trim();
                const pace = root.querySelector('#pace').value;
                if (!partyId || !routeId) { toast('Заполните ID', 'error'); return; }
                Api.post('/api/travel/journey/start', { partyId, routeId, pace })
                    .then(() => { toast('Путешествие начато', 'success'); updateStatus(); })
                    .catch(e => notifyError(e));
            }
        });

        function updateStatus() {
            // Здесь можно опрашивать состояние (если есть эндпоинт)
        }
    }

    window.Views = window.Views || {};
    window.Views.renderTravelView = renderTravelView;
})();