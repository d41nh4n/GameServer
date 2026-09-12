using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GamePanel.Api.Middleware;

public sealed class GlobalErrorMiddleware
{
    private static readonly Regex SecretPattern = new("(?i)([\"']?(?:password|passwd|token|secret|jwt|rcon)[\"']?\\s*[:=]\\s*[\"']?)([^\"',;\\s}]+)", RegexOptions.Compiled);
    private static readonly Regex BearerPattern = new("(?i)(Bearer\\s+)[^\\s,;]+", RegexOptions.Compiled);
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalErrorMiddleware> _logger;

    public GlobalErrorMiddleware(RequestDelegate next, ILogger<GlobalErrorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            var safeMessage = Mask(exception.Message);
            _logger.LogError("Unhandled request exception {TraceId} {HttpMethod} {RequestPath} {StatusCode} {ErrorType} {ErrorMessage} {DurationMs}", traceId, context.Request.Method, context.Request.Path.Value ?? "", StatusCodes.Status500InternalServerError, exception.GetType().Name, safeMessage, stopwatch.ElapsedMilliseconds);
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new { type = "about:blank", title = "Internal server error", status = 500, traceId });
            }
        }
        finally
        {
            if (context.Response.StatusCode >= 400)
            {
                _logger.LogWarning("Request completed with error status {TraceId} {HttpMethod} {RequestPath} {StatusCode} {DurationMs}", Activity.Current?.Id ?? context.TraceIdentifier, context.Request.Method, context.Request.Path.Value ?? "", context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private static string Mask(string value)
    {
        var masked = SecretPattern.Replace(value, "$1[REDACTED]");
        masked = BearerPattern.Replace(masked, "$1[REDACTED]");
        return masked.Length <= 500 ? masked : masked[..500];
    }
}
