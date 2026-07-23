using System.ComponentModel.DataAnnotations;

namespace ECommerce.Api.Models;

public class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = string.Empty;
}
