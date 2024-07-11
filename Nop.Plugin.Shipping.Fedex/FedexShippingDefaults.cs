using Nop.Core;

namespace Nop.Plugin.Shipping.Fedex;

public class FedexShippingDefaults
{
    public const decimal MAX_PACKAGE_WEIGHT = 150;

    public const string MEASURE_WEIGHT_SYSTEM_KEYWORD = "lb";

    public const string MEASURE_DIMENSION_SYSTEM_KEYWORD = "inches";
    
    /// <summary>
    /// Gets a default period (in seconds) before the request times out
    /// </summary>
    public static int RequestTimeout => 15;

    /// <summary>
    /// Gets the user agent used to request third-party services
    /// </summary>
    public static string UserAgent => $"nopCommerce-{NopVersion.CURRENT_VERSION}";

    /// <summary>
    /// Gets the production API URL
    /// </summary>
    public static string ApiUrl => "https://apis.fedex.com";
}
