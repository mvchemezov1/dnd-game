#nullable enable
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using dnd_game.application.security;

namespace dnd_game.infrastructure.security
{
    /// <summary>Пара access и refresh токенов.</summary>
    public sealed record TokenPair(string AccessToken, string RefreshToken);

    /// <summary>Настройки JWT.</summary>
    public sealed record TokenSettings
    {
        public string Secret { get; init; } = "change-me";
        public string Issuer { get; init; } = "DnD.Game";
        public string Audience { get; init; } = "DnD.Players";
        public int AccessTokenLifetimeMinutes { get; init; } = 60;
        public int RefreshTokenLifetimeDays { get; init; } = 7;
        public bool ValidateIssuerSigningKey { get; init; } = true;
        public bool ValidateLifetime { get; init; } = true;
    }

    /// <summary>
    /// Сервис выпуска и проверки JWT-токенов.
    /// </summary>
    public interface ITokenService
    {
        /// <summary>Генерирует access-токен для пользователя.</summary>
        string GenerateAccessToken(UserAccount user);

        /// <summary>Генерирует refresh-токен и сохраняет его хэш в хранилище.</summary>
        Task<string> GenerateRefreshTokenAsync(UserAccount user, string? deviceInfo = null, CancellationToken cancellationToken = default);

        /// <summary>Проверяет access-токен и возвращает ClaimsPrincipal.</summary>
        ClaimsPrincipal? ValidateToken(string token);

