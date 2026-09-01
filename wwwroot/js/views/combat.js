// js/views/combat.js
// Общий трекер боя. Игрок видит бой и действует за своих участников,
// мастер/админ управляют всем боем целиком. Права реально проверяются
// на сервере — здесь лишь скрываются неуместные кнопки для удобства.
//
// Живые обновления через WebSocket. Если WebSocket недоступен,
// используется резервный поллинг раз в 4 секунды.

'use strict';

(function () {
    const g = UI.g;
    const COMBAT_ID_KEY = 'dnd.combatId';
    let pollTimer = null;
    let unsubscribeFns = [];

    // Функция генерации UUID (используется только если браузер не поддерживает crypto.randomUUID)
    function generateUuid() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
            const r = (Math.random() * 16) | 0;
            return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
        });
    }

    // Экспортируем утилиту генерации UUID, если её ещё нет
    if (!UI.uuid) {
        UI.uuid = generateUuid;
    }

    /**
     * Рендерит представление боевого трекера.
     * @param {HTMLElement} root - контейнер для отображения
     */
    function renderCombatView(root) {
        // Останавливаем предыдущий поллинг и отписываемся от событий
        stopPollingAndUnsubscribe();

        const user = Api.currentUser;
        const isDm = user && (user.role === 'GameMaster' || user.role === 'Admin');
        let combatId = localStorage.getItem(COMBAT_ID_KEY) || '';

        root.innerHTML = `
            <div class="card">
                <div class="row">
                    <h2 style="margin:0">Бой</h2>
                    <span class="spacer"></span>
                    ${isDm ? '<button class="btn btn-primary" data-action="new-combat">Начать новый бой</button>' : ''}
                </div>
                <div class="row" style="margin-top:10px">
                    <div class="field" style="flex:1">
                        ID боя (поделитесь им с игроками)
                        <input id="combat-id-input" value="${UI.esc(combatId)}" placeholder="вставьте GUID боя">
                    </div>
                    <button class="btn" data-action="open-combat">Открыть</button>
                </div>
            </div>
            <div id="combat-body"></div>
        `;

        UI.bindActions(root, {
            'new-combat': () => openNewCombatModal((id) => {
                combatId = id;
                root.querySelector('#combat-id-input').value = id;
                load();
            }),
            'open-combat': () => {
                const input = root.querySelector('#combat-id-input');
                const id = input.value.trim();
                if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(id)) {
                    toast('Введите корректный GUID боя', 'error');
                    return;
                }
                combatId = id;
                localStorage.setItem(COMBAT_ID_KEY, combatId);
                load();
            }
        });

        /**
         * Отписывается от всех WebSocket-событий и останавливает поллинг.
         */
        function stopPollingAndUnsubscribe() {
            if (pollTimer) {
                clearInterval(pollTimer);
                pollTimer = null;
            }
            unsubscribeFns.forEach(fn => fn());
            unsubscribeFns = [];
        }

        /**
         * Подписывается на события боя через WebSocket.
         * @param {string} combatIdToWatch - идентификатор боя
         * @param {Function} reloadFn - функция перезагрузки
         */
        function subscribeToCombatEvents(combatIdToWatch, reloadFn) {
            const events = [
                'CombatStarted', 'CombatEnded', 'InitiativeRolled',
                'CombatRoundStarted', 'CombatTurnStarted', 'CombatTurnEnded',
                'CombatActionTaken', 'CombatBonusActionTaken', 'CombatReactionUsed',
                'CombatMovementUsed', 'ParticipantAddedToCombat', 'ParticipantRemovedFromCombat',
                'ConditionAppliedToCombatant', 'ConditionRemovedFromCombatant',
                'CombatActionReadied', 'CombatReadiedActionTriggered'
            ];

            events.forEach(eventName => {
                if (window.GameSocket && typeof window.GameSocket.on === 'function') {
                    const unsub = window.GameSocket.on(eventName, (payload) => {
                        const eventCombatId = UI.g(payload, 'combatId');
                        if (eventCombatId && eventCombatId === combatIdToWatch) {
                            reloadFn();
                        }
                    });
                    unsubscribeFns.push(unsub);
                }
            });
        }

        async function load() {
            if (!combatId) return;
            localStorage.setItem(COMBAT_ID_KEY, combatId);

            const body = root.querySelector('#combat-body');
            if (!body) return;

            body.innerHTML = UI.loadingBlock('Загрузка боя…');
            try {
                const status = await Api.get(`/api/combat/${combatId}`);
                drawCombat(body, combatId, status, isDm, reload);

                // Подписка на WebSocket-события (если доступен)
                if (window.GameSocket && window.GameSocket.on) {
                    subscribeToCombatEvents(combatId, reload);
                } else {
                    // Если WebSocket недоступен, включаем поллинг как fallback
                    if (!pollTimer) {
                        pollTimer = setInterval(load, 4000);
                    }
                }
            } catch (e) {
                body.innerHTML = UI.emptyState('Бой не найден или ещё не начат.');
                stopPollingAndUnsubscribe();
            }
        }

        async function reload() {
            try {
                const status = await Api.get(`/api/combat/${combatId}`);
                const body = root.querySelector('#combat-body');
                if (body) {
                    drawCombat(body, combatId, status, isDm, reload);
                }
            } catch (e) {
                notifyError(e, 'Не удалось обновить бой');
            }
        }

        // Если у нас уже есть ID боя, сразу загружаем
        if (combatId) {
            load();
        }

        // Экспортируем функцию остановки, чтобы вызывать при уходе
        window.Views = window.Views || {};
        window.Views._stopCombatPolling = stopPollingAndUnsubscribe;
    }

    /**
     * Открывает модальное окно для создания нового боя.
     * @param {Function} onCreated - колбэк, вызывается с ID созданного боя
     */
    function openNewCombatModal(onCreated) {
        Api.get('/api/characters')
            .then(chars => {
                openModal({
                    title: 'Новый бой',
                    bodyHtml: `
                        <div class="stack">
                            <div class="small muted">Выберите участников боя:</div>
                            <div style="max-height:240px; overflow:auto" class="stack">
                                ${(chars || []).map(c => `
                                    <label class="row small">
                                        <input type="checkbox" value="${c.id}"> ${UI.esc(c.name)}
                                    </label>`).join('') || '<span class="muted">Нет персонажей</span>'}
                            </div>
                        </div>`,
                    actions: [
                        { label: 'Отмена', className: 'btn', onClick: closeModal },
                        {
                            label: 'Создать бой',
                            className: 'btn btn-primary',
                            onClick: async (ev) => {
                                const box = ev.target.closest('.modal-box');
                                const ids = Array.from(box.querySelectorAll('input[type=checkbox]:checked'))
                                    .map(i => i.value);
                                if (ids.length < 2) {
                                    toast('Выберите минимум двух участников', 'error');
                                    return;
                                }
                                const combatId = UI.uuid();
                                try {
                                    await Api.post('/api/combat', {
                                        combatId,
                                        participants: ids
                                    });
                                    closeModal();
                                    toast('Бой начат', 'success');
                                    onCreated(combatId);
                                } catch (e) {
                                    notifyError(e, 'Не удалось начать бой');
                                }
                            }
                        }
                    ]
                });
            })
            .catch(e => notifyError(e, 'Не удалось загрузить список персонажей'));
    }

    /**
     * Отрисовывает состояние боя.
     */
    function drawCombat(body, combatId, status, isDm, reload) {
        const participants = g(status, 'participants', []) || [];
        const round = g(status, 'round', '—');
        const currentName = g(status, 'currentTurnCharacterName', '—');
        const isActive = g(status, 'isActive', false);

        body.innerHTML = `
            <div class="card">
                <div class="row">
                    <div>Раунд <b>${round}</b></div>
                    <div>Текущий ход: <b>${UI.esc(currentName)}</b></div>
                    ${UI.pill(isActive ? 'Идёт' : 'Завершён', isActive ? 'success' : 'danger')}
                    <span class="spacer"></span>
                    ${isDm ? `
                        <button class="btn btn-sm" data-action="start-round">Начать раунд</button>
                        <button class="btn btn-sm" data-action="next-turn">Следующий ход</button>
                        <button class="btn btn-sm" data-action="end-round">Завершить раунд</button>
                        <button class="btn btn-sm btn-danger" data-action="end-combat">Завершить бой</button>
                    ` : ''}
                </div>
            </div>

            <div class="card">
                <h3>Участники</h3>
                ${participants.length ? participants.map(p => participantRow(p, status, isDm)).join('') : UI.emptyState('Нет участников')}
                ${isDm ? `
                    <div class="row" style="margin-top:10px">
                        <input id="add-part-id" placeholder="ID персонажа" style="width:260px">
                        <input id="add-part-init" type="number" placeholder="Инициатива" style="width:110px">
                        <button class="btn btn-sm" data-action="add-part">Добавить участника</button>
                    </div>` : ''}
            </div>

            <div class="card">
                <h3>Действие</h3>
                ${actionForm()}
            </div>
        `;

        UI.bindActions(body, {
            'start-round': () => act(() => Api.post(`/api/combat/${combatId}/rounds`), reload),
            'next-turn': () => act(() => Api.post(`/api/combat/${combatId}/turns/next`), reload),
            'end-round': () => act(() => Api.post(`/api/combat/${combatId}/rounds/end`), reload),
            'end-combat': async () => {
                if (await confirmDialog('Завершить бой?')) {
                    await act(() => Api.del(`/api/combat/${combatId}`), reload);
                }
            },
            'add-part': () => {
                const id = body.querySelector('#add-part-id').value.trim();
                const init = +body.querySelector('#add-part-init').value || 0;
                if (!id) {
                    toast('Введите ID персонажа', 'error');
                    return;
                }
                act(() => Api.post(`/api/combat/${combatId}/participants`, {
                    participantId: id,
                    initiative: init
                }), reload);
            },
            'remove-part': (el) => act(() => Api.del(`/api/combat/${combatId}/participants/${el.dataset.id}`), reload),
            'roll-init': (el) => {
                const roll = prompt('Результат броска d20 (без модификатора)');
                if (roll === null) return;
                const mod = prompt('Модификатор ловкости', '0');
                if (mod === null) return;
                const rollNum = +roll;
                if (isNaN(rollNum) || rollNum < 1 || rollNum > 20) {
                    toast('Бросок d20 должен быть от 1 до 20', 'error');
                    return;
                }
                act(() => Api.post(`/api/combat/${combatId}/initiative`, {
                    participantId: el.dataset.id,
                    initiativeRoll: rollNum,
                    dexterityModifier: +mod || 0
                }), reload);
            },
            'submit-action': () => submitAction(body, combatId, reload)
        });
    }

    /**
     * Генерирует HTML-разметку строки участника боя.
     */
    function participantRow(p, status, isDm) {
        const isCurrent = g(status, 'currentTurnCharacterName') === g(p, 'name');
        const conditions = g(p, 'conditions', []) || [];

        return `
            <div class="combat-turn ${isCurrent ? 'current' : ''}">
                <div>
                    <b>${UI.esc(g(p, 'name'))}</b> ${isCurrent ? UI.pill('ходит', 'warn') : ''}
                    <div class="small muted">
                        Иниц. ${g(p, 'initiative', '—')} · AC ${g(p, 'armorClass', '—')} · движение ${g(p, 'movementRemaining', '—')} фт
                    </div>
                    ${conditions.length ? UI.tagList(conditions) : ''}
                </div>
                <div style="min-width:160px">
                    ${UI.hpBar(g(p, 'currentHitPoints', 0), g(p, 'maxHitPoints', 0), g(p, 'temporaryHitPoints', 0))}
                </div>
                <div class="row small">
                    ${g(p, 'hasAction') ? UI.pill('действие') : ''}
                    ${g(p, 'hasBonusAction') ? UI.pill('бонус') : ''}
                    ${g(p, 'hasReaction') ? UI.pill('реакция') : ''}
                    ${g(p, 'concentrating') ? UI.pill('концентр.', 'warn') : ''}
                </div>
                ${isDm ? `
                    <div class="row">
                        <button class="btn btn-sm" data-action="roll-init" data-id="${g(p, 'characterId')}">Иниц.</button>
                        <button class="btn btn-sm btn-danger" data-action="remove-part" data-id="${g(p, 'characterId')}">Убрать</button>
                    </div>` : ''}
            </div>`;
    }

    /**
     * Обёртка для выполнения действия и перезагрузки.
     */
    function act(actionFn, reloadFn) {
        return Promise.resolve()
            .then(actionFn)
            .then(() => {
                toast('Готово', 'success');
                if (reloadFn) reloadFn();
            })
            .catch(e => notifyError(e, 'Действие не выполнено'));
    }

    /**
     * Генерирует HTML-форму для выбора действия.
     */
    function actionForm() {
        const options = [
            ['move', 'Движение'],
            ['standard', 'Стандартное действие'],
            ['bonus', 'Бонусное действие'],
            ['reaction', 'Реакция'],
            ['ready', 'Отложить действие'],
            ['trigger', 'Сработать отложенным'],
            ['delay', 'Задержать ход'],
            ['surrender', 'Сдаться'],
            ['damage', 'Нанести урон'],
            ['heal', 'Вылечить'],
            ['condition-add', 'Наложить состояние'],
            ['condition-remove', 'Снять состояние'],
            ['save', 'Спасбросок'],
            ['death-save', 'Спасбросок от смерти'],
            ['stabilize', 'Стабилизировать'],
            ['concentration', 'Проверка концентрации']
        ];

        return `
            <div class="grid grid-3">
                <div class="field">Тип действия
                    <select id="act-kind">
                        ${options.map(o => `<option value="${o[0]}">${o[1]}</option>`).join('')}
                    </select>
                </div>
                <div class="field">ID действующего<input id="act-participant" placeholder="participantId (= characterId)"></div>
                <div class="field">ID цели (если нужно)<input id="act-target"></div>
                <div class="field">Текст (тип действия/состояние/способность)<input id="act-text"></div>
                <div class="field">Доп. текст (условие срабатывания)<input id="act-text2"></div>
                <div class="field">Число (урон/фт/DC/раунды)<input id="act-num" type="number"></div>
                <div class="field">Число 2 (результат броска)<input id="act-num2" type="number"></div>
            </div>
            <div class="row" style="margin-top:10px">
                <button class="btn btn-primary" data-action="submit-action">Выполнить</button>
            </div>
            <div class="small muted" style="margin-top:6px">
                ID участников боя — это ID персонажей, добавленных в бой (см. список выше).
            </div>`;
    }

    /**
     * Обрабатывает отправку выбранного действия.
     */
    async function submitAction(container, combatId, reloadFn) {
        const kind = container.querySelector('#act-kind').value;
        const participantId = container.querySelector('#act-participant').value.trim();
        const targetId = container.querySelector('#act-target').value.trim() || undefined;
        const text = container.querySelector('#act-text').value.trim();
        const text2 = container.querySelector('#act-text2').value.trim();
        const num = parseFloat(container.querySelector('#act-num').value);
        const num2 = parseFloat(container.querySelector('#act-num2').value);

        if (!participantId) {
            toast('Укажите ID участника', 'error');
            return;
        }

        let requestBody = {};
        let endpoint = '';

        switch (kind) {
            case 'move':
                if (isNaN(num) || num <= 0) {
                    toast('Дистанция движения должна быть положительной', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/actions/move`;
                requestBody = { participantId, distanceFeet: num };
                break;

            case 'standard':
                endpoint = `/api/combat/${combatId}/actions/standard`;
                requestBody = { participantId, actionType: text, targetId };
                break;

            case 'bonus':
                endpoint = `/api/combat/${combatId}/actions/bonus`;
                requestBody = { participantId, actionType: text, targetId };
                break;

            case 'reaction':
                endpoint = `/api/combat/${combatId}/actions/reaction`;
                requestBody = { participantId, reactionType: text, triggerDescription: text2, targetId };
                break;

            case 'ready':
                endpoint = `/api/combat/${combatId}/actions/ready`;
                requestBody = { participantId, actionToReady: text, triggerCondition: text2 };
                break;

            case 'trigger':
                endpoint = `/api/combat/${combatId}/actions/trigger`;
                requestBody = { participantId };
                break;

            case 'delay':
                endpoint = `/api/combat/${combatId}/delay`;
                requestBody = { participantId };
                break;

            case 'surrender':
                endpoint = `/api/combat/${combatId}/surrender`;
                requestBody = { participantId };
                break;

            case 'damage':
                if (!targetId) {
                    toast('Укажите ID цели', 'error');
                    return;
                }
                if (isNaN(num) || num <= 0) {
                    toast('Урон должен быть положительным', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/damage`;
                requestBody = {
                    sourceParticipantId: participantId,
                    targetParticipantId: targetId,
                    damageAmount: num,
                    damageType: text || 'bludgeoning'
                };
                break;

            case 'heal':
                if (!targetId) {
                    toast('Укажите ID цели', 'error');
                    return;
                }
                if (isNaN(num) || num <= 0) {
                    toast('Лечение должно быть положительным', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/heal`;
                requestBody = {
                    sourceParticipantId: participantId,
                    targetParticipantId: targetId,
                    healingAmount: num
                };
                break;

            case 'condition-add':
                if (!text) {
                    toast('Укажите состояние', 'error');
                    return;
                }
                if (isNaN(num) || num <= 0) {
                    toast('Длительность должна быть положительной', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/conditions`;
                requestBody = {
                    targetParticipantId: targetId || participantId,
                    conditionType: text,
                    durationRounds: num
                };
                break;

            case 'condition-remove':
                if (!text) {
                    toast('Укажите состояние', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/conditions`;
                requestBody = {
                    targetParticipantId: targetId || participantId,
                    conditionType: text
                };
                break;

            case 'save':
                if (!text) {
                    toast('Укажите способность (например, Dexterity)', 'error');
                    return;
                }
                if (isNaN(num) || num <= 0) {
                    toast('DC должен быть положительным', 'error');
                    return;
                }
                if (isNaN(num2)) {
                    toast('Укажите результат броска', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/saving-throws`;
                requestBody = {
                    participantId,
                    ability: text,
                    difficultyClass: num,
                    rollResult: num2,
                    modifiers: 0
                };
                break;

            case 'death-save':
                if (isNaN(num) || num < 1 || num > 20) {
                    toast('Бросок d20 должен быть от 1 до 20', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/death-saves`;
                requestBody = { participantId, rollResult: num };
                break;

            case 'stabilize':
                endpoint = `/api/combat/${combatId}/stabilize`;
                requestBody = {
                    participantId,
                    stabilizedByParticipantId: targetId || participantId
                };
                break;

            case 'concentration':
                if (isNaN(num) || num <= 0) {
                    toast('DC должен быть положительным', 'error');
                    return;
                }
                if (isNaN(num2)) {
                    toast('Укажите результат броска', 'error');
                    return;
                }
                endpoint = `/api/combat/${combatId}/concentration`;
                requestBody = {
                    participantId,
                    difficultyClass: num,
                    rollResult: num2,
                    constitutionModifier: 0
                };
                break;

            default:
                toast('Неизвестный тип действия', 'error');
                return;
        }

        try {
            if (kind === 'condition-remove') {
                await Api.del(endpoint, requestBody);
            } else {
                await Api.post(endpoint, requestBody);
            }
            toast('Действие отправлено', 'success');
            reloadFn();
        } catch (e) {
            notifyError(e, 'Не удалось выполнить действие');
        }
    }

    // Экспорт в глобальную область видимости
    window.Views = window.Views || {};
    window.Views.renderCombatView = renderCombatView;
    window.Views._stopCombatPolling = () => {
        if (pollTimer) {
            clearInterval(pollTimer);
            pollTimer = null;
        }
        unsubscribeFns.forEach(fn => fn());
        unsubscribeFns = [];
    };
})();