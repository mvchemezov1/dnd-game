using FluentValidation;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api.validators
{
    /// <summary>
    /// Валидатор запроса на создание персонажа.
    /// Проверяет имя и максимальные хиты.
    /// </summary>
    public class CreateCharacterRequestValidator : AbstractValidator<CreateCharacterRequest>
    {
        public CreateCharacterRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Имя персонажа обязательно.")
                .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Имя персонажа не может состоять только из пробелов.")
                .MaximumLength(50).WithMessage("Имя персонажа не должно превышать 50 символов.");

            RuleFor(x => x.MaxHitPoints)
                .GreaterThan(0).WithMessage("Максимальные хиты должны быть больше 0.")
                .LessThanOrEqualTo(1000).WithMessage("Максимальные хиты не могут превышать 1000.");
        }
    }
}