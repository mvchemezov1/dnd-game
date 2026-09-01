#nullable enable
using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.infrastructure.config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace dnd_game.infrastructure.notifications
{
    /// <summary>
    /// Реализация <see cref="IEmailSender"/> с использованием SMTP-клиента.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpSettings _settings;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender>? logger = null)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? NullLogger<SmtpEmailSender>.Instance;
        }

        public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(to))
                throw new ArgumentException("Адрес получателя не может быть пустым.", nameof(to));
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Тема письма не может быть пустой.", nameof(subject));
            if (string.IsNullOrWhiteSpace(body))
                throw new ArgumentException("Тело письма не может быть пустым.", nameof(body));
            cancellationToken.ThrowIfCancellationRequested();

            using var mail = new MailMessage
            {
                From = new MailAddress(_settings.FromEmail, _settings.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mail.To.Add(to);

            using var smtp = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = _settings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password)
            };

            try
            {
                await smtp.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Письмо отправлено на {To}. Тема: {Subject}", to, subject);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Отправка письма на {To} отменена.", to);
                throw;
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "Ошибка SMTP при отправке письма на {To}.", to);
                throw new InvalidOperationException("Не удалось отправить письмо. Проверьте настройки SMTP.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Непредвиденная ошибка при отправке письма на {To}.", to);
                throw;
            }
        }
    }
}