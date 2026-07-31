# Customer test script

Environment: `https://dev.boxdropaz.com`

## Credentials

The deployed dev username and password remain outside source control. Retrieve them in PowerShell:

Username: `customer@example.com`

```powershell
$c = aws lambda get-function-configuration --function-name BoxDropAzWeb-dev --region us-west-2 --query Environment.Variables --output json | ConvertFrom-Json
$username = $c.SEED_CUSTOMER_EMAIL
$password = $c.SEED_CUSTOMER_PASSWORD
"Username: $username`nPassword: $password"
```

## Test

1. Open `/Account/Login`, sign in, and verify the customer is sent to `/dashboard`.
2. Open **Book more totes** and complete region, package, schedule, contact, and address steps.
3. Accept the rental agreement and submit the review page.
4. Verify the Stripe payment form appears inside BoxDrop AZ and the browser is not sent to `checkout.stripe.com`.
5. Pay with test card `4242 4242 4242 4242`, any future expiry, any CVC, and any valid postal code.
6. Verify the return page confirms the booking and `/dashboard` shows the order.
7. Open the order and verify dates, addresses, totals, and payment status.
8. Select **Update card**. Verify the embedded Stripe form opens on-site and save test card `5555 5555 5555 4444`.
9. Repeat checkout with `4000 0025 0000 3155`; complete the test 3DS prompt and verify success.
10. Repeat checkout with decline card `4000 0000 0000 9995`; verify an inline decline message appears and no order is confirmed.
11. Attempt to open `/Admin`, `/Worker`, `/Agent`, and `/SaaSAdmin`; verify access is denied or redirected.

Record the order number and any Stripe event ID when reporting a failure.
