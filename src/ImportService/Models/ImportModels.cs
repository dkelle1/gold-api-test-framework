using System.Globalization;

namespace ImportService.Models;

public enum ImportBatchStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}

public class ProductImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Queued;
    public int TotalRecords { get; set; }
    public int SucceededRecords { get; set; }
    public int FailedRecords { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<ImportedProduct> ImportedProducts { get; set; } = new List<ImportedProduct>();
}

public class ImportedProduct
{
    public int Id { get; set; }
    public Guid BatchId { get; set; }
    public ProductImportBatch Batch { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

public class ImportCsvProduct
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public string Category { get; set; } = string.Empty;

    public static bool TryParse(string csvLine, out ImportCsvProduct? product)
    {
        product = null;
        var parts = csvLine.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 5)
            return false;

        if (!decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            return false;

        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stockQuantity))
            return false;

        if (string.IsNullOrWhiteSpace(parts[0]))
            return false;

        product = new ImportCsvProduct
        {
            Name = parts[0],
            Description = parts[1],
            Price = price,
            StockQuantity = stockQuantity,
            Category = parts[4]
        };

        return true;
    }
}

public class ImportAcceptedResponse
{
    public Guid BatchId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;

    public static ImportAcceptedResponse FromEntity(ProductImportBatch batch) => new()
    {
        BatchId = batch.Id,
        Status = batch.Status.ToString(),
        StatusUrl = $"/api/imports/{batch.Id}/status"
    };
}

public class ImportStatusResponse
{
    public BatchInfo Batch { get; set; } = new();
    public ProgressInfo Progress { get; set; } = new();
    public AuditInfo Audit { get; set; } = new();

    public static ImportStatusResponse FromEntity(ProductImportBatch batch) => new()
    {
        Batch = new BatchInfo
        {
            BatchId = batch.Id,
            FileName = batch.FileName,
            Status = batch.Status.ToString(),
            ErrorMessage = batch.ErrorMessage
        },
        Progress = new ProgressInfo
        {
            TotalRecords = batch.TotalRecords,
            SucceededRecords = batch.SucceededRecords,
            FailedRecords = batch.FailedRecords
        },
        Audit = new AuditInfo
        {
            CreatedAt = batch.CreatedAt,
            StartedAt = batch.StartedAt,
            CompletedAt = batch.CompletedAt
        }
    };
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
