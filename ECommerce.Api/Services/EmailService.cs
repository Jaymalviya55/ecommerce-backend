using Microsoft.Extensions.Options;
using ECommerce.Api.Models;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace ECommerce.Api.Services;

public class EmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> emailSettings, ILogger<EmailService> logger)
    {
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlMessage)
    {
        try
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
            email.To.Add(MailboxAddress.Parse(to));
            email.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = htmlMessage };
            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            
            // SecureSocketOptions.Auto will automatically use Implicit SSL on port 465 
            // and STARTTLS on port 587. It's smart enough to handle whatever we pass it!
            await smtp.ConnectAsync(_emailSettings.SmtpServer, _emailSettings.SmtpPort, SecureSocketOptions.Auto);
            
            await smtp.AuthenticateAsync(_emailSettings.SenderEmail, _emailSettings.Password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
            
            _logger.LogInformation($"Email sent successfully to {to}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send email to {to}: {ex.Message}");
            throw; 
        }
    }
}
