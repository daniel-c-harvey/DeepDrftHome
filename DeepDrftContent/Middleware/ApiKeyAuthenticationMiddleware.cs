using System.Reflection;

namespace DeepDrftContent.Middleware
{
    public class ApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _apiKey;

        public ApiKeyAuthenticationMiddleware(RequestDelegate next, string apiKey)
        {
            _next = next;
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            if (endpoint == null)
            {
                await _next(context);
                return;
            }

            var hasApiKeyAuthorize = endpoint.Metadata.GetMetadata<ApiKeyAuthorizeAttribute>() != null;
            if (!hasApiKeyAuthorize)
            {
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue("ApiKey", out var extractedApiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("API Key was not provided");
                return;
            }

            if (!string.Equals(extractedApiKey, _apiKey, StringComparison.Ordinal))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized client");
                return;
            }

            await _next(context);
        }
    }

    public static class ApiKeyAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder builder, string apiKey)
        {
            return builder.UseMiddleware<ApiKeyAuthenticationMiddleware>(apiKey);
        }
    }
}