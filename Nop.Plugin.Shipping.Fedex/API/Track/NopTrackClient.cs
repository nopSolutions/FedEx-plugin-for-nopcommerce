using Newtonsoft.Json;

namespace Nop.Plugin.Shipping.Fedex.API.Track;

public partial class TrackClient
{
    private FedexSettings _fedexSettings;
    private string _accessToken;

    public TrackClient(HttpClient httpClient, FedexSettings fedexSettings, string accessToken) : this(httpClient)
    {
        _fedexSettings = fedexSettings;
        _accessToken = accessToken;

        if (!_fedexSettings.UseSandbox)
            BaseUrl = FedexShippingDefaults.ApiUrl;
    }

    partial void PrepareRequest(HttpClient client, HttpRequestMessage request,
        string url)
    {
        client.PrepareRequest(request, _fedexSettings, _accessToken);
    }

    /// <summary>
    /// Get tracking info
    /// </summary>
    /// <param name="trackingNumber">The tracking number</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the tracking info
    /// </returns>
    public virtual async Task<BaseProcessOutputVO_TrackingNumber> TrackAsync(string trackingNumber, string accessToken)
    {
        var rez = await Track_by_Tracking_NumberAsync(new Full_Schema_Tracking_Numbers
            {
                IncludeDetailedScans = true,
                TrackingInfo = new List<MasterTrackingInfo>
                {
                    new()
                    {
                        TrackingNumberInfo = new TrackingNumberInfo
                        {
                            TrackingNumber = trackingNumber
                        }
                    }
                }
            }, Guid.NewGuid().ToString(), "application/json",
            "en_US", $"Bearer {accessToken}");

        return rez.Output;
    }
}