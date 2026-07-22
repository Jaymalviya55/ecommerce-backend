namespace ECommerce.Api.Controllers;

using ECommerce.Domain.Entities;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using ECommerce.Api.Hubs;
using ECommerce.Api.Services;
using Microsoft.Extensions.DependencyInjection;

[ApiController]
[Route("api/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly ECommerceDbContext _context;
    private readonly IHubContext<SupportChatHub> _hubContext;
    private readonly IAiSupportService _aiService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiTaskQueue _taskQueue;

    public TicketsController(ECommerceDbContext context, IHubContext<SupportChatHub> hubContext, IAiSupportService aiService, IServiceScopeFactory scopeFactory, AiTaskQueue taskQueue)
    {
        _context = context;
        _hubContext = hubContext;
        _aiService = aiService;
        _scopeFactory = scopeFactory;
        _taskQueue = taskQueue;
    }

    private TicketPriority DeterminePriority(string subject, string message)
    {
        var combinedText = (subject + " " + message).ToLowerInvariant();
        
        var urgentKeywords = new[] { "payment", "charge", "fraud", "unauthorized", "scam", "stolen", "hack" };
        var highKeywords = new[] { "damage", "broken", "missing", "wrong", "defect", "shattered", "ruined" };
        var mediumKeywords = new[] { "delay", "late", "where is", "tracking", "return", "refund" };

        if (urgentKeywords.Any(k => combinedText.Contains(k))) return TicketPriority.Urgent;
        if (highKeywords.Any(k => combinedText.Contains(k))) return TicketPriority.High;
        if (mediumKeywords.Any(k => combinedText.Contains(k))) return TicketPriority.Medium;
        
        return TicketPriority.Low;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest request)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email)) return Unauthorized();

        var ticket = new SupportTicket
        {
            CustomerEmail = email,
            Subject = request.Subject,
            Priority = DeterminePriority(request.Subject, request.Message),
            OrderId = request.OrderId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = TicketStatus.Open
        };

        var initialMessage = new TicketMessage
        {
            SenderEmail = email,
            IsStaff = false,
            Message = request.Message,
            AttachmentUrl = request.AttachmentUrl,
            CreatedAt = DateTime.UtcNow
        };

        ticket.Messages.Add(initialMessage);

        _context.SupportTickets.Add(ticket);
        await _context.SaveChangesAsync();

        return Ok(ticket);
    }

    [Authorize]
    [HttpGet("my-tickets")]
    public async Task<IActionResult> GetMyTickets()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email)) return Unauthorized();

        var tickets = await _context.SupportTickets
            .Where(t => t.CustomerEmail == email)
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new {
                t.Id,
                t.Subject,
                Status = t.Status.ToString(),
                Priority = t.Priority.ToString(),
                t.UpdatedAt
            })
            .ToListAsync();

        return Ok(tickets);
    }

    [Authorize(Roles = "Admin,SupportAgent")]
    [HttpGet]
    public async Task<IActionResult> GetAllTickets([FromQuery] string status = "Open")
    {
        var query = _context.SupportTickets.AsQueryable();
        
        if (Enum.TryParse<TicketStatus>(status, out var parsedStatus))
        {
            query = query.Where(t => t.Status == parsedStatus);
        }

        var tickets = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new {
                t.Id,
                t.CustomerEmail,
                t.Subject,
                t.AssignedToEmail,
                t.IsHumanRequested,
                Status = t.Status.ToString(),
                Priority = t.Priority.ToString(),
                t.UpdatedAt
            })
            .ToListAsync();

        return Ok(tickets);
    }

    [Authorize(Roles = "Admin,SupportAgent")]
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        var allTickets = await _context.SupportTickets.ToListAsync();
        
        var totalTickets = allTickets.Count;
        var openTickets = allTickets.Count(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress);
        var resolvedTickets = allTickets.Count(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed);
        
        var resolvedList = allTickets.Where(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed).ToList();
        double avgResolutionTimeHours = 0;
        if (resolvedList.Any())
        {
            avgResolutionTimeHours = resolvedList.Average(t => (t.UpdatedAt - t.CreatedAt).TotalHours);
        }

        var ticketsByStatus = allTickets
            .GroupBy(t => t.Status.ToString())
            .Select(g => new { name = g.Key, value = g.Count() })
            .ToList();

        var agentPerformance = allTickets
            .Where(t => !string.IsNullOrEmpty(t.AssignedToEmail))
            .GroupBy(t => t.AssignedToEmail)
            .Select(g => new {
                agent = g.Key,
                resolved = g.Count(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed),
                open = g.Count(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress)
            })
            .OrderByDescending(a => a.resolved)
            .ToList();

        var last7Days = Enumerable.Range(0, 7).Select(i => DateTime.UtcNow.Date.AddDays(-i)).Reverse().ToList();
        var ticketsByDay = last7Days.Select(date => new {
            date = date.ToString("MMM dd"),
            tickets = allTickets.Count(t => t.CreatedAt.Date == date)
        }).ToList();

        var analytics = new {
            totalTickets,
            openTickets,
            resolvedTickets,
            avgResolutionTimeHours = Math.Round(avgResolutionTimeHours, 1),
            ticketsByStatus,
            agentPerformance,
            ticketsByDay
        };

        return Ok(analytics);
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTicket(int id)
    {
        var ticket = await _context.SupportTickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket == null) return NotFound("Ticket not found");

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var isStaff = User.IsInRole("Admin") || User.IsInRole("SupportAgent");

        if (!isStaff && ticket.CustomerEmail != email)
        {
            return Forbid(); // Customer can only see their own tickets
        }

        var messages = ticket.Messages.OrderBy(m => m.CreatedAt).AsEnumerable();
        if (!isStaff)
        {
            messages = messages.Where(m => !m.IsInternalNote);
        }

        var result = new {
            ticket.Id,
            ticket.CustomerEmail,
            ticket.Subject,
            ticket.AssignedToEmail,
            ticket.IsHumanRequested,
            Status = ticket.Status.ToString(),
            Priority = ticket.Priority.ToString(),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            Messages = messages.Select(m => new {
                m.Id,
                m.SenderEmail,
                m.Message,
                m.AttachmentUrl,
                m.IsInternalNote,
                m.IsStaff,
                m.CreatedAt
            })
        };

        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id}/reply")]
    public async Task<IActionResult> ReplyToTicket(int id, [FromBody] ReplyTicketRequest request)
    {
        var ticket = await _context.SupportTickets.FindAsync(id);
        if (ticket == null) return NotFound("Ticket not found");

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var isStaff = User.IsInRole("Admin") || User.IsInRole("SupportAgent");

        if (!isStaff && ticket.CustomerEmail != email)
        {
            return Forbid();
        }

        if (request.IsInternalNote && !isStaff)
        {
            return Forbid();
        }

        var message = new TicketMessage
        {
            SupportTicketId = id,
            SenderEmail = email ?? "Unknown",
            IsStaff = isStaff,
            Message = request.Message,
            AttachmentUrl = request.AttachmentUrl,
            IsInternalNote = request.IsInternalNote,
            CreatedAt = DateTime.UtcNow
        };

        ticket.UpdatedAt = DateTime.UtcNow;
        if (isStaff && ticket.Status == TicketStatus.Open && !request.IsInternalNote)
        {
            ticket.Status = TicketStatus.InProgress;
        }

        _context.TicketMessages.Add(message);
        await _context.SaveChangesAsync();

        var messageResponse = new { message.Id, message.SenderEmail, message.Message, message.AttachmentUrl, message.IsInternalNote, message.IsStaff, message.CreatedAt };
        
        if (message.IsInternalNote)
        {
            await _hubContext.Clients.Group($"StaffTicket_{id}").SendAsync("ReceiveMessage", messageResponse);
        }
        else
        {
            await _hubContext.Clients.Group($"Ticket_{id}").SendAsync("ReceiveMessage", messageResponse);
            // Also notify staff of a customer reply
            await _hubContext.Clients.Group($"StaffTicket_{id}").SendAsync("ReceiveMessage", messageResponse);
        }

        // If customer replied and AI is active
        if (!isStaff && !ticket.IsHumanRequested)
        {
            // Background process the AI response safely using the queue
            await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<ECommerceDbContext>();
                    var scopedAiService = scope.ServiceProvider.GetRequiredService<IAiSupportService>();
                    var scopedHubContext = scope.ServiceProvider.GetRequiredService<IHubContext<SupportChatHub>>();

                    var scopedTicket = await scopedContext.SupportTickets
                        .Include(t => t.Messages)
                        .FirstOrDefaultAsync(t => t.Id == id, token);

                    if (scopedTicket != null)
                    {
                        var aiReply = await scopedAiService.HandleCustomerMessageAsync(scopedTicket);
                        
                        var aiMessage = new TicketMessage
                        {
                            SupportTicketId = id,
                            SenderEmail = "Exousia AI",
                            IsStaff = true,
                            Message = aiReply,
                            IsInternalNote = false,
                            CreatedAt = DateTime.UtcNow
                        };
                        
                        scopedContext.TicketMessages.Add(aiMessage);
                        await scopedContext.SaveChangesAsync(token);

                        var aiResponseData = new { aiMessage.Id, aiMessage.SenderEmail, aiMessage.Message, aiMessage.AttachmentUrl, aiMessage.IsInternalNote, aiMessage.IsStaff, aiMessage.CreatedAt };
                        await scopedHubContext.Clients.Group($"Ticket_{id}").SendAsync("ReceiveMessage", aiResponseData, token);
                        await scopedHubContext.Clients.Group($"StaffTicket_{id}").SendAsync("ReceiveMessage", aiResponseData, token);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("AI Reply Error: " + ex.Message);
                }
            });
        }

        return Ok(messageResponse);
    }

    [Authorize]
    [HttpPut("{id}/escalate")]
    public async Task<IActionResult> EscalateTicket(int id)
    {
        var ticket = await _context.SupportTickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket == null) return NotFound("Ticket not found");

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (ticket.CustomerEmail != email) return Forbid();

        if (ticket.IsHumanRequested) return BadRequest("Already escalated");

        ticket.IsHumanRequested = true;
        ticket.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _taskQueue.QueueBackgroundWorkItemAsync(async token =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedContext = scope.ServiceProvider.GetRequiredService<ECommerceDbContext>();
                var scopedAiService = scope.ServiceProvider.GetRequiredService<IAiSupportService>();
                var scopedHubContext = scope.ServiceProvider.GetRequiredService<IHubContext<SupportChatHub>>();

                var scopedTicket = await scopedContext.SupportTickets
                    .Include(t => t.Messages)
                    .FirstOrDefaultAsync(t => t.Id == id, token);

                if (scopedTicket != null)
                {
                    var summary = await scopedAiService.SummarizeConversationAsync(scopedTicket);
                    var summaryMessage = new TicketMessage
                    {
                        SupportTicketId = id,
                        SenderEmail = "Exousia AI (Summary)",
                        IsStaff = true,
                        Message = "AI Summary of Chat:\n" + summary,
                        IsInternalNote = true,
                        CreatedAt = DateTime.UtcNow
                    };
                            
                    scopedContext.TicketMessages.Add(summaryMessage);
                    await scopedContext.SaveChangesAsync(token);
                    
                    var msgData = new { summaryMessage.Id, summaryMessage.SenderEmail, summaryMessage.Message, summaryMessage.AttachmentUrl, summaryMessage.IsInternalNote, summaryMessage.IsStaff, summaryMessage.CreatedAt };
                    await scopedHubContext.Clients.Group($"StaffTicket_{id}").SendAsync("ReceiveMessage", msgData, token);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AI Summary Error: " + ex.Message);
            }
        });

        return Ok(new { Message = "Ticket escalated to human agents." });
    }

    [Authorize(Roles = "Admin,SupportAgent")]
    [HttpGet("{id}/draft-reply")]
    public async Task<IActionResult> DraftReply(int id)
    {
        var ticket = await _context.SupportTickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == id);
        if (ticket == null) return NotFound("Ticket not found");

        var draft = await _aiService.DraftAgentReplyAsync(ticket);
        return Ok(new { Draft = draft });
    }

    [Authorize(Roles = "Admin,SupportAgent")]
    [HttpPut("{id}/resolve")]
    public async Task<IActionResult> ResolveTicket(int id)
    {
        var ticket = await _context.SupportTickets.FindAsync(id);
        if (ticket == null) return NotFound("Ticket not found");

        ticket.Status = TicketStatus.Resolved;
        ticket.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var systemMessage = new {
            Id = -1,
            SenderEmail = "System",
            Message = "This ticket has been marked as resolved.",
            IsStaff = true,
            CreatedAt = DateTime.UtcNow
        };
        await _hubContext.Clients.Group($"Ticket_{id}").SendAsync("ReceiveMessage", systemMessage);
        await _hubContext.Clients.Group($"Ticket_{id}").SendAsync("TicketResolved");

        return Ok(new { Message = "Ticket resolved" });
    }

    [Authorize(Roles = "Admin,SupportAgent")]
    [HttpPut("{id}/claim")]
    public async Task<IActionResult> ClaimTicket(int id)
    {
        var ticket = await _context.SupportTickets.FindAsync(id);
        if (ticket == null) return NotFound("Ticket not found");

        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        ticket.AssignedToEmail = email;
        ticket.UpdatedAt = DateTime.UtcNow;
        if (ticket.Status == TicketStatus.Open) ticket.Status = TicketStatus.InProgress;

        await _context.SaveChangesAsync();

        var systemMessage = new {
            Id = -1,
            SenderEmail = "System",
            Message = $"Ticket claimed by {email}",
            IsInternalNote = true,
            IsStaff = true,
            CreatedAt = DateTime.UtcNow
        };
        await _hubContext.Clients.Group($"StaffTicket_{id}").SendAsync("ReceiveMessage", systemMessage);

        return Ok(ticket);
    }
}

public class CreateTicketRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public int? OrderId { get; set; }
}

public class ReplyTicketRequest
{
    public string Message { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public bool IsInternalNote { get; set; } = false;
}
