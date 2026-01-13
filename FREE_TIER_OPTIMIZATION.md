# Azure Free Tier Optimization Guide

## Free Tier Reality Check

Azure AI Document Intelligence Free (F0) tier has significant limitations:

- ✅ **Limited monthly quota** - Fixed number of analyses per month
- ✅ **Rate limiting** - Restricted requests per second (RPS)
- ❌ **No SLA** - No service level agreement
- ❌ **No batch processing** - Single requests only
- ⚠️ **One resource per subscription per region**

### Key Insight

**Goal: Reduce the number of API calls, not speed them up!**

Every API call counts against your monthly quota. The focus is on **avoiding unnecessary calls**, not on making calls faster.

## Built-in Optimizations

This API includes several optimizations specifically for Free tier usage:

### 1. Intelligent Caching (Automatic)

**How it works:**
- Receipt images are hashed (SHA256)
- Results cached in memory for 24 hours
- Sliding expiration: refreshed if accessed within 6 hours
- Cache up to 100 receipt results

**Benefits:**
```
First scan:  Azure API call → Result cached
Second scan: Return from cache → NO API call!
```

**Example savings:**
- User scans same receipt 5 times (fixing angle, lighting, etc.)
- Without cache: 5 API calls
- With cache: 1 API call (saves 4 calls = 80% reduction!)

**Monitor cache effectiveness:**
```bash
curl http://localhost:5205/api/receipts/cache/stats
```

Response:
```json
{
  "cacheHits": 45,
  "cacheMisses": 15,
  "totalRequests": 60,
  "hitRate": "75.00%",
  "apiCallsSaved": 45,
  "message": "Cache saved 45 API calls! (Azure Free tier quota preserved)"
}
```

### 2. Provider Fallback Strategy

**Auto mode priority:**
1. Azure (primary) - Most accurate but costs quota
2. Ollama (fallback) - Free/local but requires setup

**Recommendation for Free tier:**
- Start with `provider: "ollama"` for development
- Use `provider: "azure"` only for production/critical receipts
- Use `provider: "auto"` for best experience with fallback

### 3. Clear Error Messages

API returns detailed warnings when providers fail, helping you avoid retries:
```json
{
  "warnings": [
    "Result from cache (no API call made)",
    "Fallback used after failures: azure: rate limit exceeded"
  ]
}
```

## Best Practices for Free Tier

### DO ✅

1. **Use Ollama for development**
   ```bash
   ollama pull llama3.2-vision
   ollama serve
   ```
   - Test your integration without touching Azure quota
   - Free and unlimited local processing

2. **Leverage the cache**
   - Same receipt scanned multiple times = only 1 API call
   - Cache persists for 24 hours
   - Perfect for user corrections and retries

3. **Pre-validate images client-side**
   Before sending to API, check:
   - Image size < 10MB
   - Format is JPEG/PNG/WebP
   - Image is reasonably sharp (not blurry)
   - Receipt is visible and well-lit

4. **Batch user feedback**
   - Don't scan on every keystroke
   - Wait for user confirmation before scanning
   - Show preview before submitting

5. **Monitor your usage**
   ```bash
   # Check cache stats regularly
   curl http://localhost:5205/api/receipts/cache/stats

   # Azure Portal: Monitor API calls per day
   ```

6. **Use specific provider when needed**
   ```json
   {
     "imageBase64": "...",
     "language": "fi",
     "provider": "ollama"  // Force free local processing
   }
   ```

### DON'T ❌

1. **Don't scan every upload automatically**
   - Bad: Auto-scan on file select
   - Good: Scan on button click after user review

2. **Don't retry failed scans immediately**
   - Bad: Automatic retry loop
   - Good: Show error, let user fix issue, retry manually

3. **Don't clear cache unnecessarily**
   - Cache saves quota!
   - Only clear when needed (e.g., testing changes)

4. **Don't use Azure for testing/development**
   - Use Ollama locally instead
   - Save Azure quota for production

5. **Don't ignore rate limits**
   - If you hit rate limit, cache will help
   - Consider adding user-facing rate limiting

## Monitoring Your Quota Usage

### In This API

```bash
# Cache statistics
curl http://localhost:5205/api/receipts/cache/stats

# Response shows:
# - How many API calls were saved
# - Cache hit rate
# - Total requests handled
```

### In Azure Portal

1. Navigate to your Document Intelligence resource
2. Go to "Metrics"
3. Monitor:
   - Total calls
   - Successful calls
   - Rate limit errors

### Set up Alerts

Create Azure Monitor alerts:
- Alert when approaching 80% of monthly quota
- Alert on rate limit errors
- Alert on 429 (Too Many Requests) responses

## Advanced: Production Optimization

### Redis Cache (Multi-instance)

For production with multiple API instances:

```csharp
// Replace in-memory cache with Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "your-redis-connection-string";
});
```

Benefits:
- Shared cache across multiple API instances
- Survives API restarts
- Even higher cache hit rates

### Database Logging

Track all API calls:
```csharp
// Log each Azure API call to database
await _db.ApiCalls.AddAsync(new ApiCall
{
    Timestamp = DateTime.UtcNow,
    Provider = "azure",
    ImageHash = hash,
    CacheHit = false,
    Cost = 1 // Counts against quota
});
```

Benefits:
- Historical usage tracking
- Cost analysis
- Usage patterns

### Client-side Image Optimization

Before sending to API:
- Resize large images (max 4000px width)
- Compress to 85% quality JPEG
- Crop to receipt area only
- Adjust brightness/contrast

Smaller images = faster uploads, but same API quota cost!

## Cost Estimation

### Free Tier Quota (Example)

Assuming 1000 analyses/month free:

**Without optimization:**
- Average user scans receipt 3 times (fixing angle, etc.)
- 1000 scans / 3 = ~333 unique receipts/month

**With caching:**
- 1000 scans, 75% cache hit rate
- Only 250 API calls needed
- 750 calls saved!
- Can handle 4x more receipts (333 → ~1332 receipts/month)

## Summary

### 🎯 Key Optimizations for Free Tier

1. **Caching** - Built-in, automatic, saves 50-80% of API calls
2. **Ollama fallback** - Free local processing
3. **Smart retries** - Don't waste quota on automatic retries
4. **Client validation** - Reduce failed API calls
5. **Monitoring** - Track cache effectiveness

### 📊 Expected Results

With proper optimization:
- **Cache hit rate:** 50-80%
- **API call reduction:** 50-80%
- **Effective capacity:** 2-5x more receipts per month
- **Cost:** Stay within Free tier quota

### 🚀 Next Steps

1. Set up Ollama for development
2. Monitor cache statistics daily
3. Set up Azure quota alerts
4. Optimize client-side image handling
5. Consider Redis cache for production

---

**Remember:** Every cached result is an API call saved. Make caching your best friend! 🎉
