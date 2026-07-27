using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace BoxDropAz.Web;

/// <summary>
/// Lambda entry point for API Gateway REST API proxy payloads.
/// Handler:
/// BoxDropAz.Web::BoxDropAz.Web.LambdaEntryPoint::FunctionHandlerAsync
/// </summary>
public sealed class LambdaEntryPoint : Amazon.Lambda.AspNetCoreServer.APIGatewayProxyFunction
{
    public LambdaEntryPoint()
    {
        // Lambda proxy responses are UTF-8 strings; binary types must be base64-encoded for API Gateway.
        // Register in the constructor so encoding is configured before the first request is marshalled.
        RegisterResponseContentEncodingForContentType("application/pdf",
            Amazon.Lambda.AspNetCoreServer.ResponseContentEncoding.Base64);
        RegisterResponseContentEncodingForContentType("application/octet-stream",
            Amazon.Lambda.AspNetCoreServer.ResponseContentEncoding.Base64);
        RegisterResponseContentEncodingForContentType("image/webp",
            Amazon.Lambda.AspNetCoreServer.ResponseContentEncoding.Base64);
        RegisterResponseContentEncodingForContentType("image/svg+xml",
            Amazon.Lambda.AspNetCoreServer.ResponseContentEncoding.Base64);
        RegisterResponseContentEncodingForContentType("image/png",
            Amazon.Lambda.AspNetCoreServer.ResponseContentEncoding.Base64);
        RegisterResponseContentEncodingForContentType("image/jpeg",
            Amazon.Lambda.AspNetCoreServer.ResponseContentEncoding.Base64);
        RegisterResponseContentEncodingForContentType("font/woff2",
            Amazon.Lambda.AspNetCoreServer.ResponseContentEncoding.Base64);
    }

    protected override void Init(IWebHostBuilder builder)
    {
        builder.UseStartup<Startup>();
    }

    protected override void Init(IHostBuilder builder)
    {
    }

    /// <summary>
    /// Ensures multiple Set-Cookie headers are sent via multiValueHeaders so API Gateway REST API
    /// forwards them correctly. Without this, multiple auth cookies get merged into one
    /// comma-separated value and external login round-trips fail.
    /// </summary>
    protected override void PostMarshallResponseFeature(IHttpResponseFeature httpResponse, APIGatewayProxyResponse lambdaResponse, ILambdaContext lambdaContext)
    {
        if (httpResponse.Headers.TryGetValue(HeaderNames.SetCookie, out var setCookieValues) && setCookieValues.Count > 0)
        {
            lambdaResponse.Headers?.Remove(HeaderNames.SetCookie);
            lambdaResponse.MultiValueHeaders ??= new Dictionary<string, IList<string>>();
            lambdaResponse.MultiValueHeaders[HeaderNames.SetCookie] = setCookieValues.ToArray();
        }
    }
}
