namespace ECommerce.Domain.Entities;

public class TicketMessage
{
    public int Id { get; set; }
    public int SupportTicketId { get; set; }
    public SupportTicket Ticket { get; set; } = null!;
    
    public string SenderEmail { get; set; } = string.Empty;
    public bool IsStaff { get; set; } // True if sent by a SupportAgent/Admin
    
    public string Message { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public bool IsInternalNote { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
