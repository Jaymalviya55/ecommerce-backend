using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ECommerce.Infrastructure.Data;
using ECommerce.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Razorpay.Api;
using System.IO;
using System.Text.Json;

namespace ECommerce.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhooksController : ControllerBase
{
    private readonly ECommerceDbContext _context;
    private readonly IConfiguration _configuration;

    public WebhooksController(ECommerceDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpPost("razorpay")]
    public async Task<IActionResult> RazorpayWebhook()
    {
        string webhookSecret = _configuration["RazorpaySettings:WebhookSecret"] ?? "";
        
        // 1. Read the cryptographic signature from Razorpay
        string signature = Request.Headers["X-Razorpay-Signature"].ToString();
        
        using var reader = new StreamReader(Request.Body);
        string payload = await reader.ReadToEndAsync();

        try
        {
            // 2. VERY IMPORTANT: Cryptographically verify the payload.
            // If a hacker tries to send a fake webhook, this function will throw an exception!
            Utils.verifyWebhookSignature(payload, signature, webhookSecret);
            
            // 3. It's verified! Parse the JSON
            var data = JsonDocument.Parse(payload);
            var eventName = data.RootElement.GetProperty("event").GetString();

            // 4. Check if the event is specifically a successful payment
            if (eventName == "order.paid" || eventName == "payment.captured")
            {
                var paymentEntity = data.RootElement.GetProperty("payload").GetProperty("payment").GetProperty("entity");
                string razorpayOrderId = paymentEntity.GetProperty("order_id").GetString() ?? "";
                string razorpayPaymentId = paymentEntity.GetProperty("id").GetString() ?? "";

                // 5. Find the pending order in our database
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.RazorpayOrderId == razorpayOrderId);
                if (order != null)
                {
                    // 6. Update the Order Status to Paid!
                    order.Status = OrderStatus.Paid;
                    order.RazorpayPaymentId = razorpayPaymentId;
                    await _context.SaveChangesAsync();
                }
            }

            // Always return HTTP 200 OK to tell Razorpay we processed the webhook successfully
            return Ok(); 
        }
        catch (Exception)
        {
            // Signature verification failed. Return Bad Request so we don't process fake data.
            return BadRequest();
        }
    }
}
