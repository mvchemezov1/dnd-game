#nullable enable
using dnd_game.application.security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.security
{
    // =====================================================================
    // Интерфейс репозитория пользователей
    // =====================================================================

    /// <summary>
    /// Репозиторий учётных записей пользователей.
    /// Предоставляет операции чтения, добавления, обновления и удаления.
    /// </summary>
    public interface IUserRepository
    {
        Task<List<UserAccount>> GetAllAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// Возвращает пользователя по идентификатору.
        /// </summary>
        Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает пользователя по имени пользователя (username).
        /// </summary>
        Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает пользователя по адресу электронной почты.
        /// </summary>
        Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

        /// <summary>
        /// Добавляет нового пользователя.
        /// </summary>
        /// <exception cref="InvalidOperationException">Если пользователь с таким же идентификатором, именем или email уже существует.</exception>
        Task AddAsync(UserAccount user, CancellationToken cancellationToken = default);

        /// <summary>
        /// Обновляет существующего пользователя.
        /// </summary>
        /// <exception cref="InvalidOperationException">Если пользователь не найден или возникает конфликт индексов.</exception>
        Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет пользователя по идентификатору.
        /// </summary>
        Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
    }

    // =====================================================================
    // Модель учётной записи пользователя
    // =====================================================================

    /// <summary>
    /// Учётная запись пользователя системы.
    /// </summary>
    public sealed class UserAccount
    {
        /// <summary>Идентификатор пользователя.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Уникальное имя пользователя.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>Адрес электронной почты.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Хэш пароля (не храним открытый пароль).</summary>
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>Глобальная роль пользователя.</summary>
        public UserRole GlobalRole { get; set; } = UserRole.Player;

        /// <summary>Дата и время создания учётной записи (UTC).</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Признак активности учётной записи.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Роли пользователя в кампаниях (ключ — идентификатор кампании).</summary>
        public Dictionary<Guid, CampaignRole> CampaignRoles { get; set; } = [];
    }

    // =====================================================================
    // Реализация репозитория в памяти
    // =====================================================================

    /// <summary>
    /// Потокобезопасная реализация <see cref="IUserRepository"/> в памяти.
    /// Поддерживает индексы для быстрого поиска по имени пользователя и email.
    /// </summary>
    public sealed class InMemoryUserRepository(ILogger<InMemoryUserRepository>? logger = null) : IUserRepository
    {
        private readonly ConcurrentDictionary<Guid, UserAccount> _users = new();
        private readonly ConcurrentDictionary<string, Guid> _usernameIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Guid> _emailIndex = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<InMemoryUserRepository> _logger = logger ?? NullLogger<InMemoryUserRepository>.Instance;

        /// <inheritdoc />
        public Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);
            cancellationToken.ThrowIfCancellationRequested();

            _users.TryGetValue(userId, out var user);
            return Task.FromResult(user);
        }

        public Task<List<UserAccount>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_users.Values.ToList());
        }

        /// <inheritdoc />
        public Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            ValidateUsername(username);
            cancellationToken.ThrowIfCancellationRequested();

            if (_usernameIndex.TryGetValue(username, out var id))
                return GetByIdAsync(id, cancellationToken);
            return Task.FromResult<UserAccount?>(null);
        }

        /// <inheritdoc />
        public Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            ValidateEmail(email);
            cancellationToken.ThrowIfCancellationRequested();

            if (_emailIndex.TryGetValue(email, out var id))
                return GetByIdAsync(id, cancellationToken);
            return Task.FromResult<UserAccount?>(null);
        }

        /// <inheritdoc />
        public Task AddAsync(UserAccount user, CancellationToken cancellationToken = default)
        {
            ValidateUser(user);
            cancellationToken.ThrowIfCancellationRequested();

            // Проверяем уникальность идентификатора
            if (_users.ContainsKey(user.Id))
                throw new InvalidOperationException($"Пользователь с идентификатором '{user.Id}' уже существует.");

            // Проверяем уникальность имени пользователя
            if (_usernameIndex.ContainsKey(user.Username))
                throw new InvalidOperationException($"Имя пользователя '{user.Username}' уже занято.");

            // Проверяем уникальность email
            if (!string.IsNullOrWhiteSpace(user.Email) && _emailIndex.ContainsKey(user.Email))
                throw new InvalidOperationException($"Email '{user.Email}' уже зарегистрирован.");

            // Атомарная вставка с синхронизацией индексов
            lock (_users)
            {
                if (_users.TryAdd(user.Id, user))
                {
                    _usernameIndex[user.Username] = user.Id;
                    if (!string.IsNullOrWhiteSpace(user.Email))
                        _emailIndex[user.Email] = user.Id;
                    _logger.LogDebug("Пользователь {UserId} добавлен.", user.Id);
                }
                else
                {
                    throw new InvalidOperationException($"Не удалось добавить пользователя {user.Id}.");
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default)
        {
            ValidateUser(user);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_users)
            {
                if (!_users.TryGetValue(user.Id, out var existingUser))
                    throw new InvalidOperationException($"Пользователь с идентификатором '{user.Id}' не найден.");

                // Проверяем, что новое имя пользователя не конфликтует с другим пользователем
                if (!string.Equals(existingUser.Username, user.Username, StringComparison.OrdinalIgnoreCase)
                    && _usernameIndex.TryGetValue(user.Username, out var existingUsernameOwnerId)
                    && existingUsernameOwnerId != user.Id)
                {
                    throw new InvalidOperationException($"Имя пользователя '{user.Username}' уже занято другим пользователем.");
                }

                // Проверяем, что новый email не конфликтует с другим пользователем
                if (!string.Equals(existingUser.Email, user.Email, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(user.Email)
                    && _emailIndex.TryGetValue(user.Email, out var existingEmailOwnerId)
                    && existingEmailOwnerId != user.Id)
                {
                    throw new InvalidOperationException($"Email '{user.Email}' уже используется другим пользователем.");
                }

                // Обновляем основной объект
                _users[user.Id] = user;

                // Синхронизируем индексы
                if (!string.Equals(existingUser.Username, user.Username, StringComparison.OrdinalIgnoreCase))
                {
                    _usernameIndex.TryRemove(existingUser.Username, out _);
                    _usernameIndex[user.Username] = user.Id;
                }

                if (!string.Equals(existingUser.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(existingUser.Email))
                        _emailIndex.TryRemove(existingUser.Email, out _);
                    if (!string.IsNullOrWhiteSpace(user.Email))
                        _emailIndex[user.Email] = user.Id;
                }

                _logger.LogDebug("Пользователь {UserId} обновлён.", user.Id);
            }

            return Task.CompletedTask;
        }



        /// <inheritdoc />
        public Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            ValidateUserId(userId);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_users)
            {
                if (_users.TryRemove(userId, out var user))
                {
                    if (!string.IsNullOrWhiteSpace(user.Username))
                        _usernameIndex.TryRemove(user.Username, out _);
                    if (!string.IsNullOrWhiteSpace(user.Email))
                        _emailIndex.TryRemove(user.Email, out _);
                    _logger.LogDebug("Пользователь {UserId} удалён.", userId);
                }
                else
                {
                    _logger.LogWarning("Попытка удаления несуществующего пользователя {UserId}.", userId);
                }
            }

            return Task.CompletedTask;
        }

        // ---------- Валидация ----------

        private static void ValidateUserId(Guid userId)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));
        }

        private static void ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Имя пользователя не может быть пустым.", nameof(username));
        }

        private static void ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email не может быть пустым.", nameof(email));
        }

        private static void ValidateUser(UserAccount user)
        {
            ArgumentNullException.ThrowIfNull(user, nameof(user));
            ValidateUserId(user.Id);
            ValidateUsername(user.Username);
            if (!string.IsNullOrWhiteSpace(user.Email))
                ValidateEmail(user.Email);
        }
    }
}