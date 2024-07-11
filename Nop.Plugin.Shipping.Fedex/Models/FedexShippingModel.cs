using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Shipping.Fedex.Models;

public record FedexShippingModel : BaseNopModel
{
    public FedexShippingModel()
    {
        CarrierServicesOffered = new List<string>();
        AvailableCarrierServices = new List<string>();
    }
    
    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.AccountNumber")]
    public string AccountNumber { get; set; }
    
    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.UseResidentialRates")]
    public bool UseResidentialRates { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.ApplyDiscounts")]
    public bool ApplyDiscounts { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.AdditionalHandlingCharge")]
    public decimal AdditionalHandlingCharge { get; set; }

    public IList<string> CarrierServicesOffered { get; set; }
    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.CarrierServices")]
    public IList<string> AvailableCarrierServices { get; set; }
    public string[] CheckedCarrierServices { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.PassDimensions")]
    public bool PassDimensions { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.PackingPackageVolume")]
    public int PackingPackageVolume { get; set; }

    public int PackingType { get; set; }
    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.PackingType")]
    public SelectList PackingTypeValues { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.ClientId")]
    public string ClientId { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.ClientSecret")]
    public string ClientSecret { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.UseSandbox")]
    public bool UseSandbox { get; set; }

    [NopResourceDisplayName("Plugins.Shipping.Fedex.Fields.Tracing")]
    public bool Tracing { get; set; }
}