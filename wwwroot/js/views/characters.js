// js/views/characters.js
// Представление списка персонажей с возможностью создания нового.

'use strict';

(function () {
    /**
     * Генерирует UUID v4 (используется только если бэкенд не генерирует сам).
     * В нашем случае не требуется, так как бэкенд создаёт Guid на своей стороне.
     */
    function uuid() {
        if (window.crypto && typeof window.crypto.randomUUID === 'function') {
            return window.crypto.randomUUID();
        }
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
            const r = (Math.random() * 16) | 0;
            return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
        });
    }
    // Оставляем экспорт, даже если не используем, для совместимости с другими модулями
    window.UI.uuid = window.UI.uuid || uuid;

    /**
     * Рендерит список персонажей.
     * @param {HTMLElement} root - корневой контейнер
     */
    async function renderCharactersView(root) {
        root.innerHTML = `
            <div class="card">
                <div class="row">
                    <h2 style="margin:0">${I18n.t('nav.characters')}</h2>
                    <span class="spacer"></span>
                    <button class="btn btn-primary" data-action="create">${I18n.t('characters.create_new')}</button>
                    <button class="btn" data-action="refresh">${I18n.t('characters.refresh')}</button>
                </div>
            </div>
            <div id="char-list" class="grid grid-3"></div>
        `;

        UI.bindActions(root, {
            create: () => openCreateCharacterModal(load),
            refresh: load
        });

        // Загрузка списка при старте
        await load();

        /**
         * Загружает и отрисовывает список персонажей.
         */
        async function load() {
            const listEl = root.querySelector('#char-list');
            if (!listEl) return;
            listEl.innerHTML = UI.loadingBlock('Загрузка персонажей…');

            try {
                const list = await Api.get('/api/characters');
                if (!list || !list.length) {
                    listEl.innerHTML = UI.emptyState('Персонажей пока нет — создайте первого.');
                    return;
                }
                listEl.innerHTML = list.map(cardHtml).join('');
                // Делегирование кликов по карточкам
                listEl.querySelectorAll('.char-card').forEach(card => {
                    card.addEventListener('click', () => {
                        const characterId = card.dataset.id;
                        // Сохраняем выбранного персонажа
                        localStorage.setItem('dnd.selectedCharacterId', characterId);
                        Store.setRoute('sheet', { id: characterId });
                    });
                });
            } catch (e) {
                listEl.innerHTML = UI.emptyState('Не удалось загрузить список персонажей.');
                notifyError(e);
            }
        }

        /**
         * Генерирует HTML-карточку персонажа.
         */
        function cardHtml(c) {
            const isDead = c.isDead === true || c.isAlive === false;
            const level = c.level ?? '?';
            const race = c.race || '—';
            const className = c.class || '';
            const armorClass = c.armorClass ?? '—';

            return `
                <div class="char-card" data-id="${UI.esc(c.id)}" role="button" tabindex="0">
                    <h3>${UI.esc(c.name)} ${isDead ? '💀' : ''}</h3>
                    <div class="small muted">Ур. ${level} · ${UI.esc(race)} ${UI.esc(className)}</div>
                    ${UI.hpBar(c.hitPoints ?? 0, c.maxHitPoints ?? 0)}
                    <div class="row small muted" style="margin-top:8px">${UI.pill('AC ' + armorClass)}</div>
                </div>`;
        }
    }

    /**
     * Открывает модальное окно создания нового персонажа.
     * @param {Function} onCreated - колбэк, вызываемый после успешного создания
     */
    function openCreateCharacterModal(onCreated) {
        openModal({
            title: 'Новый персонаж',
            bodyHtml: `
                <div class="stack">
                    ${UI.field('Имя', '<input data-field="name" autofocus>')}
                    ${UI.field('Максимум HP', '<input data-field="maxHitPoints" type="number" min="1" value="10">')}
                </div>`,
            actions: [
                { label: 'Отмена', className: 'btn', onClick: closeModal },
                {
                    label: 'Создать',
                    className: 'btn btn-primary',
                    onClick: async (ev) => {
                        const box = ev.target.closest('.modal-box');
                        const data = UI.collectFields(box);

                        if (!data.name || !data.name.trim()) {
                            toast('Введите имя персонажа', 'error');
                            return;
                        }
                        const maxHp = parseInt(data.maxHitPoints, 10);
                        if (!maxHp || maxHp <= 0) {
                            toast('Максимум HP должен быть положительным числом', 'error');
                            return;
                        }

                        try {
                            // Бэкенд сам генерирует CharacterId, поэтому не отправляем его
                            await Api.post('/api/characters', {
                                name: data.name.trim(),
                                maxHitPoints: maxHp
                            });
                            closeModal();
                            toast('Персонаж создан', 'success');
                            if (typeof onCreated === 'function') {
                                onCreated();
                            }
                        } catch (e) {
                            notifyError(e, 'Не удалось создать персонажа');
                        }
                    }
                }
            ]
        });
    }

    // Экспорт представлений
    window.Views = window.Views || {};
    window.Views.renderCharactersView = renderCharactersView;
})();