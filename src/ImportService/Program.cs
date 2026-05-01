using System.Text;
using ImportService.Data;
using ImportService.Models;
using ImportService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Import Service API",
        Version = "v1",
        Description = "Microservice for asynchronous product CSV imports"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SecretKey"]!))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddDbContext<ImportDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "ImportService:";
});

builder.Services.AddSingleton<IProductImportQueue, ProductImportQueue>();
builder.Services.AddHostedService<ProductImportWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Import Service API v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/imports/products/csv", async (IFormFile file, ImportDbContext db, IProductImportQueue queue, CancellationToken ct) =>
{
    if (file.Length == 0)
        return Results.BadRequest(new { Message = "CSV file is required." });

    if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { Message = "Only .csv files are supported." });

    await using var stream = new MemoryStream();
    await file.CopyToAsync(stream, ct);

    var batch = new ProductImportBatch
    {
        FileName = file.FileName,
        Status = ImportBatchStatus.Queued,
        CreatedAt = DateTime.UtcNow
    };

    db.ProductImportBatches.Add(batch);
    await db.SaveChangesAsync(ct);

    await queue.EnqueueAsync(new ProductImportJob(batch.Id, stream.ToArray()), ct);

    return Results.Accepted($"/api/imports/{batch.Id}/status", ImportAcceptedResponse.FromEntity(batch));
})
.WithName("StartProductCsvImport")
.WithTags("Imports")
.Produces<ImportAcceptedResponse>(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest)
.DisableAntiforgery()
.RequireAuthorization();

app.MapGet("/api/imports/{batchId:guid}/status", async (Guid batchId, ImportDbContext db, CancellationToken ct) =>
{
    var batch = await db.ProductImportBatches.FirstOrDefaultAsync(x => x.Id == batchId, ct);
    return batch is null
        ? Results.NotFound(new { Message = $"Import batch {batchId} not found" })
        : Results.Ok(ImportStatusResponse.FromEntity(batch));
})
.WithName("GetImportStatus")
.WithTags("Imports")
.Produces<ImportStatusResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

app.Run();

public partial class Program { }
