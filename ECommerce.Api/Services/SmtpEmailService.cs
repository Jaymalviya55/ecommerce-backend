using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using ECommerce.Api.Models;

namespace ECommerce.Api.Services;

public class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailSettings> emailSettings, ILogger<SmtpEmailService> logger)
    {
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlMessage)
    {
        try
        {
            var mailMessage = new MailMessage
            {
                From = new MailAddress(_emailSettings.SenderEmail, _emailSettings.SenderName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };
            mailMessage.To.Add(to);

            using var smtpClient = new SmtpClient(_emailSettings.SmtpServer, _emailSettings.SmtpPort)
            {
                Credentials = new NetworkCredential(_emailSettings.SenderEmail, _emailSettings.Password),
                EnableSsl = true
            };

            await smtpClient.SendMailAsync(mailMessage);
            _logger.LogInformation($"Email sent successfully to {to} via SMTP");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send email to {to} via SMTP: {ex.Message}");
            throw;
        }
    }
}
