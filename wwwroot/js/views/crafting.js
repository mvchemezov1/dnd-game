// js/views/crafting.js
'use strict';

(function () {
    function getSelectedCharacterId() {
        return localStorage.getItem('dnd.selectedCharacterId') || '';
    }

    async function renderCraftingView(root) {
        const characterId = getSelectedCharacterId();

        // Если персонаж не выбран, показываем сообщение и не отправляем запрос
        if (!characterId) {
            root.innerHTML = `
                <div class="note">
                    Сначала выберите персонажа.
                    <button class="btn btn-sm" data-action="go-to-characters">Перейти к списку</button>
                </div>`;
            UI.bindActions(root, {
                'go-to-characters': () => Store.setRoute('characters')
            });
            return;
        }

        root.innerHTML = UI.loadingBlock('Загрузка рецептов…');
        try {
            const recipes = await Api.get(`/api/crafting/recipes?characterId=${encodeURIComponent(characterId)}`);
            if (!recipes || !recipes.length) {
                root.innerHTML = UI.emptyState('Нет доступных рецептов.');
                return;
            }

            root.innerHTML = `
                <div class="card">
                    <h2>Крафт</h2>
                    <table>
                        <thead><tr><th>Название</th><th>Стоимость</th><th>Время</th><th></th></tr></thead>
                        <tbody>
                            ${recipes.map(r => `
                                <tr>
                                    <td>${UI.esc(r.name)}</td>
                                    <td>${r.goldCost} золота</td>
                                    <td>${r.craftingTimeHours} ч</td>
                                    <td><button class="btn btn-sm" data-action="start" data-id="${r.recipeId}">Начать</button></td>
                                </tr>`).join('')}
                        </tbody>
                    </table>
                </div>
                <div class="card">
                    <h3>Активные процессы</h3>
                    <div id="active-crafting">${UI.loadingBlock()}</div>
                </div>
            `;

            UI.bindActions(root, {
                start: (el) => startCrafting(el.dataset.id, characterId, root)
            });

            await loadActiveProcesses(characterId, root);
        } catch (e) {
            root.innerHTML = UI.emptyState('Ошибка загрузки рецептов.');
            notifyError(e);
        }
    }

    async function loadActiveProcesses(characterId, root) {
        const container = root.querySelector('#active-crafting');
        if (!container) return;
        try {
            const processes = await Api.get(`/api/crafting/processes?characterId=${encodeURIComponent(characterId)}`);
            container.innerHTML = processes.length
                ? `<ul>${processes.map(p => `<li>Процесс ${p.processId} — осталось ${p.totalHours - p.elapsedHours} ч</li>`).join('')}</ul>`
                : '<span class="muted">Нет активных процессов.</span>';
        } catch (e) {
            container.innerHTML = '<span class="muted">Не удалось загрузить процессы.</span>';
        }
    }

    async function startCrafting(recipeId, characterId, root) {
        try {
            await Api.post('/api/crafting/start', { characterId, recipeId });
            toast('Крафт начат', 'success');
            await loadActiveProcesses(characterId, root);
        } catch (e) {
            notifyError(e, 'Не удалось начать крафт');
        }
    }

    window.Views = window.Views || {};
    window.Views.renderCraftingView = renderCraftingView;
})();