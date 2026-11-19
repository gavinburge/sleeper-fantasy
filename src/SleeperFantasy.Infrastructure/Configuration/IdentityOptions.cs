using System.ComponentModel.DataAnnotations;

namespace SleeperFantasy.Infrastructure.Configuration;

public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    [Required, EmailAddress]
    public string AdministratorEmail { get; set; } = string.Empty;

    [Required, MinLength(12)]
    public string AdministratorPassword { get; set; } = string.Empty;

    public bool RequireHttps { get; set; } = true;
}
