namespace ECommerce.Domain.Entities.UserManagement;

public class UserLevel
{
    public int UserLevelId { get; set; }
    public int UserTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UserType? UserType { get; set; }
}
