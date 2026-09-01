// js/ui.js
// Набор переиспользуемых функций для генерации HTML-разметки и работы с DOM.
// Без фреймворка — лёгкие хелперы, использующие шаблонные строки и делегирование событий.

'use strict';

(function () {

    /**
     * Экранирует строку для безопасной вставки в HTML.
     * Заменяет символы &, <, >, ", ' на соответствующие HTML-сущности.
     * @param {*} s - входное значение (строка, число, null, undefined)
     * @returns {string} Безопасная строка.
     */
    function esc(s) {
        if (s === null || s === undefined) return '';
        return String(s).replace(/[&<>"']/g, function (c) {
            switch (c) {
                case '&': return '&amp;';
                case '<': return '&lt;';
                case '>': return '&gt;';
                case '"': return '&quot;';
                case "'": return '&#39;';
                default: return c;
            }
        });
    }

    /**
     * Генерирует разметку полосы здоровья с числовым значением.
     * @param {number} current - текущее здоровье
     * @param {number} max - максимальное здоровье
     * @param {number} [temp=0] - временные хиты (отображаются в скобках)
     * @returns {string} HTML-разметка полосы здоровья.
     */
    function hpBar(current, max, temp = 0) {
        const safeCurrent = Number(current) || 0;
        const safeMax = Number(max) || 0;
        const pct = safeMax > 0 ? Math.max(0, Math.min(100, (safeCurrent / safeMax) * 100)) : 0;
        let cls = '';
        if (pct <= 25) {
            cls = 'low';
        } else if (pct <= 60) {
            cls = 'mid';
        }
        const tempText = temp > 0 ? ` (+${temp})` : '';
        return `
      <div class="hp-bar">
        <div class="hp-bar-fill ${cls}" style="width:${pct}%" role="progressbar" aria-valuenow="${safeCurrent}" aria-valuemin="0" aria-valuemax="${safeMax}"></div>
      </div>
      <div class="small muted">${safeCurrent}${tempText} / ${safeMax} HP</div>`;
    }

    /**
     * Создаёт HTML-пилюлю (бейдж).
     * @param {string} text - отображаемый текст
     * @param {string} [kind=''] - тип пилюли: 'danger', 'success', 'warn', 'info'
     * @returns {string} HTML-разметка пилюли.
     */
    function pill(text, kind = '') {
        return `<span class="pill ${kind ? 'pill-' + kind : ''}">${esc(text)}</span>`;
    }

    /**
     * Генерирует список тегов с опциональной кнопкой удаления.
     * @param {string[]} items - массив строк для отображения
     * @param {boolean} [onRemove=false] - добавлять ли кнопку удаления (data-remove)
     * @returns {string} HTML-разметка списка тегов.
     */
    function tagList(items, onRemove = false) {
        if (!Array.isArray(items) || items.length === 0) {
            return '<span class="muted small">—</span>';
        }
        const tags = items.map(item => `
      <span class="tag">
        ${esc(item)}
        ${onRemove ? `<button type="button" data-remove="${esc(item)}" aria-label="Удалить ${esc(item)}">✕</button>` : ''}
      </span>`).join('');
        return `<div class="tag-list">${tags}</div>`;
    }

    /**
     * Создаёт поле ввода с подписью.
     * @param {string} label - текст подписи
     * @param {string} inputHtml - HTML-разметка поля (input, select, textarea)
     * @returns {string} HTML-разметка поля.
     */
    function field(label, inputHtml) {
        return `<label class="field">${esc(label)}${inputHtml}</label>`;
    }

    /**
     * Преобразует роль из английского в русское название.
     * @param {string} role - роль ('Player', 'GameMaster', 'Admin')
     * @returns {string} Русское название роли.
     */
    function roleLabel(role) {
        const map = {
            Player: 'Игрок',
            GameMaster: 'Мастер',
            Admin: 'Админ'
        };
        return map[role] || role;
    }

    /**
     * Возвращает HTML-разметку блока загрузки.
     * @param {string} [text='Загрузка…'] - текст загрузки
     * @returns {string} HTML-разметка.
     */
    function loadingBlock(text = 'Загрузка…') {
        return `<div class="loading" role="status">${esc(text)}</div>`;
    }

    /**
     * Возвращает HTML-разметку пустого состояния.
     * @param {string} text - текст сообщения
     * @returns {string} HTML-разметка.
     */
    function emptyState(text) {
        return `<div class="empty-state">${esc(text)}</div>`;
    }

    /**
     * Назначает обработчики кликов на элементы с атрибутом data-action внутри контейнера.
     * Использует делегирование событий для производительности.
     *
     * ВАЖНО: многие экраны (combat.js — на каждый опрос боя, dm.js/campaign.js —
     * на каждое действие, sheet.js — на каждое переключение вкладки) вызывают
     * bindActions повторно на одном и том же живом DOM-узле (у него меняется
     * только innerHTML, а не сам узел). Если не снимать предыдущий обработчик,
     * они накапливаются, и один клик начинает вызывать action несколько раз
     * подряд (в бою — тем больше, чем дольше открыта вкладка). Поэтому храним
     * обработчик на самом узле и переустанавливаем его при повторном вызове.
     *
     * @param {HTMLElement} container - родительский элемент, в котором ищем data-action
     * @param {Object<string, Function>} handlers - объект вида { действие: (element, event) => void }
     */
    function bindActions(container, handlers) {
        if (!container || !(container instanceof HTMLElement)) {
            console.warn('bindActions: container не является HTMLElement');
            return;
        }
        if (container._bindActionsHandler) {
            container.removeEventListener('click', container._bindActionsHandler);
        }
        const onClick = function (ev) {
            const target = ev.target;
            // closest может отсутствовать в старых браузерах, но для современного web ok
            const el = target.closest ? target.closest('[data-action]') : null;
            if (!el || !container.contains(el)) return;
            const action = el.getAttribute('data-action');
            if (action && typeof handlers[action] === 'function') {
                handlers[action](el, ev);
            }
        };
        container._bindActionsHandler = onClick;
        container.addEventListener('click', onClick);
    }

    /**
     * Собирает значения всех полей с атрибутом data-field внутри контейнера.
     * Поддерживаются типы: text, number, checkbox, select, textarea.
     * Для number пустое значение преобразуется в null, для checkbox — в boolean.
     * @param {HTMLElement} container - родительский контейнер
     * @returns {Object} Объект с ключами из data-field и значениями.
     */
    function collectFields(container) {
        const out = {};
        if (!container) return out;
        container.querySelectorAll('[data-field]').forEach(function (inp) {
            const key = inp.getAttribute('data-field');
            if (!key) return;
            let val;
            switch (inp.type) {
                case 'number':
                    val = inp.value === '' ? null : Number(inp.value);
                    break;
                case 'checkbox':
                    val = inp.checked;
                    break;
                default:
                    val = inp.value;
            }
            out[key] = val;
        });
        return out;
    }

    /**
     * Безопасно получает значение свойства объекта, проверяя как исходный ключ,
     * так и его вариант с заглавной первой буквой (camelCase ↔ PascalCase).
     * @param {Object} obj - исходный объект
     * @param {string} key - имя ключа в camelCase
     * @param {*} [def] - значение по умолчанию
     * @returns {*} Значение из объекта или значение по умолчанию.
     */
    function g(obj, key, def) {
        if (!obj) return def;
        if (Object.prototype.hasOwnProperty.call(obj, key)) return obj[key];
        const pascal = key.charAt(0).toUpperCase() + key.slice(1);
        if (Object.prototype.hasOwnProperty.call(obj, pascal)) return obj[pascal];
        return def;
    }

    // Экспорт наружу
    window.UI = {
        esc: esc,
        hpBar: hpBar,
        pill: pill,
        tagList: tagList,
        field: field,
        roleLabel: roleLabel,
        loadingBlock: loadingBlock,
        emptyState: emptyState,
        bindActions: bindActions,
        collectFields: collectFields,
        g: g
    };
})();