using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OrderService.Clients;
using OrderService.Data;
using OrderService.Models;
using OrderService.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Order Service API",
        Version = "v1",
        Description = "Microservice for managing orders. Depends on Product Service for product validation."
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
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "OrderService:";
});

builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();

var productServiceUrl = builder.Configuration.GetValue<string>("ProductServiceUrl") ?? "http://localhost:5100";
builder.Services.AddHttpClient<IProductServiceClient, ProductServiceClient>(client =>
{
    client.BaseAddress = new Uri(productServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

// Ensure DB schema exists on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order Service API v1");
    c.RoutePrefix = string.Empty;
});

// Correlate service logs with the API test that sent the request
app.Use(async (context, next) =>
{
    var testId = context.Request.Headers["X-Test-Id"].FirstOrDefault();
    if (!string.IsNullOrEmpty(testId))
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("[X-Test-Id: {TestId}] {Method} {Path}", testId, context.Request.Method, context.Request.Path);
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// GET all orders
app.MapGet("/api/orders", async (IOrderRepository repo) =>
{
    var orders = await repo.GetAllAsync();
    return Results.Ok(orders.Select(OrderResponse.FromEntity));
})
.WithName("GetAllOrders")
.WithTags("Orders")
.Produces<IEnumerable<OrderResponse>>(StatusCodes.Status200OK)
.RequireAuthorization();

// GET order by id
app.MapGet("/api/orders/{id:int}", async (int id, IOrderRepository repo) =>
{
    var order = await repo.GetByIdAsync(id);
    return order is not null
        ? Results.Ok(OrderResponse.FromEntity(order))
        : Results.NotFound(new { Message = $"Order with Id {id} not found" });
})
.WithName("GetOrderById")
.WithTags("Orders")
.Produces<OrderResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

// GET orders by customer email
app.MapGet("/api/orders/customer/{email}", async (string email, IOrderRepository repo) =>
{
    var orders = await repo.GetByCustomerEmailAsync(email);
    return Results.Ok(orders.Select(OrderResponse.FromEntity));
})
.WithName("GetOrdersByCustomerEmail")
.WithTags("Orders")
.Produces<IEnumerable<OrderResponse>>(StatusCodes.Status200OK)
.RequireAuthorization();

// POST create order (nested request: Customer + Address, Items[], Shipping; validates products via ProductService)
app.MapPost("/api/orders", async (CreateOrderRequest request, IOrderRepository repo,
    IProductServiceClient productClient, HttpContext httpContext) =>
{
    if (request.Customer is null)
        return Results.BadRequest(new { Message = "Customer is required" });

    if (string.IsNullOrWhiteSpace(request.Customer.Name))
        return Results.BadRequest(new { Message = "Customer name is required" });

    if (string.IsNullOrWhiteSpace(request.Customer.Email))
        return Results.BadRequest(new { Message = "Customer email is required" });

    if (request.Items is null || request.Items.Count == 0)
        return Results.BadRequest(new { Message = "Order must contain at least one item" });

    if (request.Items.Any(i => i.Quantity <= 0))
        return Results.BadRequest(new { Message = "Item quantity must be greater than zero" });

    // Forward Bearer token to ProductService
    var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        productClient.SetBearerToken(authHeader.Substring("Bearer ".Length));

    // Validate every product once; stock is checked against the summed quantity per product
    var products = new Dictionary<int, ProductDto>();
    foreach (var group in request.Items.GroupBy(i => i.ProductId))
    {
        var product = await productClient.GetProductAsync(group.Key);
        if (product == null)
            return Results.BadRequest(new { Message = $"Product with Id {group.Key} not found in Product Service" });

        if (!product.IsActive)
            return Results.BadRequest(new { Message = $"Product '{product.Name}' is not active" });

        var requested = group.Sum(i => i.Quantity);
        if (product.Inventory.StockQuantity < requested)
            return Results.BadRequest(new { Message = $"Insufficient stock for '{product.Name}'. Available: {product.Inventory.StockQuantity}, Requested: {requested}" });

        products[group.Key] = product;
    }

    var items = request.Items.Select(i =>
    {
        var product = products[i.ProductId];
        return new OrderItem
        {
            ProductId = i.ProductId,
            ProductName = product.Name,
            UnitPrice = product.Price.Amount,
            Quantity = i.Quantity,
            LineTotal = product.Price.Amount * i.Quantity
        };
    }).ToList();

    // Shipping address falls back to the customer address when omitted
    var shippingAddress = request.Shipping?.Address ?? request.Customer.Address;

    var order = new Order
    {
        CustomerName = request.Customer.Name,
        CustomerEmail = request.Customer.Email,
        CustomerStreet = request.Customer.Address?.Street,
        CustomerCity = request.Customer.Address?.City,
        CustomerPostalCode = request.Customer.Address?.PostalCode,
        CustomerCountry = request.Customer.Address?.Country,
        ShippingMethod = request.Shipping?.Method ?? ShippingMethod.Standard,
        ShippingStreet = shippingAddress?.Street,
        ShippingCity = shippingAddress?.City,
        ShippingPostalCode = shippingAddress?.PostalCode,
        ShippingCountry = shippingAddress?.Country,
        ShippingNotes = request.Shipping?.Notes,
        Items = items,
        TotalPrice = items.Sum(i => i.LineTotal),
        Status = OrderStatus.Pending
    };

    var created = await repo.CreateAsync(order);
    return Results.Created($"/api/orders/{created.Id}", OrderResponse.FromEntity(created));
})
.WithName("CreateOrder")
.WithTags("Orders")
.Produces<OrderResponse>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.RequireAuthorization();

// PUT update order
app.MapPut("/api/orders/{id:int}", async (int id, UpdateOrderRequest request, IOrderRepository repo) =>
{
    var updated = await repo.UpdateAsync(id, request);
    return updated is not null
        ? Results.Ok(OrderResponse.FromEntity(updated))
        : Results.NotFound(new { Message = $"Order with Id {id} not found" });
})
.WithName("UpdateOrder")
.WithTags("Orders")
.Produces<OrderResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

// DELETE order
app.MapDelete("/api/orders/{id:int}", async (int id, IOrderRepository repo) =>
{
    var deleted = await repo.DeleteAsync(id);
    return deleted
        ? Results.NoContent()
        : Results.NotFound(new { Message = $"Order with Id {id} not found" });
})
.WithName("DeleteOrder")
.WithTags("Orders")
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status404NotFound)
.RequireAuthorization();

app.Run();

public partial class Program { }
