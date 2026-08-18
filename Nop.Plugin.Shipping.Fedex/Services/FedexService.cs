using System.Diagnostics;
using System.Globalization;
using System.Net;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Http;
using Nop.Plugin.Shipping.Fedex.API.OAuth;
using Nop.Plugin.Shipping.Fedex.API.Rates;
using Nop.Plugin.Shipping.Fedex.API.Track;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Tracking;
using ErrorResponseVO = Nop.Plugin.Shipping.Fedex.API.Rates.ErrorResponseVO;

namespace Nop.Plugin.Shipping.Fedex.Services;

public class FedexService
{
    #region Fields

    private static readonly Dictionary<string, string> _fedexServices;

    private readonly CurrencySettings _currencySettings;
    private readonly FedexSettings _fedexSettings;
    private readonly ICountryService _countryService;
    private readonly ICurrencyService _currencyService;
    private readonly ICustomerService _customerService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly IMeasureService _measureService;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly IProductService _productService;
    private readonly IShippingService _shippingService;
    private readonly IStateProvinceService _stateProvinceService;
    private readonly IWorkContext _workContext;

    private string _accessToken;

    #endregion

    #region Ctor

    static FedexService()
    {
        _fedexServices = new Dictionary<string, string>(comparer: StringComparer.InvariantCultureIgnoreCase)
        {
            ["EUROPE_FIRST_INTERNATIONAL_PRIORITY"] = "FedEx Europe First International Priority",
            ["FEDEX_1_DAY_FREIGHT"] = "FedEx 1Day Freight",
            ["FEDEX_2_DAY"] = "FedEx 2Day",
            ["FEDEX_2_DAY_FREIGHT"] = "FedEx 2Day Freight",
            ["FEDEX_3_DAY_FREIGHT"] = "FedEx 3Day Freight",
            ["FEDEX_EXPRESS_SAVER"] = "FedEx Express Saver",
            ["FEDEX_GROUND"] = "FedEx Ground",
            ["FIRST_OVERNIGHT"] = "FedEx First Overnight",
            ["GROUND_HOME_DELIVERY"] = "FedEx Ground Home Delivery",
            ["FEDEX_INTERNATIONAL_CONNECT_PLUS"] = "FedEx International Connect Plus",
            ["INTERNATIONAL_DISTRIBUTION_FREIGHT"] = "FedEx International Distribution Freight",
            ["INTERNATIONAL_ECONOMY"] = "FedEx International Economy",
            ["INTERNATIONAL_ECONOMY_DISTRIBUTION"] = "FedEx International Economy Distribution",
            ["INTERNATIONAL_ECONOMY_FREIGHT"] = "FedEx International Economy Freight",
            ["INTERNATIONAL_FIRST"] = "FedEx International First",
            ["INTERNATIONAL_PRIORITY"] = "FedEx International Priority",
            ["INTERNATIONAL_PRIORITY_FREIGHT"] = "FedEx International Priority Freight",
            ["PRIORITY_OVERNIGHT"] = "FedEx Priority Overnight",
            ["SMART_POST"] = "FedEx Ground Economy (SmartPost)",
            ["STANDARD_OVERNIGHT"] = "FedEx Standard Overnight",
            ["FEDEX_FREIGHT"] = "FedEx Freight",
            ["FEDEX_NATIONAL_FREIGHT"] = "FedEx National Freight"
        };
    }
    public FedexService(CurrencySettings currencySettings,
        FedexSettings fedexSettings,
        ICountryService countryService,
        ICurrencyService currencyService,
        ICustomerService customerService,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        IMeasureService measureService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IProductService productService,
        IShippingService shippingService,
        IStateProvinceService stateProvinceService,
        IWorkContext workContext)
    {
        _currencySettings = currencySettings;
        _fedexSettings = fedexSettings;
        _countryService = countryService;
        _currencyService = currencyService;
        _customerService = customerService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _measureService = measureService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _productService = productService;
        _shippingService = shippingService;
        _stateProvinceService = stateProvinceService;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    private async Task<decimal> ConvertChargeToPrimaryCurrencyAsync(double chargeAmount, string chargeCurrency, Currency requestedShipmentCurrency)
    {
        var primaryStoreCurrency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

        var amount = new decimal(chargeAmount);

        if (primaryStoreCurrency.CurrencyCode.Equals(chargeCurrency, StringComparison.InvariantCultureIgnoreCase))
            return amount;

        var amountCurrency = chargeCurrency == requestedShipmentCurrency.CurrencyCode ? requestedShipmentCurrency : await _currencyService.GetCurrencyByCodeAsync(chargeCurrency);

        //ensure the currency exists; otherwise, presume that it was primary store currency
        amountCurrency ??= primaryStoreCurrency;

        amount = await _currencyService.ConvertToPrimaryStoreCurrencyAsync(amount, amountCurrency);

        Debug.WriteLine($"ConvertChargeToPrimaryCurrency - from {chargeAmount} ({chargeCurrency}) to {amount} ({primaryStoreCurrency.CurrencyCode})");

        return amount;
    }

    /// <summary>
    /// Get dimensions values of the package
    /// </summary>
    /// <param name="items">Package items</param>
    /// <param name="minRate">Minimal rate</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the dimensions values
    /// </returns>
    private async Task<(int width, int length, int height)> GetDimensionsAsync(IList<GetShippingOptionRequest.PackageItem> items, int minRate = 1)
    {
        var measureDimension = await _measureService.GetMeasureDimensionBySystemKeywordAsync(FedexShippingDefaults.MEASURE_DIMENSION_SYSTEM_KEYWORD) ??
            throw new NopException($"FedEx shipping service. Could not load \"{FedexShippingDefaults.MEASURE_DIMENSION_SYSTEM_KEYWORD}\" measure dimension");

        var (width, length, height) = await _shippingService.GetDimensionsAsync(items, true);
        var rezWidth = await convertAndRoundDimension(width);
        var rezLength = await convertAndRoundDimension(length);
        var rezHeight = await convertAndRoundDimension(height);

        return (rezWidth, rezLength, rezHeight);

        #region Local functions

        async Task<int> convertAndRoundDimension(decimal dimension)
        {
            dimension = await _measureService.ConvertFromPrimaryMeasureDimensionAsync(dimension, measureDimension);
            var rezDimension = Convert.ToInt32(Math.Ceiling(dimension));

            return Math.Max(rezDimension, minRate);
        }

        #endregion
    }

    /// <summary>
    /// Get dimensions values of the single shopping cart item
    /// </summary>
    /// <param name="item">Shopping cart item</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the dimensions values
    /// </returns>
    private async Task<(int width, int length, int height)> GetDimensionsForSingleItemAsync(ShoppingCartItem item)
    {
        var product = await _productService.GetProductByIdAsync(item.ProductId);

        var items = new[] { new GetShippingOptionRequest.PackageItem(item, product, 1) };

        return await GetDimensionsAsync(items);
    }

    /// <summary>
    /// Get weight value of the package
    /// </summary>
    /// <param name="shippingOptionRequest">Shipping option request</param>
    /// <param name="minRate">Minimal rate</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the weight value
    /// </returns>
    private async Task<decimal> GetWeightAsync(GetShippingOptionRequest shippingOptionRequest, int minRate = 1)
    {
        var measureWeight = await _measureService.GetMeasureWeightBySystemKeywordAsync(FedexShippingDefaults.MEASURE_WEIGHT_SYSTEM_KEYWORD) ??
            throw new NopException($"FedEx shipping service. Could not load \"{FedexShippingDefaults.MEASURE_WEIGHT_SYSTEM_KEYWORD}\" measure weight");

        var weight = await _shippingService.GetTotalWeightAsync(shippingOptionRequest, ignoreFreeShippedItems: true);
        weight = await _measureService.ConvertFromPrimaryMeasureWeightAsync(weight, measureWeight);
        weight = Convert.ToInt32(Math.Ceiling(weight));
        return Math.Max(weight, minRate);
    }

    /// <summary>
    /// Get weight value of the single shopping cart item
    /// </summary>
    /// <param name="item">Shopping cart item</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the weight value
    /// </returns>
    private async Task<decimal> GetWeightForSingleItemAsync(ShoppingCartItem item)
    {
        var customer = await _customerService.GetCustomerByIdAsync(item.CustomerId);
        var product = await _productService.GetProductByIdAsync(item.ProductId);

        var shippingOptionRequest = new GetShippingOptionRequest
        {
            Customer = customer,
            Items = new[] { new GetShippingOptionRequest.PackageItem(item, product, 1) }
        };

        return await GetWeightAsync(shippingOptionRequest);
    }

    /// <summary>
    /// Get access token
    /// </summary>
    /// <returns>The asynchronous task whose result contains access token</returns>
    private async Task<string> GetAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken))
            return _accessToken;

        if (string.IsNullOrEmpty(_fedexSettings.ClientId))
            throw new NopException("Client ID is not set");

        if (string.IsNullOrEmpty(_fedexSettings.ClientSecret))
            throw new NopException("Client secret is not set");

        var client = new OAuthClient(_httpClientFactory.CreateClient(NopHttpDefaults.DefaultHttpClient), _fedexSettings);

        var response = await client.GenerateTokenAsync();
        _accessToken = response.Access_token;

        return _accessToken;
    }

    /// <summary>
    /// Create request details to track shipment
    /// </summary>
    /// <param name="trackingNumber">Tracking number</param>
    /// <returns>Track request details</returns>
    private async Task<BaseProcessOutputVO_TrackingNumber> CreateTrackRequestAsync(string trackingNumber)
    {
        var client = new TrackClient(_httpClientFactory.CreateClient(NopHttpDefaults.DefaultHttpClient), _fedexSettings, await GetAccessTokenAsync());

        var trackResponse = await client.TrackAsync(trackingNumber, await GetAccessTokenAsync());

        return trackResponse;
    }

    /// <summary>
    /// Create package details
    /// </summary>
    /// <param name="width">Width</param>
    /// <param name="length">Length</param>
    /// <param name="height">Height</param>
    /// <param name="weight">Weight</param>
    /// <param name="orderSubTotal"></param>
    /// <param name="currencyCode">Currency code</param>
    /// <returns>Package details</returns>
    private RequestedPackageLineItem CreatePackage(int width, int length, int height, decimal weight, decimal orderSubTotal, string currencyCode)
    {
        return new RequestedPackageLineItem
        {
            GroupPackageCount = 1,
            Weight = new()
            {
                Units = "LB",
                Value = (double)weight,
            }, // package weight

            Dimensions = new()
            {
                Length = _fedexSettings.PassDimensions ? length : 0,
                Width = _fedexSettings.PassDimensions ? width : 0,
                Height = _fedexSettings.PassDimensions ? height : 0,
                Units = "IN",
            }, // package dimensions
            DeclaredValue = new Money
            {
                Amount = (double)orderSubTotal,
                Currency = currencyCode
            } // insured value
        };
    }

    /// <summary>
    /// Create request details to get shipping rates
    /// </summary>
    /// <param name="shippingOptionRequest">Shipping option request</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the rate request details
    /// </returns>
    private async Task<(Full_Schema_Quote_Rate rateRequest, Currency requestedShipmentCurrency)> CreateRateRequestAsync(GetShippingOptionRequest shippingOptionRequest)
    {
        // Build the RateRequest
        var request = new Full_Schema_Quote_Rate
        {
            AccountNumber = new AccountNumber { Value = _fedexSettings.AccountNumber },
            RateRequestControlParameters = new() { ReturnTransitTimes = true },
            CarrierCodes = new List<string> { "FDXE", "FDXG", "FXSP" }
        };

        var (_, _, _, subTotalWithDiscountBase, _) = await _orderTotalCalculationService.GetShoppingCartSubTotalAsync(
            shippingOptionRequest.Items.Select(x => x.ShoppingCartItem).ToList(),
            false);

        request.RequestedShipment = new RequestedShipment
        {
            RateRequestType = new List<RateRequestType>
            {
                RateRequestType.LIST,
                RateRequestType.PREFERRED
            }
        };

        SetOrigin(request, shippingOptionRequest);
        await SetDestinationAsync(request, shippingOptionRequest);

        var requestedShipmentCurrency = await GetRequestedShipmentCurrencyAsync(
            request.RequestedShipment.Shipper.Address.CountryCode,    // origin
            request.RequestedShipment.Recipient.Address.CountryCode); // destination

        decimal subTotalShipmentCurrency;
        var primaryStoreCurrency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

        if (requestedShipmentCurrency.CurrencyCode == primaryStoreCurrency.CurrencyCode)
            subTotalShipmentCurrency = subTotalWithDiscountBase;
        else
            subTotalShipmentCurrency = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(subTotalWithDiscountBase, requestedShipmentCurrency);

        Debug.WriteLine($"SubTotal (Primary Currency) : {subTotalWithDiscountBase} ({primaryStoreCurrency.CurrencyCode})");
        Debug.WriteLine($"SubTotal (Shipment Currency): {subTotalShipmentCurrency} ({requestedShipmentCurrency.CurrencyCode})");

        SetPayment(request);
        SetShipmentDetails(request, subTotalShipmentCurrency, requestedShipmentCurrency.CurrencyCode);
        
        //set packages details
        switch (_fedexSettings.PackingType)
        {
            case PackingType.PackByOneItemPerPackage:
                await SetIndividualPackageLineItemsOneItemPerPackageAsync(request, shippingOptionRequest, requestedShipmentCurrency.CurrencyCode);
                break;
            case PackingType.PackByVolume:
                await SetIndividualPackageLineItemsCubicRootDimensionsAsync(request, shippingOptionRequest, subTotalShipmentCurrency, requestedShipmentCurrency.CurrencyCode);
                break;
            case PackingType.PackByDimensions:
            default:
                await SetIndividualPackageLineItemsAsync(request, shippingOptionRequest, subTotalShipmentCurrency, requestedShipmentCurrency.CurrencyCode);
                break;
        }
        return (request, requestedShipmentCurrency);
    }

    private async Task<Currency> GetRequestedShipmentCurrencyAsync(string originCountryCode, string destinCountryCode)
    {
        var primaryStoreCurrency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

        //The solution coded here might be considered a bit of a hack
        //it only supports the scenario for US / Canada / India shipping
        //because nopCommerce does not have a concept of a designated currency for a Country.
        var originCurrencyCode = getCurrencyCode(originCountryCode);
        var destinCurrencyCode = getCurrencyCode(destinCountryCode);

        //when neither the shipping origin's currency or the destinations currency is the same as the store primary currency,
        //FedEx would complain that "There are no valid services available. (code: 556)".
        if (originCurrencyCode == primaryStoreCurrency.CurrencyCode || destinCurrencyCode == primaryStoreCurrency.CurrencyCode)
            return primaryStoreCurrency;

        //ensure that this currency exists
        return await _currencyService.GetCurrencyByCodeAsync(originCurrencyCode) ?? primaryStoreCurrency;

        #region Local functions

        string getCurrencyCode(string countryCode)
        {
            return countryCode switch
            {
                "US" => "USD",
                "CA" => "CAD",
                "IN" => "INR",
                _ => primaryStoreCurrency.CurrencyCode
            };
        }

        #endregion
    }

    private async Task<IList<ShippingOption>> ParseResponseAsync(BaseProcessOutputVO reply, Currency requestedShipmentCurrency)
    {
        var result = new List<ShippingOption>();

        Debug.WriteLine("RateReply details:");
        Debug.WriteLine("**********************************************************");
        foreach (var rateDetail in reply.RateReplyDetails)
        {
            var shippingOption = new ShippingOption();
            var serviceName = GetFedExServiceName(rateDetail.ServiceType);

            // Skip the current service if services are selected and this service hasn't been selected
            if (!string.IsNullOrEmpty(_fedexSettings.CarrierServicesOffered) && !_fedexSettings.CarrierServicesOffered.Contains(rateDetail.ServiceType))
                continue;

            Debug.WriteLine("ServiceType: " + rateDetail.ServiceType);
            if (!serviceName.Equals("UNKNOWN"))
            {
                shippingOption.Name = serviceName;

                foreach (var shipmentDetail in rateDetail.RatedShipmentDetails)
                {
                    Debug.WriteLine("RateType : " + shipmentDetail.RateType);
                    Debug.WriteLine("Total Billing Weight : " + shipmentDetail.ShipmentRateDetail.TotalBillingWeight.Value);
                    Debug.WriteLine("Total Base Charge : " + shipmentDetail.TotalBaseCharge);
                    Debug.WriteLine("Total Discount : " + shipmentDetail.TotalDiscounts);
                    Debug.WriteLine("Total Surcharges : " + shipmentDetail.ShipmentRateDetail.TotalSurcharges);
                    Debug.WriteLine($"Net Charge : {shipmentDetail.TotalNetCharge}");
                    Debug.WriteLine("*********");

                    // get discounted rates if option is selected
                    if (_fedexSettings.ApplyDiscounts &
                        (shipmentDetail.RateType == RatedShipmentDetailRateType.ACCOUNT))
                    {
                        var amount = await ConvertChargeToPrimaryCurrencyAsync(shipmentDetail.TotalNetCharge, shipmentDetail.ShipmentRateDetail.Currency, requestedShipmentCurrency);
                        shippingOption.Rate = amount + _fedexSettings.AdditionalHandlingCharge;
                        break;
                    }

                    // get List Rates (not discount rates)
                    if (shipmentDetail.RateType == RatedShipmentDetailRateType.LIST)
                    {
                        var amount = await ConvertChargeToPrimaryCurrencyAsync(shipmentDetail.TotalNetCharge, shipmentDetail.ShipmentRateDetail.Currency, requestedShipmentCurrency);
                        shippingOption.Rate = amount + _fedexSettings.AdditionalHandlingCharge;
                        break;
                    }
                }

                result.Add(shippingOption);
            }
            Debug.WriteLine("**********************************************************");
        }

        return result;
    }

    private async Task SetDestinationAsync(Full_Schema_Quote_Rate request, GetShippingOptionRequest getShippingOptionRequest)
    {
        request.RequestedShipment.Recipient = new RateParty
        {
            Address = new RateAddress()
        };

        if (_fedexSettings.UseResidentialRates)
            request.RequestedShipment.Recipient.Address.Residential = true;

        request.RequestedShipment.Recipient.Address.City = getShippingOptionRequest.ShippingAddress.City;

        var recipientCountryCode = (await _countryService.GetCountryByAddressAsync(getShippingOptionRequest.ShippingAddress))?.TwoLetterIsoCode ?? string.Empty;

        if (await _stateProvinceService.GetStateProvinceByAddressAsync(getShippingOptionRequest.ShippingAddress) is { } stateProvince &&
            IncludeStateProvinceCode(recipientCountryCode))
            request.RequestedShipment.Recipient.Address.StateOrProvinceCode = stateProvince.Abbreviation;
        else
            request.RequestedShipment.Recipient.Address.StateOrProvinceCode = string.Empty;

        request.RequestedShipment.Recipient.Address.PostalCode = getShippingOptionRequest.ShippingAddress.ZipPostalCode;
        request.RequestedShipment.Recipient.Address.CountryCode = recipientCountryCode;
    }

    /// <summary>
    /// Create packages (total dimensions of shopping cart items determines number of packages)
    /// </summary>
    /// <param name="request">Shipping request</param>
    /// <param name="getShippingOptionRequest">Shipping option request</param>
    /// <param name="orderSubTotal"></param>
    /// <param name="currencyCode">Currency code</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    private async Task SetIndividualPackageLineItemsAsync(Full_Schema_Quote_Rate request, GetShippingOptionRequest getShippingOptionRequest, decimal orderSubTotal, string currencyCode)
    {
        var (length, height, width) = await GetDimensionsAsync(getShippingOptionRequest.Items);
        var weight = await GetWeightAsync(getShippingOptionRequest);

        if (!IsPackageTooHeavy(weight) && !IsPackageTooLarge(length, height, width))
        {
            request.RequestedShipment.TotalPackageCount = 1;

            var package = CreatePackage(width, length, height, weight, orderSubTotal, currencyCode);
            package.GroupPackageCount = 1;

            request.RequestedShipment.RequestedPackageLineItems = new[] { package };
        }
        else
        {
            var totalPackagesDims = 1;
            var totalPackagesWeights = 1;
            if (IsPackageTooHeavy(weight))
                totalPackagesWeights = Convert.ToInt32(Math.Ceiling(weight / FedexShippingDefaults.MAX_PACKAGE_WEIGHT));

            if (IsPackageTooLarge(length, height, width))
                totalPackagesDims = Convert.ToInt32(Math.Ceiling(TotalPackageSize(length, height, width) / 108M));

            var totalPackages = totalPackagesDims > totalPackagesWeights ? totalPackagesDims : totalPackagesWeights;

            if (totalPackages == 0)
                totalPackages = 1;

            width = Math.Max(width / totalPackages, 1);
            length = Math.Max(length / totalPackages, 1);
            height = Math.Max(height / totalPackages, 1);
            weight = Math.Max(weight / totalPackages, 1);

            var orderSubTotal2 = orderSubTotal / totalPackages;

            request.RequestedShipment.TotalPackageCount = totalPackages;

            request.RequestedShipment.RequestedPackageLineItems = Enumerable.Range(1, totalPackages - 1)
                .Select(_ => CreatePackage(width, length, height, weight, orderSubTotal2, currencyCode)).ToArray();
        }
    }

    /// <summary>
    /// Create packages (total volume of shopping cart items determines number of packages)
    /// </summary>
    /// <param name="request">Shipping request</param>
    /// <param name="getShippingOptionRequest">Shipping option request</param>
    /// <param name="orderSubTotal"></param>
    /// <param name="currencyCode">Currency code</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    private async Task SetIndividualPackageLineItemsCubicRootDimensionsAsync(Full_Schema_Quote_Rate request, GetShippingOptionRequest getShippingOptionRequest, decimal orderSubTotal, string currencyCode)
    {
        //From FedEx Guide (Ground):
        //Dimensional weight is based on volume (the amount of space a package
        //occupies in relation to its actual weight). If the cubic size of your FedEx
        //Ground package measures three cubic feet (5,184 cubic inches or 84,951
        //cubic centimetres) or greater, you will be charged the greater of the
        //dimensional weight or the actual weight.
        //A package weighing 150 lbs. (68 kg) or less and measuring greater than
        //130 inches (330 cm) in combined length and girth will be classified by
        //FedEx Ground as an “Oversize” package. All packages must have a
        //combined length and girth of no more than 165 inches (419 cm). An
        //oversize charge of $30 per package will also apply to any package
        //measuring greater than 130 inches (330 cm) in combined length and
        //girth.
        //Shipping charges for packages smaller than three cubic feet are based
        //on actual weight

        // Dimensional Weight applies to packages with volume 5,184 cubic inches or more
        // cube root(5184) = 17.3

        // Packages that exceed 130 inches in length and girth (2xHeight + 2xWidth) 
        // are considered “oversize” packages.
        // Assume a cube (H=W=L) of that size: 130 = D + (2xD + 2xD) = 5xD :  D = 130/5 = 26
        // 26x26x26 = 17,576
        // Avoid oversize by using 25"
        // 25x25x25 = 15,625

        // Which is less $  - multiple small packages, or one large package using dimensional weight
        //  15,625 / 5184 = 3.014 =  3 packages  
        // Ground for total weight:             60lbs     15lbs
        //  3 packages 17x17x17 (20 lbs each) = $66.21    39.39
        //  1 package  25x25x25 (60 lbs)      = $71.70    71.70

        var totalPackagesDims = 1;
        int length;
        int height;
        int width;

        if (getShippingOptionRequest.Items.Count == 1 && getShippingOptionRequest.Items[0].GetQuantity() == 1)
        {
            //get dimensions and weight of the single cubic size of package
            var item = getShippingOptionRequest.Items.First().ShoppingCartItem;
            (width, length, height) = await GetDimensionsForSingleItemAsync(item);
        }
        else
        {
            //or try to get them
            var dimension = 0;

            //get total volume of the package
            var totalVolume = await getShippingOptionRequest.Items.SumAwaitAsync(async item =>
            {
                //get dimensions and weight of the single item
                var (itemWidth, itemLength, itemHeight) = await GetDimensionsForSingleItemAsync(item.ShoppingCartItem);
                return item.GetQuantity() * itemWidth * itemLength * itemHeight;
            });

            if (totalVolume > decimal.Zero)
            {
                //use default value (in cubic inches) if not specified
                var packageVolume = _fedexSettings.PackingPackageVolume;
                if (packageVolume <= 0)
                    packageVolume = 5184;

                //calculate cube root (floor)
                dimension = Convert.ToInt32(Math.Floor(Math.Pow(Convert.ToDouble(packageVolume), 1.0 / 3.0)));
                if (IsPackageTooLarge(dimension, dimension, dimension))
                    throw new NopException("fedexSettings.PackingPackageVolume exceeds max package size");

                //adjust package volume for dimensions calculated
                packageVolume = dimension * dimension * dimension;

                totalPackagesDims = Convert.ToInt32(Math.Ceiling(totalVolume / packageVolume));
            }

            width = length = height = dimension;
        }

        width = Math.Max(width, 1);
        length = Math.Max(length, 1);
        height = Math.Max(height, 1);

        var weight = await GetWeightAsync(getShippingOptionRequest);

        var totalPackagesWeights = 1;
        if (IsPackageTooHeavy(weight))
            totalPackagesWeights = Convert.ToInt32(Math.Ceiling(weight / FedexShippingDefaults.MAX_PACKAGE_WEIGHT));

        var totalPackages = totalPackagesDims > totalPackagesWeights ? totalPackagesDims : totalPackagesWeights;

        var orderSubTotalPerPackage = orderSubTotal / totalPackages;
        var weightPerPackage = weight / totalPackages;

        request.RequestedShipment.TotalPackageCount = totalPackages;

        request.RequestedShipment.RequestedPackageLineItems = Enumerable.Range(1, totalPackages)
                .Select(_ => CreatePackage(width, length, height, weightPerPackage, orderSubTotalPerPackage, currencyCode))
                .ToArray();
    }

    /// <summary>
    /// Create packages (each shopping cart item is a separate package)
    /// </summary>
    /// <param name="request">Shipping request</param>
    /// <param name="getShippingOptionRequest">Shipping option request</param>
    /// <param name="currencyCode">Currency code</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    private async Task SetIndividualPackageLineItemsOneItemPerPackageAsync(Full_Schema_Quote_Rate request, GetShippingOptionRequest getShippingOptionRequest, string currencyCode)
    {
        // Rate request setup - each Shopping Cart Item is a separate package
        var i = 1;
        var items = getShippingOptionRequest.Items;
        var totalItems = items.Sum(x => x.GetQuantity());

        request.RequestedShipment.TotalPackageCount = totalItems;
        request.RequestedShipment.RequestedPackageLineItems = await getShippingOptionRequest.Items.SelectManyAwait<GetShippingOptionRequest.PackageItem, RequestedPackageLineItem>(async packageItem =>
        {
            //get dimensions and weight of the single item
            var (width, length, height) = await GetDimensionsForSingleItemAsync(packageItem.ShoppingCartItem);
            var weight = await GetWeightForSingleItemAsync(packageItem.ShoppingCartItem);

            var product = await _productService.GetProductByIdAsync(packageItem.ShoppingCartItem.ProductId);
            var package = CreatePackage(width, length, height, weight, product.Price, currencyCode);
            package.GroupPackageCount = 1;

            var packs = Enumerable.Range(i, packageItem.GetQuantity())
                .Select(_ => CreatePackage(width, length, height, weight, product.Price, currencyCode)).ToArray();
            i += packageItem.GetQuantity();

            return packs;
        }).ToArrayAsync();
    }

    private void SetPayment(Full_Schema_Quote_Rate request)
    {
        request.RequestedShipment.CustomsClearanceDetail ??= new RequestedShipmentCustomsClearanceDetail();

        request.RequestedShipment.CustomsClearanceDetail.DutiesPayment = new Payment
        {
            PaymentType = PaymentType.SENDER, // Payment options are RECIPIENT, SENDER, THIRD_PARTY
            Payor = new Payor
            {
                ResponsibleParty = new ResponsibleParty
                {
                    AccountNumber = new AccountNumber { Value = _fedexSettings.AccountNumber }
                }
            }
        };
    }

    private void SetShipmentDetails(Full_Schema_Quote_Rate request, decimal orderSubTotal, string currencyCode)
    {
        //saturday pickup is available for certain FedEx Express U.S. service types:
        //http://www.fedex.com/us/developer/product/WebServices/MyWebHelp/Services/Options/c_SaturdayShipAndDeliveryServiceDetails.html
        //if the customer orders on a Saturday, the rate calculation will use Saturday as the shipping date, and the rates will include a Saturday pickup surcharge
        //more info: https://www.nopcommerce.com/boards/t/27348/fedex-rate-can-be-excessive-for-express-methods-if-calculated-on-a-saturday.aspx
        var shipTimestamp = DateTime.Now;

        if (shipTimestamp.DayOfWeek == DayOfWeek.Saturday)
            shipTimestamp = shipTimestamp.AddDays(2);

        request.RequestedShipment.ShipDateStamp = shipTimestamp.ToString("yyyy-MM-dd"); // Shipping date and time

        var isInternational = !request.RequestedShipment.Shipper.Address.CountryCode.Equals(request.RequestedShipment.Recipient.Address.CountryCode, StringComparison.InvariantCultureIgnoreCase);
        var isShipmentToIndia = request.RequestedShipment.Shipper.Address.CountryCode.Equals("IN", StringComparison.InvariantCultureIgnoreCase) && request.RequestedShipment.Recipient.Address.CountryCode.Equals("IN", StringComparison.InvariantCultureIgnoreCase);
        
        if (!isInternational && !isShipmentToIndia) 
            return;

        //for international shipments and shipments to India, FedEx requires customs commodity details.
        var commodityPieces = request.RequestedShipment.TotalPackageCount > 0 ? request.RequestedShipment.TotalPackageCount : 1;

        var commodity = new Commodity
        {
            Name = "1",
            CountryOfManufacture = request.RequestedShipment.Shipper.Address.CountryCode,
            NumberOfPieces = commodityPieces,
            QuantityUnits = "PCS",
            Quantity = commodityPieces,
            CustomsValue = new Money
            {
                Amount = (double)orderSubTotal,
                Currency = currencyCode
            }
        };

        request.RequestedShipment.CustomsClearanceDetail ??= new RequestedShipmentCustomsClearanceDetail();

        request.RequestedShipment.CustomsClearanceDetail.CommercialInvoice = new CommercialInvoice
        {
            ShipmentPurpose = CommercialInvoiceShipmentPurpose.SOLD
        };

        request.RequestedShipment.CustomsClearanceDetail.Commodities = [ commodity ];
    }

    private static bool IsPackageTooHeavy(decimal weight)
    {
        return weight > FedexShippingDefaults.MAX_PACKAGE_WEIGHT;
    }

    private static bool IsPackageTooLarge(decimal length, decimal height, decimal width)
    {
        return TotalPackageSize(length, height, width) > 165;
    }

    private static bool IncludeStateProvinceCode(string countryCode)
    {
        return (countryCode.Equals("US", StringComparison.InvariantCultureIgnoreCase) ||
                countryCode.Equals("CA", StringComparison.InvariantCultureIgnoreCase));
    }

    private static void SetOrigin(Full_Schema_Quote_Rate request, GetShippingOptionRequest getShippingOptionRequest)
    {
        request.RequestedShipment.Shipper = new RateParty
        {
            Address = new RateAddress()
        };

        if (getShippingOptionRequest.CountryFrom is null)
            throw new Exception("FROM country is not specified");

        request.RequestedShipment.Shipper.Address.City = getShippingOptionRequest.CityFrom;
        if (IncludeStateProvinceCode(getShippingOptionRequest.CountryFrom.TwoLetterIsoCode))
        {
            var stateProvinceAbbreviation = getShippingOptionRequest.StateProvinceFrom?.Abbreviation ?? "";
            request.RequestedShipment.Shipper.Address.StateOrProvinceCode = stateProvinceAbbreviation;
        }
        request.RequestedShipment.Shipper.Address.PostalCode = getShippingOptionRequest.ZipPostalCodeFrom;
        request.RequestedShipment.Shipper.Address.CountryCode = getShippingOptionRequest.CountryFrom.TwoLetterIsoCode;
    }

    private static decimal TotalPackageSize(decimal length, decimal height, decimal width)
    {
        return height * 2 + width * 2 + length;
    }

    #endregion

    #region Methods

    /// <summary>
    /// FedEx services string names
    /// </summary>
    public static IList<string> GetAllFedExServicesName()
    {
        return _fedexServices.Values.ToList();
    }

    /// <summary>
    /// Gets the text name based on the ServiceID (in FedEx Reply)
    /// </summary>
    /// <param name="serviceId">ID of the carrier service -from FedEx</param>
    /// <returns>String representation of the carrier service</returns>
    public static string GetFedExServiceName(string serviceId)
    {
        return !_fedexServices.ContainsKey(serviceId) ? "UNKNOWN" : _fedexServices[serviceId];
    }

    /// <summary>
    /// Gets the ServiceId based on the text name
    /// </summary>
    /// <param name="serviceName">Name of the carrier service (based on the text name returned from GetServiceName())</param>
    /// <returns>Service ID as used by FedEx</returns>
    public static string GetFedExServiceId(string serviceName)
    {
        var rez = _fedexServices.FirstOrDefault(p =>
            p.Value.Equals(serviceName, StringComparison.InvariantCultureIgnoreCase));

        return string.IsNullOrEmpty(rez.Key) ? "UNKNOWN" : rez.Key;
    }

    /// <summary>
    /// Gets all events for a tracking number
    /// </summary>
    /// <param name="trackingNumber">The tracking number to track</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the shipment events
    /// </returns>
    public virtual async Task<IList<ShipmentStatusEvent>> GetShipmentEventsAsync(string trackingNumber)
    {
        try
        {
            //this is the call to the web service passing in a TrackRequest and returning a TrackReply
            var reply = await CreateTrackRequestAsync(trackingNumber);

            if (reply.Alerts?.Any(p => p.AlertType != API.Track.AlertType.NOTE) ?? false)
                throw new NopException(reply.Alerts.First(p => p.AlertType == API.Track.AlertType.WARNING).Message);

            return reply.CompleteTrackResults?
                    .SelectMany(completedTrackDetails => completedTrackDetails.TrackResults?
                            .Select(trackEvent => new ShipmentStatusEvent
                            {
                                EventName = $"{trackEvent.ReasonDetail.Description} ({trackEvent.ReasonDetail.Type})",
                                Location = trackEvent.LastUpdatedDestinationAddress.City,
                                CountryCode = trackEvent.LastUpdatedDestinationAddress.CountryCode,
                                Date = trackEvent.DateAndTimes?.Select(dt => DateTime.Parse(dt.DateTime, CultureInfo.InvariantCulture)).FirstOrDefault()
                            }))
                    .ToList();
        }
        catch (Exception exception)
        {
            //log errors
            await _logger.ErrorAsync($"Error while getting Fedex shipment tracking info - {trackingNumber}{Environment.NewLine}{exception.Message}", exception, await _workContext.GetCurrentCustomerAsync());
        }

        return new List<ShipmentStatusEvent>();
    }

    /// <summary>
    /// Gets shipping rates
    /// </summary>
    /// <param name="shippingOptionRequest">Shipping option request details</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the response of getting shipping rate options
    /// </returns>
    public virtual async Task<GetShippingOptionResponse> GetRatesAsync(GetShippingOptionRequest shippingOptionRequest)
    {
        var response = new GetShippingOptionResponse();

        //create request details
        var (request, requestedShipmentCurrency) = await CreateRateRequestAsync(shippingOptionRequest);

        var clientHandler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
        var httpClient = new HttpClient(clientHandler);

        var client = new RateClient(httpClient, _fedexSettings, await GetAccessTokenAsync());

        try
        {
            //try to get response details

            var reply = await client.ProcessRateAsync(request, await GetAccessTokenAsync());

            if (reply.Alerts?.Any() ?? false)
                throw new NopException(reply.Alerts.First().Message);

            if (reply.RateReplyDetails == null)
                return response;

            var shippingOptions = await ParseResponseAsync(reply, requestedShipmentCurrency);

            foreach (var shippingOption in shippingOptions)
                response.ShippingOptions.Add(shippingOption);

            return response;
        }
        catch (API.Rates.ApiException<ErrorResponseVO> apiException)
        {
            var errorMessage = apiException.Message;

            if (apiException.StatusCode == 400)
            {
                var errors = apiException.Result.Errors;

                if (errors?.Any() ?? false) 
                    errorMessage += $"\r\nFedEx Error Details: {string.Join(", ", errors.Select(e => e.Message))}";
            }

            Debug.WriteLine(errorMessage);
            response.AddError(errorMessage);
            return response;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.Message);
            response.AddError(e.Message);
            return response;
        }
    }

    #endregion
}
