using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Shipping.Fedex.Models;
using Nop.Plugin.Shipping.Fedex.Services;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Shipping.Fedex.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class ShippingFedexController : BasePluginController
{
    #region Fields

    private readonly FedexSettings _fedexSettings;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IPermissionService _permissionService;
    private readonly ISettingService _settingService;

    #endregion

    #region Ctor

    public ShippingFedexController(FedexSettings fedexSettings,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        ISettingService settingService)
    {
        _fedexSettings = fedexSettings;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _settingService = settingService;
    }

    #endregion

    #region Methods

    public async Task<IActionResult> Configure()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageShippingSettings))
            return AccessDeniedView();

        var model = new FedexShippingModel
        {
            UseSandbox = _fedexSettings.UseSandbox,
            ClientId = _fedexSettings.ClientId,
            ClientSecret = _fedexSettings.ClientSecret,
            AccountNumber = _fedexSettings.AccountNumber,
            Tracing = _fedexSettings.Tracing,
            UseResidentialRates = _fedexSettings.UseResidentialRates,
            ApplyDiscounts = _fedexSettings.ApplyDiscounts,
            AdditionalHandlingCharge = _fedexSettings.AdditionalHandlingCharge,
            PackingPackageVolume = _fedexSettings.PackingPackageVolume,
            PackingType = Convert.ToInt32(_fedexSettings.PackingType),
            PackingTypeValues = await _fedexSettings.PackingType.ToSelectListAsync(),
            PassDimensions = _fedexSettings.PassDimensions
        };

        // Load service names
        var availableServices = FedexService.GetAllFedExServicesName();
        model.AvailableCarrierServices = availableServices;
        
        if (!string.IsNullOrEmpty(_fedexSettings.CarrierServicesOffered))
            foreach (var service in availableServices)
            {
                var serviceId = FedexService.GetFedExServiceId(service);
                
                if (!string.IsNullOrEmpty(serviceId) && _fedexSettings.CarrierServicesOffered.Contains(serviceId))
                    model.CarrierServicesOffered.Add(service);
            }

        return View("~/Plugins/Shipping.Fedex/Views/Configure.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> Configure(FedexShippingModel model)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManageShippingSettings))
            return AccessDeniedView();

        if (!ModelState.IsValid)
            return await Configure();

        //save settings
        _fedexSettings.ClientId = model.ClientId;
        _fedexSettings.ClientSecret = model.ClientSecret;
        _fedexSettings.UseSandbox = model.UseSandbox;
        _fedexSettings.AccountNumber = model.AccountNumber;
        _fedexSettings.Tracing = model.Tracing;
        _fedexSettings.UseResidentialRates = model.UseResidentialRates;
        _fedexSettings.ApplyDiscounts = model.ApplyDiscounts;
        _fedexSettings.AdditionalHandlingCharge = model.AdditionalHandlingCharge;
        _fedexSettings.PackingPackageVolume = model.PackingPackageVolume;
        _fedexSettings.PackingType = (PackingType)model.PackingType;
        _fedexSettings.PassDimensions = model.PassDimensions;

        // Save selected services
        var carrierServicesOfferedDomestic = new StringBuilder();
        var carrierServicesDomesticSelectedCount = 0;
        
        if (model.CheckedCarrierServices != null)
            foreach (var cs in model.CheckedCarrierServices)
            {
                carrierServicesDomesticSelectedCount++;
                var serviceId = FedexService.GetFedExServiceId(cs);
                
                if (!string.IsNullOrEmpty(serviceId))
                    carrierServicesOfferedDomestic.AppendFormat("{0}:", serviceId);
            }

        // Add default options if no services were selected
        _fedexSettings.CarrierServicesOffered = carrierServicesDomesticSelectedCount == 0 ? "FEDEX_2_DAY:PRIORITY_OVERNIGHT:FEDEX_GROUND:GROUND_HOME_DELIVERY:INTERNATIONAL_ECONOMY" : carrierServicesOfferedDomestic.ToString();

        await _settingService.SaveSettingAsync(_fedexSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return await Configure();
    }

    #endregion
}
