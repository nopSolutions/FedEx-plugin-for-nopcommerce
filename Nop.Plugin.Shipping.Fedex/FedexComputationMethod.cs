//------------------------------------------------------------------------------
// Contributor(s): mb, New York. 
//------------------------------------------------------------------------------

using System;
using System.Linq;
using Nop.Core;
using Nop.Plugin.Shipping.Fedex.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Tracking;

namespace Nop.Plugin.Shipping.Fedex
{
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
        /// <returns>Represents a response of getting shipping rate options</returns>
        public GetShippingOptionResponse GetShippingOptions(GetShippingOptionRequest getShippingOptionRequest)
        {
            if (getShippingOptionRequest is null)
                throw new ArgumentNullException(nameof(getShippingOptionRequest));

            var response = new GetShippingOptionResponse();

            if (!getShippingOptionRequest.Items?.Any() ?? true)
                return new GetShippingOptionResponse { Errors = new[] { "No shipment items" } };

            if (getShippingOptionRequest.CountryFrom is null)
                return new GetShippingOptionResponse { Errors = new[] { "Shipping address is not set" } };

            return _fedexService.GetRates(getShippingOptionRequest);
        }

        /// <summary>
        /// Gets fixed shipping rate (if shipping rate computation method allows it and the rate can be calculated before checkout).
        /// </summary>
        /// <param name="getShippingOptionRequest">A request for getting shipping options</param>
        /// <returns>Fixed shipping rate; or null in case there's no fixed shipping rate</returns>
        public decimal? GetFixedRate(GetShippingOptionRequest getShippingOptionRequest)
        {
            return null;
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
        public override void Install()
        {
            //settings
            var settings = new FedexSettings
            {
                Url = "https://gatewaybeta.fedex.com:443/web-services/rate",
                DropoffType = DropoffType.BusinessServiceCenter,
                PackingPackageVolume = 5184
            };
            _settingService.SaveSetting(settings);

            //locales
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Url", "URL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Url.Hint", "Specify FedEx URL.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Key", "Key");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Key.Hint", "Specify FedEx key.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Password", "Password");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Password.Hint", "Specify FedEx password.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AccountNumber", "Account number");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AccountNumber.Hint", "Specify FedEx account number.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.MeterNumber", "Meter number");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.MeterNumber.Hint", "Specify FedEx meter number.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.UseResidentialRates", "Use residential rates");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.UseResidentialRates.Hint", "Check to use residential rates.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.ApplyDiscounts", "Use discounted rates");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.ApplyDiscounts.Hint", "Check to use discounted rates (instead of list rates).");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AdditionalHandlingCharge", "Additional handling charge");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AdditionalHandlingCharge.Hint", "Enter additional handling fee to charge your customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.CarrierServices", "Carrier Services Offered");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.CarrierServices.Hint", "Select the services you want to offer to customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PassDimensions", "Pass dimensions");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PassDimensions.Hint", "Check if you want to pass package dimensions when requesting rates.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingType", "Packing type");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingType.Hint", "Choose preferred packing type.");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByDimensions", "Pack by dimensions");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByOneItemPerPackage", "Pack by one item per package");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByVolume", "Pack by volume");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingPackageVolume", "Package volume");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingPackageVolume.Hint", "Enter your package volume.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.DropoffType", "Dropoff Type");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Shipping.Fedex.Fields.DropoffType.Hint", "Choose preferred dropoff type.");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.BusinessServiceCenter", "Business service center");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.DropBox", "Drop box");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.RegularPickup", "Regular pickup");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.RequestCourier", "Request courier");
            _localizationService.AddOrUpdatePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.Station", "Station");

            base.Install();
        }

        /// <summary>
        /// Uninstall plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<FedexSettings>();

            //locales
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Url");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Url.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Key");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Key.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Password");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.Password.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AccountNumber");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AccountNumber.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.MeterNumber");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.MeterNumber.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.UseResidentialRates");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.UseResidentialRates.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.ApplyDiscounts");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.ApplyDiscounts.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AdditionalHandlingCharge");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.AdditionalHandlingCharge.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.CarrierServices");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.CarrierServices.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PassDimensions");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PassDimensions.Hint");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByDimensions");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByOneItemPerPackage");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByVolume");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingType");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingType.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingPackageVolume");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.PackingPackageVolume.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.DropoffType");
            _localizationService.DeletePluginLocaleResource("Plugins.Shipping.Fedex.Fields.DropoffType.Hint");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.BusinessServiceCenter");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.DropBox");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.RegularPickup");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.RequestCourier");
            _localizationService.DeletePluginLocaleResource("Enums.Nop.Plugin.Shipping.Fedex.DropoffType.Station");

            base.Uninstall();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a shipping rate computation method type
        /// </summary>
        public ShippingRateComputationMethodType ShippingRateComputationMethodType => ShippingRateComputationMethodType.Realtime;

        /// <summary>
        /// Gets a shipment tracker
        /// </summary>
        public IShipmentTracker ShipmentTracker => new FedexShipmentTracker(_fedexService);

        #endregion
    }
}