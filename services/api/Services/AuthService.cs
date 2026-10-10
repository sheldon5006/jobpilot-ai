using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace JobPilot.Api.Services;

/// <summary>
/// Single-user access control: a Google sign-in for one of the allowed email addresses is exchanged
/// for a JobPilot session token. Without configuration, auth is only allowed off on loopback addresses.
/// </summary>
public sealed class AuthSettings
{
    public const string Issuer = "JobPilot";
    public const string Audience = "JobPilot";

    public bool Enabled { get; private init; }
    public string GoogleClientId { get; private init; } = string.Empty;
    public IReadOnlySet<string> AllowedEmails { get; private init; } = new HashSet<string>();
    public SymmetricSecurityKey SigningKey { get; private init; } = null!;
    public TimeSpan SessionLifetime { get; private init; } = TimeSpan.FromDays(30);

    public static AuthSettings Resolve(IConfiguration configuration, string? listenUrls)
    {
        var clientId = configuration["Auth:GoogleClientId"]?.Trim() ?? string.Empty;
        var allowedEmails = (configuration["Auth:AllowedEmails"] ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(email => email.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var signingKey = configuration["Auth:SigningKey"] ?? string.Empty;

        var anyConfigured = clientId.Length > 0 || allowedEmails.Count > 0 || signingKey.Length > 0;
        if (!anyConfigured)
        {
            if (!IsLoopbackOnly(listenUrls))
            {
                throw new InvalidOperationException(
                    "Sign-in is not configured, but the API is listening on a non-local address. " +
                    "Set Auth:GoogleClientId, Auth:AllowedEmails and Auth:SigningKey before exposing the API.");
            }

            // Local development on 127.0.0.1/localhost only: no sign-in, as before.
            return new AuthSettings
            {
                Enabled = false,
                SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")))
            };
        }

        if (clientId.Length == 0 || allowedEmails.Count == 0)
        {
            throw new InvalidOperationException("Auth:GoogleClientId and Auth:AllowedEmails must both be set to enable sign-in.");
        }

        if (signingKey.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be a random secret of at least 32 characters.");
        }

        return new AuthSettings
        {
            Enabled = true,
            GoogleClientId = clientId,
            AllowedEmails = allowedEmails,
            SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
        };
    }

    public static bool IsLoopbackOnly(string? listenUrls)
    {
        // Kestrel's default when no URL is configured is http://localhost:5000.
        if (string.IsNullOrWhiteSpace(listenUrls))
        {
            return true;
        }

        return listenUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(url =>
            {
                var host = url.Contains("://", StringComparison.Ordinal) ? url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..] : url;
                host = host.Split('/')[0];
                host = host.StartsWith('[') ? host[..(host.IndexOf(']') + 1)] : host.Split(':')[0];
                return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                       host == "127.0.0.1" ||
                       host == "[::1]";
            });
    }
}

public sealed record GoogleSignInRequest(string? IdToken);

public sealed record SessionResponse(string Token, string Email, DateTime ExpiresAtUtc);

public sealed class AuthService(AuthSettings settings)
{
    private readonly JsonWebTokenHandler _tokenHandler = new();

    /// <summary>Validates a Google ID token and returns the verified, allowed email, or null.</summary>
    public async Task<string?> ValidateGoogleIdTokenAsync(string idToken)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [settings.GoogleClientId]
            });
        }
        catch (InvalidJwtException)
        {
            return null;
        }

        var email = payload.Email?.Trim().ToLowerInvariant();
        return payload.EmailVerified && email is not null && settings.AllowedEmails.Contains(email)
            ? email
            : null;
    }

    public SessionResponse CreateSession(string email)
    {
        var expires = DateTime.UtcNow.Add(settings.SessionLifetime);
        var token = _tokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthSettings.Issuer,
            Audience = AuthSettings.Audience,
            Subject = new ClaimsIdentity([new Claim("email", email), new Claim("sub", email)]),
            Expires = expires,
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256)
        });
        return new SessionResponse(token, email, expires);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = AuthSettings.Issuer,
        ValidAudience = AuthSettings.Audience,
        IssuerSigningKey = settings.SigningKey,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(2)
    };
}
