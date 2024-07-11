//------------------------------------------------------------------------------
// Contributor(s): mb, New York. 
//------------------------------------------------------------------------------

using Nop.Core;
using Nop.Plugin.Shipping.Fedex.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Tracking;

namespace Nop.Plugin.Shipping.Fedex;

/// <summary>
/// Fedex computation method
/// </summary>
public class FedexComputationMethod : BasePlugin, IShippingRateComputationMethod
{
    #region Fields

    private readonly FedexService _fedexService;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public FedexComputationMethod(FedexService fedexService,
        ILocalizationService localizationService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _fedexService = fedexService;
        _localizationService = localizationService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    #endregion

    #region Methods

    /// <summary>
    ///  Gets available shipping options
    /// </summary>
    /// <param name="getShippingOptionRequest">A request for getting shipping options</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the represents a response of getting shipping rate options
    /// </returns>
    public async Task<GetShippingOptionResponse> GetShippingOptionsAsync(GetShippingOptionRequest getShippingOptionRequest)
    {
        ArgumentNullException.ThrowIfNull(getShippingOptionRequest);

        if (!getShippingOptionRequest.Items?.Any() ?? true)
            return new GetShippingOptionResponse { Errors = new[] { "No shipment items" } };

        if (getShippingOptionRequest.ShippingAddress?.CountryId is null)
            return new GetShippingOptionResponse { Errors = new[] { "Shipping address is not set" } };

        return await _fedexService.GetRatesAsync(getShippingOptionRequest);
    }

    /// <summary>
    /// Gets fixed shipping rate (if shipping rate computation method allows it and the rate can be calculated before checkout).
    /// </summary>
    /// <param name="getShippingOptionRequest">A request for getting shipping options</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the fixed shipping rate; or null in case there's no fixed shipping rate
    /// </returns>
    public Task<decimal?> GetFixedRateAsync(GetShippingOptionRequest getShippingOptionRequest)
    {
        return Task.FromResult<decimal?>(null);
    }

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/ShippingFedex/Configure";
    }

    /// <summary>
    /// Install plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        //settings
        var settings = new FedexSettings
        {
            PackingPackageVolume = 5184
        };
        await _settingService.SaveSettingAsync(settings);

        //locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Shipping.Fedex.Fields.ClientId"] = "Client ID",
            ["Plugins.Shipping.Fedex.Fields.ClientId.Hint"] = "Specify FedEx client ID.",
            ["Plugins.Shipping.Fedex.Fields.ClientSecret"] = "Client secret",
            ["Plugins.Shipping.Fedex.Fields.ClientSecret.Hint"] = "Specify FedEx client secret.",
            ["Plugins.Shipping.Fedex.Fields.Tracing"] = "Tracing",
            ["Plugins.Shipping.Fedex.Fields.Tracing.Hint"] = "Check if you want to record plugin tracing in System Log. Warning: The entire request and response will be logged (including Client Id/secret, AccountNumber). Do not leave this enabled in a production environment.",
            ["Plugins.Shipping.Fedex.Fields.UseSandbox"] = "Use sandbox",
            ["Plugins.Shipping.Fedex.Fields.UseSandbox.Hint"] = "Check to use sandbox (testing environment).",
            ["Plugins.Shipping.Fedex.Fields.AccountNumber"] = "Account number",
            ["Plugins.Shipping.Fedex.Fields.AccountNumber.Hint"] = "Specify FedEx account number.",
            ["Plugins.Shipping.Fedex.Fields.UseResidentialRates"] = "Use residential rates",
            ["Plugins.Shipping.Fedex.Fields.UseResidentialRates.Hint"] = "Check to use residential rates.",
            ["Plugins.Shipping.Fedex.Fields.ApplyDiscounts"] = "Use discounted rates",
            ["Plugins.Shipping.Fedex.Fields.ApplyDiscounts.Hint"] = "Check to use discounted rates (instead of list rates).",
            ["Plugins.Shipping.Fedex.Fields.AdditionalHandlingCharge"] = "Additional handling charge",
            ["Plugins.Shipping.Fedex.Fields.AdditionalHandlingCharge.Hint"] = "Enter additional handling fee to charge your customers.",
            ["Plugins.Shipping.Fedex.Fields.CarrierServices"] = "Carrier Services Offered",
            ["Plugins.Shipping.Fedex.Fields.CarrierServices.Hint"] = "Select the services you want to offer to customers.",
            ["Plugins.Shipping.Fedex.Fields.PassDimensions"] = "Pass dimensions",
            ["Plugins.Shipping.Fedex.Fields.PassDimensions.Hint"] = "Check if you want to pass package dimensions when requesting rates.",
            ["Plugins.Shipping.Fedex.Fields.PackingType"] = "Packing type",
            ["Plugins.Shipping.Fedex.Fields.PackingType.Hint"] = "Choose preferred packing type.",
            ["Plugins.Shipping.Fedex.Fields.PackingPackageVolume"] = "Package volume",
            ["Plugins.Shipping.Fedex.Fields.PackingPackageVolume.Hint"] = "Enter your package volume.",
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        //settings
        await _settingService.DeleteSettingAsync<FedexSettings>();

        //locales
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Shipping.Fedex.Fields");
        await _localizationService.DeleteLocaleResourcesAsync("Enums.Nop.Plugin.Shipping.Fedex");

        await base.UninstallAsync();
    }

    /// <summary>
    /// Get associated shipment tracker
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the shipment tracker
    /// </returns>
    public Task<IShipmentTracker> GetShipmentTrackerAsync()
    {
        return Task.FromResult<IShipmentTracker>(new FedexShipmentTracker(_fedexService));
    }

    #endregion
}