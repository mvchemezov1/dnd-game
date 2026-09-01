// js/views/dialog.js
(function () {
    async function renderDialogView(root) {
        root.innerHTML = `
            <div class="card">
                <h2>Диалоги</h2>
                <div class="row">
                    <input id="dialog-id" placeholder="ID диалога" style="width:300px">
                    <input id="npc-id" placeholder="ID NPC" style="width:300px">
                    <input id="character-id" placeholder="ID персонажа" style="width:300px">
                    <button class="btn" data-action="start">Начать</button>
                </div>
            </div>
            <div id="dialog-area"></div>
        `;

        UI.bindActions(root, {
            start: () => startDialog()
        });

        function startDialog() {
            const dialogId = root.querySelector('#dialog-id').value.trim();
            const npcId = root.querySelector('#npc-id').value.trim();
            const characterId = root.querySelector('#character-id').value.trim();
            if (!dialogId || !npcId || !characterId) {
                toast('Заполните все ID', 'error');
                return;
            }
            Api.post('/api/dialog/start', { dialogueId: dialogId, npcId, characterId })
                .then(state => {
                    showDialog(state);
                })
                .catch(e => notifyError(e, 'Не удалось начать диалог'));
        }

        function showDialog(state) {
            const area = root.querySelector('#dialog-area');
            area.innerHTML = UI.loadingBlock('Загрузка текущего узла…');

            Api.get(`/api/dialog/state/${state.dialogueId}`)
                .then(node => {
                    if (!node) {
                        area.innerHTML = UI.emptyState('Диалог завершён.');
                        return;
                    }
                    area.innerHTML = `
                        <div class="card">
                            <p>${UI.esc(node.npcText)}</p>
                            ${node.options.map((opt) => `
                                <button class="btn" data-action="select-option" data-option-id="${opt.optionId}">
                                    ${UI.esc(opt.playerText)}
                                </button>
                            `).join('')}
                        </div>
                    `;
                    UI.bindActions(area, {
                        'select-option': (el) => selectOption(state.dialogueId, el.dataset.optionId)
                    });
                })
                .catch(e => { area.innerHTML = UI.emptyState('Ошибка загрузки диалога'); notifyError(e); });
        }

        function selectOption(dialogueId, optionId) {
            Api.post('/api/dialog/option', { dialogueId, optionId })
                .then(newState => {
                    if (newState.pendingOptionId) {
                        // Открываем модальное окно для ввода результата проверки навыка
                        openSkillCheckModal(dialogueId);
                    } else {
                        showDialog(newState);
                    }
                })
                .catch(e => notifyError(e));
        }

        function openSkillCheckModal(dialogueId) {
            openModal({
                title: 'Проверка навыка',
                bodyHtml: `
                    <div class="stack">
                        ${UI.field('Результат броска d20', '<input data-field="rollResult" type="number" min="1" max="20" value="10">')}
                        ${UI.field('Бонус мастерства', '<input data-field="proficiencyBonus" type="number" min="0" value="2">')}
                        ${UI.field('Модификатор характеристики', '<input data-field="abilityModifier" type="number" value="0">')}
                    </div>`,
                actions: [
                    { label: 'Отмена', className: 'btn', onClick: closeModal },
                    {
                        label: 'Применить',
                        className: 'btn btn-primary',
                        onClick: async (ev) => {
                            const box = ev.target.closest('.modal-box');
                            const data = UI.collectFields(box);
                            const roll = +data.rollResult;
                            const prof = +data.proficiencyBonus;
                            const abil = +data.abilityModifier;
                            if (roll < 1 || roll > 20) {
                                toast('Бросок d20 должен быть от 1 до 20', 'error');
                                return;
                            }
                            try {
                                const state = await Api.post(`/api/dialog/${dialogueId}/resolve-skill-check`, {
                                    rollResult: roll,
                                    proficiencyBonus: prof,
                                    abilityModifier: abil
                                });
                                closeModal();
                                showDialog(state);
                            } catch (e) {
                                notifyError(e, 'Не удалось выполнить проверку');
                            }
                        }
                    }
                ]
            });
        }
    }

    window.Views = window.Views || {};
    window.Views.renderDialogView = renderDialogView;
})();