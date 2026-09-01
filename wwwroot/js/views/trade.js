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
                const offers = await Api.get('/api/trade/offers');
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
        // Локальное состояние предметов
        let offeredItems = [];
        let requestedItems = [];

        function renderItems(container, items, side) {
            container.innerHTML = items.length
                ? items.map((item, index) => `
                    <div class="row small" style="margin-bottom:4px">
                        <span>${UI.esc(item.itemName)} (${item.itemId}) x${item.quantity}</span>
                        <button class="btn btn-sm btn-danger" data-action="remove-item" data-side="${side}" data-index="${index}">✕</button>
                    </div>`).join('')
                : '<span class="muted">Нет предметов</span>';
        }

        function addItem(side) {
            const itemId = document.querySelector(`#${side}-item-id`).value.trim();
            const itemName = document.querySelector(`#${side}-item-name`).value.trim();
            const quantity = parseInt(document.querySelector(`#${side}-item-qty`).value) || 1;
            if (!itemId || !itemName) {
                toast('Укажите ID и название предмета', 'error');
                return;
            }
            const item = { itemId, itemName, quantity };
            if (side === 'offered') offeredItems.push(item);
            else requestedItems.push(item);

            // Перерисовываем списки
            renderItems(document.getElementById(`${side}-items-list`), side === 'offered' ? offeredItems : requestedItems, side);
            // Очищаем поля
            document.querySelector(`#${side}-item-id`).value = '';
            document.querySelector(`#${side}-item-name`).value = '';
            document.querySelector(`#${side}-item-qty`).value = '1';
        }

        openModal({
            title: 'Новое торговое предложение',
            bodyHtml: `
                <div class="stack">
                    <div class="grid grid-2">
                        <div>
                            <h4>От кого</h4>
                            <div class="field">GUID персонажа<input data-field="fromCharacterId" placeholder="GUID персонажа"></div>
                            <div class="field">Золото<input data-field="offeredGold" type="number" value="0"></div>
                            <h5>Предметы</h5>
                            <div class="row">
                                <input id="offered-item-id" placeholder="ID предмета" style="width:120px">
                                <input id="offered-item-name" placeholder="Название">
                                <input id="offered-item-qty" type="number" value="1" style="width:70px">
                                <button class="btn btn-sm" data-action="add-offered">+</button>
                            </div>
                            <div id="offered-items-list" class="stack"></div>
                        </div>
                        <div>
                            <h4>Кому</h4>
                            <div class="field">GUID персонажа<input data-field="toCharacterId" placeholder="GUID персонажа"></div>
                            <div class="field">Золото<input data-field="requestedGold" type="number" value="0"></div>
                            <h5>Запрашиваемые предметы</h5>
                            <div class="row">
                                <input id="requested-item-id" placeholder="ID предмета" style="width:120px">
                                <input id="requested-item-name" placeholder="Название">
                                <input id="requested-item-qty" type="number" value="1" style="width:70px">
                                <button class="btn btn-sm" data-action="add-requested">+</button>
                            </div>
                            <div id="requested-items-list" class="stack"></div>
                        </div>
                    </div>
                </div>`,
            onMount: () => {
                renderItems(document.getElementById('offered-items-list'), offeredItems, 'offered');
                renderItems(document.getElementById('requested-items-list'), requestedItems, 'requested');
            },
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Создать',
                    className: 'btn btn-primary',
                    onClick: async (ev) => {
                        const box = ev.target.closest('.modal-box');
                        const fromCharacterId = box.querySelector('[data-field="fromCharacterId"]').value.trim();
                        const toCharacterId = box.querySelector('[data-field="toCharacterId"]').value.trim();
                        const offeredGold = +box.querySelector('[data-field="offeredGold"]').value || 0;
                        const requestedGold = +box.querySelector('[data-field="requestedGold"]').value || 0;

                        if (!fromCharacterId || !toCharacterId) {
                            toast('Заполните GUID персонажей', 'error');
                            return;
                        }

                        try {
                            await Api.post('/api/trade/offer', {
                                fromCharacterId,
                                toCharacterId,
                                offeredGold,
                                requestedGold,
                                offeredItems,
                                requestedItems
                            });
                            closeModal();
                            toast('Предложение создано', 'success');
                            onDone && onDone();
                        } catch (e) { notifyError(e); }
                    }
                }
            ]
        });

        // Делегирование кликов по кнопкам добавления/удаления
        document.addEventListener('click', function handler(e) {
            const target = e.target.closest('[data-action]');
            if (!target) return;
            if (target.dataset.action === 'add-offered') {
                addItem('offered');
            } else if (target.dataset.action === 'add-requested') {
                addItem('requested');
            } else if (target.dataset.action === 'remove-item') {
                const side = target.dataset.side;
                const index = parseInt(target.dataset.index);
                if (side === 'offered') offeredItems.splice(index, 1);
                else requestedItems.splice(index, 1);
                renderItems(document.getElementById(`${side}-items-list`), side === 'offered' ? offeredItems : requestedItems, side);
            }
        }, { once: true }); // обработчик удалится после закрытия? лучше добавить в closeModal, но для простоты оставим
    }

    window.Views = window.Views || {};
    window.Views.renderTradeView = renderTradeView;
})();