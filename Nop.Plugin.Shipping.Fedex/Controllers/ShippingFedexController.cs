﻿using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Shipping.Fedex.Domain;
using Nop.Plugin.Shipping.Fedex.Models;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Shipping.Fedex.Controllers
{
    [Area(AreaNames.Admin)]
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

            var model = new FedexShippingModel()
            {
                Url = _fedexSettings.Url,
                Key = _fedexSettings.Key,
                Password = _fedexSettings.Password,
                AccountNumber = _fedexSettings.AccountNumber,
                MeterNumber = _fedexSettings.MeterNumber,
                DropoffType = Convert.ToInt32(_fedexSettings.DropoffType),
                AvailableDropOffTypes = await _fedexSettings.DropoffType.ToSelectListAsync(),
                UseResidentialRates = _fedexSettings.UseResidentialRates,
                ApplyDiscounts = _fedexSettings.ApplyDiscounts,
                AdditionalHandlingCharge = _fedexSettings.AdditionalHandlingCharge,
                PackingPackageVolume = _fedexSettings.PackingPackageVolume,
                PackingType = Convert.ToInt32(_fedexSettings.PackingType),
                PackingTypeValues = await _fedexSettings.PackingType.ToSelectListAsync(),
                PassDimensions = _fedexSettings.PassDimensions
            };

            // Load service names
            var availableServices = new FedexServices().Services;
            model.AvailableCarrierServices = availableServices;
            if (!string.IsNullOrEmpty(_fedexSettings.CarrierServicesOffered))
            {
                foreach (var service in availableServices)
                {
                    var serviceId = FedexServices.GetServiceId(service);
                    if (!string.IsNullOrEmpty(serviceId) && _fedexSettings.CarrierServicesOffered.Contains(serviceId))
                        model.CarrierServicesOffered.Add(service);
                }
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
            _fedexSettings.Url = model.Url;
            _fedexSettings.Key = model.Key;
            _fedexSettings.Password = model.Password;
            _fedexSettings.AccountNumber = model.AccountNumber;
            _fedexSettings.MeterNumber = model.MeterNumber;
            _fedexSettings.DropoffType = (DropoffType)model.DropoffType;
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
            {
                foreach (var cs in model.CheckedCarrierServices)
                {
                    carrierServicesDomesticSelectedCount++;
                    var serviceId = FedexServices.GetServiceId(cs);
                    if (!string.IsNullOrEmpty(serviceId))
                        carrierServicesOfferedDomestic.AppendFormat("{0}:", serviceId);
                }
            }
            // Add default options if no services were selected
            if (carrierServicesDomesticSelectedCount == 0)
                _fedexSettings.CarrierServicesOffered = "FEDEX_2_DAY:PRIORITY_OVERNIGHT:FEDEX_GROUND:GROUND_HOME_DELIVERY:INTERNATIONAL_ECONOMY";
            else
                _fedexSettings.CarrierServicesOffered = carrierServicesOfferedDomestic.ToString();

            await _settingService.SaveSettingAsync(_fedexSettings);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }

        #endregion
    }
}
