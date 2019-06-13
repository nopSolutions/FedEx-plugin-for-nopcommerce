using System.Collections.Generic;
using Nop.Plugin.Shipping.Fedex.Services;
using Nop.Services.Shipping.Tracking;

namespace Nop.Plugin.Shipping.Fedex
{
    public class FedexShipmentTracker : IShipmentTracker
    {
        #region Fields

        private readonly FedexService _fedexService;

        #endregion

        #region Ctor

        public FedexShipmentTracker(FedexService fedexService)
        {
            _fedexService = fedexService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Gets if the current tracker can track the tracking number.
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track.</param>
        /// <returns>True if the tracker can track, otherwise false.</returns>
        public virtual bool IsMatch(string trackingNumber)
        {
            if (string.IsNullOrWhiteSpace(trackingNumber))
                return false;

            //What is a FedEx tracking number format?
            return false;
        }

        /// <summary>
        /// Gets an URL for a page to show tracking info (third party tracking page).
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track.</param>
        /// <returns>URL of a tracking page.</returns>
        public virtual string GetUrl(string trackingNumber)
        {
            return $"https://www.fedex.com/apps/fedextrack/?action=track&tracknumbers={trackingNumber}";
        }

        /// <summary>
        /// Gets all events for a tracking number.
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track</param>
        /// <returns>List of Shipment Events.</returns>
        public virtual IList<ShipmentStatusEvent> GetShipmentEvents(string trackingNumber)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return new List<ShipmentStatusEvent>();

            return _fedexService.GetShipmentEvents(trackingNumber);
        }

        #endregion
    }
}