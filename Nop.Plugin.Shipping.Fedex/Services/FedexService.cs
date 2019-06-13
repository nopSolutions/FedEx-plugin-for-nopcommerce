using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Shipping.Fedex.Domain;
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
        private readonly ICurrencyService _currencyService;
        private readonly ILogger _logger;
        private readonly IMeasureService _measureService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IShippingService _shippingService;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public FedexService(CurrencySettings currencySettings,
            FedexSettings fedexSettings,
            ICurrencyService currencyService,
            ILogger logger,
            IMeasureService measureService,
            IOrderTotalCalculationService orderTotalCalculationService,
            IShippingService shippingService,
            IWorkContext workContext)
        {
            _currencySettings = currencySettings;
            _fedexSettings = fedexSettings;
            _currencyService = currencyService;
            _logger = logger;
            _measureService = measureService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _shippingService = shippingService;
            _workContext = workContext;
        }

        #endregion

        #region Utilities

        private decimal ConvertChargeToPrimaryCurrency(FedexRate.Money charge, Currency requestedShipmentCurrency)
        {
            decimal amount;
            var primaryStoreCurrency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId);

            if (primaryStoreCurrency.CurrencyCode.Equals(charge.Currency, StringComparison.InvariantCultureIgnoreCase))
            {
                amount = charge.Amount;
            }
            else
            {
                var amountCurrency = charge.Currency == requestedShipmentCurrency.CurrencyCode ? requestedShipmentCurrency : _currencyService.GetCurrencyByCode(charge.Currency);

                //ensure the the currency exists; otherwise, presume that it was primary store currency
                if (amountCurrency is null)
                    amountCurrency = primaryStoreCurrency;

                amount = _currencyService.ConvertToPrimaryStoreCurrency(charge.Amount, amountCurrency);

                Debug.WriteLine($"ConvertChargeToPrimaryCurrency - from {charge.Amount} ({charge.Currency}) to {amount} ({primaryStoreCurrency.CurrencyCode})");
            }

            return amount;
        }

        private int ConvertFromPrimaryMeasureDimension(decimal quantity, MeasureDimension usedMeasureDimension, int minReturnQuantity = 0)
        {
            var original = Convert.ToInt32(Math.Ceiling(_measureService.ConvertFromPrimaryMeasureDimension(quantity, usedMeasureDimension)));
            return original > minReturnQuantity ? original : minReturnQuantity;
        }

        private int ConvertFromPrimaryMeasureWeight(decimal quantity, MeasureWeight usedMeasureWeight, int minReturnQuantity = 0)
        {
            var original = Convert.ToInt32(Math.Ceiling(_measureService.ConvertFromPrimaryMeasureWeight(quantity, usedMeasureWeight)));
            return original > minReturnQuantity ? original : minReturnQuantity;
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
                //
                WebAuthenticationDetail = new FedexTracking.WebAuthenticationDetail
                {
                    UserCredential = new FedexTracking.WebAuthenticationCredential
                    {
                        Key = _fedexSettings.Key, // Replace "XXX" with the Key
                        Password = _fedexSettings.Password // Replace "XXX" with the Password
                    }
                },
                //
                ClientDetail = new FedexTracking.ClientDetail
                {
                    AccountNumber = _fedexSettings.AccountNumber, // Replace "XXX" with client's account number
                    MeterNumber = _fedexSettings.MeterNumber // Replace "XXX" with client's meter number
                },
                //
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
        /// Create request details to get shipping rates
        /// </summary>
        /// <param name="shippingOptionRequest">Shipping option request</param>
        /// <param name="saturdayDelivery">Whether to get rates for Saturday Delivery</param>
        /// <returns>Rate request details</returns>
        private FedexRate.RateRequest CreateRateRequest(GetShippingOptionRequest shippingOptionRequest, out Currency requestedShipmentCurrency)
        {
            // Build the RateRequest
            var request = new FedexRate.RateRequest
            {
                WebAuthenticationDetail = new FedexRate.WebAuthenticationDetail
                {
                    UserCredential = new FedexRate.WebAuthenticationCredential
                    {
                        Key = _fedexSettings.Key,
                        Password = _fedexSettings.Password
                    }
                },

                ClientDetail = new FedexRate.ClientDetail
                {
                    AccountNumber = _fedexSettings.AccountNumber,
                    MeterNumber = _fedexSettings.MeterNumber
                },

                TransactionDetail = new FedexRate.TransactionDetail
                {
                    CustomerTransactionId = "***Rate Available Services v16 Request - nopCommerce***" // This is a reference field for the customer.  Any value can be used and will be provided in the response.
                },

                Version = new FedexRate.VersionId(), // WSDL version information, value is automatically set from wsdl            

                ReturnTransitAndCommit = true,
                ReturnTransitAndCommitSpecified = true,
                // Insert the Carriers you would like to see the rates for
                CarrierCodes = new[] {
                    FedexRate.CarrierCodeType.FDXE,
                    FedexRate.CarrierCodeType.FDXG
                }
            };

            //TODO we should use getShippingOptionRequest.Items.GetQuantity() method to get subtotal
            _orderTotalCalculationService.GetShoppingCartSubTotal(
                shippingOptionRequest.Items.Select(x => x.ShoppingCartItem).ToList(),
                false, out var _, out var _, out var _, out var subTotalWithDiscountBase);

            request.RequestedShipment = new FedexRate.RequestedShipment();

            SetOrigin(request, shippingOptionRequest);
            SetDestination(request, shippingOptionRequest);

            requestedShipmentCurrency = GetRequestedShipmentCurrency(
                request.RequestedShipment.Shipper.Address.CountryCode,    // origin
                request.RequestedShipment.Recipient.Address.CountryCode); // destination

            decimal subTotalShipmentCurrency;
            var primaryStoreCurrency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId);

            if (requestedShipmentCurrency.CurrencyCode == primaryStoreCurrency.CurrencyCode)
                subTotalShipmentCurrency = subTotalWithDiscountBase;
            else
                subTotalShipmentCurrency = _currencyService.ConvertFromPrimaryStoreCurrency(subTotalWithDiscountBase, requestedShipmentCurrency);

            Debug.WriteLine($"SubTotal (Primary Currency) : {subTotalWithDiscountBase} ({primaryStoreCurrency.CurrencyCode})");
            Debug.WriteLine($"SubTotal (Shipment Currency): {subTotalShipmentCurrency} ({requestedShipmentCurrency.CurrencyCode})");

            SetShipmentDetails(request, subTotalShipmentCurrency, requestedShipmentCurrency.CurrencyCode);
            SetPayment(request);

            //set packages details
            switch (_fedexSettings.PackingType)
            {
                case PackingType.PackByOneItemPerPackage:
                    SetIndividualPackageLineItemsOneItemPerPackage(request, shippingOptionRequest, requestedShipmentCurrency.CurrencyCode);
                    break;
                case PackingType.PackByVolume:
                    SetIndividualPackageLineItemsCubicRootDimensions(request, shippingOptionRequest, subTotalShipmentCurrency, requestedShipmentCurrency.CurrencyCode);
                    break;
                case PackingType.PackByDimensions:
                default:
                    SetIndividualPackageLineItems(request, shippingOptionRequest, subTotalShipmentCurrency, requestedShipmentCurrency.CurrencyCode);
                    break;
            }
            return request;
        }

        private Currency GetRequestedShipmentCurrency(string originCountryCode, string destinCountryCode)
        {
            var primaryStoreCurrency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId);

            //The solution coded here might be considered a bit of a hack
            //it only supports the scenario for US / Canada / India shipping
            //because nopCommerce does not have a concept of a designated currency for a Country.
            string originCurrencyCode;
            if (originCountryCode == "US")
                originCurrencyCode = "USD";
            else if (originCountryCode == "CA")
                originCurrencyCode = "CAD";
            else if (originCountryCode == "IN")
                originCurrencyCode = "INR";
            else
                originCurrencyCode = primaryStoreCurrency.CurrencyCode;

            string destinCurrencyCode;
            if (destinCountryCode == "US")
                destinCurrencyCode = "USD";
            else if (destinCountryCode == "CA")
                destinCurrencyCode = "CAD";
            else if (destinCountryCode == "IN")
                destinCurrencyCode = "INR";
            else
                destinCurrencyCode = primaryStoreCurrency.CurrencyCode;

            //when neither the shipping origin's currency or the destinations currency is the same as the store primary currency,
            //FedEx would complain that "There are no valid services available. (code: 556)".
            if (originCurrencyCode == primaryStoreCurrency.CurrencyCode || destinCurrencyCode == primaryStoreCurrency.CurrencyCode)
            {
                return primaryStoreCurrency;
            }

            //ensure that this currency exists
            return _currencyService.GetCurrencyByCode(originCurrencyCode) ?? primaryStoreCurrency;
        }

        private MeasureDimension GetUsedMeasureDimension()
        {
            return _measureService.GetMeasureDimensionBySystemKeyword(FedexShippingDefaults.MEASURE_DIMENSION_SYSTEM_KEYWORD) ??
                 throw new NopException($"FedEx shipping service. Could not load \"{FedexShippingDefaults.MEASURE_DIMENSION_SYSTEM_KEYWORD}\" measure dimension");
        }

        private MeasureWeight GetUsedMeasureWeight()
        {
            return _measureService.GetMeasureWeightBySystemKeyword(FedexShippingDefaults.MEASURE_WEIGHT_SYSTEM_KEYWORD) ??
                throw new NopException($"FedEx shipping service. Could not load \"{FedexShippingDefaults.MEASURE_WEIGHT_SYSTEM_KEYWORD}\" measure weight");
        }

        private bool IsPackageTooHeavy(int weight)
        {
            return weight > FedexShippingDefaults.MAX_PACKAGE_WEIGHT;
        }

        private bool IsPackageTooLarge(int length, int height, int width)
        {
            return TotalPackageSize(length, height, width) > 165;
        }

        private bool IncludeStateProvinceCode(string countryCode)
        {
            return (countryCode.Equals("US", StringComparison.InvariantCultureIgnoreCase) ||
                    countryCode.Equals("CA", StringComparison.InvariantCultureIgnoreCase));
        }

        private IList<ShippingOption> ParseResponse(FedexRate.RateReply reply, Currency requestedShipmentCurrency)
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
                {
                    continue;
                }

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
                            (shipmentDetail.ShipmentRateDetail.RateType == FedexRate.ReturnedRateType.PAYOR_ACCOUNT_PACKAGE ||
                            shipmentDetail.ShipmentRateDetail.RateType == FedexRate.ReturnedRateType.PAYOR_ACCOUNT_SHIPMENT))
                        {
                            var amount = ConvertChargeToPrimaryCurrency(shipmentDetail.ShipmentRateDetail.TotalNetCharge, requestedShipmentCurrency);
                            shippingOption.Rate = amount + _fedexSettings.AdditionalHandlingCharge;
                            break;
                        }
                        else if (shipmentDetail.ShipmentRateDetail.RateType == FedexRate.ReturnedRateType.PAYOR_LIST_PACKAGE ||
                            shipmentDetail.ShipmentRateDetail.RateType == FedexRate.ReturnedRateType.PAYOR_LIST_SHIPMENT) // Get List Rates (not discount rates)
                        {
                            var amount = ConvertChargeToPrimaryCurrency(shipmentDetail.ShipmentRateDetail.TotalNetCharge, requestedShipmentCurrency);
                            shippingOption.Rate = amount + _fedexSettings.AdditionalHandlingCharge;
                            break;
                        }
                        else // Skip the rate (RATED_ACCOUNT, PAYOR_MULTIWEIGHT, or RATED_LIST)
                        {
                            continue;
                        }
                    }
                    result.Add(shippingOption);
                }
                Debug.WriteLine("**********************************************************");
            }
            return result;
        }

        private void SetDestination(FedexRate.RateRequest request, GetShippingOptionRequest getShippingOptionRequest)
        {
            request.RequestedShipment.Recipient = new FedexRate.Party
            {
                Address = new FedexRate.Address()
            };
            if (_fedexSettings.UseResidentialRates)
            {
                request.RequestedShipment.Recipient.Address.Residential = true;
                request.RequestedShipment.Recipient.Address.ResidentialSpecified = true;
            }

            request.RequestedShipment.Recipient.Address.StreetLines = new[] { getShippingOptionRequest.ShippingAddress.Address1 };
            request.RequestedShipment.Recipient.Address.City = getShippingOptionRequest.ShippingAddress.City;

            if (getShippingOptionRequest.ShippingAddress.StateProvince != null &&
                IncludeStateProvinceCode(getShippingOptionRequest.ShippingAddress.Country.TwoLetterIsoCode))
            {
                request.RequestedShipment.Recipient.Address.StateOrProvinceCode = getShippingOptionRequest.ShippingAddress.StateProvince.Abbreviation;
            }
            else
            {
                request.RequestedShipment.Recipient.Address.StateOrProvinceCode = string.Empty;
            }
            request.RequestedShipment.Recipient.Address.PostalCode = getShippingOptionRequest.ShippingAddress.ZipPostalCode;
            request.RequestedShipment.Recipient.Address.CountryCode = getShippingOptionRequest.ShippingAddress.Country.TwoLetterIsoCode;
        }

        private void SetIndividualPackageLineItems(FedexRate.RateRequest request, GetShippingOptionRequest getShippingOptionRequest, decimal orderSubTotal, string currencyCode)
        {
            // Rate request setup - Total Dimensions of Shopping Cart Items determines number of packages

            var usedMeasureWeight = GetUsedMeasureWeight();
            var usedMeasureDimension = GetUsedMeasureDimension();

            _shippingService.GetDimensions(getShippingOptionRequest.Items, out var widthTmp, out var lengthTmp, out var heightTmp, true);

            var length = ConvertFromPrimaryMeasureDimension(lengthTmp, usedMeasureDimension, 1);
            var height = ConvertFromPrimaryMeasureDimension(heightTmp, usedMeasureDimension, 1);
            var width = ConvertFromPrimaryMeasureDimension(widthTmp, usedMeasureDimension, 1);
            var weight = ConvertFromPrimaryMeasureWeight(_shippingService.GetTotalWeight(getShippingOptionRequest, ignoreFreeShippedItems: true), usedMeasureWeight, 1);

            if (!IsPackageTooHeavy(weight) && !IsPackageTooLarge(length, height, width))
            {
                request.RequestedShipment.PackageCount = "1";

                request.RequestedShipment.RequestedPackageLineItems = new[] {
                    new FedexRate.RequestedPackageLineItem
                        {
                            SequenceNumber = "1", // package sequence number
                            GroupPackageCount = "1",
                            Weight = new FedexRate.Weight
                            {
                                Units = FedexRate.WeightUnits.LB,
                                UnitsSpecified = true,
                                Value = weight,
                                ValueSpecified = true
                            }, // package weight

                            Dimensions = new FedexRate.Dimensions
                            {
                                Length = _fedexSettings.PassDimensions ? length.ToString() : "0",
                                Width = _fedexSettings.PassDimensions ? width.ToString() : "0",
                                Height = _fedexSettings.PassDimensions ? height.ToString() : "0",
                                Units = FedexRate.LinearUnits.IN,
                                UnitsSpecified = true
                            }, // package dimensions
                            InsuredValue = new FedexRate.Money
                            {
                                Amount = orderSubTotal,
                                Currency = currencyCode
                            } // insured value
                        }
                };

            }
            else
            {
                var totalPackagesDims = 1;
                var totalPackagesWeights = 1;
                if (IsPackageTooHeavy(weight))
                {
                    totalPackagesWeights = Convert.ToInt32(Math.Ceiling(weight / FedexShippingDefaults.MAX_PACKAGE_WEIGHT));
                }
                if (IsPackageTooLarge(length, height, width))
                {
                    totalPackagesDims = Convert.ToInt32(Math.Ceiling(TotalPackageSize(length, height, width) / 108M));
                }
                var totalPackages = totalPackagesDims > totalPackagesWeights ? totalPackagesDims : totalPackagesWeights;
                if (totalPackages == 0)
                    totalPackages = 1;

                var weight2 = weight / totalPackages;
                var height2 = height / totalPackages;
                var width2 = width / totalPackages;
                var length2 = length / totalPackages;
                if (weight2 < 1)
                    weight2 = 1;
                if (height2 < 1)
                    height2 = 1;
                if (width2 < 1)
                    width2 = 1;
                if (length2 < 1)
                    length2 = 1;

                var orderSubTotal2 = orderSubTotal / totalPackages;

                request.RequestedShipment.PackageCount = totalPackages.ToString();
                request.RequestedShipment.RequestedPackageLineItems = new FedexRate.RequestedPackageLineItem[totalPackages];

                for (var i = 0; i < totalPackages; i++)
                {
                    request.RequestedShipment.RequestedPackageLineItems[i] = new FedexRate.RequestedPackageLineItem
                    {
                        SequenceNumber = (i + 1).ToString(), // package sequence number            
                        GroupPackageCount = "1",
                        Weight = new FedexRate.Weight
                        {
                            Units = FedexRate.WeightUnits.LB,
                            UnitsSpecified = true,
                            Value = weight2,
                            ValueSpecified = true
                        }, // package weight

                        Dimensions = new FedexRate.Dimensions
                        {
                            Length = _fedexSettings.PassDimensions ? length2.ToString() : "0",
                            Width = _fedexSettings.PassDimensions ? width2.ToString() : "0",
                            Height = _fedexSettings.PassDimensions ? height2.ToString() : "0",
                            Units = FedexRate.LinearUnits.IN,
                            UnitsSpecified = true
                        }, // package dimensions
                        InsuredValue = new FedexRate.Money
                        {
                            Amount = orderSubTotal2,
                            Currency = currencyCode
                        } // insured value
                    };
                }
            }
        }

        private void SetIndividualPackageLineItemsCubicRootDimensions(FedexRate.RateRequest request, GetShippingOptionRequest getShippingOptionRequest, decimal orderSubTotal, string currencyCode)
        {
            // Rate request setup - Total Volume of Shopping Cart Items determines number of packages

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


            var usedMeasureWeight = GetUsedMeasureWeight();
            var usedMeasureDimension = GetUsedMeasureDimension();

            var totalPackagesDims = 1;
            int length;
            int height;
            int width;

            if (getShippingOptionRequest.Items.Count == 1 && getShippingOptionRequest.Items[0].GetQuantity() == 1)
            {
                var sci = getShippingOptionRequest.Items[0].ShoppingCartItem;

                //get dimensions for qty 1
                _shippingService.GetDimensions(new List<GetShippingOptionRequest.PackageItem>
                                               {
                                                   new GetShippingOptionRequest.PackageItem(sci, 1)
                                               }, out var widthTmp, out var lengthTmp, out var heightTmp, true);

                length = ConvertFromPrimaryMeasureDimension(lengthTmp, usedMeasureDimension);
                height = ConvertFromPrimaryMeasureDimension(heightTmp, usedMeasureDimension);
                width = ConvertFromPrimaryMeasureDimension(widthTmp, usedMeasureDimension);
            }
            else
            {
                var totalVolume = 0M;
                foreach (var item in getShippingOptionRequest.Items)
                {
                    var sci = item.ShoppingCartItem;

                    //get dimensions for qty 1
                    _shippingService.GetDimensions(new List<GetShippingOptionRequest.PackageItem>
                                               {
                                                   new GetShippingOptionRequest.PackageItem(sci, 1)
                                               }, out var widthTmp, out var lengthTmp, out var heightTmp, true);

                    var productLength = ConvertFromPrimaryMeasureDimension(lengthTmp, usedMeasureDimension);
                    var productHeight = ConvertFromPrimaryMeasureDimension(heightTmp, usedMeasureDimension);
                    var productWidth = ConvertFromPrimaryMeasureDimension(widthTmp, usedMeasureDimension);
                    totalVolume += item.GetQuantity() * (productHeight * productWidth * productLength);
                }

                var dimension = 0;

                if (totalVolume != 0)
                {
                    // cubic inches
                    var packageVolume = _fedexSettings.PackingPackageVolume;
                    if (packageVolume <= 0)
                        packageVolume = 5184;

                    // cube root (floor)
                    dimension = Convert.ToInt32(Math.Floor(Math.Pow(Convert.ToDouble(packageVolume), 1.0 / 3.0)));
                    if (IsPackageTooLarge(dimension, dimension, dimension))
                        throw new NopException("fedexSettings.PackingPackageVolume exceeds max package size");

                    // adjust packageVolume for dimensions calculated
                    packageVolume = dimension * dimension * dimension;

                    totalPackagesDims = Convert.ToInt32(Math.Ceiling(totalVolume / packageVolume));
                }

                length = width = height = dimension;
            }
            if (length < 1)
                length = 1;
            if (height < 1)
                height = 1;
            if (width < 1)
                width = 1;

            var weight = ConvertFromPrimaryMeasureWeight(_shippingService.GetTotalWeight(getShippingOptionRequest, ignoreFreeShippedItems: true), usedMeasureWeight, 1);

            var totalPackagesWeights = 1;
            if (IsPackageTooHeavy(weight))
            {
                totalPackagesWeights = Convert.ToInt32(Math.Ceiling(weight / FedexShippingDefaults.MAX_PACKAGE_WEIGHT));
            }

            var totalPackages = totalPackagesDims > totalPackagesWeights ? totalPackagesDims : totalPackagesWeights;

            var orderSubTotalPerPackage = orderSubTotal / totalPackages;
            var weightPerPackage = weight / totalPackages;

            request.RequestedShipment.PackageCount = totalPackages.ToString();
            request.RequestedShipment.RequestedPackageLineItems = new FedexRate.RequestedPackageLineItem[totalPackages];

            for (var i = 0; i < totalPackages; i++)
            {
                request.RequestedShipment.RequestedPackageLineItems[i] = new FedexRate.RequestedPackageLineItem
                {
                    SequenceNumber = (i + 1).ToString(), // package sequence number          
                    GroupPackageCount = "1",
                    Weight = new FedexRate.Weight
                    {
                        Units = FedexRate.WeightUnits.LB,
                        UnitsSpecified = true,
                        Value = weightPerPackage,
                        ValueSpecified = true
                    }, // package weight

                    Dimensions = new FedexRate.Dimensions
                    {
                        Length = length.ToString(),
                        Height = height.ToString(),
                        Width = width.ToString(),
                        Units = FedexRate.LinearUnits.IN,
                        UnitsSpecified = true
                    }, // package dimensions
                    InsuredValue = new FedexRate.Money
                    {
                        Amount = orderSubTotalPerPackage,
                        Currency = currencyCode
                    } // insured value
                };
            }

        }

        private void SetIndividualPackageLineItemsOneItemPerPackage(FedexRate.RateRequest request, GetShippingOptionRequest getShippingOptionRequest, string currencyCode)
        {
            // Rate request setup - each Shopping Cart Item is a separate package
            var usedMeasureWeight = GetUsedMeasureWeight();
            var usedMeasureDimension = GetUsedMeasureDimension();

            var items = getShippingOptionRequest.Items;
            var totalItems = items.Sum(x => x.GetQuantity());
            request.RequestedShipment.PackageCount = totalItems.ToString();
            request.RequestedShipment.RequestedPackageLineItems = new FedexRate.RequestedPackageLineItem[totalItems];

            var i = 0;
            foreach (var packageItem in items)
            {
                var sci = packageItem.ShoppingCartItem;
                var qty = packageItem.GetQuantity();

                //get dimensions for qty 1
                _shippingService.GetDimensions(new List<GetShippingOptionRequest.PackageItem>
                                               {
                                                   new GetShippingOptionRequest.PackageItem(sci, 1)
                                               }, out var widthTmp, out var lengthTmp, out var heightTmp, true);

                var length = ConvertFromPrimaryMeasureDimension(lengthTmp, usedMeasureDimension, 1);
                var height = ConvertFromPrimaryMeasureDimension(heightTmp, usedMeasureDimension, 1);
                var width = ConvertFromPrimaryMeasureDimension(widthTmp, usedMeasureDimension, 1);
                var weight = ConvertFromPrimaryMeasureWeight(sci.Product.Weight, usedMeasureWeight, 1);

                for (var j = 0; j < qty; j++)
                {
                    request.RequestedShipment.RequestedPackageLineItems[i] = new FedexRate.RequestedPackageLineItem
                    {
                        SequenceNumber = (i + 1).ToString(), // package sequence number                     
                        GroupPackageCount = "1",
                        Weight = new FedexRate.Weight
                        {
                            Units = FedexRate.WeightUnits.LB,
                            UnitsSpecified = true,
                            Value = weight,
                            ValueSpecified = true
                        }, // package weight

                        Dimensions = new FedexRate.Dimensions
                        {
                            Length = length.ToString(),
                            Height = height.ToString(),
                            Width = width.ToString(),
                            Units = FedexRate.LinearUnits.IN,
                            UnitsSpecified = true
                        }, // package dimensions

                        InsuredValue = new FedexRate.Money
                        {
                            Amount = sci.Product.Price,
                            Currency = currencyCode
                        } // insured value
                    };

                    i++;
                }
            }
        }

        private void SetOrigin(FedexRate.RateRequest request, GetShippingOptionRequest getShippingOptionRequest)
        {
            request.RequestedShipment.Shipper = new FedexRate.Party
            {
                Address = new FedexRate.Address()
            };

            if (getShippingOptionRequest.CountryFrom is null)
                throw new Exception("FROM country is not specified");

            request.RequestedShipment.Shipper.Address.StreetLines = new[] { getShippingOptionRequest.AddressFrom };
            request.RequestedShipment.Shipper.Address.City = getShippingOptionRequest.CityFrom;
            if (IncludeStateProvinceCode(getShippingOptionRequest.CountryFrom.TwoLetterIsoCode))
            {
                var stateProvinceAbbreviation = getShippingOptionRequest.StateProvinceFrom is null ? "" : getShippingOptionRequest.StateProvinceFrom.Abbreviation;
                request.RequestedShipment.Shipper.Address.StateOrProvinceCode = stateProvinceAbbreviation;
            }
            request.RequestedShipment.Shipper.Address.PostalCode = getShippingOptionRequest.ZipPostalCodeFrom;
            request.RequestedShipment.Shipper.Address.CountryCode = getShippingOptionRequest.CountryFrom.TwoLetterIsoCode;
        }

        private void SetPayment(FedexRate.RateRequest request)
        {
            request.RequestedShipment.ShippingChargesPayment = new FedexRate.Payment
            {
                PaymentType = FedexRate.PaymentType.SENDER, // Payment options are RECIPIENT, SENDER, THIRD_PARTY
                PaymentTypeSpecified = true,
                Payor = new FedexRate.Payor
                {
                    ResponsibleParty = new FedexRate.Party
                    {
                        AccountNumber = _fedexSettings.AccountNumber
                    }
                }
            }; // Payment Information
        }

        private void SetShipmentDetails(FedexRate.RateRequest request, decimal orderSubTotal, string currencyCode)
        {
            //set drop off type
            switch (_fedexSettings.DropoffType)
            {
                case DropoffType.BusinessServiceCenter:
                    request.RequestedShipment.DropoffType = FedexRate.DropoffType.BUSINESS_SERVICE_CENTER;
                    break;
                case DropoffType.DropBox:
                    request.RequestedShipment.DropoffType = FedexRate.DropoffType.DROP_BOX;
                    break;
                case DropoffType.RegularPickup:
                    request.RequestedShipment.DropoffType = FedexRate.DropoffType.REGULAR_PICKUP;
                    break;
                case DropoffType.RequestCourier:
                    request.RequestedShipment.DropoffType = FedexRate.DropoffType.REQUEST_COURIER;
                    break;
                case DropoffType.Station:
                    request.RequestedShipment.DropoffType = FedexRate.DropoffType.STATION;
                    break;
                default:
                    request.RequestedShipment.DropoffType = FedexRate.DropoffType.BUSINESS_SERVICE_CENTER;
                    break;
            }
            request.RequestedShipment.TotalInsuredValue = new FedexRate.Money
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
                FedexRate.RateRequestType.PREFERRED,
                FedexRate.RateRequestType.LIST
            };
            //request.RequestedShipment.PackageDetail = RequestedPackageDetailType.INDIVIDUAL_PACKAGES;
            //request.RequestedShipment.PackageDetailSpecified = true;

            //for India domestic shipping add additional details
            if (request.RequestedShipment.Shipper.Address.CountryCode.Equals("IN", StringComparison.InvariantCultureIgnoreCase) &&
                request.RequestedShipment.Recipient.Address.CountryCode.Equals("IN", StringComparison.InvariantCultureIgnoreCase))
            {
                var commodity = new FedexRate.Commodity
                {
                    Name = "1",
                    NumberOfPieces = "1",
                    CustomsValue = new FedexRate.Money
                    {
                        Amount = orderSubTotal,
                        AmountSpecified = true,
                        Currency = currencyCode
                    }
                };

                request.RequestedShipment.CustomsClearanceDetail = new FedexRate.CustomsClearanceDetail
                {
                    CommercialInvoice = new FedexRate.CommercialInvoice
                    {
                        Purpose = FedexRate.PurposeOfShipmentType.SOLD,
                        PurposeSpecified = true
                    },
                    Commodities = new[] { commodity }
                };
            }
        }

        private int TotalPackageSize(int length, int height, int width)
        {
            var girth = height + height + width + width;
            var total = girth + length;
            return total;
        }

        /// <summary>
        /// Get tracking info
        /// </summary>
        /// <param name="request">Request details</param>
        /// <returns>The asynchronous task whose result contains the tracking info</returns>
        private async Task<FedexTracking.TrackReply> TrackAsync(FedexTracking.TrackRequest request)
        {
            //initialize the service
            using (var service = new FedexTracking.TrackPortTypeClient(FedexTracking.TrackPortTypeClient.EndpointConfiguration.TrackServicePort, _fedexSettings.Url))
            {
                var trackResponse = await service.trackAsync(request);

                return trackResponse.TrackReply;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets all events for a tracking number
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track</param>
        /// <returns>Shipment events</returns>
        public virtual IList<ShipmentStatusEvent> GetShipmentEvents(string trackingNumber)
        {
            try
            {
                //build the TrackRequest
                var request = CreateTrackRequest(trackingNumber);

                //this is the call to the web service passing in a TrackRequest and returning a TrackReply
                var reply = TrackAsync(request).Result;

                //parse response
                if (new[] { FedexTracking.NotificationSeverityType.SUCCESS, FedexTracking.NotificationSeverityType.NOTE, FedexTracking.NotificationSeverityType.WARNING }.Contains(reply.HighestSeverity)) // check if the call was successful
                {

                    return reply.CompletedTrackDetails?
                        .SelectMany(completedTrackDetails => completedTrackDetails.TrackDetails?
                            .SelectMany(trackDetails => trackDetails.Events?
                                .Select(trackEvent => new ShipmentStatusEvent
                                {
                                    EventName = $"{trackEvent.EventDescription} ({trackEvent.EventType})",
                                    Location = trackEvent?.Address?.City,
                                    CountryCode = trackEvent?.Address?.CountryCode,
                                    Date = trackEvent.TimestampSpecified ? trackEvent.Timestamp as DateTime? : null
                                })))
                        .ToList();
                }
            }
            catch (Exception exception)
            {
                //log errors
                _logger.Error($"Error while getting Fedex shipment tracking info - {trackingNumber}{Environment.NewLine}{exception.Message}", exception, _workContext.CurrentCustomer);
            }

            return new List<ShipmentStatusEvent>();
        }

        /// <summary>
        /// Gets shipping rates
        /// </summary>
        /// <param name="shippingOptionRequest">Shipping option request details</param>
        /// <returns>Represents a response of getting shipping rate options</returns>
        public virtual GetShippingOptionResponse GetRates(GetShippingOptionRequest shippingOptionRequest)
        {
            var response = new GetShippingOptionResponse();

            var request = CreateRateRequest(shippingOptionRequest, out var requestedShipmentCurrency);

            var service = new FedexRate.RatePortTypeClient(FedexRate.RatePortTypeClient.EndpointConfiguration.RateServicePort, _fedexSettings.Url);

            try
            {
                // This is the call to the web service passing in a RateRequest and returning a RateReply
                var reply = service.getRatesAsync(request).Result.RateReply; // Service call

                if (new[] { FedexRate.NotificationSeverityType.SUCCESS, FedexRate.NotificationSeverityType.NOTE, FedexRate.NotificationSeverityType.WARNING }.Contains(reply.HighestSeverity)) // check if the call was successful
                {
                    if (reply.RateReplyDetails != null)
                    {
                        var shippingOptions = ParseResponse(reply, requestedShipmentCurrency);
                        foreach (var shippingOption in shippingOptions)
                            response.ShippingOptions.Add(shippingOption);
                    }
                    else
                    {
                        if (reply.Notifications?.Length > 0 && !string.IsNullOrEmpty(reply.Notifications[0].Message))
                        {
                            response.AddError($"{reply.Notifications[0].Message} (code: {reply.Notifications[0].Code})");
                        }
                        else
                        {
                            response.AddError("Could not get reply from shipping server");
                        }
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
