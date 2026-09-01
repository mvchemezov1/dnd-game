#nullable enable
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Проверяет, что аутентифицированный пользователь активен (IsActive).
    /// Если нет — завершает запрос с 401.
    /// </summary>
    public class UserActivityMiddleware
    {
        private readonly IAccessTokenBlacklist _blacklist;
        private readonly RequestDelegate _next;
        private readonly ILogger<UserActivityMiddleware> _logger;

        public UserActivityMiddleware(RequestDelegate next, IAccessTokenBlacklist blacklist, ILogger<UserActivityMiddleware>? logger = null)
        {
            _next = next;
            _blacklist = blacklist;
            _logger = logger ?? NullLogger<UserActivityMiddleware>.Instance;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Только для аутентифицированных запросов
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                // Проверяем отозванный ли access-токен
                var token = await context.GetTokenAsync("access_token");
                if (!string.IsNullOrEmpty(token))
                {
                    if (await _blacklist.IsRevokedAsync(token, context.RequestAborted))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "Токен отозван." });
                        return;
                    }
                }

                // Проверяем активность пользователя
                var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (Guid.TryParse(userIdClaim, out var userId))
                {
                    var userRepository = context.RequestServices.GetRequiredService<IUserRepository>();
                    var user = await userRepository.GetByIdAsync(userId, context.RequestAborted);
                    if (user == null || !user.IsActive)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "Пользователь неактивен или удалён." });
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}