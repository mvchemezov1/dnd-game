#nullable enable
using System;
using System.Collections.Generic;
using dnd_game.application.projections; // DTO лежат здесь

namespace dnd_game.domain.queries
{
    // --------------------------------------------------------------------------------------------
    // Запросы, связанные с персонажами: получение информации, характеристик, экипировки и поиск.
    // --------------------------------------------------------------------------------------------

    /// <summary>
    /// Получить полную информацию о персонаже по идентификатору.
    /// </summary>
    public record GetCharacterById(Guid CharacterId) : IQuery<CharacterDto?>;

    /// <summary>
    /// Получить список всех персонажей.
    /// </summary>
    public record GetAllCharacters : IQuery<List<CharacterDto>>;

    /// <summary>
    /// Получить информацию о текущих, максимальных и временных хитах персонажа.
    /// </summary>
    public record GetCharacterHitPoints(Guid CharacterId) : IQuery<CharacterHitPointsDto?>;

    /// <summary>
    /// Получить боевые характеристики персонажа (класс брони, скорость, кости хитов, спасброски от смерти).
    /// </summary>
    public record GetCharacterCombatStats(Guid CharacterId) : IQuery<CharacterCombatStatsDto?>;

    /// <summary>
    /// Получить информацию о заклинаниях персонажа (известные заклинания, ячейки).
    /// </summary>
    public record GetCharacterSpells(Guid CharacterId) : IQuery<CharacterSpellsDto?>;

    /// <summary>
    /// Получить список предметов в инвентаре персонажа.
    /// </summary>
    public record GetCharacterInventory(Guid CharacterId) : IQuery<List<InventoryItemDto>>;

    /// <summary>
    /// Получить список экипированных предметов персонажа.
    /// </summary>
    public record GetCharacterEquipment(Guid CharacterId) : IQuery<List<EquippedItemDto>>;

    /// <summary>
    /// Получить текущий статус смерти персонажа (жив, при смерти, стабилен, мёртв) и счётчики спасбросков.
    /// </summary>
    public record GetCharacterDeathStatus(Guid CharacterId) : IQuery<CharacterDeathStatusDto?>;

    /// <summary>
    /// Получить список активных состояний (conditions) персонажа.
    /// </summary>
    public record GetCharacterConditions(Guid CharacterId) : IQuery<List<string>>;

    /// <summary>
    /// Получить защиты персонажа: сопротивления, уязвимости, иммунитеты.
    /// </summary>
    public record GetCharacterDefenses(Guid CharacterId) : IQuery<CharacterDefensesDto?>;

    /// <summary>
    /// Выполнить поиск персонажей по заданным фильтрам (имя, класс, раса, уровень, жив/мёртв).
    /// </summary>
    /// <param name="NameFilter">Фильтр по имени (частичное совпадение, без учёта регистра).</param>
    /// <param name="ClassFilter">Фильтр по классу (точное совпадение, без учёта регистра).</param>
    /// <param name="RaceFilter">Фильтр по расе (точное совпадение, без учёта регистра).</param>
    /// <param name="IsAliveFilter">Фильтр по жизненному статусу: <c>true</c> — только живые, <c>false</c> — только мёртвые.</param>
    /// <param name="MinLevel">Минимальный уровень персонажей.</param>
    /// <param name="MaxLevel">Максимальный уровень персонажей.</param>
    public record SearchCharacters(
        string? NameFilter = null,
        string? ClassFilter = null,
        string? RaceFilter = null,
        bool? IsAliveFilter = null,
        int? MinLevel = null,
        int? MaxLevel = null
    ) : IQuery<List<CharacterSummaryDto>>;
}