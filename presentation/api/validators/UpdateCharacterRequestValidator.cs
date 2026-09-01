using FluentValidation;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api.validators
{
    /// <summary>
    /// Валидатор запроса на обновление персонажа.
    /// Проверяет имя и максимальные хиты, если они указаны.
    /// </summary>
    public class UpdateCharacterRequestValidator : AbstractValidator<UpdateCharacterRequest>
    {
        public UpdateCharacterRequestValidator()
        {
            // Если имя передано, проверяем, что оно не пустое и не превышает 50 символов.
            When(x => x.Name != null, () =>
            {
                RuleFor(x => x.Name)
                    .NotEmpty().WithMessage("Имя персонажа не может быть пустым, если оно указано.")
                    .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Имя персонажа не может состоять только из пробелов.")
                    .MaximumLength(50).WithMessage("Имя персонажа не должно превышать 50 символов.");
            });

            // Если максимальные хиты указаны, проверяем их диапазон.
            When(x => x.MaxHitPoints.HasValue, () =>
            {
                RuleFor(x => x.MaxHitPoints)
                    .GreaterThan(0).WithMessage("Максимальные хиты должны быть больше 0.")
                    .LessThanOrEqualTo(1000).WithMessage("Максимальные хиты не могут превышать 1000.");
            });
        }
    }
}