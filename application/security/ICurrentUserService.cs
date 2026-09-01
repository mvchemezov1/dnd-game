using System;

namespace dnd_game.application.security
{
    /// <summary>
    /// Предоставляет доступ к идентификатору текущего аутентифицированного пользователя.
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>
        /// Возвращает идентификатор текущего пользователя.
        /// Если пользователь не аутентифицирован, выбрасывает <see cref="UnauthorizedAccessException"/>.
        /// </summary>
        Guid GetCurrentUserId();

        /// <summary>
        /// Пытается получить идентификатор текущего пользователя без выбрасывания исключения.
        /// Возвращает <c>null</c>, если пользователь не аутентифицирован или идентификатор некорректен.
        /// </summary>
        Guid? TryGetCurrentUserId();
    }
}