using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

namespace mood_recommendation.Services;

// A browser may redeem a login handoff once, within one minute.
public sealed class AdminLoginHandoff : IDisposable
{
    public const string CookieName = "moodify_admin_handoff";
    public const string CookiePath = "/api/Auth/admin-session";
    private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly object _gate = new();

    public string Issue(int userId)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _entries.Set(code, userId, new MemoryCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1), Size = 1
        });
        return code;
    }

    public int? Consume(string code)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue<int>(code, out var userId)) return null;
            _entries.Remove(code);
            return userId;
        }
    }

    public void Dispose() => _entries.Dispose();
}
