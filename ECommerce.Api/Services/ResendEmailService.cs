using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ECommerce.Api.Models;

namespace ECommerce.Api.Services;

public class ResendEmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ResendEmailService> _logger;
    private readonly HttpClient _httpClient;

    public ResendEmailService(IOptions<EmailSettings> emailSettings, ILogger<ResendEmailService> logger, HttpClient httpClient)
    {
        _emailSettings = emailSettings.Value;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlMessage)
    {
        try
        {
            var requestBody = new
            {
                from = $"{_emailSettings.SenderName} <{_emailSettings.SenderEmail}>",
                to = new[] { to },
                subject = subject,
                html = htmlMessage
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _emailSettings.ApiKey);

            var response = await _httpClient.PostAsync("https://api.resend.com/emails", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation($"Email sent successfully to {to}");
            }
            else
            {
                var errorResponse = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to send email to {to}. Status: {response.StatusCode}, Response: {errorResponse}");
                throw new Exception($"Resend API Error: {errorResponse}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send email to {to}: {ex.Message}");
            throw; 
        }
    }
}
