#nullable enable
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using dnd_game.application.security;
using dnd_game.infrastructure.network;

namespace dnd_game.infrastructure.security
{
    // ---------- Интерфейсы и DTO ----------

    /// <summary>
    /// Провайдер аутентификации и управления токенами.
    /// </summary>
    public interface IAuthProvider
    {
        /// <summary>Регистрирует нового пользователя.</summary>
        Task<AuthResult> RegisterAsync(AuthRequest request, CancellationToken cancellationToken = default);

        /// <summary>Выполняет вход пользователя.</summary>
        Task<AuthResult> LoginAsync(AuthRequest request, CancellationToken cancellationToken = default);

        /// <summary>Обновляет пару токенов по refresh-токену.</summary>
        Task<AuthResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

        /// <summary>Проверяет действительность access-токена.</summary>
        Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);

        /// <summary>Извлекает контекст безопасности из access-токена.</summary>
        Task<UserSecurityContext?> GetUserContextFromTokenAsync(string token, CancellationToken cancellationToken = default);
    }

    /// <summary>Результат операции аутентификации.</summary>
    public class AuthResult
    {
        public bool Success { get; set; }
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid? UserId { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>Запрос на регистрацию или вход.</summary>
    public class AuthRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        /// <summary>
        /// Желаемая глобальная роль ("Player" или "GameMaster").
        /// Роль "Admin" нельзя получить через публичную регистрацию.
        /// </summary>
        public string? Role { get; set; }
    }

    /// <summary>Настройки JWT.</summary>
    public class JwtSettings
    {
        public string Secret { get; set; } = "change-me";
        public string Issuer { get; set; } = "DnD.Game";
        public string Audience { get; set; } = "DnD.Players";
        public int AccessTokenExpirationMinutes { get; set; } = 60;
        public int RefreshTokenExpirationDays { get; set; } = 7;
    }

    // ---------- Реализация AuthProvider ----------

    /// <summary>
    /// Реализация сервиса аутентификации.
    /// Отвечает за регистрацию, вход, обновление токенов и построение контекста безопасности.
    /// </summary>
    public class AuthProvider(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        JwtSettings jwtSettings,
        ICharacterOwnershipRepository ownershipRepository,
        IRateLimiter rateLimiter,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthProvider> logger) : IAuthProvider
    {
        private readonly IUserRepository _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        private readonly IPasswordHasher _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        private readonly ITokenService _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        private readonly JwtSettings _jwtSettings = jwtSettings ?? throw new ArgumentNullException(nameof(jwtSettings));
        private readonly ILogger<AuthProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        private readonly ICharacterOwnershipRepository _ownershipRepository = ownershipRepository ?? throw new ArgumentNullException(nameof(ownershipRepository));
        private readonly IRateLimiter _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

        /// <summary>
        /// Получает идентификатор клиента на основе IP-адреса текущего HTTP-запроса.
        /// Используется для ограничения частоты попыток входа.
        /// </summary>
        private Guid GetClientId()
        {
            var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
            if (ip == null)
                return Guid.Empty; // fallback, если контекст недоступен

            // Преобразуем IP-адрес в Guid с помощью MD5 (детерминированно)
            var hash = System.Security.Cryptography.MD5.HashData(ip.GetAddressBytes());
            return new Guid(hash);
        }

        /// <inheritdoc />
        public async Task<AuthResult> RegisterAsync(AuthRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Username))
                return Error("Имя пользователя не может быть пустым.");
            if (string.IsNullOrWhiteSpace(request.Email))
                return Error("Email не может быть пустым.");
            if (string.IsNullOrWhiteSpace(request.Password))
                return Error("Пароль не может быть пустым.");

            // ✅ Добавленная проверка формата email
            if (!System.Net.Mail.MailAddress.TryCreate(request.Email, out _))
                return Error("Некорректный формат email.");

            var existingByUsername = await _userRepository.GetByUsernameAsync(request.Username);
            if (existingByUsername != null)
                return Error("Имя пользователя уже занято.");

            var existingByEmail = await _userRepository.GetByEmailAsync(request.Email);
            if (existingByEmail != null)
                return Error("Email уже зарегистрирован.");

            if (!_passwordHasher.IsStrongPassword(request.Password))
                return Error("Пароль должен содержать не менее 8 символов, включая заглавные и строчные буквы, цифру и специальный символ.");

            // Публичная регистрация выдаёт только Player или GameMaster.
            // Роль Admin не может быть назначена через этот метод.
            var role = UserRole.Player;
            if (!string.IsNullOrWhiteSpace(request.Role) &&
                Enum.TryParse<UserRole>(request.Role, true, out var requestedRole) &&
                requestedRole == UserRole.GameMaster)
            {
                role = UserRole.GameMaster;
            }

            var user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = request.Username,
                Email = request.Email,
                PasswordHash = _passwordHasher.Hash(request.Password),
                GlobalRole = role,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                CampaignRoles = []
            };

            await _userRepository.AddAsync(user);

            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user);

            return new AuthResult
            {
                Success = true,
                Token = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes)
            };
        }

        /// <inheritdoc />
        public async Task<AuthResult> LoginAsync(AuthRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Username))
                return Error("Имя пользователя не может быть пустым.");
            if (string.IsNullOrWhiteSpace(request.Password))
                return Error("Пароль не может быть пустым.");

            // Ограничение частоты попыток входа
            var clientId = GetClientId();
            if (!_rateLimiter.IsAllowed(clientId, "login"))
            {
                return Error("Слишком много попыток входа. Попробуйте позже.");
            }

            var user = await _userRepository.GetByUsernameAsync(request.Username);
            if (user == null || !user.IsActive)
                return Error("Неверные учётные данные.");

            if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
                return Error("Неверные учётные данные.");

            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user);

            return new AuthResult
            {
                Success = true,
                Token = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes)
            };
        }

        /// <inheritdoc />
        public async Task<AuthResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Error("Refresh-токен не может быть пустым.");

            var tokenPair = await _tokenService.RefreshTokensAsync(refreshToken);
            if (tokenPair == null)
                return Error("Недействительный или истёкший refresh-токен.");

            var principal = _tokenService.ValidateToken(tokenPair.AccessToken);
            var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return Error("Некорректные данные токена.");

            return new AuthResult
            {
                Success = true,
                Token = tokenPair.AccessToken,
                RefreshToken = tokenPair.RefreshToken,
                UserId = userId,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes)
            };
        }

        /// <inheritdoc />
        public Task<bool> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Task.FromResult(false);

            try
            {
                var principal = _tokenService.ValidateToken(token);
                return Task.FromResult(principal != null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Проверка токена завершилась исключением.");
                return Task.FromResult(false);
            }
        }

        /// <inheritdoc />
        public async Task<UserSecurityContext?> GetUserContextFromTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var principal = _tokenService.ValidateToken(token);
            if (principal == null)
                return null;

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
                return null;

            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
                return null;

            // ✅ Проверка активности пользователя
            if (!user.IsActive)
            {
                _logger.LogWarning("Попытка входа неактивного пользователя {UserId}.", userId);
                return null;
            }

            var roleClaim = principal.FindFirst(ClaimTypes.Role);
            Enum.TryParse<UserRole>(roleClaim?.Value, true, out var globalRole);

            return new UserSecurityContext
            {
                UserId = user.Id,
                GlobalRole = globalRole,
                OwnedCharacterIds = await _ownershipRepository.GetOwnedCharacterIdsAsync(userId),
                CampaignRoles = user.CampaignRoles ?? []
            };
        }

        // Вспомогательный метод для единообразного возврата ошибки
        private static AuthResult Error(string message, CancellationToken cancellationToken = default)
            => new()
            { Success = false, ErrorMessage = message };
    }
}