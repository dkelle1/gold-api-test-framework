using System.Threading.Channels;
using ImportService.Data;
using ImportService.Models;
using Microsoft.EntityFrameworkCore;

namespace ImportService.Services;

public sealed record ProductImportJob(Guid BatchId, byte[] CsvContent);

public interface IProductImportQueue
{
    ValueTask EnqueueAsync(ProductImportJob job, CancellationToken cancellationToken = default);
    ValueTask<ProductImportJob> DequeueAsync(CancellationToken cancellationToken);
}

public class ProductImportQueue : IProductImportQueue
{
    private readonly Channel<ProductImportJob> _channel = Channel.CreateUnbounded<ProductImportJob>();

    public ValueTask EnqueueAsync(ProductImportJob job, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(job, cancellationToken);

    public ValueTask<ProductImportJob> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}

public class ProductImportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProductImportQueue _queue;
    private readonly ILogger<ProductImportWorker> _logger;

    public ProductImportWorker(
        IServiceScopeFactory scopeFactory,
        IProductImportQueue queue,
        ILogger<ProductImportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);

            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while processing import batch {BatchId}", job.BatchId);
            }
        }
    }

    private async Task ProcessJobAsync(ProductImportJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImportDbContext>();

        var batch = await db.ProductImportBatches
            .FirstOrDefaultAsync(x => x.Id == job.BatchId, cancellationToken);

        if (batch is null)
        {
            _logger.LogWarning("Import batch {BatchId} was not found", job.BatchId);
            return;
        }

        batch.Status = ImportBatchStatus.Processing;
        batch.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            using var stream = new MemoryStream(job.CsvContent);
            using var reader = new StreamReader(stream);

            var header = await reader.ReadLineAsync(cancellationToken);
            if (header is null)
            {
                batch.Status = ImportBatchStatus.Failed;
                batch.ErrorMessage = "CSV file is empty.";
                batch.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var expectedHeader = "Name,Description,Price,StockQuantity,Category";
            if (!string.Equals(header, expectedHeader, StringComparison.OrdinalIgnoreCase))
            {
                batch.Status = ImportBatchStatus.Failed;
                batch.ErrorMessage = $"Invalid CSV header. Expected: '{expectedHeader}'.";
                batch.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var importedProducts = new List<ImportedProduct>();
            var total = 0;
            var succeeded = 0;
            var failed = 0;

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                total++;

                if (!ImportCsvProduct.TryParse(line, out var parsed) || parsed is null)
                {
                    failed++;
                    continue;
                }

                importedProducts.Add(new ImportedProduct
                {
                    BatchId = batch.Id,
                    Name = parsed.Name,
                    Description = parsed.Description,
                    Price = parsed.Price,
                    StockQuantity = parsed.StockQuantity,
                    Category = parsed.Category,
                    ImportedAt = DateTime.UtcNow
                });

                succeeded++;
            }

            batch.TotalRecords = total;
            batch.SucceededRecords = succeeded;
            batch.FailedRecords = failed;
            batch.Status = ImportBatchStatus.Completed;
            batch.CompletedAt = DateTime.UtcNow;

            if (importedProducts.Count > 0)
            {
                await db.ImportedProducts.AddRangeAsync(importedProducts, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            batch.Status = ImportBatchStatus.Failed;
            batch.ErrorMessage = ex.Message;
            batch.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Failed to process import batch {BatchId}", batch.Id);
        }
    }
}
