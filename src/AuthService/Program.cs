using AuthService.Data;
using AuthService.Models;
using AuthService.Repositories;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Auth Service API",
        Version = "v1",
        Description = "Microservice for authentication and JWT token generation"
    });
});

// EF Core — SQL Server
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Redis distributed cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "AuthService:";
});

builder.Services.AddScoped<IUserRepository, SqlUserRepository>();
builder.Services.AddScoped<IAuthService, JwtAuthService>();

var app = builder.Build();

// Ensure DB schema exists on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth Service API v1");
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

// POST register
app.MapPost("/api/auth/register", async (RegisterRequest request, IAuthService authService) =>
{
    if (string.IsNullOrWhiteSpace(request.Username))
        return Results.BadRequest(new { Message = "Username is required" });

    if (string.IsNullOrWhiteSpace(request.Email))
        return Results.BadRequest(new { Message = "Email is required" });

    if (string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { Message = "Password is required" });

    if (request.Password.Length < 6)
        return Results.BadRequest(new { Message = "Password must be at least 6 characters" });

    var result = await authService.RegisterAsync(request);
    if (result == null)
        return Results.Conflict(new { Message = "User with this username or email already exists" });

    return Results.Created($"/api/auth/users/{result.User.Username}", result);
})
.WithName("Register")
.WithTags("Auth")
.Produces<AuthResponse>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status409Conflict);

// POST login
app.MapPost("/api/auth/login", async (LoginRequest request, IAuthService authService) =>
{
    if (string.IsNullOrWhiteSpace(request.Username))
        return Results.BadRequest(new { Message = "Username is required" });

    if (string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { Message = "Password is required" });

    var result = await authService.LoginAsync(request);
    if (result == null)
        return Results.Unauthorized();

    return Results.Ok(result);
})
.WithName("Login")
.WithTags("Auth")
.Produces<AuthResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized);

app.Run();

public partial class Program { }
