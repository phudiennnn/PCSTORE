using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PCStore.Core.Ai;
using PCStore.Core.Entities;
using PCStore.Core.Enums;
using PCStore.Infrastructure.Data;

namespace PCStore.API.Services.Faq;

/// <summary>Chỉ cho Admin đã đăng nhập, kèm token chống CSRF trên yêu cầu ghi.</summary>
public sealed class AdminRequestGuard
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IOptionsMonitor<FaqOptions> _options;
    private readonly IAiClock _clock;
    private readonly ConcurrentDictionary<int, TokenEntry> _tokens = new();

    public AdminRequestGuard(IDataProtectionProvider dataProtectionProvider, IOptionsMonitor<FaqOptions> options, IAiClock clock)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _options = options;
        _clock = clock;
    }

    public IResult? RejectDisallowedOrigin(HttpContext httpContext)
    {
        var origin = httpContext.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin) || IsAllowedOrigin(origin, _options.CurrentValue))
            return null;
        return Results.Json(new { message = "Nguồn yêu cầu không được phép." }, statusCode: StatusCodes.Status403Forbidden);
    }

    public async Task<User?> FindAdministratorAsync(HttpContext httpContext, AppDbContext context, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        string protectedPayload;
        try
        {
            protectedPayload = _dataProtectionProvider.CreateProtector(options.AccessTokenPurpose).Unprotect(authorization["Bearer ".Length..].Trim());
        }
        catch (CryptographicException)
        {
            return null;
        }

        var tokenParts = protectedPayload.Split('|', 2);
        if (tokenParts.Length != 2 || !int.TryParse(tokenParts[0], out var userId) || !long.TryParse(tokenParts[1], out var expiresAt) || expiresAt <= _clock.UtcNow.ToUnixTimeSeconds())
            return null;

        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId && item.IsActive, cancellationToken);
        return user?.Role == UserRole.Admin ? user : null;
    }

    public string IssueCsrfToken(int userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var lifetime = TimeSpan.FromMinutes(Math.Max(5, _options.CurrentValue.CsrfLifetimeMinutes));
        _tokens[userId] = new TokenEntry(token, _clock.UtcNow.Add(lifetime));
        return token;
    }

    public bool HasValidCsrf(HttpContext httpContext, int userId)
    {
        var headerName = _options.CurrentValue.CsrfHeaderName;
        if (!httpContext.Request.Headers.TryGetValue(headerName, out var presented))
            return false;
        if (!_tokens.TryGetValue(userId, out var entry) || entry.ExpiresAt <= _clock.UtcNow)
            return false;
        var expected = Encoding.UTF8.GetBytes(entry.Token);
        var actual = Encoding.UTF8.GetBytes(presented.ToString());
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public string CsrfHeaderName => _options.CurrentValue.CsrfHeaderName;

    private static bool IsAllowedOrigin(string origin, FaqOptions options)
    {
        var trimmed = origin.Trim().TrimEnd('/');
        return options.AllowedOrigins.Any(allowed => string.Equals(allowed.Trim().TrimEnd('/'), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record TokenEntry(string Token, DateTimeOffset ExpiresAt);
}
