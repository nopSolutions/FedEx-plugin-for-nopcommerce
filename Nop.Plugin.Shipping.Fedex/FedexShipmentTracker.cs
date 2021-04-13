using System.Collections.Generic;
using System.Threading.Tasks;
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
        /// Gets an URL for a page to show tracking info (third party tracking page).
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track.</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the uRL of a tracking page.
        /// </returns>
        public virtual Task<string> GetUrlAsync(string trackingNumber)
        {
            return Task.FromResult($"https://www.fedex.com/apps/fedextrack/?action=track&tracknumbers={trackingNumber}");
        }

        /// <summary>
        /// Gets all events for a tracking number.
        /// </summary>
        /// <param name="trackingNumber">The tracking number to track</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of Shipment Events.
        /// </returns>
        public virtual async Task<IList<ShipmentStatusEvent>> GetShipmentEventsAsync(string trackingNumber)
        {
            if (string.IsNullOrEmpty(trackingNumber))
                return new List<ShipmentStatusEvent>();

            return await _fedexService.GetShipmentEventsAsync(trackingNumber);
        }

        #endregion
    }
}