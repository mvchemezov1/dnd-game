// js/views/trade.js
(function () {
    async function renderTradeView(root) {
        root.innerHTML = `
            <div class="card">
                <h2>Торговля</h2>
                <button class="btn btn-primary" data-action="new-offer">+ Новое предложение</button>
                <button class="btn" data-action="refresh">Обновить</button>
            </div>
            <div id="offers-list">${UI.loadingBlock()}</div>
        `;

        UI.bindActions(root, {
            'new-offer': () => openNewOfferModal(loadOffers),
            'refresh': loadOffers
        });

        loadOffers();

        async function loadOffers() {
            const container = root.querySelector('#offers-list');
            try {
                const offersRaw = await Api.get('/api/trade/offers');
                const offers = Array.isArray(offersRaw) ? offersRaw : [];
                container.innerHTML = offers.length
                    ? `<table><thead><tr><th>От</th><th>Кому</th><th>Статус</th><th></th></tr></thead><tbody>
                        ${offers.map(o => `
                            <tr>
                                <td>${UI.esc(o.fromCharacterId)}</td>
                                <td>${UI.esc(o.toCharacterId)}</td>
                                <td>${UI.pill(o.status, statusClass(o.status))}</td>
                                <td>
                                    <button class="btn btn-sm" data-action="accept" data-id="${o.offerId}">Принять</button>
                                    <button class="btn btn-sm" data-action="decline" data-id="${o.offerId}">Отклонить</button>
                                </td>
                            </tr>`).join('')}
                    </tbody></table>`
                    : UI.emptyState('Нет предложений.');
            } catch (e) {
                container.innerHTML = UI.emptyState('Ошибка загрузки.');
                notifyError(e);
            }
        }

        function statusClass(status) {
            switch (status.toLowerCase()) {
                case 'accepted': return 'success';
                case 'declined': return 'danger';
                case 'cancelled': return 'info';
                default: return 'warn';
            }
        }
    }

    function openNewOfferModal(onDone) {
        // Упрощённая форма: выбор персонажей и предметов
        openModal({
            title: 'Новое торговое предложение',
            bodyHtml: `
                <div class="stack">
                    <div class="field">От кого<input data-field="fromCharacterId" placeholder="GUID персонажа"></div>
                    <div class="field">Кому<input data-field="toCharacterId" placeholder="GUID персонажа"></div>
                    <div class="field">Золото от отправителя<input data-field="offeredGold" type="number" value="0"></div>
                    <div class="field">Золото от получателя<input data-field="requestedGold" type="number" value="0"></div>
                </div>`,
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Создать', className: 'btn btn-primary', onClick: async (ev) => {
                        const d = UI.collectFields(ev.target.closest('.modal-box'));
                        // Валидация GUID и т.д.
                        if (!d.fromCharacterId || !d.toCharacterId) {
                            toast('Заполните GUID персонажей', 'error');
                            return;
                        }
                        try {
                            await Api.post('/api/trade/offer', {
                                fromCharacterId: d.fromCharacterId,
                                toCharacterId: d.toCharacterId,
                                offeredGold: +d.offeredGold || 0,
                                requestedGold: +d.requestedGold || 0,
                                offeredItems: [],  // пока без предметов
                                requestedItems: []
                            });
                            closeModal();
                            toast('Предложение создано', 'success');
                            onDone && onDone();
                        } catch (e) { notifyError(e); }
                    }
                }
            ]
        });
    }

    window.Views = window.Views || {};
    window.Views.renderTradeView = renderTradeView;
})();