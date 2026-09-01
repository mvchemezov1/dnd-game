// js/app.js
// Главный модуль приложения: маршрутизация, управление экранами, статус соединения.

'use strict';

(function () {
    // Получаем ключевые DOM-элементы
    const authScreen = document.getElementById('auth-screen');
    const mainShell = document.getElementById('main-shell');
    const viewRoot = document.getElementById('view-root');
    const sidebarEl = document.getElementById('sidebar');
    const userBadge = document.getElementById('user-badge');
    const wsStatus = document.getElementById('ws-status');
    const sessionInput = document.getElementById('session-id-input');
    const logoutBtn = document.getElementById('logout-btn');

    // Состояние для предотвращения повторного рендеринга
    let currentRouteName = null;
    let currentRouteParamsKey = '';
    let isRendering = false;

    // =====================================================================
    // Описание маршрутов
    // =====================================================================
    const routes = {
        login: {
            render: null,
            auth: true,
            roles: null
        },
        register: {
            render: null,
            auth: true,
            roles: null
        },
        characters: {
            render: (root) => Views.renderCharactersView(root),
            auth: false,
            roles: ['Player', 'GameMaster', 'Admin']
        },
        sheet: {
            render: (root, params) => Views.renderSheetView(root, params),
            auth: false,
            roles: ['Player', 'GameMaster', 'Admin']
        },
        combat: {
            render: (root) => Views.renderCombatView(root),
            auth: false,
            roles: ['Player', 'GameMaster', 'Admin']
        },
        campaign: {
            render: (root) => Views.renderCampaignView(root),
            auth: false,
            roles: ['Player', 'GameMaster', 'Admin']
        },
        dm: {
            render: (root) => Views.renderDmView(root),
            auth: false,
            roles: ['GameMaster', 'Admin']
        },
        admin: {
            render: (root) => Views.renderAdminView(root),
            auth: false,
            roles: ['Admin']
        },
        crafting: { render: (root) => Views.renderCraftingView(root), auth: false, roles: ['Player', 'GameMaster', 'Admin'] },
        trade: { render: (root) => Views.renderTradeView(root), auth: false, roles: ['Player', 'GameMaster', 'Admin'] },
        dialog: { render: (root) => Views.renderDialogView(root), auth: false, roles: ['Player', 'GameMaster', 'Admin'] },
        travel: { render: (root) => Views.renderTravelView(root), auth: false, roles: ['Player', 'GameMaster', 'Admin'] },
        dev: { render: (root) => Views.renderDevView(root), auth: false, roles: ['Admin'] }
    };

    // =====================================================================
    // Вспомогательные функции
    // =====================================================================

    /**
     * Возвращает текущую роль пользователя или null, если не аутентифицирован.
     */
    function getRole() {
        return Api.currentUser?.role || null;
    }

    /**
     * Проверяет, имеет ли пользователь право на доступ к маршруту.
     * @param {string[]|null} roles - список допустимых ролей или null, если доступ не ограничен
     * @returns {boolean}
     */
    function hasAccess(routeDef) {
        // Маршруты входа/регистрации доступны всегда, независимо от аутентификации
        if (routeDef.auth === true) return true;

        // Остальные маршруты требуют аутентификации
        if (!Api.isAuthenticated) return false;

        // Если роли не указаны, доступ разрешён любому аутентифицированному пользователю
        if (!routeDef.roles) return true;

        const userRole = getRole();
        return userRole !== null && routeDef.roles.includes(userRole);
    }

    /**
     * Переключает видимость экранов авторизации и основного интерфейса.
     * @param {boolean} isAuth - если true, показываем экран входа, иначе основной интерфейс
     */
    function showScreen(isAuth) {
        authScreen.classList.toggle('hidden', !isAuth);
        mainShell.classList.toggle('hidden', isAuth);
    }

    /**
     * Строит боковую навигацию на основе роли пользователя.
     * @param {string|null} role - текущая роль
     */
    function buildSidebar(role) {
        if (!role) {
            sidebarEl.innerHTML = '';
            return;
        }

        const links = [
            { href: 'characters', label: '🧙 Персонажи', roles: ['Player', 'GameMaster', 'Admin'] },
            { href: 'campaign', label: '📜 Кампания', roles: ['Player', 'GameMaster', 'Admin'] },
            { href: 'combat', label: '⚔️ Бой', roles: ['Player', 'GameMaster', 'Admin'] },
            { href: 'dm', label: '🛡️ Мастер', roles: ['GameMaster', 'Admin'] },
            { href: 'admin', label: '🔧 Админ', roles: ['Admin'] },
            { href: 'crafting', label: '🛠️ Крафт', roles: ['Player', 'GameMaster', 'Admin'] },
            { href: 'trade', label: '💰 Торговля', roles: ['Player', 'GameMaster', 'Admin'] },
            { href: 'dialog', label: '💬 Диалоги', roles: ['Player', 'GameMaster', 'Admin'] },
            { href: 'travel', label: '🗺️ Путешествия', roles: ['Player', 'GameMaster', 'Admin'] },
            { href: 'dev', label: '⚙️ Разработчик', roles: ['Admin'] }
        ].filter(link => link.roles.includes(role));

        // ВАЖНО: класс должен совпадать с тем, что стилизует styles.css
        // (.nav-btn) — раньше здесь стоял несуществующий класс
        // "sidebar-link", из-за чего меню рисовалось обычными
        // непрооформленными ссылками браузера. Заодно подсвечиваем
        // текущий раздел классом .active (тоже определён в styles.css).
        sidebarEl.innerHTML = links.map(link => `
            <a href="#" data-route="${link.href}" class="nav-btn${link.href === currentRouteName ? ' active' : ''}">${link.label}</a>
        `).join('');

        // Делегирование перехода по маршруту
        sidebarEl.querySelectorAll('a[data-route]').forEach(a => {
            a.addEventListener('click', (e) => {
                e.preventDefault();
                Store.setRoute(a.dataset.route);
            });
        });
    }

    /**
     * Обновляет отображение имени пользователя и кнопки выхода.
     */
    function updateUserBadge() {
        const user = Api.currentUser;
        if (user) {
            userBadge.textContent = `Роль: ${UI.roleLabel(user.role)}`;
            logoutBtn.style.display = 'inline-block';
        } else {
            userBadge.textContent = '';
            logoutBtn.style.display = 'none';
        }
    }

    /**
     * Обновляет индикатор статуса WebSocket.
     * @param {string} status - 'online', 'offline', 'connecting', 'auth-failed'
     */
    function updateWsStatus(status) {
        if (!wsStatus) return;

        const statusMap = {
            online: ['ws-on', '● онлайн'],
            offline: ['ws-off', '● офлайн'],
            connecting: ['ws-off', '● подключение…'],
            'auth-failed': ['ws-off', '● ошибка авторизации']
        };
        const [cls, text] = statusMap[status] || ['ws-off', '● офлайн'];
        wsStatus.className = `ws-status ${cls}`;
        wsStatus.textContent = text;
    }

    // =====================================================================
    // Рендеринг текущего маршрута
    // =====================================================================

    /**
     * Отображает представление, соответствующее текущему состоянию стора.
     */
    async function renderCurrentRoute() {
        if (isRendering) return;
        isRendering = true;

        try {
            const { route, routeParams } = Store.state;
            const routeDef = routes[route];

            if (!routeDef) {
                toast('Неизвестный маршрут', 'error');
                Store.setRoute('characters');
                return;
            }
            // Проверяем доступ
            if (!hasAccess(routeDef)) {
                const fallback = Api.isAuthenticated ? 'characters' : 'login';
                toast('Недостаточно прав для просмотра', 'error');
                Store.setRoute(fallback);
                return;
            }

            // Уходя с вкладки боя на любую другую (включая выход из
            // аккаунта), останавливаем её WS-подписки и резервный поллинг —
            // иначе они продолжают дёргать API в фоне даже после ухода с
            // экрана (сам #combat-body к этому моменту уже удалён).
            if (currentRouteName === 'combat' && route !== 'combat' && window.Views && Views._stopCombatPolling) {
                Views._stopCombatPolling();
            }

            // Показываем нужный экран
            showScreen(routeDef.auth === true);

            // Для экранов входа/регистрации контент уже в DOM, ничего не рендерим
            if (routeDef.render === null) {
                currentRouteName = route;
                currentRouteParamsKey = JSON.stringify(routeParams || {});
                return;
            }

            // Показываем индикатор загрузки
            viewRoot.innerHTML = UI.loadingBlock();

            // Выполняем рендер
            await routeDef.render(viewRoot, routeParams);

            // Обновляем навигацию и информацию о пользователе
            currentRouteName = route;
            currentRouteParamsKey = JSON.stringify(routeParams || {});
            buildSidebar(getRole());
            updateUserBadge();
        } catch (e) {
            console.error('Ошибка рендеринга маршрута:', e);
            viewRoot.innerHTML = UI.emptyState('Ошибка загрузки');
            notifyError(e, 'Не удалось загрузить страницу');
        } finally {
            isRendering = false;
        }
    }

    // =====================================================================
    // Инициализация приложения
    // =====================================================================

    async function init() {
        // Устанавливаем начальное значение поля сессии
        if (sessionInput) {
            sessionInput.value = Api.sessionId || '';
            sessionInput.addEventListener('change', () => {
                Api.sessionId = sessionInput.value.trim();
                toast('ID сессии сохранён', 'success');
            });
        }

        // Обработчик выхода
        logoutBtn.addEventListener('click', async () => {
            if (window.GameSocket) {
                GameSocket.disconnect();
            }
            await Api.logout();
            Store.setRoute('login');
        });

        // Инициализация экранов входа/регистрации
        if (typeof Views.initAuthScreen === 'function') {
            Views.initAuthScreen(() => Store.setRoute('characters'));
        }

        // Подписка на изменения стора
        Store.subscribe(() => {
            const paramsKey = JSON.stringify(Store.state.routeParams || {});
            if (Store.state.route !== currentRouteName || paramsKey !== currentRouteParamsKey) {
                renderCurrentRoute();
            }
        });

        // Подписка на статус WebSocket
        if (window.GameSocket) {
            window.GameSocket.onStatus((status) => updateWsStatus(status));
            window.GameSocket.connect();
        } else {
            updateWsStatus('offline');
        }

        // Инициализация локализации и ожидание загрузки переводов
        await I18n.init();

        // После загрузки переводов определяем стартовый маршрут
        const initialRoute = Api.isAuthenticated ? 'characters' : 'login';
        Store.setRoute(initialRoute);

        // Скрываем индикатор загрузки
        if (typeof window.hideLoadingIndicator === 'function') {
            window.hideLoadingIndicator();
        }
    }

    document.addEventListener('i18n:changed', () => {
        const paramsKey = JSON.stringify(Store.state.routeParams || {});
        if (Store.state.route && typeof Views.render === 'function') {
            renderCurrentRoute();
        }
    });

    // Запускаем после полной загрузки DOM
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();