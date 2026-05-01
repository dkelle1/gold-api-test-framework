namespace ApiTestFramework.Clients.ImportService;

public class ImportAcceptedResponse
{
    public Guid BatchId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;
}

public class ImportStatusResponse
{
    public BatchInfo Batch { get; set; } = new();
    public ProgressInfo Progress { get; set; } = new();
    public AuditInfo Audit { get; set; } = new();
}

public class BatchInfo
{
    public Guid BatchId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class ProgressInfo
{
    public int TotalRecords { get; set; }
    public int SucceededRecords { get; set; }
    public int FailedRecords { get; set; }
}

public class AuditInfo
{
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
