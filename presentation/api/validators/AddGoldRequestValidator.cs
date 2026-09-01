using FluentValidation;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api.validators
{
    /// <summary>
    /// Валидатор запроса на добавление золота.
    /// Проверяет, что сумма положительна и не превышает разумный предел.
    /// </summary>
    public class AddGoldRequestValidator : AbstractValidator<AddGoldRequest>
    {
        private const int MaxGoldAmount = 1_000_000_000; // 1 млрд золотых — достаточно большой предел

        public AddGoldRequestValidator()
        {
            RuleFor(x => x.Amount)
                .GreaterThan(0)
                .WithMessage("Количество золота должно быть положительным.");

            RuleFor(x => x.Amount)
                .LessThanOrEqualTo(MaxGoldAmount)
                .WithMessage($"Количество золота не должно превышать {MaxGoldAmount}.");
        }
    }
}