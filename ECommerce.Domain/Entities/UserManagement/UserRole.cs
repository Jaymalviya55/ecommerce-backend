namespace ECommerce.Domain.Entities.UserManagement;

public class UserRole
{
    public int UserRoleId { get; set; }
    public int UserLevelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool ApplicableToAllSelectedLevelUser { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UserLevel? UserLevel { get; set; }
}
