using System.ComponentModel.DataAnnotations;

namespace Azure.Sdk.Tools.Cli.Options
{
  /// <summary>
  /// Configuration options for the AI completion service.
  /// </summary>
  public class AiCompletionOptions
  {
    /// <summary>
    /// Configuration section name for binding.
    /// </summary>
    public const string SectionName = "AiCompletion";

    /// <summary>
    /// The base URL of the AI completion endpoint.
    /// </summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD tenant ID for authentication.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Azure AD client ID for authentication.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Scopes to request during authentication.
    /// </summary>
    public string[] Scopes { get; set; } = Array.Empty<string>();
  }
}
