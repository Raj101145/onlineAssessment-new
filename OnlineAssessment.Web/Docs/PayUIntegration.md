# PayU Integration Documentation

This document explains how to use the PayU payment gateway integration in the Online Assessment Platform.

## Overview

The PayU integration consists of the following components:

1. **PayUHelper** - A static helper class with methods for common PayU operations
   - Automatically generates Success and Failure URLs based on the application URL
   - Centralizes all PayU-related functionality
   - Initialized at application startup
2. **PayUService** - A service that uses the helper methods and is registered with dependency injection
3. **PayURequestModel** - A model for PayU payment request parameters
4. **PaymentController** - Controller that handles payment processing

### Dynamic URL Generation

One of the key features of this implementation is the dynamic generation of Success and Failure URLs based on the application's base URL. This eliminates the need to manually update these URLs when deploying to different environments.

## Configuration

PayU settings are configured in `appsettings.json`:

```json
"PayU": {
  "Key": "your_merchant_key",
  "Salt": "your_merchant_salt",
  "BaseUrl": "https://test.payu.in/_payment"
},
"ApplicationUrl": "http://yourdomain.com"
```

The Success and Failure URLs are now dynamically generated based on the ApplicationUrl setting. This makes deployment to different environments easier as you only need to update the ApplicationUrl setting.

## How to Use

### 1. Initiating a Payment

```csharp
// In your controller
public IActionResult InitiatePayment()
{
    // Generate a transaction ID
    string txnid = _payUService.GenerateTransactionId();

    // Prepare payment parameters
    string amount = "100.00";
    string productinfo = "Product Description";
    string firstname = "Customer Name";
    string email = "customer@example.com";
    string phone = "9999999999";

    // Create PayU request
    var payUParams = _payUService.PreparePayURequest(txnid, amount, productinfo, firstname, email, phone);

    // Create model for view
    var model = new PayURequestModel
    {
        Parameters = payUParams
    };

    return View("PayUInitiate", model);
}
```

### 2. Handling Payment Response

Create endpoints for success and failure callbacks:

```csharp
[Route("Payment/Success")]
[AllowAnonymous]
public IActionResult Success()
{
    // Process successful payment
    // ...
    return View("Success");
}

[Route("Payment/Failure")]
[AllowAnonymous]
public IActionResult Failure()
{
    // Handle failed payment
    // ...
    return View("Failure");
}
```

### 3. Validating Payment Response

```csharp
// In your success handler
var responseParams = new Dictionary<string, string>();
foreach (var key in Request.Form.Keys)
{
    responseParams[key] = Request.Form[key];
}

bool isValid = _payUService.ValidateResponseHash(responseParams);
if (isValid)
{
    // Process payment
}
else
{
    // Handle invalid response
}
```

## Security Considerations

1. Always validate the response hash to ensure the payment response is authentic
2. Use HTTPS for all payment-related communications
3. Keep your PayU Key and Salt secure
4. Consider using environment variables for sensitive configuration in production

## Troubleshooting

- If payments are not being processed, check the PayU configuration in `appsettings.json`
- Ensure the ApplicationUrl is correctly set and accessible from the internet
- Use the `/Payment/TestPayU` endpoint to verify the configuration and generated URLs
- Check the application logs for any errors during payment processing
- For testing, use PayU's test environment and test credentials

### Testing the Integration

You can test the PayU integration by accessing the following endpoint:

```
GET /Payment/TestPayU
```

This will return a JSON response with:
- All PayU parameters
- The current configuration
- The dynamically generated Success and Failure URLs

Verify that the URLs are correctly formed and accessible from the internet.

## References

- [PayU Integration Documentation](https://www.payumoney.com/dev-guide/)
- [PayU API Reference](https://www.payumoney.com/pdf/PayUMoney-Technical-Integration-Document.pdf)
