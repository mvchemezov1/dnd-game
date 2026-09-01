using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.domain.queries;

namespace dnd_game.application.query_handlers
{
    /// <summary>
    /// Обработчик запросов, связанных с персонажами.
    /// Предоставляет доступ к данным проекции персонажей: характеристики, инвентарь, экипировка, заклинания, статусы.
    /// </summary>
    public class CharacterQueryHandler(CharacterProjection projection) : IQueryHandler<GetCharacterById, CharacterDto?>,
                                         IQueryHandler<GetAllCharacters, List<CharacterDto>>,
                                         IQueryHandler<GetCharacterHitPoints, CharacterHitPointsDto?>,
                                         IQueryHandler<GetCharacterCombatStats, CharacterCombatStatsDto?>,
                                         IQueryHandler<GetCharacterSpells, CharacterSpellsDto?>,
                                         IQueryHandler<GetCharacterInventory, List<InventoryItemDto>>,
                                         IQueryHandler<GetCharacterEquipment, List<EquippedItemDto>>,
                                         IQueryHandler<GetCharacterDeathStatus, CharacterDeathStatusDto?>,
                                         IQueryHandler<GetCharacterConditions, List<string>>,
                                         IQueryHandler<GetCharacterDefenses, CharacterDefensesDto?>,
                                         IQueryHandler<SearchCharacters, List<CharacterSummaryDto>>
    {
        private readonly CharacterProjection _projection = projection ?? throw new ArgumentNullException(nameof(projection));

        /// <summary>
        /// Загружает персонажа по идентификатору, пробрасывая токен отмены.
        /// </summary>
        private Task<CharacterDto?> GetCharacterAsync(Guid characterId, CancellationToken cancellationToken)
        {
            return _projection.GetById(characterId, cancellationToken);
        }

        /// <summary>
        /// Получает полную информацию о персонаже по идентификатору.
        /// </summary>
        public Task<CharacterDto?> Handle(GetCharacterById q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            return GetCharacterAsync(q.CharacterId, ct);
        }

        /// <summary>
        /// Получает список всех персонажей.
        /// </summary>
        public Task<List<CharacterDto>> Handle(GetAllCharacters q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            return _projection.GetAll(ct);
        }

        /// <summary>
        /// Получает текущее количество хитов, максимальное и временные хиты персонажа.
        /// </summary>
        public async Task<CharacterHitPointsDto?> Handle(GetCharacterHitPoints q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            return c is null
                ? null
                : new CharacterHitPointsDto(c.HitPoints, c.MaxHitPoints, c.TemporaryHitPoints);
        }

        /// <summary>
        /// Получает боевые характеристики персонажа: класс брони, скорость, кости хитов и спасброски от смерти.
        /// </summary>
        public async Task<CharacterCombatStatsDto?> Handle(GetCharacterCombatStats q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            return c is null
                ? null
                : new CharacterCombatStatsDto(
                    c.ArmorClass,
                    c.Speed,
                    c.HitDiceRemaining,
                    c.DeathSaveSuccesses,
                    c.DeathSaveFailures,
                    c.IsStable);
        }

        /// <summary>
        /// Получает информацию о заклинаниях персонажа: известные заклинания, максимальные и использованные ячейки.
        /// </summary>
        public async Task<CharacterSpellsDto?> Handle(GetCharacterSpells q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            return c is null
                ? null
                : new CharacterSpellsDto(c.KnownSpells, c.MaxSpellSlots, c.UsedSpellSlots);
        }

        /// <summary>
        /// Получает список предметов в инвентаре персонажа.
        /// </summary>
        public async Task<List<InventoryItemDto>> Handle(GetCharacterInventory q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            return c?.Inventory ?? [];
        }

        /// <summary>
        /// Получает список экипированных предметов персонажа.
        /// </summary>
        public async Task<List<EquippedItemDto>> Handle(GetCharacterEquipment q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            return c?.Equipment ?? [];
        }

        /// <summary>
        /// Получает текущий статус смерти персонажа (жив, при смерти, стабилен, мёртв) и счётчики спасбросков.
        /// </summary>
        public async Task<CharacterDeathStatusDto?> Handle(GetCharacterDeathStatus q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            if (c is null)
                return null;

            // Определяем статус на русском языке
            string status = c.IsDead
                ? "Мёртв"
                : c.HitPoints > 0
                    ? "Жив"
                    : c.IsStable
                        ? "Стабилен"
                        : "При смерти";

            return new CharacterDeathStatusDto(status, c.DeathSaveSuccesses, c.DeathSaveFailures);
        }

        /// <summary>
        /// Получает список активных состояний (conditions) персонажа.
        /// </summary>
        public async Task<List<string>> Handle(GetCharacterConditions q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            return c?.Conditions ?? [];
        }

        /// <summary>
        /// Получает защиты персонажа: сопротивления, уязвимости и иммунитеты.
        /// </summary>
        public async Task<CharacterDefensesDto?> Handle(GetCharacterDefenses q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();
            var c = await GetCharacterAsync(q.CharacterId, ct);
            return c is null
                ? null
                : new CharacterDefensesDto(c.Resistances, c.Vulnerabilities, c.Immunities);
        }

        /// <summary>
        /// Выполняет поиск персонажей по заданным фильтрам (имя, класс, раса, уровень, жив/мёртв).
        /// </summary>
        public async Task<List<CharacterSummaryDto>> Handle(SearchCharacters q, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(q);
            ct.ThrowIfCancellationRequested();

            var all = await _projection.GetAll(ct);
            IEnumerable<CharacterDto> filtered = all;

            if (!string.IsNullOrWhiteSpace(q.NameFilter))
                filtered = filtered.Where(c => c.Name.Contains(q.NameFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(q.ClassFilter))
                filtered = filtered.Where(c => c.Class.Equals(q.ClassFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(q.RaceFilter))
                filtered = filtered.Where(c => c.Race.Equals(q.RaceFilter, StringComparison.OrdinalIgnoreCase));

            if (q.IsAliveFilter.HasValue)
                filtered = filtered.Where(c => !c.IsDead == q.IsAliveFilter.Value);

            if (q.MinLevel.HasValue)
                filtered = filtered.Where(c => c.Level >= q.MinLevel.Value);

            if (q.MaxLevel.HasValue)
                filtered = filtered.Where(c => c.Level <= q.MaxLevel.Value);

            return [.. filtered
                .Select(c => new CharacterSummaryDto(
                    c.Id,
                    c.Name,
                    c.Level,
                    c.Class,
                    c.Race,
                    c.HitPoints,
                    c.MaxHitPoints,
                    !c.IsDead,
                    c.ArmorClass))];
        }
    }
}