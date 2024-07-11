namespace Nop.Plugin.Shipping.Fedex.API.Rates;

public partial class RateClient
{
    private readonly FedexSettings _fedexSettings;
    private readonly string _accessToken;

    public RateClient(HttpClient httpClient, FedexSettings fedexSettings, string accessToken) : this(httpClient)
    {
        _fedexSettings = fedexSettings;
        _accessToken = accessToken;

        if (!_fedexSettings.UseSandbox)
            BaseUrl = FedexShippingDefaults.ApiUrl;
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request, string url)
    {
        client.PrepareRequest(request, _fedexSettings, _accessToken);
    }
    
    public async Task<BaseProcessOutputVO> ProcessRateAsync(Full_Schema_Quote_Rate request, string accessToken)
    {
        var rez = await Rate_and_Transit_timesAsync(request, Guid.NewGuid().ToString(), "application/json", "en_US", $"Bearer {accessToken}");

        return rez.Output;
    }
}