// js/api.js
// Тонкий клиент для REST API и бинарного WebSocket-протокола бэкенда.
// Реализует аутентификацию, обновление токенов, CRUD-запросы и подписку на события реального времени.

'use strict';

(function () {
    // Ключи для localStorage
    const AUTH_KEY = 'dnd.auth';
    const SESSION_KEY = 'dnd.sessionId';

    // =====================================================================
    // Вспомогательные функции
    // =====================================================================

    /**
     * Декодирует JWT-токен и возвращает payload в виде объекта.
     * Безопасно обрабатывает base64url.
     * @param {string} token - JWT-токен
     * @returns {Object|null} payload токена или null при ошибке
     */
    function decodeJwt(token) {
        try {
            const payload = token.split('.')[1];
            const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
            const json = decodeURIComponent(
                atob(base64)
                    .split('')
                    .map(c => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
                    .join('')
            );
            return JSON.parse(json);
        } catch (e) {
            console.warn('Не удалось декодировать JWT:', e);
            return null;
        }
    }

    // Стандартные claim'ы в .NET
    const ROLE_CLAIM = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/role';
    const EMAIL_CLAIM = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';

    /**
     * Ошибка API с HTTP-статусом.
     */
    class ApiError extends Error {
        constructor(status, message) {
            super(message);
            this.name = 'ApiError';
            this.status = status;
        }
    }

    // =====================================================================
    // Основной класс API
    // =====================================================================
    class Api {
        constructor() {
            this._auth = this._loadAuth();
            this._refreshingPromise = null;
        }

        // ---------- Хранение авторизации ----------
        _loadAuth() {
            try {
                return JSON.parse(localStorage.getItem(AUTH_KEY) || 'null');
            } catch {
                console.warn('Повреждены данные авторизации в localStorage');
                return null;
            }
        }

        _saveAuth(auth) {
            this._auth = auth;
            if (auth) {
                localStorage.setItem(AUTH_KEY, JSON.stringify(auth));
            } else {
                localStorage.removeItem(AUTH_KEY);
            }
        }

        // ---------- Свойства ----------
        get isAuthenticated() {
            return !!(this._auth && this._auth.token);
        }

        get currentUser() {
            if (!this._auth || !this._auth.token) return null;
            const claims = decodeJwt(this._auth.token);
            if (!claims) return null;
            return {
                userId: claims.sub,
                username: claims.unique_name || claims.username || '—',
                email: claims[EMAIL_CLAIM] || '',
                role: claims[ROLE_CLAIM] || claims.role || 'Player'
            };
        }

        get sessionId() {
            return localStorage.getItem(SESSION_KEY) || '';
        }

        set sessionId(v) {
            if (v && !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v)) {
                console.warn('Невалидный GUID для сессии, не сохраняем:', v);
                return;
            }
            if (v) {
                localStorage.setItem(SESSION_KEY, v);
            } else {
                localStorage.removeItem(SESSION_KEY);
            }
        }

        // ---------- Аутентификация ----------
        async register(username, email, password) {
            const res = await this._raw('POST', '/api/auth/register', { username, email, password }, false);
            this._saveAuth({ token: res.token, refreshToken: res.refreshToken, expiresAt: res.expiresAt });
            if (window.GameSocket) window.GameSocket.connect();
            return res;
        }

        async login(username, password) {
            const res = await this._raw('POST', '/api/auth/login', { username, password }, false);
            this._saveAuth({ token: res.token, refreshToken: res.refreshToken, expiresAt: res.expiresAt });
            if (window.GameSocket) window.GameSocket.connect();
            return res;
        }

        async logout() {
            try {
                const refreshToken = this._auth?.refreshToken;
                if (refreshToken) {
                    await fetch('/api/auth/logout', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ refreshToken })
                    });
                }
            } catch (e) {
                console.warn('Не удалось выполнить logout на сервере:', e);
            } finally {
                this._saveAuth(null);
                if (window.GameSocket) window.GameSocket.disconnect();
            }
        }

        /**
         * Обновляет access-токен с помощью refresh-токена.
         * Гарантирует, что одновременные вызовы используют один и тот же запрос.
         */
        async _refresh() {
            if (!this._auth || !this._auth.refreshToken) {
                throw new ApiError(401, 'Нет refresh-токена');
            }
            if (!this._refreshingPromise) {
                this._refreshingPromise = this._raw(
                    'POST',
                    '/api/auth/refresh',
                    { refreshToken: this._auth.refreshToken },
                    false
                )
                    .then(res => {
                        this._saveAuth({
                            token: res.token,
                            refreshToken: res.refreshToken,
                            expiresAt: res.expiresAt
                        });
                        return res;
                    })
                    .finally(() => {
                        this._refreshingPromise = null;
                    });
            }
            return this._refreshingPromise;
        }

        // ---------- HTTP-ядро ----------
        async _raw(method, path, body, useAuth = true) {
            const headers = { 'Content-Type': 'application/json' };
            if (useAuth && this._auth && this._auth.token) {
                headers['Authorization'] = `Bearer ${this._auth.token}`;
            }

            const sid = this.sessionId;
            if (useAuth && sid && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(sid)) {
                headers['X-Session-Id'] = sid;
            }

            let res;
            try {
                res = await fetch(path, {
                    method,
                    headers,
                    body: body !== undefined ? JSON.stringify(body) : undefined
                });
            } catch (e) {
                // Сетевая ошибка (сервер недоступен, нет соединения)
                console.error('Сетевая ошибка при запросе:', e);
                if (typeof window.notifyError === 'function') {
                    window.notifyError(new Error('Проблемы с сетью или сервером. Проверьте подключение.'), 'Ошибка сети');
                }
                throw new ApiError(0, 'Сетевая ошибка: невозможно выполнить запрос.');
            }

            if (res.status === 204) {
                return null;
            }

            const text = await res.text();
            let data = null;
            if (text) {
                try {
                    data = JSON.parse(text);
                } catch {
                    data = text;
                }
            }

            if (!res.ok) {
                let msg = (data && (data.error || data.message || data.title)) || `Ошибка ${res.status}`;
                // Если есть структура errors (FluentValidation), берём первую ошибку
                if (data && data.errors) {
                    const firstKey = Object.keys(data.errors)[0];
                    if (firstKey && Array.isArray(data.errors[firstKey]) && data.errors[firstKey].length) {
                        msg = data.errors[firstKey][0];
                    }
                }
                throw new ApiError(res.status, msg);
            }

            return data;
        }

        /**
         * Выполняет запрос с автоматическим обновлением токена при 401.
         */
        async call(method, path, body) {
            try {
                return await this._raw(method, path, body, true);
            } catch (e) {
                if (e instanceof ApiError && e.status === 401 && this._auth && this._auth.refreshToken) {
                    await this._refresh();
                    return this._raw(method, path, body, true);
                }
                throw e;
            }
        }

        get(path) {
            return this.call('GET', path);
        }

        post(path, body) {
            return this.call('POST', path, body === undefined ? {} : body);
        }

        put(path, body) {
            return this.call('PUT', path, body === undefined ? {} : body);
        }

        del(path, body) {
            return this.call('DELETE', path, body);
        }
    }

    // =====================================================================
    // WebSocket-клиент реального времени
    // =====================================================================

    const MSG_TYPE = {
        Command: 1,
        CommandResponse: 2,
        Event: 3,
        Query: 4,
        QueryResponse: 5,
        AuthRequest: 6,
        AuthResponse: 7,
        Error: 8,
        Ping: 9,
        Pong: 10,
        Disconnect: 11
    };
    const HEADER_SIZE = 13;

    /**
     * Кодирует исходящее сообщение в бинарный кадр согласно протоколу.
     */
    function encodeFrame(typeByte, payloadObj) {
        const json = JSON.stringify(payloadObj);
        const payload = new TextEncoder().encode(json);
        const header = new Uint8Array(HEADER_SIZE);
        const view = new DataView(header.buffer);
        header[0] = 1; // version
        header[1] = typeByte;
        header[2] = 0; // flags — исходящее не сжимаем
        view.setUint32(3, 0, true); // messageId
        view.setUint32(7, payload.length, true);
        const packet = new Uint8Array(header.length + payload.length);
        packet.set(header, 0);
        packet.set(payload, header.length);
        return packet;
    }

    /**
     * Распаковывает gzip-данные с использованием DecompressionStream.
     */
    async function gunzip(bytes) {
        if (!('DecompressionStream' in window)) {
            throw new Error('DecompressionStream не поддерживается браузером');
        }
        const ds = new DecompressionStream('gzip');
        const stream = new Blob([bytes]).stream().pipeThrough(ds);
        const buf = await new Response(stream).arrayBuffer();
        return new Uint8Array(buf);
    }

    /**
     * Декодирует один или несколько кадров из ArrayBuffer.
     */
    async function decodeFrames(buffer) {
        const bytes = new Uint8Array(buffer);
        const out = [];
        let offset = 0;
        while (offset + HEADER_SIZE <= bytes.length) {
            const view = new DataView(bytes.buffer, bytes.byteOffset + offset, HEADER_SIZE);
            const type = bytes[offset + 1];
            const flags = bytes[offset + 2];
            const payloadLength = view.getUint32(7, true);
            offset += HEADER_SIZE;
            if (offset + payloadLength > bytes.length) break;
            let payloadBytes = bytes.slice(offset, offset + payloadLength);
            offset += payloadLength;

            if (flags & 1) { // Compressed
                try {
                    payloadBytes = await gunzip(payloadBytes);
                } catch (e) {
                    console.warn('Не удалось распаковать кадр:', e);
                    continue;
                }
            }
            try {
                const json = new TextDecoder().decode(payloadBytes);
                out.push({ type, data: JSON.parse(json) });
            } catch (e) {
                console.warn('Кадр повреждён или не является валидным JSON:', e);
            }
        }
        return out;
    }

    /**
     * Класс для работы с WebSocket.
     */
    class GameSocket {
        constructor(api) {
            this.api = api;
            this.ws = null;
            this.listeners = new Map();
            this.statusListeners = new Set();
            this._reconnectTimer = null;
            this._manualClose = false;
            this._pingInterval = null;
            this.lastPingTimestamp = null;
            this.latencyMs = null;
        }

        /**
         * Подписывает функцию на событие по имени типа.
         */
        on(eventTypeName, fn) {
            if (!this.listeners.has(eventTypeName)) {
                this.listeners.set(eventTypeName, new Set());
            }
            this.listeners.get(eventTypeName).add(fn);
            return () => this.listeners.get(eventTypeName)?.delete(fn);
        }

        /**
         * Подписывает на все события.
         */
        onAny(fn) {
            return this.on('*', fn);
        }

        /**
         * Подписывает на изменение статуса соединения.
         */
        onStatus(fn) {
            this.statusListeners.add(fn);
            return () => this.statusListeners.delete(fn);
        }

        _emitStatus(s) {
            this.statusListeners.forEach(fn => {
                try {
                    fn(s);
                } catch (e) {
                    console.error('Ошибка в обработчике статуса:', e);
                }
            });
        }

        /**
         * Устанавливает соединение с сервером.
         */
        connect() {
            if (!this.api.isAuthenticated) return;
            this._manualClose = false;
            this._stopPing();
            const proto = location.protocol === 'https:' ? 'wss' : 'ws';
            this.ws = new WebSocket(
                `${proto}://${location.host}/ws?token=${encodeURIComponent(this.api._auth.token)}`
            );
            this.ws.binaryType = 'arraybuffer';
            this._emitStatus('connecting');

            this.ws.onopen = () => {
                const frame = encodeFrame(MSG_TYPE.AuthRequest, {
                    token: this.api._auth.token,
                    sessionId: this.api.sessionId || undefined
                });
                this.ws.send(frame);
                // Запускаем регулярную отправку пинга
                this._startPing();
            };

            this.ws.onmessage = async (ev) => {
                if (!(ev.data instanceof ArrayBuffer)) {
                    console.warn('Получен неожиданный текстовый кадр от сервера');
                    return;
                }
                let frames;
                try {
                    frames = await decodeFrames(ev.data);
                } catch (e) {
                    console.error('Ошибка декодирования кадров:', e);
                    return;
                }
                for (const f of frames) {
                    this._handleFrame(f);
                }
            };

            this.ws.onclose = () => {
                this._stopPing();
                this._emitStatus('offline');
                if (!this._manualClose) {
                    this._scheduleReconnect();
                }
            };

            this.ws.onerror = (e) => {
                console.warn('Ошибка WebSocket:', e);
            };
        }

        _handleFrame(f) {
            switch (f.type) {
                case MSG_TYPE.AuthResponse:
                    if (f.data.success) {
                        this._emitStatus('online');
                    } else {
                        this._emitStatus('auth-failed');
                        this._handleAuthFailure();
                    }
                    break;

                case MSG_TYPE.Error: {
                    const msg = f.data.message || f.data.error || 'Неизвестная ошибка сервера';
                    console.error('Ошибка от сервера:', msg);
                    if (typeof window.notifyError === 'function') {
                        window.notifyError(new Error(msg));
                    }
                    break;
                }

                case MSG_TYPE.Event: {
                    const fullTypeName = f.data.eventTypeName || '';
                    const shortName = fullTypeName.split(',')[0].split('.').pop() || 'Event';
                    let payload = {};
                    try {
                        payload = JSON.parse(f.data.eventJson || '{}');
                    } catch {
                        payload = {};
                    }

                    (this.listeners.get(shortName) || []).forEach(fn => {
                        try { fn(payload); } catch (e) { console.error(`Ошибка в обработчике события ${shortName}:`, e); }
                    });
                    (this.listeners.get('*') || []).forEach(fn => {
                        try { fn({ type: shortName, payload }); } catch (e) { console.error('Ошибка в общем обработчике события:', e); }
                    });
                    break;
                }

                case MSG_TYPE.Pong:
                    if (this.lastPingTimestamp) {
                        this.latencyMs = Date.now() - this.lastPingTimestamp;
                        this.lastPingTimestamp = null;
                        const latencyEl = document.getElementById('connection-latency');
                        if (latencyEl) latencyEl.textContent = `${this.latencyMs} мс`;
                    }
                    break;

                default:
                    break;
            }
        }

        async _handleAuthFailure() {
            // Закрываем текущее соединение, чтобы не висело
            if (this.ws && this.ws.readyState === WebSocket.OPEN) {
                try { this.ws.close(); } catch (e) { }
            }
            try {
                // Пытаемся обновить токен и переподключиться
                await this.api._refresh();
                this.connect();
            } catch (e) {
                // Не удалось обновить — разлогиниваем и перенаправляем на вход
                this.api.logout();
                this._emitStatus('offline');
                if (typeof window.notifyError === 'function') {
                    window.notifyError(new Error('Сессия истекла. Войдите заново.'));
                }
                if (window.Store) {
                    window.Store.setRoute('login');
                }
            }
        }

        _startPing() {
            this._stopPing();
            this._pingInterval = setInterval(() => {
                if (this.ws && this.ws.readyState === WebSocket.OPEN) {
                    this.lastPingTimestamp = Date.now();
                    const frame = encodeFrame(MSG_TYPE.Ping, {});
                    try {
                        this.ws.send(frame);
                    } catch (e) {
                        console.warn('Ошибка отправки пинга:', e);
                    }
                }
            }, 25000); // каждые 25 секунд
        }

        _stopPing() {
            if (this._pingInterval) {
                clearInterval(this._pingInterval);
                this._pingInterval = null;
            }
        }

        _scheduleReconnect() {
            clearTimeout(this._reconnectTimer);
            this._reconnectTimer = setTimeout(() => {
                if (!this._manualClose) {
                    this.connect();
                }
            }, 4000);
        }

        disconnect() {
            this._manualClose = true;
            clearTimeout(this._reconnectTimer);
            this._stopPing();
            if (this.ws) {
                try { this.ws.close(); } catch (e) { console.warn('Ошибка при закрытии WebSocket:', e); }
                this.ws = null;
            }
        }
    }

    // Глобальная индикация состояния сети
    window.addEventListener('offline', () => {
        if (window.toast) window.toast('Соединение с интернетом потеряно', 'error', 5000);
    });
    window.addEventListener('online', () => {
        if (window.toast) window.toast('Соединение восстановлено', 'success', 3000);
    });

    // Экспорт в глобальную область видимости
    window.Api = new Api();
    window.ApiError = ApiError;
    window.GameSocket = new GameSocket(window.Api);
})();