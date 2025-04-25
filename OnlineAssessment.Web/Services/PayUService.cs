using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using OnlineAssessment.Web.Helpers;

namespace OnlineAssessment.Web.Services
{
    /// <summary>
    /// Service for PayU payment gateway integration
    /// </summary>
    public class PayUService
    {
        private readonly IConfiguration _configuration;

        public PayUService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Prepares a PayU payment request with all required parameters
        /// </summary>
        public Dictionary<string, string> PreparePayURequest(string txnid, string amount, string productinfo, string firstname, string email, string phone, string testId = null)
        {
            // Use the helper method to prepare the request
            return PayUHelper.PreparePayURequest(txnid, amount, productinfo, firstname, email, phone, testId);
        }

        /// <summary>
        /// Validates a PayU response hash
        /// </summary>
        public bool ValidateResponseHash(Dictionary<string, string> responseParams)
        {
            return PayUHelper.ValidateResponseHash(responseParams);
        }

        /// <summary>
        /// Generates a unique transaction ID for PayU
        /// </summary>
        public string GenerateTransactionId()
        {
            return PayUHelper.GenerateTransactionId();
        }

        /// <summary>
        /// Gets the PayU base URL from configuration
        /// </summary>
        public string PayUBaseUrl => _configuration["PayU:BaseUrl"];
    }
}
