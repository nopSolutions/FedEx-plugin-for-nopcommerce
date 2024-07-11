using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Services.Configuration;
using Nop.Services.Localization;

namespace Nop.Plugin.Shipping.Fedex.Migrations;

[NopMigration("2024-07-11 20:00:00", "Shipping.Fedex Update to v4.70.2 (migrate to RestFull API)", MigrationProcessType.Update)]
public class UpgradeTo470 : Migration
{
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;

    public UpgradeTo470(ILocalizationService localizationService,
        ISettingService settingService)
    {
        _localizationService = localizationService;
        _settingService = settingService;
    }

    public override void Up()
    {
        _localizationService.DeleteLocaleResources(new[]
        {
            "Plugins.Shipping.Fedex.Fields.Password",
            "Plugins.Shipping.Fedex.Fields.Password.Hint",
            "Plugins.Shipping.Fedex.Fields.Key",
            "Plugins.Shipping.Fedex.Fields.Key.Hint",
            "Plugins.Shipping.Fedex.Fields.MeterNumber",
            "Plugins.Shipping.Fedex.Fields.MeterNumber.Hint",
            "Plugins.Shipping.Fedex.Fields.Url",
            "Plugins.Shipping.Fedex.Fields.Url.Hint",
            "Plugins.Shipping.Fedex.Fields.DropoffType",
            "Plugins.Shipping.Fedex.Fields.DropoffType.Hint",
            "Enums.Nop.Plugin.Shipping.Fedex.DropoffType.BusinessServiceCenter",
            "Enums.Nop.Plugin.Shipping.Fedex.DropoffType.DropBox",
            "Enums.Nop.Plugin.Shipping.Fedex.DropoffType.RegularPickup",
            "Enums.Nop.Plugin.Shipping.Fedex.DropoffType.RequestCourier",
            "Enums.Nop.Plugin.Shipping.Fedex.DropoffType.Station",
            "Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByDimensions",
            "Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByOneItemPerPackage",
            "Enums.Nop.Plugin.Shipping.Fedex.PackingType.PackByVolume"
        });

        _localizationService.AddOrUpdateLocaleResource(new Dictionary<string, string>
        {
            ["Plugins.Shipping.Fedex.Fields.ClientId"] = "Client ID",
            ["Plugins.Shipping.Fedex.Fields.ClientId.Hint"] = "Specify FedEx client ID.",
            ["Plugins.Shipping.Fedex.Fields.ClientSecret"] = "Client secret",
            ["Plugins.Shipping.Fedex.Fields.ClientSecret.Hint"] = "Specify FedEx client secret.",
            ["Plugins.Shipping.Fedex.Fields.Tracing"] = "Tracing",
            ["Plugins.Shipping.Fedex.Fields.Tracing.Hint"] = "Check if you want to record plugin tracing in System Log. Warning: The entire request and response will be logged (including Client Id/secret, AccountNumber). Do not leave this enabled in a production environment.",
            ["Plugins.Shipping.Fedex.Fields.UseSandbox"] = "Use sandbox",
            ["Plugins.Shipping.Fedex.Fields.UseSandbox.Hint"] = "Check to use sandbox (testing environment).",
        });

        var setting = _settingService.LoadSetting<FedexSettings>();
        
        if (!_settingService.SettingExists(setting, settings => settings.RequestTimeout))
        {
            setting.RequestTimeout = FedexShippingDefaults.RequestTimeout;
            _settingService.SaveSetting(setting, settings => settings.RequestTimeout);
        }
        
        var key = _settingService.GetSetting("fedexsettings.key");
        
        if (key is not null)
            _settingService.DeleteSetting(key);

        var password = _settingService.GetSetting("fedexsettings.password");
        
        if (password is not null)
            _settingService.DeleteSetting(password);

        var meterNumber = _settingService.GetSetting("fedexsettings.meternumber");
        
        if (meterNumber is not null)
            _settingService.DeleteSetting(meterNumber);

        var url = _settingService.GetSetting("fedexsettings.url");
        
        if (url is not null)
            _settingService.DeleteSetting(url);

        var dropoffType = _settingService.GetSetting("fedexsettings.dropofftype");

        if (dropoffType is not null)
            _settingService.DeleteSetting(dropoffType);
    }

    public override void Down()
    {
        //add the downgrade logic if necessary 
    }
}