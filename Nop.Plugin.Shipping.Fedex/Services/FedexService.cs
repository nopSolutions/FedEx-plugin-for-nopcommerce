using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FedexRate;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Shipping.Fedex.Domain;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Tracking;

namespace Nop.Plugin.Shipping.Fedex.Services
{
    public class FedexService
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly FedexSettings _fedexSettings;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly ILogger _logger;
        private readonly IMeasureService _measureService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IProductService _productService;
        private readonly IShippingService _shippingService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public FedexService(CurrencySettings currencySettings,
            FedexSettings fedexSettings,
            ICountryService countryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
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

        private async Task<decimal> ConvertChargeToPrimaryCurrencyAsync(Money charge, Currency requestedShipmentCurrency)
        {
            decimal amount;
            var primaryStoreCurrency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

            if (primaryStoreCurrency.CurrencyCode.Equals(charge.Currency, StringComparison.InvariantCultureIgnoreCase))
                amount = charge.Amount;
            else
            {
                var amountCurrency = charge.Currency == requestedShipmentCurrency.CurrencyCode ? requestedShipmentCurrency : await _currencyService.GetCurrencyByCodeAsync(charge.Currency);

                //ensure the the currency exists; otherwise, presume that it was primary store currency
                amountCurrency ??= primaryStoreCurrency;

                amount = await _currencyService.ConvertToPrimaryStoreCurrencyAsync(charge.Amount, amountCurrency);

                Debug.WriteLine($"ConvertChargeToPrimaryCurrency - from {charge.Amount} ({charge.Currency}) to {amount} ({primaryStoreCurrency.CurrencyCode})");
            }

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
        private async Task<(decimal width, decimal length, decimal height)> GetDimensionsAsync(IList<GetShippingOptionRequest.PackageItem> items, int minRate = 1)
        {
            var measureDimension = await _measureService.GetMeasureDimensionBySystemKeywordAsync(FedexShippingDefaults.MEASURE_DIMENSION_SYSTEM_KEYWORD) ??
                throw new NopException($"FedEx shipping service. Could not load \"{FedexShippingDefaults.MEASURE_DIMENSION_SYSTEM_KEYWORD}\" measure dimension");

            var (width,  length,  height) = await _shippingService.GetDimensionsAsync(items, true);
            width = await convertAndRoundDimensionAsync(width);
            length = await convertAndRoundDimensionAsync(length);
            height = await convertAndRoundDimensionAsync(height);

            return (width, length, height);

            #region Local functions

            async Task<decimal> convertAndRoundDimensionAsync(decimal dimension)
            {
                dimension = await _measureService.ConvertFromPrimaryMeasureDimensionAsync(dimension, measureDimension);
                dimension = Convert.ToInt32(Math.Ceiling(dimension));
                return Math.Max(dimension, minRate);
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
        private async Task<(decimal width, decimal length, decimal height)> GetDimensionsForSingleItemAsync(ShoppingCartItem item)
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
        /// Create request details to track shipment
        /// </summary>
        /// <param name="trackingNumber">Tracking number</param>
        /// <returns>Track request details</returns>
        private FedexTracking.TrackRequest CreateTrackRequest(string trackingNumber)
        {
            return new FedexTracking.TrackRequest
            {
                WebAuthenticationDetail = new FedexTracking.WebAuthenticationDetail
                {
                    UserCredential = new FedexTracking.WebAuthenticationCredential
                    {
                        Key = _fedexSettings.Key, // Replace "XXX" with the Key
                        Password = _fedexSettings.Password // Replace "XXX" with the Password
                    }
                },
                ClientDetail = new FedexTracking.ClientDetail
                {
                    AccountNumber = _fedexSettings.AccountNumber, // Replace "XXX" with client's account number
                    MeterNumber = _fedexSettings.MeterNumber // Replace "XXX" with client's meter number
                },
                TransactionDetail = new FedexTracking.TransactionDetail
                {
                    CustomerTransactionId = "***nopCommerce v16 Request using VC#***"
                },
                //creates the Version element with all child elements populated from the wsdl
                Version = new FedexTracking.VersionId(),
                //tracking information
                SelectionDetails = new[]
                    {
                        new FedexTracking.TrackSelectionDetail
                        {
                            PackageIdentifier = new FedexTracking.TrackPackageIdentifier
                            {
                                Value = trackingNumber,
                                Type = FedexTracking.TrackIdentifierType.TRACKING_NUMBER_OR_DOORTAG
                            }
                        }
                    }
            };
        }

        /// <summary>
        /// Create package details
        /// </summary>
        /// <param name="width">Width</param>
        /// <param name="length">Length</param>
        /// <param name="height">Height</param>
        /// <param name="weight">Weight</param>
        /// <param name="orderSubTotal"></param>
        /// <param name="sequenceNumber">Number</param>
        /// <param name="currencyCode">Currency code</param>
        /// <returns>Package details</returns>
        private RequestedPackageLineItem CreatePackage(decimal width, decimal length, decimal height, decimal weight, decimal orderSubTotal, string sequenceNumber, string currencyCode)
        {
            return new RequestedPackageLineItem
            {
                SequenceNumber = sequenceNumber, // package sequence number            
                GroupPackageCount = "1",
                Weight = new Weight
                {
                    Units = WeightUnits.LB,
                    UnitsSpecified = true,
                    Value = weight,
                    ValueSpecified = true
                }, // package weight

                Dimensions = new Dimensions
                {
                    Length = _fedexSettings.PassDimensions ? length.ToString() : "0",
                    Width = _fedexSettings.PassDimensions ? width.ToString() : "0",
                    Height = _fedexSettings.PassDimensions ? height.ToString() : "0",
                    Units = LinearUnits.IN,
                    UnitsSpecified = true
                }, // package dimensions
                InsuredValue = new Money
                {
                    Amount = orderSubTotal,
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
        private async Task<(RateRequest rateRequest, Currency requestedShipmentCurrency)> CreateRateRequestAsync(GetShippingOptionRequest shippingOptionRequest)
        {
            // Build the RateRequest
            var request = new RateRequest
            {
                WebAuthenticationDetail = new WebAuthenticationDetail
                {
                    UserCredential = new WebAuthenticationCredential
                    {
                        Key = _fedexSettings.Key,
                        Password = _fedexSettings.Password
                    }
                },

                ClientDetail = new ClientDetail
                {
                    AccountNumber = _fedexSettings.AccountNumber,
                    MeterNumber = _fedexSettings.MeterNumber
                },

                TransactionDetail = new TransactionDetail
                {
                    CustomerTransactionId = "***Rate Available Services v16 Request - nopCommerce***" // This is a reference field for the customer.  Any value can be used and will be provided in the response.
                },

                Version = new VersionId(), // WSDL version information, value is automatically set from wsdl            

                ReturnTransitAndCommit = true,
                ReturnTransitAndCommitSpecified = true,
                // Insert the Carriers you would like to see the rates for
                CarrierCodes = new[] {
                    CarrierCodeType.FDXE,
                    CarrierCodeType.FDXG,
                    CarrierCodeType.FXSP
                }
            };

            //TODO we should use getShippingOptionRequest.Items.GetQuantity() method to get subtotal
            var ( _,  _, _, subTotalWithDiscountBase, _) = await _orderTotalCalculationService.GetShoppingCartSubTotalAsync(
                shippingOptionRequest.Items.Select(x => x.ShoppingCartItem).ToList(),
                false);

            request.RequestedShipment = new RequestedShipment();
			
			if (_fedexSettings.CarrierServicesOffered.Contains("SMART_POST")){
                request.RequestedShipment.SmartPostDetail = new SmartPostShipmentDetail()
                {
                    IndiciaSpecified = true,
                    Indicia = SmartPostIndiciaType.PARCEL_SELECT
                };
            }

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

            SetShipmentDetails(request, subTotalShipmentCurrency, requestedShipmentCurrency.CurrencyCode);
            SetPayment(request);

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

        private bool IsPackageTooHeavy(decimal weight)
        {
            return weight > FedexShippingDefaults.MAX_PACKAGE_WEIGHT;
        }

        private bool IsPackageTooLarge(decimal length, decimal height, decimal width)
        {
            return TotalPackageSize(length, height, width) > 165;
        }

        private bool IncludeStateProvinceCode(string countryCode)
        {
            return (countryCode.Equals("US", StringComparison.InvariantCultureIgnoreCase) ||
                    countryCode.Equals("CA", StringComparison.InvariantCultureIgnoreCase));
        }

        private async Task<IList<ShippingOption>> ParseResponseAsync(RateReply reply, Currency requestedShipmentCurrency)
        {
            var result = new List<ShippingOption>();

            Debug.WriteLine("RateReply details:");
            Debug.WriteLine("**********************************************************");
            foreach (var rateDetail in reply.RateReplyDetails)
            {
                var shippingOption = new ShippingOption();
                var serviceName = FedexServices.GetServiceName(rateDetail.ServiceType.ToString());

                // Skip the current service if services are selected and this service hasn't been selected
                if (!string.IsNullOrEmpty(_fedexSettings.CarrierServicesOffered) && !_fedexSettings.CarrierServicesOffered.Contains(rateDetail.ServiceType.ToString())) 
                    continue;

                Debug.WriteLine("ServiceType: " + rateDetail.ServiceType);
                if (!serviceName.Equals("UNKNOWN"))
                {
                    shippingOption.Name = serviceName;

                    foreach (var shipmentDetail in rateDetail.RatedShipmentDetails)
                    {
                        Debug.WriteLine("RateType : " + shipmentDetail.ShipmentRateDetail.RateType);
                        Debug.WriteLine("Total Billing Weight : " + shipmentDetail.ShipmentRateDetail.TotalBillingWeight.Value);
                        Debug.WriteLine("Total Base Charge : " + shipmentDetail.ShipmentRateDetail.TotalBaseCharge.Amount);
                        Debug.WriteLine("Total Discount : " + shipmentDetail.ShipmentRateDetail.TotalFreightDiscounts.Amount);
                        Debug.WriteLine("Total Surcharges : " + shipmentDetail.ShipmentRateDetail.TotalSurcharges.Amount);
                        Debug.WriteLine($"Net Charge : {shipmentDetail.ShipmentRateDetail.TotalNetCharge.Amount} ({shipmentDetail.ShipmentRateDetail.TotalNetCharge.Currency})");
                        Debug.WriteLine("*********");

                        // Get discounted rates if option is selected
                        if (_fedexSettings.ApplyDiscounts &
                            (shipmentDetail.ShipmentRateDetail.RateType == ReturnedRateType.PAYOR_ACCOUNT_PACKAGE ||
                            shipmentDetail.ShipmentRateDetail.RateType == ReturnedRateType.PAYOR_ACCOUNT_SHIPMENT))
                        {
                            var amount = await ConvertChargeToPrimaryCurrencyAsync(shipmentDetail.ShipmentRateDetail.TotalNetCharge, requestedShipmentCurrency);
                            shippingOption.Rate = amount + _fedexSettings.AdditionalHandlingCharge;
                            break;
                        }

                        if (shipmentDetail.ShipmentRateDetail.RateType == ReturnedRateType.PAYOR_LIST_PACKAGE ||
                            shipmentDetail.ShipmentRateDetail.RateType == ReturnedRateType.PAYOR_LIST_SHIPMENT) // Get List Rates (not discount rates)
                        {
                            var amount = await ConvertChargeToPrimaryCurrencyAsync(shipmentDetail.ShipmentRateDetail.TotalNetCharge, requestedShipmentCurrency);
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

        private async Task SetDestinationAsync(RateRequest request, GetShippingOptionRequest getShippingOptionRequest)
        {
            request.RequestedShipment.Recipient = new Party
            {
                Address = new Address()
            };

            if (_fedexSettings.UseResidentialRates)
            {
                request.RequestedShipment.Recipient.Address.Residential = true;
                request.RequestedShipment.Recipient.Address.ResidentialSpecified = true;
            }

            request.RequestedShipment.Recipient.Address.StreetLines = new[] { getShippingOptionRequest.ShippingAddress.Address1 };
            request.RequestedShipment.Recipient.Address.City = getShippingOptionRequest.ShippingAddress.City;

            var recipientCountryCode = (await _countryService.GetCountryByAddressAsync(getShippingOptionRequest.ShippingAddress))?.TwoLetterIsoCode ?? string.Empty;

            if (await _stateProvinceService.GetStateProvinceByAddressAsync(getShippingOptionRequest.ShippingAddress) is StateProvince stateProvince &&
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
        private async Task SetIndividualPackageLineItemsAsync(RateRequest request, GetShippingOptionRequest getShippingOptionRequest, decimal orderSubTotal, string currencyCode)
        {
            var (length, height, width) = await GetDimensionsAsync(getShippingOptionRequest.Items);
            var weight = await GetWeightAsync(getShippingOptionRequest);

            if (!IsPackageTooHeavy(weight) && !IsPackageTooLarge(length, height, width))
            {
                request.RequestedShipment.PackageCount = "1";

                var package = CreatePackage(width, length, height, weight, orderSubTotal, "1", currencyCode);
                package.GroupPackageCount = "1";

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

                request.RequestedShipment.PackageCount = totalPackages.ToString();

                request.RequestedShipment.RequestedPackageLineItems = Enumerable.Range(1, totalPackages - 1)
                    .Select(i => CreatePackage(width, length, height, weight, orderSubTotal2, i.ToString(), currencyCode)).ToArray();
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
        private async Task SetIndividualPackageLineItemsCubicRootDimensionsAsync(RateRequest request, GetShippingOptionRequest getShippingOptionRequest, decimal orderSubTotal, string currencyCode)
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
            decimal length;
            decimal height;
            decimal width;

            if (getShippingOptionRequest.Items.Count == 1 && getShippingOptionRequest.Items[0].GetQuantity() == 1)
            {
                var sci = getShippingOptionRequest.Items[0].ShoppingCartItem;

                //get dimensions and weight of the single cubic size of package
                var item = getShippingOptionRequest.Items.FirstOrDefault().ShoppingCartItem;
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

            request.RequestedShipment.PackageCount = totalPackages.ToString();

            request.RequestedShipment.RequestedPackageLineItems = Enumerable.Range(1, totalPackages)
                    .Select(i => CreatePackage(width, length, height, weightPerPackage, orderSubTotalPerPackage, i.ToString(), currencyCode))
                    .ToArray();
        }

        /// <summary>
        /// Create packages (each shopping cart item is a separate package)
        /// </summary>
        /// <param name="request">Shipping request</param>
        /// <param name="getShippingOptionRequest">Shipping option request</param>
        /// <param name="currencyCode">Currency code</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        private async Task SetIndividualPackageLineItemsOneItemPerPackageAsync(RateRequest request, GetShippingOptionRequest getShippingOptionRequest, string currencyCode)
        {
            // Rate request setup - each Shopping Cart Item is a separate package
            var i = 1;
            var items = getShippingOptionRequest.Items;
            var totalItems = items.Sum(x => x.GetQuantity());

            request.RequestedShipment.PackageCount = totalItems.ToString();
            request.RequestedShipment.RequestedPackageLineItems = await getShippingOptionRequest.Items.SelectManyAwait<GetShippingOptionRequest.PackageItem, RequestedPackageLineItem>(async packageItem =>
            {
                //get dimensions and weight of the single item
                var (width, length, height) = await GetDimensionsForSingleItemAsync(packageItem.ShoppingCartItem);
                var weight = await GetWeightForSingleItemAsync(packageItem.ShoppingCartItem);

                var product = await _productService.GetProductByIdAsync(packageItem.ShoppingCartItem.ProductId);
                var package = CreatePackage(width, length, height, weight, product.Price, (i + 1).ToString(), currencyCode);
                package.GroupPackageCount = "1";

                var packs = Enumerable.Range(i, packageItem.GetQuantity())
                    .Select(j => CreatePackage(width, length, height, weight, product.Price, j.ToString(), currencyCode)).ToArray();
                i += packageItem.GetQuantity();

                return packs;
            }).ToArrayAsync();
        }

        private void SetOrigin(RateRequest request, GetShippingOptionRequest getShippingOptionRequest)
        {
            request.RequestedShipment.Shipper = new Party
            {
                Address = new Address()
            };

            if (getShippingOptionRequest.CountryFrom is null)
                throw new Exception("FROM country is not specified");

            request.RequestedShipment.Shipper.Address.StreetLines = new[] { getShippingOptionRequest.AddressFrom };
            request.RequestedShipment.Shipper.Address.City = getShippingOptionRequest.CityFrom;
            if (IncludeStateProvinceCode(getShippingOptionRequest.CountryFrom.TwoLetterIsoCode))
            {
                var stateProvinceAbbreviation = getShippingOptionRequest.StateProvinceFrom?.Abbreviation ?? "";
                request.RequestedShipment.Shipper.Address.StateOrProvinceCode = stateProvinceAbbreviation;
            }
            request.RequestedShipment.Shipper.Address.PostalCode = getShippingOptionRequest.ZipPostalCodeFrom;
            request.RequestedShipment.Shipper.Address.CountryCode = getShippingOptionRequest.CountryFrom.TwoLetterIsoCode;
        }

        private void SetPayment(RateRequest request)
        {
            request.RequestedShipment.ShippingChargesPayment = new Payment
            {
                PaymentType = PaymentType.SENDER, // Payment options are RECIPIENT, SENDER, THIRD_PARTY
                PaymentTypeSpecified = true,
                Payor = new Payor
                {
                    ResponsibleParty = new Party
                    {
                        AccountNumber = _fedexSettings.AccountNumber
                    }
                }
            }; // Payment Information
        }

        private void SetShipmentDetails(RateRequest request, decimal orderSubTotal, string currencyCode)
        {
            //set drop off type
            request.RequestedShipment.DropoffType = _fedexSettings.DropoffType switch
            {
                DropoffType.BusinessServiceCenter => FedexRate.DropoffType.BUSINESS_SERVICE_CENTER,
                DropoffType.DropBox => FedexRate.DropoffType.DROP_BOX,
                DropoffType.RegularPickup => FedexRate.DropoffType.REGULAR_PICKUP,
                DropoffType.RequestCourier => FedexRate.DropoffType.REQUEST_COURIER,
                DropoffType.Station => FedexRate.DropoffType.STATION,
                _ => FedexRate.DropoffType.BUSINESS_SERVICE_CENTER
            };

            request.RequestedShipment.TotalInsuredValue = new Money
            {
                Amount = orderSubTotal,
                Currency = currencyCode
            };

            //Saturday pickup is available for certain FedEx Express U.S. service types:
            //http://www.fedex.com/us/developer/product/WebServices/MyWebHelp/Services/Options/c_SaturdayShipAndDeliveryServiceDetails.html
            //If the customer orders on a Saturday, the rate calculation will use Saturday as the shipping date, and the rates will include a Saturday pickup surcharge
            //More info: https://www.nopcommerce.com/boards/t/27348/fedex-rate-can-be-excessive-for-express-methods-if-calculated-on-a-saturday.aspx
            var shipTimestamp = DateTime.Now;
            if (shipTimestamp.DayOfWeek == DayOfWeek.Saturday)
                shipTimestamp = shipTimestamp.AddDays(2);
            request.RequestedShipment.ShipTimestamp = shipTimestamp; // Shipping date and time
            request.RequestedShipment.ShipTimestampSpecified = true;

            request.RequestedShipment.RateRequestTypes = new[] {
                RateRequestType.PREFERRED,
                RateRequestType.LIST
            };
            //request.RequestedShipment.PackageDetail = RequestedPackageDetailType.INDIVIDUAL_PACKAGES;
            //request.RequestedShipment.PackageDetailSpecified = true;

            //for India domestic shipping add additional details
            if (request.RequestedShipment.Shipper.Address.CountryCode.Equals("IN", StringComparison.InvariantCultureIgnoreCase) &&
                request.RequestedShipment.Recipient.Address.CountryCode.Equals("IN", StringComparison.InvariantCultureIgnoreCase))
            {
                var commodity = new Commodity
                {
                    Name = "1",
                    NumberOfPieces = "1",
                    CustomsValue = new Money
                    {
                        Amount = orderSubTotal,
                        AmountSpecified = true,
                        Currency = currencyCode
                    }
                };

                request.RequestedShipment.CustomsClearanceDetail = new CustomsClearanceDetail
                {
                    CommercialInvoice = new CommercialInvoice
                    {
                        Purpose = PurposeOfShipmentType.SOLD,
                        PurposeSpecified = true
                    },
                    Commodities = new[] { commodity }
                };
            }
        }

        private decimal TotalPackageSize(decimal length, decimal height, decimal width)
        {
            return height * 2 + width * 2 + length;
        }

        /// <summary>
        /// Get tracking info
        /// </summary>
        /// <param name="request">Request details</param>
        /// <returns>The asynchronous task whose result contains the tracking info</returns>
        private async Task<FedexTracking.TrackReply> TrackAsync(FedexTracking.TrackRequest request)
        {
            //initialize the service
            using var service = new FedexTracking.TrackPortTypeClient(FedexTracking.TrackPortTypeClient.EndpointConfiguration.TrackServicePort, _fedexSettings.Url);
            var trackResponse = await service.trackAsync(request);

            return trackResponse.TrackReply;
        }

        #endregion

        #region Methods

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
                //build the TrackRequest
                var request = CreateTrackRequest(trackingNumber);

                //this is the call to the web service passing in a TrackRequest and returning a TrackReply
                var reply = await TrackAsync(request);

                //parse response
                if (new[] { FedexTracking.NotificationSeverityType.SUCCESS, FedexTracking.NotificationSeverityType.NOTE, FedexTracking.NotificationSeverityType.WARNING }.Contains(reply.HighestSeverity)) // check if the call was successful
                    return reply.CompletedTrackDetails?
                        .SelectMany(completedTrackDetails => completedTrackDetails.TrackDetails?
                            .SelectMany(trackDetails => trackDetails.Events?.Where(trackEvent=> trackEvent != null)
                                .Select(trackEvent => new ShipmentStatusEvent
                                {
                                    EventName = $"{trackEvent.EventDescription} ({trackEvent.EventType})",
                                    Location = trackEvent.Address?.City,
                                    CountryCode = trackEvent.Address?.CountryCode,
                                    Date = trackEvent.TimestampSpecified ? trackEvent.Timestamp as DateTime? : null
                                })))
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

            var (request, requestedShipmentCurrency) = await CreateRateRequestAsync(shippingOptionRequest);

            var service = new RatePortTypeClient(RatePortTypeClient.EndpointConfiguration.RateServicePort, _fedexSettings.Url);

            try
            {
                // This is the call to the web service passing in a RateRequest and returning a RateReply
                var rateResult = await service.getRatesAsync(request);
                var reply = rateResult.RateReply;

                if (new[] { NotificationSeverityType.SUCCESS, NotificationSeverityType.NOTE, NotificationSeverityType.WARNING }.Contains(reply.HighestSeverity)) // check if the call was successful
                {
                    if (reply.RateReplyDetails != null)
                    {
                        var shippingOptions = await ParseResponseAsync(reply, requestedShipmentCurrency);
                        foreach (var shippingOption in shippingOptions)
                            response.ShippingOptions.Add(shippingOption);
                    }
                    else
                    {
                        if (reply.Notifications?.Length > 0 && !string.IsNullOrEmpty(reply.Notifications[0].Message))
                            response.AddError($"{reply.Notifications[0].Message} (code: {reply.Notifications[0].Code})");
                        else
                            response.AddError("Could not get reply from shipping server");
                    }
                }
                else
                {
                    Debug.WriteLine(reply.Notifications[0].Message);
                    response.AddError(reply.Notifications[0].Message);
                }

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
}
