using Nop.Core.Configuration;

namespace Nop.Plugin.Shipping.Fedex;

public class FedexSettings : ISettings
{
    /// <summary>
    /// Gets or sets the account number
    /// </summary>
    public string AccountNumber { get; set; }
    
    /// <summary>
    /// Gets or sets a value indicating whether to use residential rates
    /// </summary>
    public bool UseResidentialRates { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use discounted rates (instead of list rates)
    /// </summary>
    public bool ApplyDiscounts { get; set; }

    /// <summary>
    /// Gets or sets an amount of the additional handling charge
    /// </summary>
    public decimal AdditionalHandlingCharge { get; set; }

    /// <summary>
    /// Gets or sets offered carrier services
    /// </summary>
    public string CarrierServicesOffered { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to pass package dimensions
    /// </summary>
    public bool PassDimensions { get; set; }

    /// <summary>
    /// Gets or sets the packing package volume
    /// </summary>
    public int PackingPackageVolume { get; set; }

    /// <summary>
    /// Gets or sets packing type
    /// </summary>
    public PackingType PackingType { get; set; }
    
    /// <summary>
    /// Gets or sets the client ID
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret
    /// </summary>
    public string ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use sandbox environment
    /// </summary>
    public bool UseSandbox { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to record plugin tracing in log
    /// </summary>
    public bool Tracing { get; set; }

    /// <summary>
    /// Gets or sets a period (in seconds) before the request times out
    /// </summary>
    public int? RequestTimeout { get; set; }
}