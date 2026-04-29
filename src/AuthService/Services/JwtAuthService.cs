using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AuthService.Models;
using AuthService.Repositories;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Services;

public interface IAuthService
{
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);
    Task<AuthResponse?> LoginAsync(LoginRequest request);
}

public class JwtAuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;

    public JwtAuthService(IUserRepository userRepository, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _configuration = configuration;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        if (await _userRepository.GetByUsernameAsync(request.Username) != null)
            return null;

        if (await _userRepository.GetByEmailAsync(request.Email) != null)
            return null;

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = HashPassword(request.Password),
            Role = request.Role,
            IsActive = true
        };

        var created = await _userRepository.CreateAsync(user);
        return GenerateAuthResponse(created);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);
        if (user == null || !user.IsActive)
            return null;

        if (!VerifyPassword(request.Password, user.PasswordHash))
            return null;

        return GenerateAuthResponse(user);
    }

    private AuthResponse GenerateAuthResponse(User user)
    {
        var expirationMinutes = _configuration.GetValue<int>("Jwt:ExpirationMinutes", 60);
        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);
        var token = GenerateJwtToken(user, expiresAt);

        return new AuthResponse
        {
            Token = new TokenInfo
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresAt = expiresAt,
                ExpiresInSeconds = expirationMinutes * 60
            },
            User = new UserProfile
            {
                Username = user.Username,
                Email = user.Email,
                Role = user.Role
            }
        };
    }

    private string GenerateJwtToken(User user, DateTime expiresAt)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private static bool VerifyPassword(string password, string hash)
        => HashPassword(password) == hash;
}

