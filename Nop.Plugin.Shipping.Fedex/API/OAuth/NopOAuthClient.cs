namespace Nop.Plugin.Shipping.Fedex.API.OAuth;

public partial class OAuthClient
{
    private readonly FedexSettings _fedexSettings;

    public OAuthClient(HttpClient httpClient, FedexSettings fedexSettings) : this(httpClient)
    {
        _fedexSettings = fedexSettings;

        if (!_fedexSettings.UseSandbox)
            BaseUrl = FedexShippingDefaults.ApiUrl;
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        client.PrepareRequest(request, _fedexSettings);
    }

    public virtual Task<Response> GenerateTokenAsync()
    {
        return API_AuthorizationAsync("application/x-www-form-urlencoded",  new FullSchema
        {
            Grant_type = "client_credentials",
            Client_id = _fedexSettings.ClientId,
            Client_secret = _fedexSettings.ClientSecret
        });
    }

    partial void ProcessResponse(HttpClient client, HttpResponseMessage response)
    {
        client.ProcessResponse(response, _fedexSettings);
    }
}