using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ECommerce.Infrastructure.Data;
using System.Security.Claims;

namespace ECommerce.Api.Hubs;

[Authorize]
public class SupportChatHub : Hub
{
    private readonly ECommerceDbContext _context;

    public SupportChatHub(ECommerceDbContext context)
    {
        _context = context;
    }

    public async Task JoinTicketGroup(string ticketId)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email) ?? Context.User?.Identity?.Name;
        var isStaff = Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("SupportAgent") == true;

        if (int.TryParse(ticketId, out int parsedId))
        {
            var ticket = await _context.SupportTickets.FindAsync(parsedId);
            if (ticket != null && (isStaff || ticket.CustomerEmail == email))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"Ticket_{ticketId}");
                if (isStaff)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"StaffTicket_{ticketId}");
                }
            }
            else
            {
                throw new HubException("Unauthorized access to this ticket group.");
            }
        }
    }

    public async Task LeaveTicketGroup(string ticketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Ticket_{ticketId}");
    }

    public async Task Typing(string ticketId, bool isTyping)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email) ?? Context.User?.Identity?.Name ?? "Unknown";
        // Send to everyone else in the group EXCEPT the sender
        await Clients.GroupExcept($"Ticket_{ticketId}", Context.ConnectionId).SendAsync("ReceiveTyping", email, isTyping);
    }
}
