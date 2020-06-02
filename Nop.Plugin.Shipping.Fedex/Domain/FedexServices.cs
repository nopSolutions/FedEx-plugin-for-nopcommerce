//------------------------------------------------------------------------------
// Contributor(s): mb. 
//------------------------------------------------------------------------------

namespace Nop.Plugin.Shipping.Fedex.Domain
{
    /// <summary>
    /// Class for FedEx services
    /// </summary>
    public class FedexServices
    {
        #region Properties

        /// <summary>
        /// FedEx services string names
        /// </summary>
        public string[] Services { get; } = {
                                        "FedEx Europe First International Priority",
                                        "FedEx 1Day Freight",
                                        "FedEx 2Day",
                                        "FedEx 2Day Freight",
                                        "FedEx 3Day Freight",
                                        "FedEx Express Saver",
                                        "FedEx Ground",
                                        "FedEx First Overnight",
                                        "FedEx Ground Home Delivery",
                                        "FedEx International Distribution Freight",
                                        "FedEx International Economy",
                                        "FedEx International Economy Distribution",
                                        "FedEx International Economy Freight",
                                        "FedEx International First",
                                        "FedEx International Priority",
                                        "FedEx International Priority Freight",
                                        "FedEx Priority Overnight",
                                        "FedEx Smart Post",
                                        "FedEx Standard Overnight",
                                        "FedEx Freight",
                                        "FedEx National Freight"
                                        };

        #endregion

        #region Utilities
        /// <summary>
        /// Gets the text name based on the ServiceID (in FedEx Reply)
        /// </summary>
        /// <param name="serviceId">ID of the carrier service -from FedEx</param>
        /// <returns>String representation of the carrier service</returns>
        public static string GetServiceName(string serviceId) 
        {
            return serviceId switch
            {
                "EUROPE_FIRST_INTERNATIONAL_PRIORITY" => "FedEx Europe First International Priority",
                "FEDEX_1_DAY_FREIGHT" => "FedEx 1Day Freight",
                "FEDEX_2_DAY" => "FedEx 2Day",
                "FEDEX_2_DAY_FREIGHT" => "FedEx 2Day Freight",
                "FEDEX_3_DAY_FREIGHT" =>"FedEx 3Day Freight",
                "FEDEX_EXPRESS_SAVER" => "FedEx Express Saver",
                "FEDEX_GROUND" => "FedEx Ground",
                "FIRST_OVERNIGHT" => "FedEx First Overnight",
                "GROUND_HOME_DELIVERY" => "FedEx Ground Home Delivery",
                "INTERNATIONAL_DISTRIBUTION_FREIGHT" => "FedEx International Distribution Freight",
                "INTERNATIONAL_ECONOMY" => "FedEx International Economy",
                "INTERNATIONAL_ECONOMY_DISTRIBUTION" => "FedEx International Economy Distribution",
                "INTERNATIONAL_ECONOMY_FREIGHT" => "FedEx International Economy Freight",
                "INTERNATIONAL_FIRST" => "FedEx International First",
                "INTERNATIONAL_PRIORITY" => "FedEx International Priority",
                "INTERNATIONAL_PRIORITY_FREIGHT" => "FedEx International Priority Freight",
                "PRIORITY_OVERNIGHT" => "FedEx Priority Overnight",
                "SMART_POST" => "FedEx Smart Post",
                "STANDARD_OVERNIGHT" => "FedEx Standard Overnight",
                "FEDEX_FREIGHT" => "FedEx Freight",
                "FEDEX_NATIONAL_FREIGHT" => "FedEx National Freight",
                _ => "UNKNOWN"
            };
        }

        /// <summary>
        /// Gets the ServiceId based on the text name
        /// </summary>
        /// <param name="serviceName">Name of the carrier service (based on the text name returned from GetServiceName())</param>
        /// <returns>Service ID as used by FedEx</returns>
        public static string GetServiceId(string serviceName)
        {
            return serviceName switch
            {
                "FedEx Europe First International Priority" => "EUROPE_FIRST_INTERNATIONAL_PRIORITY",
                "FedEx 1Day Freight" => "FEDEX_1_DAY_FREIGHT",
                "FedEx 2Day" => "FEDEX_2_DAY",
                "FedEx 2Day Freight" => "FEDEX_2_DAY_FREIGHT",
                "FedEx 3Day Freight" => "FEDEX_3_DAY_FREIGHT",
                "FedEx Express Saver" => "FEDEX_EXPRESS_SAVER",
                "FedEx Ground" => "FEDEX_GROUND",
                "FedEx First Overnight" => "FIRST_OVERNIGHT",
                "FedEx Ground Home Delivery" => "GROUND_HOME_DELIVERY",
                "FedEx International Distribution Freight" => "INTERNATIONAL_DISTRIBUTION_FREIGHT",
                "FedEx International Economy" => "INTERNATIONAL_ECONOMY",
                "FedEx International Economy Distribution" => "INTERNATIONAL_ECONOMY_DISTRIBUTION",
                "FedEx International Economy Freight" => "INTERNATIONAL_ECONOMY_FREIGHT",
                "FedEx International First" => "INTERNATIONAL_FIRST",
                "FedEx International Priority" => "INTERNATIONAL_PRIORITY",
                "FedEx International Priority Freight" => "INTERNATIONAL_PRIORITY_FREIGHT",
                "FedEx Priority Overnight" => "PRIORITY_OVERNIGHT",
                "FedEx Smart Post" => "SMART_POST",
                "FedEx Standard Overnight" => "STANDARD_OVERNIGHT",
                "FedEx Freight" => "FEDEX_FREIGHT",
                "FedEx National Freight" => "FEDEX_NATIONAL_FREIGHT",
                _ => "UNKNOWN"
            };
        }

        #endregion

    }
}
