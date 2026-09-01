#nullable enable
using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.domain.queries;
using dnd_game.infrastructure.message_bus;
using dnd_game.infrastructure.security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api
{
    /// <summary>
    /// Базовый класс контроллера с извлечением контекста пользователя и сессии.
    /// </summary>
    [ApiController]
    public abstract class GameControllerBase : ControllerBase
    {
        /// <summary>Идентификатор текущего аутентифицированного пользователя.</summary>
        public Guid UserId =>
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

        /// <summary>Идентификатор игровой сессии из заголовка X-Session-Id.</summary>
        public Guid SessionId
        {
            get
            {
                var header = HttpContext.Request.Headers["X-Session-Id"].FirstOrDefault();
                return Guid.TryParse(header, out var sid) ? sid : Guid.Empty;
            }
        }

        /// <summary>Создаёт контекст выполнения команды.</summary>
        protected CommandContext CreateContext() => new()
        {
            UserId = UserId,
            GameSessionId = SessionId,
            CancellationToken = HttpContext.RequestAborted
        };

        /// <summary>Возвращает Ok(value) или NotFound, если value равно null.</summary>
        protected IActionResult OkOrNotFound<T>(T? value) where T : class
            => value is null ? NotFound() : Ok(value);

        /// <summary>Создаёт контекст с явным токеном отмены.</summary>
        protected CommandContext CreateContext(CancellationToken cancellationToken) => new()
        {
            UserId = UserId,
            GameSessionId = SessionId,
            CancellationToken = cancellationToken
        };
    }

    // =================================================================================
    // Персонажи
    // =================================================================================

    /// <summary>
    /// Контроллер управления персонажами.
    /// Все методы требуют аутентификации.
    /// </summary>
    [Route("api/[controller]")]
    [Authorize]
    public class CharactersController(
        ICommandBus commandBus,
        IQueryBus queryBus,
        ICharacterOwnershipRepository ownershipRepo,
        PermissionChecker permissionChecker) : GameControllerBase
    {
        private readonly ICommandBus _commandBus = commandBus;
        private readonly IQueryBus _queryBus = queryBus;
        private readonly ICharacterOwnershipRepository _ownershipRepo = ownershipRepo;
        private readonly PermissionChecker _permissionChecker = permissionChecker;

        // ---------- CRUD ----------

        /// <summary>Создаёт нового персонажа.</summary>
        [HttpPost]
        public async Task<IActionResult> CreateCharacter(
            [FromBody] CreateCharacterRequest request,
            CancellationToken cancellationToken)
        {
            var characterId = Guid.NewGuid();
            var command = new CreateCharacter(characterId, request.Name, request.MaxHitPoints);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));

            // Устанавливаем владельца
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim, out var userId))
            {
                await _ownershipRepo.AssignOwnerAsync(characterId, userId, cancellationToken);
            }

            return Ok();
        }

        /// <summary>Обновляет данные персонажа.</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateCharacter(
            Guid id,
            [FromBody] UpdateCharacterRequest request,
            CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var command = new UpdateCharacter(id, request.Name, request.MaxHitPoints);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return NoContent();
        }

        /// <summary>Возвращает персонажа по идентификатору.</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetCharacter(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterById(id), cancellationToken);
            return OkOrNotFound(result);
        }

        /// <summary>Возвращает список всех персонажей.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAllCharacters(CancellationToken cancellationToken)
        {
            var characters = await _queryBus.QueryAsync(new GetAllCharacters(), cancellationToken);
            var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (user != null && Guid.TryParse(user, out var userId))
            {
                bool isGameMaster = await _permissionChecker.IsGameMasterAsync(cancellationToken);
                if (!isGameMaster)
                {
                    var ownedIds = await _ownershipRepo.GetOwnedCharacterIdsAsync(userId, cancellationToken);
                    characters = characters.Where(c => ownedIds.Contains(c.Id)).ToList();
                }
            }
            return Ok(characters);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string? name,
            [FromQuery] string? className,
            [FromQuery] string? race,
            [FromQuery] bool? alive,
            [FromQuery] int? minLvl,
            [FromQuery] int? maxLvl,
            CancellationToken cancellationToken)
        {
            // Выполняем запрос на поиск
            var query = new SearchCharacters(name, className, race, alive, minLvl, maxLvl);
            var result = await _queryBus.QueryAsync(query, cancellationToken);

            // Фильтрация по владению для обычных игроков
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim, out var userId))
            {
                bool isGameMaster = await _permissionChecker.IsGameMasterAsync(cancellationToken);
                if (!isGameMaster)
                {
                    var ownedIds = await _ownershipRepo.GetOwnedCharacterIdsAsync(userId, cancellationToken);
                    var ownedSet = new HashSet<Guid>(ownedIds);
                    result = result.Where(c => ownedSet.Contains(c.Id)).ToList();
                }
            }

            return Ok(result);
        }

        // ---------- Здоровье ----------

        /// <summary>Наносит урон персонажу.</summary>
        [HttpPost("{id:guid}/damage")]
        public async Task<IActionResult> DealDamage(Guid id, [FromBody] DealDamage command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        /// <summary>Лечит персонажа.</summary>
        [HttpPost("{id:guid}/heal")]
        public async Task<IActionResult> Heal(Guid id, [FromBody] HealCharacter command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        /// <summary>Устанавливает временные хиты.</summary>
        [HttpPut("{id:guid}/temporary-hit-points")]
        public async Task<IActionResult> SetTemporaryHitPoints(Guid id, [FromBody] SetTemporaryHitPoints command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        /// <summary>Возвращает текущие, максимальные и временные хиты.</summary>
        [HttpGet("{id:guid}/hit-points")]
        public async Task<IActionResult> GetHitPoints(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterHitPoints(id), cancellationToken);
            return OkOrNotFound(result);
        }

        // ---------- Характеристики ----------

        /// <summary>Устанавливает значение характеристики.</summary>
        [HttpPut("{id:guid}/ability-scores/{ability}")]
        public async Task<IActionResult> SetAbilityScore(Guid id, string ability, [FromBody] SetAbilityScoreRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(ability)) return BadRequest(new { error = "Название характеристики не может быть пустым." });

            var command = new SetAbilityScore(id, ability, request.Score);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Раса, класс, предыстория ----------

        [HttpPost("{id:guid}/race")]
        public async Task<IActionResult> ChooseRace(Guid id, [FromBody] ChooseRace command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/class")]
        public async Task<IActionResult> ChooseClass(Guid id, [FromBody] ChooseClass command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/background")]
        public async Task<IActionResult> ChooseBackground(Guid id, [FromBody] ChooseBackground command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Владения ----------

        [HttpPost("{id:guid}/skills/{skill}")]
        public async Task<IActionResult> AddSkill(Guid id, string skill, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(skill)) return BadRequest(new { error = "Название навыка не может быть пустым." });
            await _commandBus.SendAsync(new AddSkillProficiency(id, skill), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/skills/{skill}")]
        public async Task<IActionResult> RemoveSkill(Guid id, string skill, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(skill)) return BadRequest(new { error = "Название навыка не может быть пустым." });
            await _commandBus.SendAsync(new RemoveSkillProficiency(id, skill), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/saving-throws/{ability}")]
        public async Task<IActionResult> AddSavingThrow(Guid id, string ability, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(ability)) return BadRequest(new { error = "Название характеристики не может быть пустым." });
            await _commandBus.SendAsync(new AddSavingThrowProficiency(id, ability), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/saving-throws/{ability}")]
        public async Task<IActionResult> RemoveSavingThrow(Guid id, string ability, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(ability)) return BadRequest(new { error = "Название характеристики не может быть пустым." });
            await _commandBus.SendAsync(new RemoveSavingThrowProficiency(id, ability), CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Черты ----------

        [HttpPost("{id:guid}/feats")]
        public async Task<IActionResult> AddFeat(Guid id, [FromBody] AddFeat command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/feats/{featName}")]
        public async Task<IActionResult> RemoveFeat(Guid id, string featName, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(featName)) return BadRequest(new { error = "Название черты не может быть пустым." });
            await _commandBus.SendAsync(new RemoveFeat(id, featName), CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Заклинания ----------

        [HttpPost("{id:guid}/spells")]
        public async Task<IActionResult> AddSpell(Guid id, [FromBody] AddSpell command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/spells/{spellId}")]
        public async Task<IActionResult> RemoveSpell(Guid id, string spellId, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(spellId)) return BadRequest(new { error = "Идентификатор заклинания не может быть пустым." });
            await _commandBus.SendAsync(new RemoveSpell(id, spellId), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/spells/prepare")]
        public async Task<IActionResult> PrepareSpell(Guid id, [FromBody] PrepareSpell command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/spell-slots/use")]
        public async Task<IActionResult> UseSpellSlot(Guid id, [FromBody] UseSpellSlot command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/spell-slots/restore")]
        public async Task<IActionResult> RestoreAllSpellSlots(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(new RestoreAllSpellSlots(id), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpGet("{id:guid}/spells")]
        public async Task<IActionResult> GetSpells(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterSpells(id), cancellationToken);
            return OkOrNotFound(result);
        }

        // ---------- Инвентарь и экипировка ----------

        [HttpPost("{id:guid}/inventory")]
        public async Task<IActionResult> AddInventoryItem(Guid id, [FromBody] AddInventoryItem command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/inventory/{itemId}")]
        public async Task<IActionResult> RemoveInventoryItem(Guid id, string itemId, [FromQuery] int quantity = 1, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(itemId)) return BadRequest(new { error = "Идентификатор предмета не может быть пустым." });
            if (quantity <= 0) return BadRequest(new { error = "Количество должно быть положительным." });
            await _commandBus.SendAsync(new RemoveInventoryItem(id, itemId, quantity), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpGet("{id:guid}/inventory")]
        public async Task<IActionResult> GetInventory(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterInventory(id), cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/equip")]
        public async Task<IActionResult> EquipItem(Guid id, [FromBody] EquipItem command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/unequip")]
        public async Task<IActionResult> UnequipItem(Guid id, [FromBody] UnequipItem command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpGet("{id:guid}/equipment")]
        public async Task<IActionResult> GetEquipment(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterEquipment(id), cancellationToken);
            return Ok(result);
        }

        // ---------- Состояния ----------

        [HttpPost("{id:guid}/conditions")]
        public async Task<IActionResult> ApplyCondition(Guid id, [FromBody] ApplyCondition command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/conditions/{condition}")]
        public async Task<IActionResult> RemoveCondition(Guid id, string condition, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            if (string.IsNullOrWhiteSpace(condition)) return BadRequest(new { error = "Название состояния не может быть пустым." });
            await _commandBus.SendAsync(new RemoveCondition(id, condition), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/conditions/clear")]
        public async Task<IActionResult> ClearAllConditions(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(new ClearAllConditionsCommand(id), CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Смерть и спасброски ----------

        [HttpPost("{id:guid}/death-saves")]
        public async Task<IActionResult> DeathSavingThrow(Guid id, [FromBody] DeathSavingThrow command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/stabilize")]
        public async Task<IActionResult> Stabilize(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(new StabilizeCharacter(id), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpGet("{id:guid}/death-status")]
        public async Task<IActionResult> GetDeathStatus(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterDeathStatus(id), cancellationToken);
            return OkOrNotFound(result);
        }

        // ---------- Опыт и уровень ----------

        [HttpPost("{id:guid}/experience")]
        public async Task<IActionResult> GainExperience(Guid id, [FromBody] GainExperience command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/level-up")]
        public async Task<IActionResult> LevelUp(Guid id, [FromBody] LevelUpCharacter command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Отдых ----------

        [HttpPost("{id:guid}/rest/start")]
        public async Task<IActionResult> StartRest(Guid id, [FromBody] StartRest command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/rest/end")]
        public async Task<IActionResult> EndRest(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(new EndRest(id), CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Перемещение ----------

        [HttpPost("{id:guid}/move")]
        public async Task<IActionResult> Move(Guid id, [FromBody] MoveCharacter command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Боевые параметры ----------

        [HttpGet("{id:guid}/combat-stats")]
        public async Task<IActionResult> GetCombatStats(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterCombatStats(id), cancellationToken);
            return OkOrNotFound(result);
        }

        [HttpPut("{id:guid}/armor-class")]
        public async Task<IActionResult> UpdateArmorClass(Guid id, [FromBody] UpdateArmorClass command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPut("{id:guid}/speed")]
        public async Task<IActionResult> UpdateSpeed(Guid id, [FromBody] UpdateSpeed command, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            await _commandBus.SendAsync(command with { CharacterId = id }, CreateContext(cancellationToken));
            return Ok();
        }

        // ---------- Защиты ----------

        [HttpGet("{id:guid}/defenses")]
        public async Task<IActionResult> GetDefenses(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCharacterDefenses(id), cancellationToken);
            return OkOrNotFound(result);
        }

        // ---------- Золото ----------

        [HttpPost("{id:guid}/gold/add")]
        public async Task<IActionResult> AddGold(Guid id, [FromBody] AddGoldRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var command = new AddGold(id, request.Amount);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/gold/spend")]
        public async Task<IActionResult> SpendGold(Guid id, [FromBody] SpendGoldRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var command = new SpendGold(id, request.Amount);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPut("{id:guid}/gold")]
        [Authorize(Roles = "Admin,GameMaster")]
        public async Task<IActionResult> SetGold(Guid id, [FromBody] SetGoldRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор персонажа не может быть пустым." });
            var command = new SetGoldCommand(id, request.Amount);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }
    }

    // =================================================================================
    // Бой
    // =================================================================================

    /// <summary>
    /// Контроллер управления боевыми сценами.
    /// </summary>
    [Route("api/[controller]")]
    [Authorize]
    public class CombatController(ICommandBus commandBus, IQueryBus queryBus) : GameControllerBase
    {
        private readonly ICommandBus _commandBus = commandBus;
        private readonly IQueryBus _queryBus = queryBus;

        [HttpPost]
        public async Task<IActionResult> StartCombat([FromBody] StartCombatRequest request, CancellationToken cancellationToken)
        {
            var command = new StartCombat(request.CombatId, request.Participants);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> EndCombat(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            await _commandBus.SendAsync(new EndCombat(id), CreateContext(cancellationToken));
            return NoContent();
        }

        [HttpPost("{id:guid}/initiative")]
        public async Task<IActionResult> RollInitiative(Guid id, [FromBody] RollInitiativeRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new RollInitiative(id, request.ParticipantId, request.InitiativeRoll, request.DexterityModifier);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/rounds")]
        public async Task<IActionResult> StartRound(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            await _commandBus.SendAsync(new StartRound(id), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/turns/next")]
        public async Task<IActionResult> NextTurn(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            await _commandBus.SendAsync(new NextTurn(id), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/rounds/end")]
        public async Task<IActionResult> EndRound(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            await _commandBus.SendAsync(new EndRound(id), CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/participants")]
        public async Task<IActionResult> AddParticipant(Guid id, [FromBody] AddParticipantRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new AddParticipantToCombat(id, request.ParticipantId, request.Initiative);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/participants/{participantId:guid}")]
        public async Task<IActionResult> RemoveParticipant(Guid id, Guid participantId, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty || participantId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы не могут быть пустыми." });
            await _commandBus.SendAsync(new RemoveParticipantFromCombat(id, participantId), CreateContext(cancellationToken));
            return NoContent();
        }

        [HttpPost("{id:guid}/actions/move")]
        public async Task<IActionResult> TakeMoveAction(Guid id, [FromBody] TakeMoveActionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new TakeMoveAction(id, request.ParticipantId, request.DistanceFeet);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/actions/standard")]
        public async Task<IActionResult> TakeStandardAction(Guid id, [FromBody] TakeStandardActionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new TakeStandardAction(
                id, request.ParticipantId, request.ActionType, request.TargetId, request.ActionData);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/actions/bonus")]
        public async Task<IActionResult> TakeBonusAction(Guid id, [FromBody] TakeBonusActionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new TakeBonusAction(
                id, request.ParticipantId, request.ActionType, request.TargetId, request.ActionData);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/actions/reaction")]
        public async Task<IActionResult> TakeReaction(Guid id, [FromBody] TakeReactionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new TakeReaction(
                id, request.ParticipantId, request.ReactionType, request.TriggerDescription, request.TargetId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/actions/ready")]
        public async Task<IActionResult> ReadyAction(Guid id, [FromBody] ReadyActionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new ReadyAction(id, request.ParticipantId, request.ActionToReady, request.TriggerCondition);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/actions/trigger")]
        public async Task<IActionResult> TriggerReadyAction(Guid id, [FromBody] TriggerReadyActionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new TriggerReadyAction(id, request.ParticipantId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/damage")]
        public async Task<IActionResult> DealDamage(Guid id, [FromBody] DealDamageRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new DealDamageToTarget(
                id, request.SourceParticipantId, request.TargetParticipantId, request.DamageAmount, request.DamageType);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/heal")]
        public async Task<IActionResult> HealTarget(Guid id, [FromBody] HealTargetRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new HealTarget(id, request.SourceParticipantId, request.TargetParticipantId, request.HealingAmount);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/conditions")]
        public async Task<IActionResult> ApplyCondition(Guid id, [FromBody] ApplyConditionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new ApplyConditionToTarget(
                id, request.TargetParticipantId, request.ConditionType, request.DurationRounds);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpDelete("{id:guid}/conditions")]
        public async Task<IActionResult> RemoveCondition(Guid id, [FromBody] RemoveConditionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new RemoveConditionFromTarget(id, request.TargetParticipantId, request.ConditionType);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return NoContent();
        }

        [HttpPost("{id:guid}/saving-throws")]
        public async Task<IActionResult> MakeSavingThrow(Guid id, [FromBody] MakeSavingThrowRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new MakeSavingThrowInCombat(
                id, request.ParticipantId, request.Ability, request.DifficultyClass, request.RollResult, request.Modifiers);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/death-saves")]
        public async Task<IActionResult> MakeDeathSavingThrow(Guid id, [FromBody] MakeDeathSavingThrowRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new MakeDeathSavingThrowInCombat(id, request.ParticipantId, request.RollResult);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/stabilize")]
        public async Task<IActionResult> Stabilize(Guid id, [FromBody] StabilizeRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new StabilizeInCombat(id, request.ParticipantId, request.StabilizedByParticipantId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/concentration")]
        public async Task<IActionResult> MakeConcentrationCheck(Guid id, [FromBody] MakeConcentrationCheckRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new MakeConcentrationCheck(
                id, request.ParticipantId, request.DifficultyClass, request.RollResult, request.ConstitutionModifier);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/delay")]
        public async Task<IActionResult> DelayTurn(Guid id, [FromBody] DelayTurnRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new DelayTurn(id, request.ParticipantId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/surrender")]
        public async Task<IActionResult> Surrender(Guid id, [FromBody] SurrenderRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new SurrenderInCombat(id, request.ParticipantId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{id:guid}/actions")]
        public async Task<IActionResult> PerformAction(Guid id, [FromBody] PerformActionRequest request, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var command = new PerformAction(id, request.ParticipantId, request.ActionType, request.TargetId, request.ActionData);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetCombatStatus(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCombatStatus(id), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }

        [HttpGet("{id:guid}/participants")]
        public async Task<IActionResult> GetParticipants(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCombatParticipants(id), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}/current")]
        public async Task<IActionResult> GetCurrentParticipant(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCurrentCombatParticipant(id), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }

        [HttpGet("{id:guid}/round")]
        public async Task<IActionResult> GetRound(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCombatRound(id), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}/turn-order")]
        public async Task<IActionResult> GetTurnOrder(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCombatTurnOrder(id), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}/active")]
        public async Task<IActionResult> IsActive(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор боя не может быть пустым." });
            var result = await _queryBus.QueryAsync(new IsCombatActive(id), cancellationToken);
            return Ok(result);
        }
    }

    // =================================================================================
    // Кампания
    // =================================================================================

    /// <summary>
    /// Контроллер управления кампаниями.
    /// </summary>
    [Route("api/[controller]")]
    [Authorize]
    public class CampaignController : GameControllerBase
    {
        private readonly ICommandBus _commandBus;
        private readonly IQueryBus _queryBus;
        private readonly PermissionChecker _permissionChecker;

        public CampaignController(
            ICommandBus commandBus,
            IQueryBus queryBus,
            PermissionChecker permissionChecker)
        {
            _commandBus = commandBus;
            _queryBus = queryBus;
            _permissionChecker = permissionChecker;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetCampaignState(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор кампании не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetCampaignState(id), cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }

        [HttpGet("{id:guid}/quests/active")]
        public async Task<IActionResult> GetActiveQuests(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор кампании не может быть пустым." });
            var result = await _queryBus.QueryAsync(new GetActiveQuests(id), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}/quests")]
        public async Task<IActionResult> GetQuests(Guid id, [FromQuery] string? status = null, CancellationToken cancellationToken = default)
        {
            if (id == Guid.Empty) return BadRequest(new { error = "Идентификатор кампании не может быть пустым." });
            var parsedStatus = ParseStatus(status);
            var result = await _queryBus.QueryAsync(new GetQuestsByStatus(id, parsedStatus), cancellationToken);
            return Ok(result);
        }

        [HttpPost("{campaignId:guid}/quests/{questId:guid}/accept")]
        public async Task<IActionResult> AcceptQuest(Guid campaignId, Guid questId, CancellationToken cancellationToken)
        {
            if (campaignId == Guid.Empty || questId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы не могут быть пустыми." });
            var command = new AcceptQuestCommand(campaignId, questId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{campaignId:guid}/quests/{questId:guid}/complete")]
        public async Task<IActionResult> CompleteQuest(Guid campaignId, Guid questId, CancellationToken cancellationToken)
        {
            if (campaignId == Guid.Empty || questId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы не могут быть пустыми." });
            var command = new CompleteQuestCommand(campaignId, questId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{campaignId:guid}/quests/{questId:guid}/fail")]
        public async Task<IActionResult> FailQuest(Guid campaignId, Guid questId, CancellationToken cancellationToken)
        {
            if (campaignId == Guid.Empty || questId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы не могут быть пустыми." });
            var command = new FailQuestCommand(campaignId, questId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPost("{campaignId:guid}/quests")]
        public async Task<IActionResult> CreateQuest(Guid campaignId, [FromBody] CreateQuestRequest request, CancellationToken cancellationToken)
        {
            if (campaignId == Guid.Empty)
                return BadRequest(new { error = "Идентификатор кампании не может быть пустым." });
            var command = new CreateQuestCommand(
                campaignId,
                request.QuestId,
                request.Title,
                request.Description,
                request.Objectives,
                request.Rewards,
                request.ParticipantIds);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        [HttpPut("{campaignId:guid}/quests/{questId:guid}/objectives")]
        public async Task<IActionResult> UpdateQuestObjective(Guid campaignId, Guid questId, [FromBody] UpdateQuestObjectiveRequest request, CancellationToken cancellationToken)
        {
            if (campaignId == Guid.Empty || questId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы не могут быть пустыми." });
            var command = new UpdateQuestObjectiveCommand(
                campaignId,
                questId,
                request.ObjectiveIndex,
                request.IsCompleted,
                request.CurrentProgress);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        private static QuestStatus? ParseStatus(string? status)
        {
            if (string.IsNullOrEmpty(status)) return null;
            return Enum.TryParse<QuestStatus>(status, true, out var s) ? s : null;
        }

        /// <summary>Создаёт новую кампанию. Требуется роль GameMaster или Admin.</summary>
        [HttpPost]
        public async Task<IActionResult> CreateCampaign(
            [FromBody] CreateCampaignRequest request,
            CancellationToken cancellationToken)
        {
            if (!await _permissionChecker.IsGameMasterAsync(cancellationToken))
                return Forbid();

            if (request.CampaignId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name) || request.GameMasterId == Guid.Empty)
                return BadRequest(new { error = "Необходимо указать CampaignId, Name и GameMasterId." });

            var command = new CreateCampaignCommand(
                request.CampaignId,
                request.Name,
                request.GameMasterId);

            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return CreatedAtAction(nameof(GetCampaignState), new { id = request.CampaignId }, null);
        }

        /// <summary>Добавляет игрока в кампанию. Только мастер кампании или админ.</summary>
        [HttpPost("{campaignId:guid}/players")]
        public async Task<IActionResult> AddPlayer(
            Guid campaignId,
            [FromBody] AddPlayerRequest request,
            CancellationToken cancellationToken)
        {
            if (campaignId == Guid.Empty || request.PlayerId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы кампании и игрока обязательны." });

            if (!await IsCampaignMasterAsync(campaignId, cancellationToken))
                return Forbid();

            var command = new AddPlayerToCampaignCommand(campaignId, request.PlayerId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return Ok();
        }

        /// <summary>Удаляет игрока из кампании. Только мастер кампании или админ.</summary>
        [HttpDelete("{campaignId:guid}/players/{playerId:guid}")]
        public async Task<IActionResult> RemovePlayer(
            Guid campaignId,
            Guid playerId,
            CancellationToken cancellationToken)
        {
            if (campaignId == Guid.Empty || playerId == Guid.Empty)
                return BadRequest(new { error = "Идентификаторы кампании и игрока обязательны." });

            if (!await IsCampaignMasterAsync(campaignId, cancellationToken))
                return Forbid();

            var command = new RemovePlayerFromCampaignCommand(campaignId, playerId);
            await _commandBus.SendAsync(command, CreateContext(cancellationToken));
            return NoContent();
        }

        private async Task<bool> IsCampaignMasterAsync(Guid campaignId, CancellationToken ct)
        {
            // Администратор всегда имеет доступ
            if (await _permissionChecker.IsAdminAsync(ct))
                return true;

            // Проверяем, является ли текущий пользователь мастером данной кампании
            // PermissionChecker использует текущий контекст пользователя
            return await _permissionChecker.IsGameMasterOfCampaignAsync(campaignId, ct);
        }
    }

    // =================================================================================
    // Аутентификация
    // =================================================================================

    /// <summary>
    /// Контроллер аутентификации: регистрация, вход, обновление токенов.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthProvider _authProvider;
        private readonly ITokenService _tokenService;

        public AuthController(IAuthProvider authProvider, ITokenService tokenService)
        {
            _authProvider = authProvider;
            _tokenService = tokenService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] AuthRequest request, CancellationToken cancellationToken)
        {
            var result = await _authProvider.RegisterAsync(request, cancellationToken);
            if (!result.Success)
                return BadRequest(new { error = result.ErrorMessage });
            return Ok(result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] AuthRequest request, CancellationToken cancellationToken)
        {
            var result = await _authProvider.LoginAsync(request, cancellationToken);
            if (!result.Success)
                return Unauthorized(new { error = result.ErrorMessage });
            return Ok(result);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
        {
            var result = await _authProvider.RefreshTokenAsync(request.RefreshToken, cancellationToken);
            if (!result.Success)
                return Unauthorized(new { error = result.ErrorMessage });
            return Ok(result);
        }

        /// <summary>
        /// Выход из системы: отзывает refresh-токен, переданный в теле запроса.
        /// </summary>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return BadRequest(new { error = "Refresh-токен обязателен." });

            await _tokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
            return NoContent();
        }
    }

    /// <summary>Запрос на обновление токена.</summary>
    public sealed record RefreshRequest(string RefreshToken);
}