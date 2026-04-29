using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ProductService.Data;
using ProductService.Models;
using ProductService.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Product Service API",
        Version = "v1",
        Description = "Microservice for managing products"
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

// JWT Authentication
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

// EF Core — SQL Server
builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "ProductService:";
});

builder.Services.AddScoped<IProductRepository, SqlProductRepository>();

var app = builder.Build();

// Ensure DB schema exists on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Product Service API v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthentication();
app.UseAuthorization();

// GET all products
app.MapGet("/api/products", async (IProductRepository repo) =>
{
    var products = await repo.GetAllAsync();
    return Results.Ok(products.Select(ProductResponse.FromEntity));
})
.WithName("GetAllProducts")
.WithTags("Products")
.Produces<IEnumerable<ProductResponse>>(StatusCodes.Status200OK)
.RequireAuthorization();

// GET product by id
app.MapGet("/api/products/{id:int}", async (int id, IProductRepository repo) =>
{
    var product = await repo.GetByIdAsync(id);
    return product is not null
        ? Results.Ok(ProductResponse.FromEntity(product))
        : Results.NotFound(new { Message = $"Product with Id {id} not found" });
})
.WithName("GetProductById")
.WithTags("Products")
.Produces<ProductResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

// GET products by category
app.MapGet("/api/products/category/{category}", async (string category, IProductRepository repo) =>
{
    var products = await repo.GetByCategoryAsync(category);
    return Results.Ok(products.Select(ProductResponse.FromEntity));
})
.WithName("GetProductsByCategory")
.WithTags("Products")
.Produces<IEnumerable<ProductResponse>>(StatusCodes.Status200OK)
.RequireAuthorization();

// POST create product
app.MapPost("/api/products", async (CreateProductRequest request, IProductRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { Message = "Product name is required" });

    if (request.Price <= 0)
        return Results.BadRequest(new { Message = "Price must be greater than zero" });

    var product = new Product
    {
        Name = request.Name,
        Description = request.Description,
        Price = request.Price,
        StockQuantity = request.StockQuantity,
        Category = request.Category
    };

    var created = await repo.CreateAsync(product);
    return Results.Created($"/api/products/{created.Id}", ProductResponse.FromEntity(created));
})
.WithName("CreateProduct")
.WithTags("Products")
.Produces<ProductResponse>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.RequireAuthorization();

// PUT update product
app.MapPut("/api/products/{id:int}", async (int id, UpdateProductRequest request, IProductRepository repo) =>
{
    var updated = await repo.UpdateAsync(id, request);
    return updated is not null
        ? Results.Ok(ProductResponse.FromEntity(updated))
        : Results.NotFound(new { Message = $"Product with Id {id} not found" });
})
.WithName("UpdateProduct")
.WithTags("Products")
.Produces<ProductResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

// DELETE product
app.MapDelete("/api/products/{id:int}", async (int id, IProductRepository repo) =>
{
    var deleted = await repo.DeleteAsync(id);
    return deleted
        ? Results.NoContent()
        : Results.NotFound(new { Message = $"Product with Id {id} not found" });
})
.WithName("DeleteProduct")
.WithTags("Products")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

app.Run();

public partial class Program { }