        /// <summary>Обновляет пару токенов по refresh-токену (с ротацией).</summary>
        Task<TokenPair?> RefreshTokensAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>Обновляет только access-токен по refresh-токену (без ротации refresh-токена).</summary>
        Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>Отзывает refresh-токен по его значению.</summary>
        Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>Отзывает все refresh-токены пользователя.</summary>
        Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default);
        Task RevokeAccessTokenAsync(string token, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Модель записи refresh-токена.
    /// </summary>
    public sealed class RefreshTokenEntry
    {
        /// <summary>Хэш (SHA-256) самого токена. Открытый токен не хранится.</summary>
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>Идентификатор пользователя, которому выдан токен.</summary>
        public Guid UserId { get; set; }

        /// <summary>Дополнительная информация об устройстве (опционально).</summary>
        public string? DeviceInfo { get; set; }

        /// <summary>Дата и время истечения срока действия.</summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>Флаг отзыва токена.</summary>
        public bool IsRevoked { get; set; }
    }

    /// <summary>
    /// Сервис выпуска и проверки JWT-токенов.
    /// Refresh-токены хранятся в IRefreshTokenStore (PostgreSQL), что переживает рестарт
    /// и работает корректно при нескольких экземплярах API за балансировщиком.
    /// </summary>
    public sealed class TokenService : ITokenService
    {
        private readonly TokenSettings _settings;
        private readonly IUserRepository _userRepository;
        private readonly IRefreshTokenStore _refreshTokenStore;
        private readonly ILogger<TokenService> _logger;
        private readonly IAccessTokenBlacklist _accessTokenBlacklist;

        public TokenService(
            IOptions<TokenSettings> settings,
            IUserRepository userRepository,
            IRefreshTokenStore refreshTokenStore,
            ILogger<TokenService> logger,
            IAccessTokenBlacklist accessTokenBlacklist)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _refreshTokenStore = refreshTokenStore ?? throw new ArgumentNullException(nameof(refreshTokenStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _accessTokenBlacklist = accessTokenBlacklist ?? throw new ArgumentNullException(nameof(accessTokenBlacklist));

            if (string.IsNullOrWhiteSpace(_settings.Secret) || _settings.Secret.Length < 32)
                throw new ArgumentException("Секретный ключ JWT должен быть не короче 32 символов.", nameof(settings));
        }

        /// <inheritdoc />
        public string GenerateAccessToken(UserAccount user)
        {
            ValidateUser(user);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.UniqueName, user.Username),
                new(ClaimTypes.Email, user.Email),
                new("role", user.GlobalRole.ToString())
            };

            if (user.CampaignRoles is { Count: > 0 })
            {
                var campaignRolesJson = System.Text.Json.JsonSerializer.Serialize(user.CampaignRoles);
                claims.Add(new Claim("campaign_roles", campaignRolesJson));
            }

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenLifetimeMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task RevokeAccessTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var lifetime = jwt.ValidTo - DateTime.UtcNow;
            if (lifetime > TimeSpan.Zero)
            {
                await _accessTokenBlacklist.RevokeAsync(token, lifetime, cancellationToken);
            }
        }

        public async Task<bool> IsTokenRevokedAsync(string token, CancellationToken cancellationToken = default)
        {
            return await _accessTokenBlacklist.IsRevokedAsync(token, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<string> GenerateRefreshTokenAsync(UserAccount user, string? deviceInfo = null, CancellationToken cancellationToken = default)
        {
            ValidateUser(user);
            cancellationToken.ThrowIfCancellationRequested();

            var randomBytes = RandomNumberGenerator.GetBytes(64);
            var refreshToken = Convert.ToBase64String(randomBytes);
            var hash = ComputeSha256Hash(refreshToken);

            var entry = new RefreshTokenEntry
            {
                TokenHash = hash,
                UserId = user.Id,
                DeviceInfo = deviceInfo,
                ExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenLifetimeDays),
                IsRevoked = false
            };

            await _refreshTokenStore.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Refresh-токен выпущен для пользователя {UserId}.", user.Id);

            // Асинхронно удаляем истёкшие токены, чтобы не блокировать основной поток.
            _ = Task.Run(async () =>
            {
                try
                {
                    int deleted = await _refreshTokenStore.DeleteExpiredAsync(CancellationToken.None).ConfigureAwait(false);
                    if (deleted > 0)
                        _logger.LogDebug("Удалено истёкших refresh-токенов: {DeletedCount}.", deleted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось очистить истёкшие refresh-токены.");
                }
            }, CancellationToken.None);

            return refreshToken;
        }

        /// <inheritdoc />
        public ClaimsPrincipal? ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_settings.Secret);

            try
            {
                var validationParams = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _settings.Audience,
                    ValidateLifetime = _settings.ValidateLifetime,
                    ClockSkew = TimeSpan.Zero
                };

                return tokenHandler.ValidateToken(token, validationParams, out _);
            }
            catch (SecurityTokenExpiredException)
            {
                _logger.LogWarning("Access-токен истёк.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Проверка токена завершилась ошибкой.");
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<TokenPair?> RefreshTokensAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return null;
            cancellationToken.ThrowIfCancellationRequested();

            var hash = ComputeSha256Hash(refreshToken);
            var entry = await _refreshTokenStore.GetByHashAsync(hash, cancellationToken).ConfigureAwait(false);
            if (entry == null || entry.IsRevoked || entry.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Refresh-токен недействителен, отозван или истёк.");
                return null;
            }

            var user = await _userRepository.GetByIdAsync(entry.UserId, cancellationToken).ConfigureAwait(false);
            if (user == null || !user.IsActive)
            {
                _logger.LogWarning("Пользователь не найден или неактивен для refresh-токена.");
                return null;
            }

            // Генерируем новые токены
            var newAccessToken = GenerateAccessToken(user);
            var newRefreshToken = await GenerateRefreshTokenAsync(user, entry.DeviceInfo, cancellationToken).ConfigureAwait(false);

            // Отзываем старый refresh-токен (ротация)
            await RevokeRefreshTokenAsync(refreshToken, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Refresh-токен обновлён для пользователя {UserId}.", user.Id);
            return new TokenPair(newAccessToken, newRefreshToken);
        }

        /// <inheritdoc />
        public async Task<string?> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return null;
            cancellationToken.ThrowIfCancellationRequested();

            var hash = ComputeSha256Hash(refreshToken);
            var entry = await _refreshTokenStore.GetByHashAsync(hash, cancellationToken).ConfigureAwait(false);
            if (entry == null)
            {
                _logger.LogWarning("Refresh-токен не найден.");
                return null;
            }

            if (entry.IsRevoked || entry.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Refresh-токен отозван или истёк.");
                return null;
            }

            var user = await _userRepository.GetByIdAsync(entry.UserId, cancellationToken).ConfigureAwait(false);
            if (user == null || !user.IsActive)
            {
                _logger.LogWarning("Пользователь не найден или неактивен для refresh-токена.");
                return null;
            }

            return GenerateAccessToken(user);
        }

        /// <inheritdoc />
        public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return;
            cancellationToken.ThrowIfCancellationRequested();

            var hash = ComputeSha256Hash(refreshToken);
            await _refreshTokenStore.RevokeAsync(hash, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Refresh-токен отозван.");
        }

        /// <inheritdoc />
        public async Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();

            await _refreshTokenStore.RevokeAllForUserAsync(userId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Все refresh-токены пользователя {UserId} отозваны.", userId);
        }

        // Вспомогательные методы

        private static string ComputeSha256Hash(string rawData)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
            return Convert.ToBase64String(bytes);
        }

        private static void ValidateUser(UserAccount user)
        {
            ArgumentNullException.ThrowIfNull(user, nameof(user));
            if (user.Id == Guid.Empty)
                throw new ArgumentException("Идентификатор пользователя не может быть пустым.", nameof(user));
            if (string.IsNullOrWhiteSpace(user.Username))
                throw new ArgumentException("Имя пользователя не может быть пустым.", nameof(user));
            if (string.IsNullOrWhiteSpace(user.Email))
                throw new ArgumentException("Email не может быть пустым.", nameof(user));
        }
    }
}