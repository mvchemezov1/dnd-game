#nullable enable
using System;

namespace dnd_game.domain.commands
{
    /// <summary>
    /// Команда начала отдыха.
    /// </summary>
    /// <param name="CharacterId">Идентификатор персонажа.</param>
    /// <param name="RestType">Тип отдыха: "Short" или "Long".</param>
    public record StartRest(Guid CharacterId, string RestType) : ICommand;

    /// <summary>
    /// Команда траты одной кости хитов во время короткого отдыха.
    /// </summary>
    /// <param name="CharacterId">Идентификатор персонажа.</param>
    /// <param name="HitDieType">Тип кости (например, 6, 8, 10, 12).</param>
    /// <param name="Roll">Результат броска кости хитов.</param>
    /// <param name="ConstitutionModifier">Модификатор телосложения персонажа.</param>
    public record SpendHitDie(
        Guid CharacterId,
        int HitDieType,
        int Roll,
        int ConstitutionModifier) : ICommand;

    /// <summary>
    /// Команда прерывания отдыха (например, из-за нападения).
    /// </summary>
    /// <param name="CharacterId">Идентификатор персонажа.</param>
    /// <param name="InterruptionType">Тип прерывания: "Combat", "StrenuousActivity", "Environmental".</param>
    public record InterruptRest(
        Guid CharacterId,
        string InterruptionType) : ICommand;

    /// <summary>
    /// Команда завершения отдыха и применения всех его эффектов.
    /// </summary>
    /// <param name="CharacterId">Идентификатор персонажа.</param>
    public record EndRest(Guid CharacterId) : ICommand;
}