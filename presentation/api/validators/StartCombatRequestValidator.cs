using FluentValidation;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api.validators
{
    /// <summary>
    /// Валидатор запроса на начало боя.
    /// Проверяет идентификатор боя и список участников.
    /// </summary>
    public class StartCombatRequestValidator : AbstractValidator<StartCombatRequest>
    {
        public StartCombatRequestValidator()
        {
            RuleFor(x => x.CombatId)
                .NotEmpty().WithMessage("Идентификатор боя обязателен.")
                .Must(id => id != Guid.Empty).WithMessage("Идентификатор боя не может быть пустым GUID.");

            RuleFor(x => x.Participants)
                .NotNull().WithMessage("Список участников обязателен.")
                .Must(p => p.Count >= 2).WithMessage("Требуется как минимум два участника.");

            RuleForEach(x => x.Participants)
                .NotEmpty().WithMessage("Идентификатор участника не может быть пустым GUID.")
                .Must(id => id != Guid.Empty).WithMessage("Идентификатор участника не может быть пустым GUID.");
        }
    }
}