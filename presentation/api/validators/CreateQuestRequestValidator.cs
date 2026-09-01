using FluentValidation;
using static dnd_game.presentation.api.Schemas;

namespace dnd_game.presentation.api.validators
{
    /// <summary>
    /// Валидатор запроса на создание квеста.
    /// Проверяет идентификатор, название, цели и участников.
    /// </summary>
    public class CreateQuestRequestValidator : AbstractValidator<CreateQuestRequest>
    {
        public CreateQuestRequestValidator()
        {
            RuleFor(x => x.QuestId)
                .NotEmpty().WithMessage("Идентификатор квеста обязателен.")
                .Must(id => id != Guid.Empty).WithMessage("Идентификатор квеста не может быть пустым GUID.");

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Название квеста обязательно.")
                .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("Название квеста не может состоять только из пробелов.")
                .MaximumLength(200).WithMessage("Название квеста не должно превышать 200 символов.");

            RuleFor(x => x.Objectives)
                .NotNull().WithMessage("Список целей обязателен.")
                .Must(o => o.Count > 0).WithMessage("Должна быть хотя бы одна цель.");

            RuleForEach(x => x.Objectives)
                .ChildRules(objective =>
                {
                    objective.RuleFor(o => o.Description)
                        .NotEmpty().WithMessage("Описание цели обязательно.")
                        .Must(desc => !string.IsNullOrWhiteSpace(desc)).WithMessage("Описание цели не может состоять только из пробелов.");

                    objective.RuleFor(o => o.RequiredProgress)
                        .GreaterThan(0).WithMessage("Требуемый прогресс цели должен быть больше 0.");
                });

            RuleFor(x => x.ParticipantIds)
                .NotNull().WithMessage("Список участников обязателен.");

            RuleForEach(x => x.ParticipantIds)
                .Must(id => id != Guid.Empty).WithMessage("Идентификатор участника не может быть пустым GUID.");
        }
    }
}