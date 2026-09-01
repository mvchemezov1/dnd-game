// js/store.js
// Небольшой стейт-менеджер и утилиты для всплывающих уведомлений (toast) и модальных окон.
// Без внешних зависимостей, рассчитан на работу в связке с другими модулями приложения.

'use strict';

(function () {
    // =====================================================================
    // Утилита безопасного экранирования HTML (для предотвращения XSS)
    // =====================================================================
    function escapeHtml(str) {
        if (str === null || str === undefined) return '';
        return String(str).replace(/[&<>"']/g, function (char) {
            switch (char) {
                case '&': return '&amp;';
                case '<': return '&lt;';
                case '>': return '&gt;';
                case '"': return '&quot;';
                case "'": return '&#39;';
                default: return char;
            }
        });
    }

    // =====================================================================
    // Простой стор (state management)
    // =====================================================================
    const listeners = new Set();

    /** @type {{route: string, routeParams: Object, selectedCharacterId: string|null}} */
    const state = {
        route: 'characters',
        routeParams: {},
        selectedCharacterId: null,
    };

    /**
     * Изменяет текущий маршрут и параметры, уведомляя всех подписчиков.
     * @param {string} route - название маршрута (например, 'characters', 'sheet', 'combat')
     * @param {Object} [params={}] - дополнительные параметры маршрута
     */
    function setRoute(route, params = {}) {
        state.route = route;
        state.routeParams = params;
        // Если мы переходим на маршрут, не связанный с конкретным персонажем, сбрасываем выбранного персонажа.
        // Это делается для предотвращения некорректного состояния.
        if (!params?.characterId && route !== 'sheet') {
            // не сбрасываем, так как selectedCharacterId может использоваться в других местах
        }
        listeners.forEach(fn => {
            try {
                fn({ ...state }); // передаём копию, чтобы подписчики не мутировали исходный объект
            } catch (e) {
                console.error('Ошибка в подписчике стора:', e);
            }
        });
    }

    /**
     * Подписывает функцию на изменения состояния.
     * @param {Function} fn - функция-слушатель, вызывается с копией состояния
     * @returns {Function} функция отписки
     */
    function subscribe(fn) {
        listeners.add(fn);
        return () => listeners.delete(fn);
    }

    window.Store = { state, setRoute, subscribe };

    // =====================================================================
    // Всплывающие уведомления (toast)
    // =====================================================================

    /**
     * Показывает всплывающее уведомление.
     * @param {string} message - текст уведомления
     * @param {('info'|'success'|'error'|'event')} [kind='info'] - тип уведомления
     * @param {number} [ttl=4200] - время жизни в миллисекундах
     */
    function toast(message, kind = 'info', ttl = 4200) {
        const root = document.getElementById('toasts');
        if (!root) {
            console.warn('Контейнер для уведомлений #toasts не найден.');
            return;
        }

        const el = document.createElement('div');
        el.className = `toast ${kind}`;
        el.setAttribute('role', 'alert'); // для скринридеров
        el.textContent = message; // textContent исключает XSS

        // Кнопка закрытия (крестик)
        const closeBtn = document.createElement('button');
        closeBtn.innerHTML = '&times;';
        closeBtn.setAttribute('aria-label', 'Закрыть уведомление');
        closeBtn.style.cssText = 'background:none;border:none;color:inherit;font-size:1.2em;margin-left:8px;cursor:pointer;';
        closeBtn.addEventListener('click', () => removeToast(el));
        el.appendChild(closeBtn);

        // Автоматическое скрытие по таймеру
        const timeoutId = setTimeout(() => removeToast(el), ttl);

        // Функция удаления с учётом анимации (добавляем класс 'leaving')
        function removeToast(element) {
            clearTimeout(timeoutId);
            element.classList.add('leaving');
            element.addEventListener('animationend', () => element.remove(), { once: true });
            // fallback, если анимация не поддерживается
            setTimeout(() => element.remove(), 300);
        }

        root.appendChild(el);
    }
    window.toast = toast;

    /**
     * Универсальная обработка ошибок: показывает toast и логирует в консоль.
     * @param {Error|*} error - объект ошибки или любое значение
     * @param {string} [fallback='Что-то пошло не так'] - сообщение по умолчанию, если error не содержит message
     */
    window.notifyError = function (error, fallback) {
        const msg = (error && error.message) ? error.message : (fallback || 'Что-то пошло не так');
        toast(msg, 'error');
        console.error('Произошла ошибка:', error);
    };

    // =====================================================================
    // Модальные окна
    // =====================================================================

    /** @type {HTMLElement|null} Сохранённый элемент, который был в фокусе до открытия модального окна */
    let lastFocusedElement = null;

    /**
     * Открывает модальное окно.
     * @param {Object} options - параметры
     * @param {string} options.title - заголовок окна
     * @param {string} [options.bodyHtml] - HTML-разметка тела (должна быть доверенной!)
     * @param {Function} [options.onMount] - колбэк, вызываемый после монтирования (получает корневой элемент)
     * @param {Array<{label: string, className?: string, onClick: Function}>} [options.actions] - кнопки действий
     * @returns {HTMLElement} корневой элемент оверлея
     */
    function openModal({ title, bodyHtml = '', onMount, actions = [] }) {
        const root = document.getElementById('modal-root');
        if (!root) {
            console.warn('Контейнер для модальных окон #modal-root не найден.');
            return null;
        }

        // Сохраняем элемент, который был в фокусе
        lastFocusedElement = document.activeElement;

        // Очищаем предыдущее содержимое
        root.innerHTML = '';

        // Создаём оверлей
        const overlay = document.createElement('div');
        overlay.className = 'modal-overlay';
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        overlay.setAttribute('aria-labelledby', 'modal-title');

        // Создаём контейнер окна
        const box = document.createElement('div');
        box.className = 'modal-box';

        // Заголовок
        const titleEl = document.createElement('h3');
        titleEl.id = 'modal-title';
        titleEl.textContent = title; // textContent для безопасности
        box.appendChild(titleEl);

        // Тело
        const bodyEl = document.createElement('div');
        bodyEl.className = 'modal-body';
        // ВНИМАНИЕ: bodyHtml вставляется как HTML. Убедитесь, что данные доверенные.
        // Для произвольного текста используйте textContent или escapeHtml.
        bodyEl.innerHTML = bodyHtml;
        box.appendChild(bodyEl);

        // Контейнер для кнопок действий
        const actionsRoot = document.createElement('div');
        actionsRoot.className = 'modal-actions';
        actionsRoot.id = 'modal-actions';
        box.appendChild(actionsRoot);

        overlay.appendChild(box);
        root.appendChild(overlay);

        // Добавляем кнопки действий
        (actions.length ? actions : [{ label: 'Закрыть', className: 'btn', onClick: closeModal }]).forEach(action => {
            const btn = document.createElement('button');
            btn.className = action.className || 'btn';
            btn.textContent = action.label;
            btn.addEventListener('click', action.onClick);
            actionsRoot.appendChild(btn);
        });

        // Закрытие по клику на оверлей (вне окна)
        overlay.addEventListener('click', (e) => {
            if (e.target === overlay) closeModal();
        });

        // Закрытие по клавише Escape
        const keydownHandler = (e) => {
            if (e.key === 'Escape') {
                closeModal();
                document.removeEventListener('keydown', keydownHandler);
            }
        };
        document.addEventListener('keydown', keydownHandler);
        overlay._keydownHandler = keydownHandler; // сохраняем для очистки

        // Блокируем прокрутку фона
        document.body.style.overflow = 'hidden';

        // Перемещаем фокус на первый интерактивный элемент или на само окно
        const firstFocusable = box.querySelector('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
        (firstFocusable || box).focus();

        // Вызываем onMount, если передан
        if (typeof onMount === 'function') {
            onMount(overlay);
        }

        return overlay;
    }

    /**
     * Закрывает текущее модальное окно.
     */
    function closeModal() {
        const root = document.getElementById('modal-root');
        if (!root) return;

        const overlay = root.querySelector('.modal-overlay');
        if (overlay && overlay._keydownHandler) {
            document.removeEventListener('keydown', overlay._keydownHandler);
        }

        root.innerHTML = '';
        document.body.style.overflow = ''; // восстанавливаем прокрутку

        // Возвращаем фокус на элемент, который был активен до открытия
        if (lastFocusedElement && typeof lastFocusedElement.focus === 'function') {
            lastFocusedElement.focus();
            lastFocusedElement = null;
        }
    }

    window.openModal = openModal;
    window.closeModal = closeModal;

    /**
     * Показывает модальное окно подтверждения.
     * @param {string} message - текст сообщения
     * @returns {Promise<boolean>} Promise, который резолвится true при подтверждении, иначе false
     */
    window.confirmDialog = function (message) {
        return new Promise((resolve) => {
            openModal({
                title: 'Подтверждение',
                bodyHtml: `<p>${escapeHtml(message)}</p>`, // экранируем, чтобы избежать XSS
                actions: [
                    {
                        label: 'Отмена',
                        className: 'btn',
                        onClick: () => {
                            closeModal();
                            resolve(false);
                        }
                    },
                    {
                        label: 'Подтвердить',
                        className: 'btn btn-danger',
                        onClick: () => {
                            closeModal();
                            resolve(true);
                        }
                    }
                ]
            });
        });
    };
})();