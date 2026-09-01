// js/views/sheet.js
// Полный лист персонажа: HP/AC, характеристики, навыки, заклинания, инвентарь,
// состояния, смерть/отдых. Все действия выполняются через REST API CharactersController.

'use strict';

(function () {
    const g = UI.g;
    const ABILITIES = ['Strength', 'Dexterity', 'Constitution', 'Intelligence', 'Wisdom', 'Charisma'];
    const ABIL_RU = {
        Strength: 'Сила',
        Dexterity: 'Ловкость',
        Constitution: 'Телосложение',
        Intelligence: 'Интеллект',
        Wisdom: 'Мудрость',
        Charisma: 'Харизма'
    };
    const SKILLS = [
        'Acrobatics', 'AnimalHandling', 'Arcana', 'Athletics', 'Deception',
        'History', 'Insight', 'Intimidation', 'Investigation', 'Medicine',
        'Nature', 'Perception', 'Performance', 'Persuasion', 'Religion',
        'SleightOfHand', 'Stealth', 'Survival'
    ];

    // Текущая вкладка листа персонажа
    let activeTab = 'overview';

    /**
     * Генерирует уникальный идентификатор (UUID v4).
     */
    function uuid() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }
        // Fallback для старых браузеров
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            const r = (Math.random() * 16) | 0;
            const v = c === 'x' ? r : (r & 0x3) | 0x8;
            return v.toString(16);
        });
    }

    /**
     * Главная функция рендеринга листа персонажа.
     * @param {HTMLElement} root - корневой контейнер
     * @param {Object} params - параметры маршрута, должно содержать id персонажа
     */
    async function renderSheetView(root, params) {
        const id = params?.id;
        if (!id) {
            root.innerHTML = UI.emptyState('Персонаж не выбран.');
            return;
        }

        // Сохраняем ID в localStorage, чтобы крафт и другие модули могли его использовать
        localStorage.setItem('dnd.selectedCharacterId', id);

        root.innerHTML = UI.loadingBlock('Загрузка персонажа…');

        let char;
        try {
            char = await Api.get(`/api/characters/${id}`);
        } catch (e) {
            root.innerHTML = UI.emptyState('Не удалось загрузить персонажа.');
            notifyError(e);
            return;
        }

        if (!char) {
            root.innerHTML = UI.emptyState('Персонаж не найден.');
            return;
        }

        // Функция обновления данных персонажа и перерисовки
        async function reload() {
            try {
                char = await Api.get(`/api/characters/${id}`);
                draw();
            } catch (e) {
                notifyError(e, 'Не удалось обновить данные');
            }
        }

        /**
         * Отрисовывает весь лист и активную вкладку.
         */
        function draw() {
            const hp = g(char, 'hitPoints', 0);
            const maxHp = g(char, 'maxHitPoints', 0);
            const temp = g(char, 'temporaryHitPoints', 0);
            const isDead = g(char, 'isDead', false);
            const isStable = g(char, 'isStable', false);

            // Общая шапка
            let statusPill = '';
            if (isDead) {
                statusPill = UI.pill('Мёртв', 'danger');
            } else if (hp <= 0) {
                statusPill = UI.pill(isStable ? 'Стабилен (0 HP)' : 'При смерти', 'warn');
            }

            root.innerHTML = `
                <div class="row" style="margin-bottom:10px">
                    <button class="btn btn-sm" data-action="back">← К списку</button>
                    <span class="spacer"></span>
                    ${statusPill}
                </div>
                <div class="card">
                    <div class="row">
                        <div>
                            <h2 style="margin:0">${UI.esc(g(char, 'name'))}</h2>
                            <div class="muted small">
                                Ур. ${g(char, 'level', 1)} · ${UI.esc(g(char, 'race') || '—')} ${UI.esc(g(char, 'class') || '')} ${g(char, 'background') ? '· ' + UI.esc(g(char, 'background')) : ''}
                            </div>
                        </div>
                        <span class="spacer"></span>
                        <div style="min-width:200px">${UI.hpBar(hp, maxHp, temp)}</div>
                    </div>
                </div>
                <div class="tabs-inline" id="sheet-tabs">
                    ${tabBtn('overview', 'Обзор')}
                    ${tabBtn('abilities', 'Характеристики')}
                    ${tabBtn('skills', 'Навыки и спасброски')}
                    ${tabBtn('spells', 'Заклинания')}
                    ${tabBtn('inventory', 'Инвентарь и экипировка')}
                    ${tabBtn('conditions', 'Состояния и защита')}
                    ${tabBtn('vitals', 'Смерть, отдых, опыт')}
                </div>
                <div id="sheet-tab-body"></div>
            `;

            // Обработчик возврата к списку
            root.querySelector('[data-action="back"]').addEventListener('click', () => {
                Store.setRoute('characters');
            });

            // Обработчики переключения вкладок
            root.querySelectorAll('#sheet-tabs button').forEach(btn => {
                btn.addEventListener('click', () => {
                    activeTab = btn.dataset.tab;
                    draw();
                });
            });

            drawTab();
        }

        /**
         * Возвращает HTML-кнопку вкладки.
         */
        function tabBtn(key, label) {
            return `<button class="${activeTab === key ? 'active' : ''}" data-tab="${key}">${label}</button>`;
        }

        /**
         * Рисует содержимое активной вкладки.
         */
        function drawTab() {
            const body = document.getElementById('sheet-tab-body');
            if (!body) return;

            // Актуализируем активную кнопку
            root.querySelectorAll('#sheet-tabs button').forEach(btn => {
                btn.classList.toggle('active', btn.dataset.tab === activeTab);
            });

            switch (activeTab) {
                case 'overview': drawOverview(body); break;
                case 'abilities': drawAbilities(body); break;
                case 'skills': drawSkills(body); break;
                case 'spells': drawSpells(body); break;
                case 'inventory': drawInventory(body); break;
                case 'conditions': drawConditions(body); break;
                case 'vitals': drawVitals(body); break;
                default: body.innerHTML = UI.emptyState('Неизвестная вкладка');
            }
        }

        // ===================== ВКЛАДКА: ОБЗОР =====================
        function drawOverview(body) {
            body.innerHTML = `
                <div class="grid grid-2">
                    <div class="card">
                        <h3>Здоровье</h3>
                        <div class="row">
                            <div class="field">Урон<input id="dmg-amt" type="number" min="1" value="1"></div>
                            <div class="field">Тип<input id="dmg-type" value="bludgeoning"></div>
                            <button class="btn btn-danger" data-action="dmg">Нанести урон</button>
                        </div>
                        <div class="row">
                            <div class="field">Лечение<input id="heal-amt" type="number" min="1" value="1"></div>
                            <button class="btn btn-success" data-action="heal">Вылечить</button>
                        </div>
                        <div class="row">
                            <div class="field">Временные HP<input id="temp-amt" type="number" min="0" value="${g(char, 'temporaryHitPoints', 0)}"></div>
                            <button class="btn" data-action="temp">Установить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Параметры</h3>
                        <div class="row">
                            <div class="field">Класс брони<input id="ac-val" type="number" min="0" value="${g(char, 'armorClass', 10)}"></div>
                            <button class="btn btn-sm" data-action="ac">Сохранить</button>
                        </div>
                        <div class="row">
                            <div class="field">Скорость (фт)<input id="speed-val" type="number" min="0" value="${g(char, 'speed', 30)}"></div>
                            <button class="btn btn-sm" data-action="speed">Сохранить</button>
                        </div>
                        <div class="row">
                            <div class="field">Максимум HP<input id="maxhp-val" type="number" min="1" value="${g(char, 'maxHitPoints', 10)}"></div>
                            <button class="btn btn-sm" data-action="maxhp">Сохранить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Раса / класс / предыстория</h3>
                        <div class="row">
                            <div class="field">Раса<input id="race-val" value="${UI.esc(g(char, 'race') || '')}"></div>
                            <button class="btn btn-sm" data-action="race">Сохранить</button>
                        </div>
                        <div class="row">
                            <div class="field">Класс<input id="class-val" value="${UI.esc(g(char, 'class') || '')}"></div>
                            <button class="btn btn-sm" data-action="class">Сохранить</button>
                        </div>
                        <div class="row">
                            <div class="field">Предыстория<input id="bg-val" value="${UI.esc(g(char, 'background') || '')}"></div>
                            <button class="btn btn-sm" data-action="bg">Сохранить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Прочее</h3>
                        <div class="row"><span class="muted">Бонус мастерства:</span> ${UI.pill('+' + g(char, 'proficiencyBonus', 2))}</div>
                        <div class="row"><span class="muted">Золото:</span> ${UI.pill(g(char, 'gold', 0))}</div>
                        <div class="row"><span class="muted">Концентрация:</span> ${g(char, 'concentrating', false) ? UI.pill('активна', 'warn') : UI.pill('нет')}</div>
                    </div>
                </div>
            `;

            UI.bindActions(body, {
                dmg: () => {
                    const amount = +body.querySelector('#dmg-amt').value;
                    if (!amount || amount <= 0) { toast('Урон должен быть положительным', 'error'); return; }
                    const type = body.querySelector('#dmg-type').value.trim() || 'bludgeoning';
                    act(() => Api.post(`/api/characters/${id}/damage`, { characterId: id, amount, damageType: type }));
                },
                heal: () => {
                    const amount = +body.querySelector('#heal-amt').value;
                    if (!amount || amount <= 0) { toast('Лечение должно быть положительным', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/heal`, { characterId: id, amount }));
                },
                temp: () => {
                    const amount = +body.querySelector('#temp-amt').value;
                    if (amount < 0) { toast('Временные хиты не могут быть отрицательными', 'error'); return; }
                    act(() => Api.put(`/api/characters/${id}/temporary-hit-points`, { characterId: id, amount }));
                },
                ac: () => {
                    const ac = +body.querySelector('#ac-val').value;
                    if (ac < 0) { toast('Класс брони не может быть отрицательным', 'error'); return; }
                    act(() => Api.put(`/api/characters/${id}/armor-class`, { characterId: id, newArmorClass: ac }));
                },
                speed: () => {
                    const speed = +body.querySelector('#speed-val').value;
                    if (speed < 0) { toast('Скорость не может быть отрицательной', 'error'); return; }
                    act(() => Api.put(`/api/characters/${id}/speed`, { characterId: id, newSpeed: speed }));
                },
                maxhp: () => {
                    const maxHp = +body.querySelector('#maxhp-val').value;
                    if (maxHp <= 0) { toast('Максимальные хиты должны быть положительными', 'error'); return; }
                    act(() => Api.put(`/api/characters/${id}`, { name: null, maxHitPoints: maxHp }));
                },
                race: () => {
                    const race = body.querySelector('#race-val').value.trim();
                    if (!race) { toast('Укажите расу', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/race`, { characterId: id, raceId: race }));
                },
                class: () => {
                    const className = body.querySelector('#class-val').value.trim();
                    if (!className) { toast('Укажите класс', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/class`, { characterId: id, classId: className }));
                },
                bg: () => {
                    const background = body.querySelector('#bg-val').value.trim();
                    if (!background) { toast('Укажите предысторию', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/background`, { characterId: id, backgroundId: background }));
                },
            });
        }

        // ===================== ВКЛАДКА: ХАРАКТЕРИСТИКИ =====================
        function drawAbilities(body) {
            const scores = g(char, 'abilityScores', {}) || {};
            body.innerHTML = `<div class="card"><h3>Характеристики</h3>
                <div class="grid grid-3">
                    ${ABILITIES.map(a => {
                const score = scores[a] ?? scores[a.toLowerCase()] ?? 10;
                const mod = Math.floor((score - 10) / 2);
                return `
                            <div class="field">
                                ${ABIL_RU[a]} (${mod >= 0 ? '+' : ''}${mod})
                                <div class="row">
                                    <input type="number" id="ab-${a}" min="1" max="30" value="${score}">
                                    <button class="btn btn-sm" data-action="save-${a}">✓</button>
                                </div>
                            </div>`;
            }).join('')}
                </div>
            </div>`;

            const handlers = {};
            ABILITIES.forEach(a => {
                handlers['save-' + a] = () => {
                    const score = +body.querySelector('#ab-' + a).value;
                    if (score < 1 || score > 30) {
                        toast('Значение характеристики должно быть от 1 до 30', 'error');
                        return;
                    }
                    act(() => Api.put(`/api/characters/${id}/ability-scores/${a}`, { score }));
                };
            });
            UI.bindActions(body, handlers);
        }

        // ===================== ВКЛАДКА: НАВЫКИ И СПАСБРОСКИ =====================
        function drawSkills(body) {
            const skillProf = Array.isArray(g(char, 'skillProficiencies', [])) ? g(char, 'skillProficiencies', []) : [];
            const saveProf = Array.isArray(g(char, 'savingThrowProficiencies', [])) ? g(char, 'savingThrowProficiencies', []) : [];
            const feats = Array.isArray(g(char, 'feats', [])) ? g(char, 'feats', []) : [];

            // Опции для селекта невыбранных навыков
            const availableSkills = SKILLS.filter(s => !skillProf.includes(s));
            const availableSaves = ABILITIES.filter(a => !saveProf.includes(a));

            body.innerHTML = `
                <div class="grid grid-2">
                    <div class="card">
                        <h3>Владение навыками</h3>
                        <div class="tag-list" style="margin-bottom:12px">
                            ${skillProf.map(s => `<span class="tag">${UI.esc(s)}<button data-action="rm-skill" data-name="${UI.esc(s)}">✕</button></span>`).join('') || '<span class="muted small">Нет владений</span>'}
                        </div>
                        <div class="row">
                            <select id="skill-select" ${availableSkills.length === 0 ? 'disabled' : ''}>
                                ${availableSkills.map(s => `<option value="${s}">${s}</option>`).join('')}
                            </select>
                            <button class="btn btn-sm" data-action="add-skill" ${availableSkills.length === 0 ? 'disabled' : ''}>Добавить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Спасброски</h3>
                        <div class="tag-list" style="margin-bottom:12px">
                            ${saveProf.map(s => `<span class="tag">${UI.esc(ABIL_RU[s] || s)}<button data-action="rm-save" data-name="${UI.esc(s)}">✕</button></span>`).join('') || '<span class="muted small">Нет владений</span>'}
                        </div>
                        <div class="row">
                            <select id="save-select" ${availableSaves.length === 0 ? 'disabled' : ''}>
                                ${availableSaves.map(a => `<option value="${a}">${ABIL_RU[a]}</option>`).join('')}
                            </select>
                            <button class="btn btn-sm" data-action="add-save" ${availableSaves.length === 0 ? 'disabled' : ''}>Добавить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Черты</h3>
                        <div class="tag-list" style="margin-bottom:12px">
                            ${feats.map(f => `<span class="tag">${UI.esc(f)}<button data-action="rm-feat" data-name="${UI.esc(f)}">✕</button></span>`).join('') || '<span class="muted small">Нет черт</span>'}
                        </div>
                        <div class="row">
                            <input id="feat-input" placeholder="Название черты">
                            <button class="btn btn-sm" data-action="add-feat">Добавить</button>
                        </div>
                    </div>
                </div>
            `;

            UI.bindActions(body, {
                'add-skill': () => {
                    const select = body.querySelector('#skill-select');
                    if (select.selectedIndex === -1) return;
                    act(() => Api.post(`/api/characters/${id}/skills/${select.value}`));
                },
                'rm-skill': (el) => act(() => Api.del(`/api/characters/${id}/skills/${encodeURIComponent(el.dataset.name)}`)),
                'add-save': () => {
                    const select = body.querySelector('#save-select');
                    if (select.selectedIndex === -1) return;
                    act(() => Api.post(`/api/characters/${id}/saving-throws/${select.value}`));
                },
                'rm-save': (el) => act(() => Api.del(`/api/characters/${id}/saving-throws/${encodeURIComponent(el.dataset.name)}`)),
                'add-feat': () => {
                    const name = body.querySelector('#feat-input').value.trim();
                    if (!name) { toast('Укажите название черты', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/feats`, { characterId: id, featId: name }));
                },
                'rm-feat': (el) => act(() => Api.del(`/api/characters/${id}/feats/${encodeURIComponent(el.dataset.name)}`)),
            });
        }

        // ===================== ВКЛАДКА: ЗАКЛИНАНИЯ =====================
        function drawSpells(body) {
            const known = g(char, 'knownSpells', []) || [];
            const maxSlots = g(char, 'maxSpellSlots', {}) || {};
            const usedSlots = g(char, 'usedSpellSlots', {}) || {};
            const slotLevels = Object.keys(maxSlots).filter(lvl => (maxSlots[lvl] || 0) > 0);

            body.innerHTML = `
                <div class="grid grid-2">
                    <div class="card">
                        <h3>Известные заклинания</h3>
                        <div class="tag-list" style="margin-bottom:12px">
                            ${known.map(s => `<span class="tag">${UI.esc(s)}<button data-action="rm-spell" data-name="${UI.esc(s)}">✕</button></span>`).join('') || '<span class="muted small">Нет заклинаний</span>'}
                        </div>
                        <div class="row">
                            <input id="spell-input" placeholder="ID заклинания">
                            <button class="btn btn-sm" data-action="add-spell">Добавить</button>
                            <button class="btn btn-sm" data-action="prep-spell">Подготовить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Ячейки заклинаний</h3>
                        <table>
                            <thead><tr><th>Уровень</th><th>Исп./Макс</th><th></th></tr></thead>
                            <tbody>
                                ${slotLevels.map(lvl => `
                                    <tr>
                                        <td>${lvl}</td>
                                        <td>${usedSlots[lvl] ?? 0} / ${maxSlots[lvl] ?? 0}</td>
                                        <td><button class="btn btn-sm" data-action="use-slot" data-lvl="${lvl}">Использовать</button></td>
                                    </tr>`).join('')}
                            </tbody>
                        </table>
                        <div class="row" style="margin-top:10px">
                            <button class="btn btn-sm" data-action="restore-slots">Восстановить все ячейки</button>
                        </div>
                    </div>
                </div>
            `;

            UI.bindActions(body, {
                'add-spell': () => {
                    const spellId = body.querySelector('#spell-input').value.trim();
                    if (!spellId) { toast('Укажите ID заклинания', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/spells`, { characterId: id, spellId }));
                },
                'prep-spell': () => {
                    const spellId = body.querySelector('#spell-input').value.trim();
                    if (!spellId) { toast('Укажите ID заклинания', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/spells/prepare`, { characterId: id, spellId }));
                },
                'rm-spell': (el) => act(() => Api.del(`/api/characters/${id}/spells/${encodeURIComponent(el.dataset.name)}`)),
                'use-slot': (el) => {
                    const lvl = +el.dataset.lvl;
                    act(() => Api.post(`/api/characters/${id}/spell-slots/use`, { characterId: id, slotLevel: lvl }));
                },
                'restore-slots': () => act(() => Api.post(`/api/characters/${id}/spell-slots/restore`)),
            });
        }

        // ===================== ВКЛАДКА: ИНВЕНТАРЬ И ЭКИПИРОВКА =====================
        function drawInventory(body) {
            const inv = g(char, 'inventory', []) || [];
            const equip = g(char, 'equipment', []) || [];

            body.innerHTML = `
                <div class="grid grid-2">
                    <div class="card">
                        <h3>Инвентарь</h3>
                        <table>
                            <thead><tr><th>Предмет</th><th>Кол-во</th><th></th></tr></thead>
                            <tbody>
                                ${inv.length ? inv.map(i => `
                                    <tr>
                                        <td>${UI.esc(g(i, 'name'))}</td>
                                        <td>${g(i, 'quantity')}</td>
                                        <td><button class="btn btn-sm btn-danger" data-action="rm-item" data-id="${UI.esc(g(i, 'itemId'))}">Убрать</button></td>
                                    </tr>`).join('') : '<tr><td colspan="3" class="muted">Пусто</td></tr>'}
                            </tbody>
                        </table>
                        <div class="row" style="margin-top:10px">
                            <input id="item-id" placeholder="ID предмета" style="width:110px">
                            <input id="item-name" placeholder="Название">
                            <input id="item-qty" type="number" min="1" value="1" style="width:70px">
                            <button class="btn btn-sm" data-action="add-item">Добавить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Экипировка</h3>
                        <table>
                            <thead><tr><th>Слот</th><th>Предмет</th><th></th></tr></thead>
                            <tbody>
                                ${equip.length ? equip.map(i => `
                                    <tr>
                                        <td>${UI.esc(g(i, 'slot'))}</td>
                                        <td>${UI.esc(g(i, 'name'))}</td>
                                        <td><button class="btn btn-sm" data-action="unequip" data-id="${UI.esc(g(i, 'itemId'))}">Снять</button></td>
                                    </tr>`).join('') : '<tr><td colspan="3" class="muted">Ничего не надето</td></tr>'}
                            </tbody>
                        </table>
                        <div class="stack" style="margin-top:10px">
                            <div class="row">
                                <input id="eq-id" placeholder="ID предмета" style="width:110px">
                                <input id="eq-name" placeholder="Название">
                            </div>
                            <div class="row">
                                <input id="eq-slot" placeholder="Слот (MainHand, Armor…)">
                                <input id="eq-armor" type="number" placeholder="Бонус AC" value="0" style="width:90px">
                                <input id="eq-dmg" type="number" placeholder="Бонус урона" value="0" style="width:100px">
                                <button class="btn btn-sm" data-action="equip">Надеть</button>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            UI.bindActions(body, {
                'add-item': () => {
                    const itemId = body.querySelector('#item-id').value.trim() || uuid();
                    const name = body.querySelector('#item-name').value.trim();
                    const qty = +body.querySelector('#item-qty').value || 1;
                    if (!name) { toast('Укажите название предмета', 'error'); return; }
                    if (qty <= 0) { toast('Количество должно быть положительным', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/inventory`, { characterId: id, itemId, itemName: name, quantity: qty }));
                },
                'rm-item': (el) => {
                    const itemId = el.dataset.id;
                    if (!itemId) return;
                    act(() => Api.del(`/api/characters/${id}/inventory/${encodeURIComponent(itemId)}?quantity=1`));
                },
                'equip': () => {
                    const itemId = body.querySelector('#eq-id').value.trim() || uuid();
                    const name = body.querySelector('#eq-name').value.trim();
                    const slot = body.querySelector('#eq-slot').value.trim();
                    if (!name || !slot) { toast('Укажите название и слот', 'error'); return; }
                    const armorBonus = +body.querySelector('#eq-armor').value || 0;
                    const damageBonus = +body.querySelector('#eq-dmg').value || 0;
                    act(() => Api.post(`/api/characters/${id}/equip`, {
                        characterId: id,
                        itemId,
                        slot,
                        itemName: name,
                        armorBonus,
                        damageBonus
                    }));
                },
                'unequip': (el) => {
                    const itemId = el.dataset.id;
                    if (!itemId) return;
                    act(() => Api.post(`/api/characters/${id}/unequip`, { characterId: id, itemId }));
                },
            });
        }

        // ===================== ВКЛАДКА: СОСТОЯНИЯ И ЗАЩИТА =====================
        function drawConditions(body) {
            const conditions = g(char, 'conditions', []) || [];
            const resistances = g(char, 'resistances', []) || [];
            const vulnerabilities = g(char, 'vulnerabilities', []) || [];
            const immunities = g(char, 'immunities', []) || [];

            body.innerHTML = `
                <div class="grid grid-2">
                    <div class="card">
                        <h3>Состояния</h3>
                        <div class="tag-list" style="margin-bottom:12px">
                            ${conditions.map(c => `<span class="tag">${UI.esc(c)}<button data-action="rm-cond" data-name="${UI.esc(c)}">✕</button></span>`).join('') || '<span class="muted small">Нет состояний</span>'}
                        </div>
                        <div class="row">
                            <input id="cond-name" placeholder="Состояние (Prone, Poisoned…)">
                            <input id="cond-dur" type="number" min="1" placeholder="раунды" value="1" style="width:80px">
                            <button class="btn btn-sm" data-action="add-cond">Наложить</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Сопротивления и иммунитеты</h3>
                        <div class="small muted">Сопротивления:</div>
                        ${UI.tagList(resistances)}
                        <div class="small muted" style="margin-top:8px">Уязвимости:</div>
                        ${UI.tagList(vulnerabilities)}
                        <div class="small muted" style="margin-top:8px">Иммунитеты:</div>
                        ${UI.tagList(immunities)}
                    </div>
                </div>
            `;

            UI.bindActions(body, {
                'add-cond': () => {
                    const name = body.querySelector('#cond-name').value.trim();
                    const dur = +body.querySelector('#cond-dur').value || 1;
                    if (!name) { toast('Укажите состояние', 'error'); return; }
                    if (dur <= 0) { toast('Длительность должна быть положительной', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/conditions`, { characterId: id, conditionType: name, durationRounds: dur }));
                },
                'rm-cond': (el) => {
                    const name = el.dataset.name;
                    if (!name) return;
                    act(() => Api.del(`/api/characters/${id}/conditions/${encodeURIComponent(name)}`));
                },
            });
        }

        // ===================== ВКЛАДКА: СМЕРТЬ, ОТДЫХ, ОПЫТ, ПЕРЕМЕЩЕНИЕ =====================
        function drawVitals(body) {
            const currentLevel = g(char, 'level', 1);
            body.innerHTML = `
                <div class="grid grid-2">
                    <div class="card">
                        <h3>Спасброски от смерти</h3>
                        <div class="row small muted">Успехи: ${g(char, 'deathSaveSuccesses', 0)} · Провалы: ${g(char, 'deathSaveFailures', 0)}</div>
                        <div class="row">
                            <input id="ds-roll" type="number" min="1" max="20" placeholder="Результат броска d20" style="width:170px">
                            <button class="btn btn-sm" data-action="ds-roll">Записать бросок</button>
                            <button class="btn btn-sm" data-action="stabilize">Стабилизировать</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Отдых</h3>
                        <div class="row">
                            <button class="btn btn-sm" data-action="rest-short">Начать короткий отдых</button>
                            <button class="btn btn-sm" data-action="rest-long">Начать длинный отдых</button>
                            <button class="btn btn-sm" data-action="rest-end">Завершить отдых</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Опыт и уровень</h3>
                        <div class="row small muted">Текущий опыт: ${g(char, 'experiencePoints', 0)}</div>
                        <div class="row">
                            <input id="xp-amt" type="number" min="1" placeholder="+опыт" style="width:110px">
                            <button class="btn btn-sm" data-action="gain-xp">Начислить</button>
                        </div>
                        <div class="row">
                            <input id="lvl-val" type="number" min="${currentLevel + 1}" max="20" placeholder="новый уровень" value="${currentLevel + 1}" style="width:130px">
                            <button class="btn btn-sm" data-action="level-up">Повысить уровень</button>
                        </div>
                    </div>
                    <div class="card">
                        <h3>Перемещение по карте</h3>
                        <div class="row">
                            <input id="move-x" type="number" min="0" placeholder="X" style="width:80px">
                            <input id="move-y" type="number" min="0" placeholder="Y" style="width:80px">
                            <button class="btn btn-sm" data-action="move">Переместить</button>
                        </div>
                    </div>
                </div>
            `;

            UI.bindActions(body, {
                'ds-roll': () => {
                    const roll = +body.querySelector('#ds-roll').value;
                    if (roll < 1 || roll > 20) { toast('Бросок d20 должен быть от 1 до 20', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/death-saves`, { characterId: id, rollResult: roll }));
                },
                'stabilize': () => act(() => Api.post(`/api/characters/${id}/stabilize`)),
                'rest-short': () => act(() => Api.post(`/api/characters/${id}/rest/start`, { characterId: id, restType: 'Short' })),
                'rest-long': () => act(() => Api.post(`/api/characters/${id}/rest/start`, { characterId: id, restType: 'Long' })),
                'rest-end': () => act(() => Api.post(`/api/characters/${id}/rest/end`)),
                'gain-xp': () => {
                    const amt = +body.querySelector('#xp-amt').value;
                    if (amt <= 0) { toast('Опыт должен быть положительным', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/experience`, { characterId: id, experiencePoints: amt }));
                },
                'level-up': () => {
                    const newLevel = +body.querySelector('#lvl-val').value;
                    if (newLevel <= currentLevel) { toast('Новый уровень должен быть больше текущего', 'error'); return; }
                    if (newLevel > 20) { toast('Максимальный уровень — 20', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/level-up`, { characterId: id, newLevel }));
                },
                'move': () => {
                    const x = +body.querySelector('#move-x').value;
                    const y = +body.querySelector('#move-y').value;
                    if (x < 0 || y < 0) { toast('Координаты должны быть неотрицательными', 'error'); return; }
                    act(() => Api.post(`/api/characters/${id}/move`, { characterId: id, targetX: x, targetY: y }));
                },
            });
        }

        /**
         * Обёртка для выполнения действия и обновления данных.
         */
        async function act(fn) {
            try {
                await fn();
                await reload();
                toast('Готово', 'success');
            } catch (e) {
                notifyError(e, 'Операция не выполнена');
            }
        }

        // Первичная отрисовка
        draw();
    }

    // Экспорт функции рендеринга
    window.Views = window.Views || {};
    window.Views.renderSheetView = renderSheetView;
})();