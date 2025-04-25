using System.Collections.Generic;

namespace OnlineAssessment.Web.Models
{
    /// <summary>
    /// Model for PayU payment request parameters
    /// </summary>
    public class PayURequestModel
    {
        /// <summary>
        /// Dictionary containing all PayU parameters
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// PayU gateway URL
        /// </summary>
        public string PayUUrl => Parameters.ContainsKey("payuUrl") ? Parameters["payuUrl"] : "";

        /// <summary>
        /// Transaction ID
        /// </summary>
        public string TransactionId => Parameters.ContainsKey("txnid") ? Parameters["txnid"] : "";

        /// <summary>
        /// Payment amount
        /// </summary>
        public string Amount => Parameters.ContainsKey("amount") ? Parameters["amount"] : "";

        /// <summary>
        /// Product information
        /// </summary>
        public string ProductInfo => Parameters.ContainsKey("productinfo") ? Parameters["productinfo"] : "";

        /// <summary>
        /// Customer first name
        /// </summary>
        public string FirstName => Parameters.ContainsKey("firstname") ? Parameters["firstname"] : "";

        /// <summary>
        /// Customer email
        /// </summary>
        public string Email => Parameters.ContainsKey("email") ? Parameters["email"] : "";

        /// <summary>
        /// Customer phone
        /// </summary>
        public string Phone => Parameters.ContainsKey("phone") ? Parameters["phone"] : "";

        /// <summary>
        /// Success URL
        /// </summary>
        public string SuccessUrl => Parameters.ContainsKey("surl") ? Parameters["surl"] : "";

        /// <summary>
        /// Failure URL
        /// </summary>
        public string FailureUrl => Parameters.ContainsKey("furl") ? Parameters["furl"] : "";

        /// <summary>
        /// Hash for request validation
        /// </summary>
        public string Hash => Parameters.ContainsKey("hash") ? Parameters["hash"] : "";
    }
}
