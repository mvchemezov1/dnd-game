// js/views/dm.js
// Панель мастера — быстрый обзор партии и мгновенные действия без захода
// в полный лист персонажа. Отражает функционал консольного DmUi
// (Party Status / Quick Actions / Spawn), но в виде удобного веб-интерфейса.

'use strict';

(function () {
    const g = UI.g; // безопасное получение поля с учётом camelCase/PascalCase

    /**
     * Рендерит панель мастера.
     * @param {HTMLElement} root - контейнер для отображения
     */
    async function renderDmView(root) {
        root.innerHTML = `
            <div class="note">
                Панель мастера: быстрый обзор всех персонажей и мгновенные действия.
                Для тонкой настройки конкретного персонажа откройте его полный лист во вкладке «Персонажи».
            </div>
            <div class="card">
                <div class="row">
                    <h2 style="margin:0">Пати — обзор</h2>
                    <span class="spacer"></span>
                    <button class="btn btn-primary" data-action="spawn">+ Заспавнить NPC/монстра</button>
                    <button class="btn" data-action="refresh">Обновить</button>
                </div>
            </div>
            <div id="party-list"></div>
        `;

        // Кнопки действий на верхней панели
        UI.bindActions(root, {
            spawn: () => Views.openSpawnModal(load),
            refresh: load
        });

        // Функция загрузки списка персонажей
        async function load() {
            const listEl = root.querySelector('#party-list');
            if (!listEl) return;
            listEl.innerHTML = UI.loadingBlock('Загрузка персонажей…');

            try {
                const chars = await Api.get('/api/characters');
                if (!chars || !chars.length) {
                    listEl.innerHTML = UI.emptyState('Персонажей нет.');
                    return;
                }
                listEl.innerHTML = chars.map(rowHtml).join('');

                // Делегирование действий внутри списка
                UI.bindActions(listEl, {
                    open: (el) => Store.setRoute('sheet', { id: el.dataset.id }),
                    damage: (el) => quickAction(el.dataset.id, 'damage'),
                    heal: (el) => quickAction(el.dataset.id, 'heal'),
                    condition: (el) => quickAction(el.dataset.id, 'condition')
                });
            } catch (e) {
                listEl.innerHTML = UI.emptyState('Не удалось загрузить список.');
                notifyError(e);
            }
        }

        // Генерация HTML строки для одного персонажа
        function rowHtml(c) {
            const id = g(c, 'id');
            const name = g(c, 'name');
            const level = g(c, 'level', 1);
            const race = g(c, 'race', '');
            const className = g(c, 'class', '');
            const hitPoints = g(c, 'hitPoints', 0);
            const maxHitPoints = g(c, 'maxHitPoints', 0);
            const armorClass = g(c, 'armorClass', '—');
            const isDead = g(c, 'isDead', false); // именно IsDead, а не IsAlive

            const status = isDead
                ? 'Мёртв'
                : (hitPoints <= 0 ? 'При смерти / стабилен' : 'Жив');
            const deadIcon = isDead ? ' 💀' : '';

            return `
                <div class="card" style="margin-bottom:10px">
                    <div class="row">
                        <div style="min-width:180px">
                            <b>${UI.esc(name)}${deadIcon}</b>
                            <div class="small muted">
                                Ур.${level} ${UI.esc(race)} ${UI.esc(className)} · ${status}
                            </div>
                        </div>
                        <div style="min-width:160px">${UI.hpBar(hitPoints, maxHitPoints)}</div>
                        <div class="small">${UI.pill('AC ' + armorClass)}</div>
                        <span class="spacer"></span>
                        <button class="btn btn-sm btn-danger" data-action="damage" data-id="${UI.esc(id)}">Урон</button>
                        <button class="btn btn-sm btn-success" data-action="heal" data-id="${UI.esc(id)}">Лечение</button>
                        <button class="btn btn-sm" data-action="condition" data-id="${UI.esc(id)}">Состояние</button>
                        <button class="btn btn-sm" data-action="open" data-id="${UI.esc(id)}">Открыть лист</button>
                    </div>
                </div>`;
        }

        /**
         * Выполняет быстрое действие (урон/лечение/состояние) с модальным окном.
         * @param {string} characterId - идентификатор персонажа
         * @param {string} kind - тип действия: 'damage', 'heal', 'condition'
         */
        function quickAction(characterId, kind) {
            if (kind === 'damage') {
                openModal({
                    title: 'Нанести урон',
                    bodyHtml: `
                        <div class="stack">
                            ${UI.field('Количество', '<input data-field="amount" type="number" min="1" value="1">')}
                            ${UI.field('Тип урона', '<input data-field="damageType" value="bludgeoning">')}
                        </div>`,
                    actions: [
                        { label: 'Отмена', className: 'btn', onClick: closeModal },
                        {
                            label: 'Применить',
                            className: 'btn btn-danger',
                            onClick: async (ev) => {
                                const d = UI.collectFields(ev.target.closest('.modal-box'));
                                const amount = +d.amount;
                                if (amount <= 0) {
                                    toast('Урон должен быть положительным', 'error');
                                    return;
                                }
                                try {
                                    await Api.post(`/api/characters/${characterId}/damage`, {
                                        characterId,
                                        amount,
                                        damageType: d.damageType || 'bludgeoning'
                                    });
                                    closeModal();
                                    toast('Урон нанесён', 'success');
                                    load();
                                } catch (e) {
                                    notifyError(e);
                                }
                            }
                        }
                    ]
                });
            } else if (kind === 'heal') {
                openModal({
                    title: 'Вылечить',
                    bodyHtml: `
                        <div class="stack">
                            ${UI.field('Количество', '<input data-field="amount" type="number" min="1" value="1">')}
                        </div>`,
                    actions: [
                        { label: 'Отмена', className: 'btn', onClick: closeModal },
                        {
                            label: 'Применить',
                            className: 'btn btn-success',
                            onClick: async (ev) => {
                                const d = UI.collectFields(ev.target.closest('.modal-box'));
                                const amount = +d.amount;
                                if (amount <= 0) {
                                    toast('Лечение должно быть положительным', 'error');
                                    return;
                                }
                                try {
                                    await Api.post(`/api/characters/${characterId}/heal`, {
                                        characterId,
                                        amount
                                    });
                                    closeModal();
                                    toast('Вылечено', 'success');
                                    load();
                                } catch (e) {
                                    notifyError(e);
                                }
                            }
                        }
                    ]
                });
            } else if (kind === 'condition') {
                openModal({
                    title: 'Наложить состояние',
                    bodyHtml: `
                        <div class="stack">
                            ${UI.field('Состояние', '<input data-field="conditionType" placeholder="Poisoned, Prone…">')}
                            ${UI.field('Длительность (раунды)', '<input data-field="durationRounds" type="number" min="1" value="1">')}
                        </div>`,
                    actions: [
                        { label: 'Отмена', className: 'btn', onClick: closeModal },
                        {
                            label: 'Применить',
                            className: 'btn btn-primary',
                            onClick: async (ev) => {
                                const d = UI.collectFields(ev.target.closest('.modal-box'));
                                if (!d.conditionType || !d.conditionType.trim()) {
                                    toast('Укажите состояние', 'error');
                                    return;
                                }
                                try {
                                    await Api.post(`/api/characters/${characterId}/conditions`, {
                                        characterId,
                                        conditionType: d.conditionType.trim(),
                                        durationRounds: +d.durationRounds || 1
                                    });
                                    closeModal();
                                    toast('Состояние наложено', 'success');
                                    load();
                                } catch (e) {
                                    notifyError(e);
                                }
                            }
                        }
                    ]
                });
            }
        }

        // Первичная загрузка
        await load();
    }

    /**
     * Открывает модальное окно спавна NPC/монстра.
     * @param {Function} onDone - колбэк после успешного создания
     */
    function openSpawnModal(onDone) {
        openModal({
            title: 'Заспавнить NPC / монстра',
            bodyHtml: `
                <div class="stack">
                    <div class="note" style="margin:0">
                        Отдельного эндпоинта для NPC в бэкенде нет — создаётся обычный персонаж,
                        которого мастер затем использует как NPC/монстра.
                    </div>
                    ${UI.field('Имя', '<input data-field="name" autofocus>')}
                    ${UI.field('Максимум HP', '<input data-field="maxHitPoints" type="number" min="1" value="10">')}
                </div>`,
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Заспавнить',
                    className: 'btn btn-primary',
                    onClick: async (ev) => {
                        const d = UI.collectFields(ev.target.closest('.modal-box'));
                        if (!d.name || !d.name.trim()) {
                            toast('Введите имя', 'error');
                            return;
                        }
                        const maxHp = +d.maxHitPoints || 10;
                        if (maxHp <= 0) {
                            toast('Максимальные хиты должны быть положительными', 'error');
                            return;
                        }
                        try {
                            // Бэкенд сам генерирует ID, поэтому characterId не отправляем
                            await Api.post('/api/characters', {
                                name: d.name.trim(),
                                maxHitPoints: maxHp
                            });
                            closeModal();
                            toast('NPC создан', 'success');
                            if (typeof onDone === 'function') {
                                onDone();
                            }
                        } catch (e) {
                            notifyError(e);
                        }
                    }
                }
            ]
        });
    }

    // Экспорт функций
    window.Views = window.Views || {};
    window.Views.renderDmView = renderDmView;
    window.Views.openSpawnModal = openSpawnModal;
})();