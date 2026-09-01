#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using dnd_game.domain.exceptions;

namespace dnd_game.infrastructure.exceptions
{
    /// <summary>
    /// Глобальный обработчик исключений для API. Преобразует доменные исключения в структурированные
    /// ответы ProblemDetails (RFC 7807) с русскими сообщениями.
    /// </summary>
    public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        /// <summary>
        /// Обрабатывает исключение и формирует ответ.
        /// </summary>
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Логируем с дополнительным контекстом запроса
            _logger.LogError(exception,
                "Необработанное исключение при запросе {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);

            var problemDetails = BuildProblemDetails(httpContext, exception);

            httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
            httpContext.Response.ContentType = "application/problem+json";

            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
            return true;
        }

        /// <summary>
        /// Создаёт объект ProblemDetails на основе типа исключения.
        /// </summary>
        private static ProblemDetails BuildProblemDetails(HttpContext httpContext, Exception exception)
        {
            var problemDetails = new ProblemDetails
            {
                Instance = httpContext.Request.Path,
                Type = exception.GetType().Name,
                Title = "Ошибка",
                Detail = exception.Message
            };

            switch (exception)
            {
                case InvalidAction invalidAction:
                    problemDetails.Status = StatusCodes.Status400BadRequest;
                    problemDetails.Title = "Недопустимое действие";
                    problemDetails.Detail = invalidAction.Message;
                    if (!string.IsNullOrEmpty(invalidAction.ActionName))
                        problemDetails.Extensions["actionName"] = invalidAction.ActionName;
                    if (invalidAction.CharacterId.HasValue)
                        problemDetails.Extensions["characterId"] = invalidAction.CharacterId.Value;
                    break;

                case RuleViolation ruleViolation:
                    problemDetails.Status = StatusCodes.Status400BadRequest;
                    problemDetails.Title = "Нарушение правил";
                    problemDetails.Detail = ruleViolation.Message;
                    problemDetails.Extensions["ruleName"] = ruleViolation.RuleName;
                    if (!string.IsNullOrEmpty(ruleViolation.RuleReference))
                        problemDetails.Extensions["ruleReference"] = ruleViolation.RuleReference;
                    break;

                case EntityNotFoundException notFound:
                    problemDetails.Status = StatusCodes.Status404NotFound;
                    problemDetails.Title = "Сущность не найдена";
                    problemDetails.Detail = notFound.Message;
                    problemDetails.Extensions["entityType"] = notFound.EntityType;
                    problemDetails.Extensions["entityId"] = notFound.EntityId;
                    break;

                case StateConflictException stateConflict:
                    problemDetails.Status = StatusCodes.Status409Conflict;
                    problemDetails.Title = "Конфликт состояния";
                    problemDetails.Detail = stateConflict.Message;
                    problemDetails.Extensions["aggregateId"] = stateConflict.AggregateId;
                    problemDetails.Extensions["expectedVersion"] = stateConflict.ExpectedVersion;
                    problemDetails.Extensions["actualVersion"] = stateConflict.ActualVersion;
                    break;

                case UnauthorizedActionException unauthorized:
                    problemDetails.Status = StatusCodes.Status403Forbidden;
                    problemDetails.Title = "Недостаточно прав";
                    problemDetails.Detail = unauthorized.Message;
                    problemDetails.Extensions["userId"] = unauthorized.UserId;
                    problemDetails.Extensions["action"] = unauthorized.Action;
                    break;

                case OperationCanceledException:
                    // Отмена запроса не считается ошибкой, но для единообразия возвращаем 499 (Client Closed Request)
                    problemDetails.Status = 499; // нестандартный код, можно заменить на 408 или 400
                    problemDetails.Title = "Запрос отменён";
                    problemDetails.Detail = "Клиент отменил запрос.";
                    break;

                case DomainError domainError:
                    problemDetails.Status = StatusCodes.Status400BadRequest;
                    problemDetails.Title = "Ошибка домена";
                    problemDetails.Detail = domainError.Message;
                    break;

                default:
                    problemDetails.Status = StatusCodes.Status500InternalServerError;
                    problemDetails.Title = "Внутренняя ошибка сервера";
                    problemDetails.Detail = "Произошла непредвиденная ошибка. Пожалуйста, попробуйте позже.";
                    // В production не раскрываем детали неизвестных исключений
#if !DEBUG
                    problemDetails.Detail = "Произошла непредвиденная ошибка. Подробности доступны в журнале сервера.";
#endif
                    break;
            }

            return problemDetails;
        }
    }
}