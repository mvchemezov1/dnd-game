#nullable enable
using dnd_game.application.security;
using dnd_game.infrastructure.security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api
{
    /// <summary>
    /// Контроллер управления пользователями. Все методы требуют роли Admin.
    /// </summary>
    [Route("api/users")]
    [ApiController]
    [Authorize(Policy = "RequireAdmin")]
    public class UserManagementController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;

        public UserManagementController(
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            ITokenService tokenService)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        /// <summary>Возвращает список всех пользователей.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            // IUserRepository не имеет GetAllAsync в текущем интерфейсе.
            // Добавим метод GetAllAsync в интерфейс и реализации.
            // Пока обходной путь: использовать GetByUsername/Email не получится.
            // Поэтому необходимо расширить IUserRepository.
            // (Реализация добавлена ниже в разделе "Изменения в IUserRepository")
            var users = await _userRepository.GetAllAsync(cancellationToken);
            var result = users.Select(MapToDto);
            return Ok(result);
        }

        /// <summary>Возвращает информацию о конкретном пользователе.</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден." });
            return Ok(MapToDto(user));
        }

        /// <summary>Изменяет глобальную роль пользователя.</summary>
        [HttpPut("{id:guid}/role")]
        public async Task<IActionResult> ChangeRole(
            Guid id,
            [FromBody] ChangeUserRoleRequest request,
            CancellationToken cancellationToken)
        {
            if (!Enum.TryParse<UserRole>(request.Role, true, out var newRole))
                return BadRequest(new { error = $"Неизвестная роль: {request.Role}" });

            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден." });

            user.GlobalRole = newRole;
            await _userRepository.UpdateAsync(user, cancellationToken);

            // Отзываем все refresh-токены, чтобы изменения вступили в силу немедленно
            await _tokenService.RevokeAllRefreshTokensAsync(id, cancellationToken);

            return Ok(MapToDto(user));
        }

        /// <summary>Блокирует или разблокирует пользователя.</summary>
        [HttpPut("{id:guid}/status")]
        public async Task<IActionResult> ChangeStatus(
            Guid id,
            [FromBody] ChangeUserStatusRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден." });

            user.IsActive = request.IsActive;
            await _userRepository.UpdateAsync(user, cancellationToken);

            // Если заблокировали, отзываем все токены
            if (!request.IsActive)
                await _tokenService.RevokeAllRefreshTokensAsync(id, cancellationToken);

            return Ok(MapToDto(user));
        }

        /// <summary>Сбрасывает пароль пользователя (администратор задаёт новый).</summary>
        [HttpPut("{id:guid}/password")]
        public async Task<IActionResult> ResetPassword(
            Guid id,
            [FromBody] ResetUserPasswordRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден." });

            if (!_passwordHasher.IsStrongPassword(request.NewPassword))
                return BadRequest(new { error = "Пароль не соответствует требованиям сложности." });

            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            await _userRepository.UpdateAsync(user, cancellationToken);

            // Отзываем все refresh-токены, чтобы старые сессии стали недействительными
            await _tokenService.RevokeAllRefreshTokensAsync(id, cancellationToken);

            return Ok(new { message = "Пароль успешно изменён." });
        }

        /// <summary>Удаляет пользователя.</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(
            Guid id,
            CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(id, cancellationToken);
            if (user == null)
                return NotFound(new { error = "Пользователь не найден." });

            // Отзываем токены перед удалением
            await _tokenService.RevokeAllRefreshTokensAsync(id, cancellationToken);

            await _userRepository.DeleteAsync(id, cancellationToken);
            return NoContent();
        }

        private static AdminUserDto MapToDto(UserAccount user)
        {
            return new AdminUserDto(
                Id: user.Id,
                Username: user.Username,
                Email: user.Email,
                GlobalRole: user.GlobalRole.ToString(),
                IsActive: user.IsActive,
                CreatedAt: user.CreatedAt,
                CampaignRoles: user.CampaignRoles ?? new Dictionary<Guid, CampaignRole>());
        }
    }
}