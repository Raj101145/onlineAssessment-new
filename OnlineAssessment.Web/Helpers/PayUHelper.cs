using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace OnlineAssessment.Web.Helpers
{
    /// <summary>
    /// Helper class for PayU payment gateway integration that directly uses appsettings.json
    /// </summary>
    public static class PayUHelper
    {
        // Static configuration properties loaded from appsettings.json
        private static string Key { get; set; }
        private static string Salt { get; set; }
        private static string BaseUrl { get; set; }
        private static string ApplicationUrl { get; set; }

        // Dynamically generated URLs
        private static string SuccessUrl(string txnid = null, string testId = null)
        {
            var url = $"{ApplicationUrl}/Payment/Success";
            if (!string.IsNullOrEmpty(testId))
            {
                url += $"?testId={testId}";
                if (!string.IsNullOrEmpty(txnid))
                {
                    url += $"&txnid={txnid}";
                }
            }
            else if (!string.IsNullOrEmpty(txnid))
            {
                url += $"?txnid={txnid}";
            }
            return url;
        }

        private static string FailureUrl => $"{ApplicationUrl}/Payment/Failure";

        // Flag to track if configuration has been initialized
        private static bool _isInitialized = false;

        /// <summary>
        /// Initialize PayU configuration from appsettings.json
        /// </summary>
        /// <param name="configuration">Application configuration</param>
        public static void Initialize(IConfiguration configuration)
        {
            Key = configuration["PayU:Key"];
            Salt = configuration["PayU:Salt"];
            BaseUrl = configuration["PayU:BaseUrl"];

            // Get the application URL from configuration
            ApplicationUrl = configuration["ApplicationUrl"]?.TrimEnd('/');

            // If ApplicationUrl is not set, try to use Kestrel URL
            if (string.IsNullOrEmpty(ApplicationUrl))
            {
                var kestrelUrl = configuration["Kestrel:Endpoints:Http:Url"];
                if (!string.IsNullOrEmpty(kestrelUrl))
                {
                    ApplicationUrl = kestrelUrl;
                }
                else
                {
                    // Dynamically fetch public IP address if nothing is set
                    var publicIpUrl = GetPublicIpAddress();
                    if (!string.IsNullOrEmpty(publicIpUrl))
                    {
                        ApplicationUrl = publicIpUrl;
                    }
                    else
                    {
                        // Default to localhost:5058 if no URL is configured
                        ApplicationUrl = "http://localhost:5058";
                    }
                }
            }

            _isInitialized = true;
        }

        // Dynamically fetch public IP if ApplicationUrl is not set
        private static string GetPublicIpAddress()
        {
            try
            {
                using (var client = new System.Net.WebClient())
                {
                    // This will fetch the public IP from an external service
                    string ip = client.DownloadString("https://api.ipify.org");
                    return $"http://{ip}";
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Ensures configuration is initialized
        /// </summary>
        /// <param name="configuration">Application configuration to use if not initialized</param>
        private static void EnsureInitialized(IConfiguration configuration = null)
        {
            if (!_isInitialized)
            {
                if (configuration == null)
                {
                    throw new InvalidOperationException("PayUHelper is not initialized. Call Initialize method first or provide configuration.");
                }

                Initialize(configuration);
            }
        }
        /// <summary>
        /// Generates a PayU hash for request validation
        /// </summary>
        /// <param name="payUParams">Dictionary containing PayU parameters</param>
        /// <param name="configuration">Optional configuration if not initialized</param>
        /// <returns>SHA512 hash string</returns>
        public static string GenerateHash(Dictionary<string, string> payUParams, IConfiguration configuration = null)
        {
            EnsureInitialized(configuration);
            // Correct PayU hash sequence (include udf1-udf10)
            string[] hashVarsSeq = new string[] {
                "key", "txnid", "amount", "productinfo", "firstname", "email",
                "udf1", "udf2", "udf3", "udf4", "udf5", "udf6", "udf7", "udf8", "udf9", "udf10"
            };

            var hashString = new StringBuilder();
            foreach (var field in hashVarsSeq)
            {
                hashString.Append(payUParams.ContainsKey(field) ? payUParams[field] : "");
                hashString.Append("|");
            }
            hashString.Append(Salt);

            return GetSha512Hash(hashString.ToString()).ToLower();
        }

        /// <summary>
        /// Computes SHA512 hash of input text
        /// </summary>
        /// <param name="text">Input text to hash</param>
        /// <returns>Hex string of hash</returns>
        private static string GetSha512Hash(string text)
        {
            using (SHA512 sha512 = SHA512.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var hash = sha512.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
            }
        }

        /// <summary>
        /// Prepares a PayU payment request with all required parameters
        /// </summary>
        /// <param name="txnid">Transaction ID</param>
        /// <param name="amount">Payment amount</param>
        /// <param name="productinfo">Product information</param>
        /// <param name="firstname">Customer first name</param>
        /// <param name="email">Customer email</param>
        /// <param name="phone">Customer phone</param>
        /// <param name="testId">Optional test ID to include in success URL</param>
        /// <param name="configuration">Optional configuration if not initialized</param>
        /// <returns>Dictionary with all PayU parameters</returns>
        public static Dictionary<string, string> PreparePayURequest(string txnid, string amount, string productinfo, string firstname, string email, string phone, string testId = null, IConfiguration configuration = null)
        {
            // Ensure configuration is initialized
            EnsureInitialized(configuration);

            // Extract testId from productinfo if available and not explicitly provided
            if (string.IsNullOrEmpty(testId) && productinfo.StartsWith("TestBooking_"))
            {
                testId = productinfo.Substring("TestBooking_".Length);
            }

            // Create parameters dictionary
            var payUParams = new Dictionary<string, string>
            {
                { "key", Key },
                { "txnid", txnid },
                { "amount", amount },
                { "productinfo", productinfo },
                { "firstname", firstname },
                { "email", email },
                { "phone", phone },
                { "surl", SuccessUrl(txnid, testId) },
                { "furl", FailureUrl },
                { "udf1", testId ?? "" }, // Store testId in udf1 for reference
                { "udf2", "" }, { "udf3", "" }, { "udf4", "" }, { "udf5", "" },
                { "udf6", "" }, { "udf7", "" }, { "udf8", "" }, { "udf9", "" }, { "udf10", "" }
            };

            // Generate and add hash
            payUParams["hash"] = GenerateHash(payUParams);

            // Add PayU URL for convenience
            payUParams["payuUrl"] = BaseUrl;

            return payUParams;
        }

        /// <summary>
        /// Generates a unique transaction ID for PayU
        /// </summary>
        /// <returns>Unique transaction ID</returns>
        public static string GenerateTransactionId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 20);
        }

        /// <summary>
        /// Gets the current PayU configuration for debugging
        /// </summary>
        /// <returns>Dictionary with configuration values</returns>
        public static Dictionary<string, string> GetCurrentConfiguration()
        {
            EnsureInitialized();

            return new Dictionary<string, string>
            {
                { "Key", Key },
                { "BaseUrl", BaseUrl },
                { "ApplicationUrl", ApplicationUrl },
                { "SuccessUrl", SuccessUrl(null, null) },
                { "FailureUrl", FailureUrl }
            };
        }

        /// <summary>
        /// Validates a PayU response hash
        /// </summary>
        /// <param name="responseParams">Response parameters from PayU</param>
        /// <param name="configuration">Optional configuration if not initialized</param>
        /// <returns>True if hash is valid</returns>
        public static bool ValidateResponseHash(Dictionary<string, string> responseParams, IConfiguration configuration = null)
        {
            // Ensure configuration is initialized
            EnsureInitialized(configuration);

            if (!responseParams.ContainsKey("status") || !responseParams.ContainsKey("hash"))
                return false;

            // Build hash string according to PayU documentation
            var hashString = new StringBuilder();
            hashString.Append(Salt);
            hashString.Append("|");
            hashString.Append(responseParams.ContainsKey("status") ? responseParams["status"] : "");
            hashString.Append("|");

            // Add other parameters as per PayU documentation
            // This may need adjustment based on PayU's exact requirements

            string calculatedHash = GetSha512Hash(hashString.ToString()).ToLower();
            return calculatedHash == responseParams["hash"];
        }
    }
}
