using Microsoft.Net.Http.Headers;
using Nop.Core.Infrastructure;
using Nop.Services.Logging;

namespace Nop.Plugin.Shipping.Fedex.API;

public static class HttpClientExtensions
{
    public static void PrepareRequest(this HttpClient httpClient,
        HttpRequestMessage request, FedexSettings fedexSettings, string accessToken = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fedexSettings);

        httpClient.Timeout = TimeSpan.FromSeconds(fedexSettings.RequestTimeout ?? FedexShippingDefaults.RequestTimeout);
        httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, FedexShippingDefaults.UserAgent);

        if (!string.IsNullOrEmpty(accessToken) && !request.Headers.Contains(HeaderNames.Authorization))
            request.Headers.Add(HeaderNames.Authorization, $"Bearer {accessToken}");

        //save debug info
        if (!fedexSettings.Tracing)
            return;

        var logger = EngineContext.Current.Resolve<ILogger>();
        logger.Information($"FedEx rates. Request: {request}{Environment.NewLine}Content: {request.Content?.ReadAsStringAsync().Result}");
    }

    public static void ProcessResponse(this HttpClient httpClient, HttpResponseMessage response, FedexSettings fedexSettings)
    {
        //save debug info
        if (!fedexSettings.Tracing)
            return;

        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(fedexSettings);

        var logger = EngineContext.Current.Resolve<ILogger>();
        logger.Information($"FedEx rates. Response: {response}{Environment.NewLine}Content: {response.Content.ReadAsStringAsync().Result}");
    }
}