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
            _apiKey = config["GeminiSettings:ApiKey"] ?? "";
            _model = config["GeminiSettings:Model"] ?? "gemini-1.5-flash";
            
            _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
            _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);
        }

        public async Task<string> HandleCustomerMessageAsync(SupportTicket ticket)
        {
            var systemPrompt = @"You are 'Exousia AI', the helpful, professional, and friendly first-line customer support assistant for Exousia E-commerce.
Your goal is to solve the customer's problem if possible. Be concise.
CRITICAL INSTRUCTION: If you cannot resolve their issue (e.g., they need a manual refund, policy override, or explicitly demand a human), or if you reach the end of your capabilities, you MUST output the exact string: [ESCALATE_TO_HUMAN]. Do not output this string unless you are stuck or the user asks for a human.";

            return await CallGeminiAsync(systemPrompt, ticket);
        }

        public async Task<string> SummarizeConversationAsync(SupportTicket ticket)
        {
            var systemPrompt = @"You are an internal AI assistant for support agents. 
The customer has requested to speak to a human. Please provide a brief, bulleted summary of the customer's problem based on the chat history so the human agent can quickly understand the context before taking over.";

            return await CallGeminiAsync(systemPrompt, ticket);
        }

        public async Task<string> DraftAgentReplyAsync(SupportTicket ticket)
        {
            var systemPrompt = @"You are an AI co-pilot for a human support agent at Exousia E-commerce.
Draft a polite, professional, and empathetic response to the customer based on the chat history. The agent will review your draft before sending it. 
Do not include placeholders like [Agent Name], just write the core message.";

            return await CallGeminiAsync(systemPrompt, ticket);
        }

        private async Task<string> CallGeminiAsync(string systemPrompt, SupportTicket ticket)
        {
            var contents = new List<object>();

            foreach (var msg in ticket.Messages.OrderBy(m => m.CreatedAt))
            {
                if (msg.IsInternalNote) continue; // Skip internal notes for AI context
                
                var role = msg.IsStaff ? "model" : "user";
                contents.Add(new { role, parts = new[] { new { text = msg.Message } } });
            }

            // If there's no conversation yet, we must send at least one user message to Gemini
            if (contents.Count == 0)
            {
                contents.Add(new { role = "user", parts = new[] { new { text = "Hello" } } });
            }

            var requestBody = new
            {
                systemInstruction = new
                {
                    role = "system",
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = contents,
                generationConfig = new { temperature = 0.7 }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"models/{_model}:generateContent?key={_apiKey}", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Gemini API Error: {error}");
                return "I apologize, but I am currently experiencing technical difficulties connecting to my brain. Please click 'Connect to Customer Executive' to speak with a human.";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            
            try
            {
                var reply = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return reply ?? "Error generating response.";
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to parse Gemini response: " + ex.Message);
                return "I apologize, but I am currently experiencing technical difficulties connecting to my brain. Please click 'Connect to Customer Executive' to speak with a human.";
            }
        }
    }
}
