#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.notifications
{
    /// <summary>Отправляет email-сообщения.</summary>
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    }

    /// <summary>Заглушка: пишет письмо в лог вместо реальной отправки.</summary>
    public class LogEmailSender : IEmailSender
    {
        private readonly ILogger<LogEmailSender> _logger;

        public LogEmailSender(ILogger<LogEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[EMAIL] To: {To}\nSubject: {Subject}\nBody: {Body}", to, subject, body);
            return Task.CompletedTask;
        }
    }
}