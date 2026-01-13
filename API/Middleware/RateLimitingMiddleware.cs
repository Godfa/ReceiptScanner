using System.Collections.Concurrent;
using System.Net;

namespace ReceiptScanner.API.Middleware;

/// <summary>
/// Simple in-memory rate limiting middleware
/// Limits requests per IP address to protect Azure Free tier quota
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly int _requestsPerMinute;
    private readonly int _requestsPerHour;

    // Track requests per IP: IP -> list of request timestamps
    private static readonly ConcurrentDictionary<string, List<DateTime>> _requestLog = new();

    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;

        // Read limits from configuration, with defaults for Free tier
        _requestsPerMinute = configuration.GetValue<int>("RateLimiting:RequestsPerMinute", 10);
        _requestsPerHour = configuration.GetValue<int>("RateLimiting:RequestsPerHour", 100);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply rate limiting to scan endpoints
        if (!context.Request.Path.StartsWithSegments("/api/receipts/scan"))
        {
            await _next(context);
            return;
        }

        var ipAddress = GetClientIpAddress(context);
        var now = DateTime.UtcNow;

        // Get or create request log for this IP
        var requestTimes = _requestLog.GetOrAdd(ipAddress, _ => new List<DateTime>());

        bool rateLimitExceeded = false;
        string? errorMessage = null;
        string? retryAfter = null;
        string? hint = null;

        lock (requestTimes)
        {
            // Clean old entries (older than 1 hour)
            requestTimes.RemoveAll(time => time < now.AddHours(-1));

            // Check per-minute limit
            var requestsInLastMinute = requestTimes.Count(time => time >= now.AddMinutes(-1));
            if (requestsInLastMinute >= _requestsPerMinute)
            {
                _logger.LogWarning(
                    "Rate limit exceeded (per minute) for IP: {IP}. Requests: {Count}/{Limit}",
                    ipAddress,
                    requestsInLastMinute,
                    _requestsPerMinute);

                rateLimitExceeded = true;
                errorMessage = $"Maximum {_requestsPerMinute} scan requests per minute allowed";
                retryAfter = "60";
                hint = "Use cache for repeated scans of same receipt";
            }
            else
            {
                // Check per-hour limit
                var requestsInLastHour = requestTimes.Count;
                if (requestsInLastHour >= _requestsPerHour)
                {
                    _logger.LogWarning(
                        "Rate limit exceeded (per hour) for IP: {IP}. Requests: {Count}/{Limit}",
                        ipAddress,
                        requestsInLastHour,
                        _requestsPerHour);

                    rateLimitExceeded = true;
                    errorMessage = $"Maximum {_requestsPerHour} scan requests per hour allowed";
                    retryAfter = "3600";
                    hint = "Consider using Ollama provider for unlimited local scanning";
                }
            }

            if (!rateLimitExceeded)
            {
                // Add current request to log
                requestTimes.Add(now);
            }
        }

        // Handle rate limit outside of lock
        if (rateLimitExceeded)
        {
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = retryAfter!;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                message = errorMessage,
                retryAfter = retryAfter == "60" ? "60 seconds" : "1 hour",
                hint
            });
            return;
        }

        // Continue processing
        await _next(context);
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Try to get real IP from X-Forwarded-For header (if behind proxy/load balancer)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0)
            {
                return ips[0].Trim();
            }
        }

        // Fallback to remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Clear rate limit tracking (for testing)
    /// </summary>
    public static void ClearLimits()
    {
        _requestLog.Clear();
    }
}
