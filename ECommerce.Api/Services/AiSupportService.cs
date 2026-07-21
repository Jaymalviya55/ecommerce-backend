using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECommerce.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace ECommerce.Api.Services
{
    public interface IAiSupportService
    {
        Task<string> HandleCustomerMessageAsync(SupportTicket ticket);
        Task<string> SummarizeConversationAsync(SupportTicket ticket);
        Task<string> DraftAgentReplyAsync(SupportTicket ticket);
    }

    public class AiSupportService : IAiSupportService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public AiSupportService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _apiKey = config["OpenRouterSettings:ApiKey"] ?? "";
            _model = config["OpenRouterSettings:Model"] ?? "google/gemma-4-26b-a4b-it:free";
            
            _httpClient.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5173"); // Required by OpenRouter
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Exousia Support AI");
        }

        public async Task<string> HandleCustomerMessageAsync(SupportTicket ticket)
        {
            var systemPrompt = @"You are 'Exousia AI', the helpful, professional, and friendly first-line customer support assistant for Exousia E-commerce.
Your goal is to solve the customer's problem if possible. Be concise.
CRITICAL INSTRUCTION: If you cannot resolve their issue (e.g., they need a manual refund, policy override, or explicitly demand a human), or if you reach the end of your capabilities, you MUST output the exact string: [ESCALATE_TO_HUMAN]. Do not output this string unless you are stuck or the user asks for a human.";

            return await CallOpenRouterAsync(systemPrompt, ticket);
        }

        public async Task<string> SummarizeConversationAsync(SupportTicket ticket)
        {
            var systemPrompt = @"You are an internal AI assistant for support agents. 
The customer has requested to speak to a human. Please provide a brief, bulleted summary of the customer's problem based on the chat history so the human agent can quickly understand the context before taking over.";

            return await CallOpenRouterAsync(systemPrompt, ticket);
        }

        public async Task<string> DraftAgentReplyAsync(SupportTicket ticket)
        {
            var systemPrompt = @"You are an AI co-pilot for a human support agent at Exousia E-commerce.
Draft a polite, professional, and empathetic response to the customer based on the chat history. The agent will review your draft before sending it. 
Do not include placeholders like [Agent Name], just write the core message.";

            return await CallOpenRouterAsync(systemPrompt, ticket);
        }

        private async Task<string> CallOpenRouterAsync(string systemPrompt, SupportTicket ticket)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            foreach (var msg in ticket.Messages.OrderBy(m => m.CreatedAt))
            {
                if (msg.IsInternalNote) continue; // Skip internal notes for AI context
                
                var role = msg.IsStaff ? "assistant" : "user";
                messages.Add(new { role, content = msg.Message });
            }

            // If there's no conversation yet, we must send at least one user message
            if (messages.Count == 1)
            {
                messages.Add(new { role = "user", content = "Hello" });
            }

            var requestBody = new
            {
                model = _model,
                messages = messages,
                temperature = 0.7
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("chat/completions", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"OpenRouter API Error: {error}");
                return "I apologize, but I am currently experiencing technical difficulties connecting to my brain. Please click 'Connect to Customer Executive' to speak with a human.";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            
            try
            {
                var reply = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return reply ?? "Error generating response.";
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to parse OpenRouter response: " + ex.Message);
                return "I apologize, but I am currently experiencing technical difficulties connecting to my brain. Please click 'Connect to Customer Executive' to speak with a human.";
            }
        }
    }
}
