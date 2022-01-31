using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core.Domain.Shipping;
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
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the rue if the tracker can track, otherwise false.
        /// </returns>
        public virtual Task<bool> IsMatchAsync(string trackingNumber)
        {
            if (string.IsNullOrWhiteSpace(trackingNumber))
                return Task.FromResult(false);

            //What is a FedEx tracking number format?
            return Task.FromResult(false);
        }

        /// <summary>
        /// Get URL for a page to show tracking info (third party tracking page)
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track</param>
        /// <param name="shipment">Shipment; pass null if the tracking number is not associated with a specific shipment</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the URL of a tracking page
        /// </returns>
        public virtual Task<string> GetUrlAsync(string trackingNumber, Shipment shipment = null)
        {
            return Task.FromResult($"https://www.fedex.com/apps/fedextrack/?action=track&tracknumbers={trackingNumber}");
        }
        
        /// <summary>
        /// Get all shipment events
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track</param>
        /// <param name="shipment">Shipment; pass null if the tracking number is not associated with a specific shipment</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of shipment events
        /// </returns>
        public virtual async Task<IList<ShipmentStatusEvent>> GetShipmentEventsAsync(string trackingNumber, Shipment shipment = null)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return new List<ShipmentStatusEvent>();

            return await _fedexService.GetShipmentEventsAsync(trackingNumber);
        }

        #endregion
    }
}