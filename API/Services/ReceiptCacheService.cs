using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using ReceiptScanner.API.Models;

namespace ReceiptScanner.API.Services;

/// <summary>
/// In-memory cache for receipt scan results to reduce expensive API calls to Azure
/// </summary>
public interface IReceiptCacheService
{
    ScannedReceiptResult? GetCachedResult(byte[] imageData, string language, string provider);
    void CacheResult(byte[] imageData, string language, string provider, ScannedReceiptResult result);
    void ClearCache();
    CacheStatistics GetStatistics();
}

public class ReceiptCacheService : IReceiptCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<ReceiptCacheService> _logger;
    private long _hits = 0;
    private long _misses = 0;

    public ReceiptCacheService(IMemoryCache cache, ILogger<ReceiptCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public ScannedReceiptResult? GetCachedResult(byte[] imageData, string language, string provider)
    {
        var cacheKey = GenerateCacheKey(imageData, language, provider);

        if (_cache.TryGetValue<ScannedReceiptResult>(cacheKey, out var cachedResult))
        {
            Interlocked.Increment(ref _hits);
            _logger.LogInformation("Cache HIT for key: {CacheKey}", cacheKey[..16] + "...");
            return cachedResult;
        }

        Interlocked.Increment(ref _misses);
        _logger.LogDebug("Cache MISS for key: {CacheKey}", cacheKey[..16] + "...");
        return null;
    }

    public void CacheResult(byte[] imageData, string language, string provider, ScannedReceiptResult result)
    {
        var cacheKey = GenerateCacheKey(imageData, language, provider);

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromHours(24)) // Cache for 24 hours
            .SetSlidingExpiration(TimeSpan.FromHours(6))   // Refresh if accessed within 6 hours
            .SetSize(1); // Each entry counts as 1 unit

        _cache.Set(cacheKey, result, cacheOptions);
        _logger.LogInformation("Cached result for key: {CacheKey}", cacheKey[..16] + "...");
    }

    public void ClearCache()
    {
        if (_cache is MemoryCache memCache)
        {
            memCache.Compact(1.0); // Remove all entries
        }
        _logger.LogInformation("Cache cleared");
    }

    public CacheStatistics GetStatistics()
    {
        var hits = Interlocked.Read(ref _hits);
        var misses = Interlocked.Read(ref _misses);
        var total = hits + misses;
        var hitRate = total > 0 ? (double)hits / total * 100 : 0;

        return new CacheStatistics
        {
            Hits = hits,
            Misses = misses,
            Total = total,
            HitRate = hitRate
        };
    }

    private static string GenerateCacheKey(byte[] imageData, string language, string provider)
    {
        // Generate SHA256 hash of image data + language + provider
        using var sha256 = SHA256.Create();
        var combinedData = new List<byte>();
        combinedData.AddRange(imageData);
        combinedData.AddRange(Encoding.UTF8.GetBytes(language));
        combinedData.AddRange(Encoding.UTF8.GetBytes(provider));

        var hashBytes = sha256.ComputeHash(combinedData.ToArray());
        return Convert.ToHexString(hashBytes);
    }
}

public record CacheStatistics
{
    public long Hits { get; init; }
    public long Misses { get; init; }
    public long Total { get; init; }
    public double HitRate { get; init; }
}
