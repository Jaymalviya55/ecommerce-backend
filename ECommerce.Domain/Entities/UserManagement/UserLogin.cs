namespace ECommerce.Domain.Entities.UserManagement;

public class UserLogin
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int UserTypeId { get; set; }
    public int UserLevelId { get; set; }
    public string? UserReferenceId { get; set; }
    public string? TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefaultPasswordChange { get; set; }
    public int LoginAttemptsCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UserType? UserType { get; set; }
    public UserLevel? UserLevel { get; set; }
}
