namespace ApiTestFramework.Clients.Common;

/// <summary>
/// Audit timestamps shared across multiple service DTOs.
/// </summary>
public class AuditInfo
{
    public System.DateTimeOffset CreatedAt { get; set; }
    public System.DateTimeOffset? UpdatedAt { get; set; }
}
