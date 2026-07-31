# Realtor test script

Environment: `https://dev.boxdropaz.com`

## Credentials

Username: `agent@example.com`

```powershell
$c = aws lambda get-function-configuration --function-name BoxDropAzWeb-dev --region us-west-2 --query Environment.Variables --output json | ConvertFrom-Json
$username = $c.SEED_REALTOR_EMAIL
$password = $c.SEED_REALTOR_PASSWORD
"Username: $username`nPassword: $password"
```

## Test

1. Sign in at `/Account/Login` and verify the realtor is sent to `/Agent/dashboard`.
2. Choose a subscription plan and verify Stripe Checkout is embedded within BoxDrop AZ.
3. Subscribe with test card `4242 4242 4242 4242`, any future expiry, any CVC, and any valid postal code.
4. Verify the dashboard shows the selected plan, renewal details, and granted gift-credit balance.
5. Open billing history and confirm the paid invoice appears.
6. Create a closing gift with a unique client email and valid Phoenix-area property details.
7. Verify the gift appears as outstanding and that its credit is deducted/reserved correctly.
8. Resend the gift and verify the action succeeds without duplicating the gift.
9. Cancel an unused gift and verify its status and credit balance update.
10. Open subscription cancellation, cancel at period end, and verify already-earned credit remains available.
11. Attempt to open `/Admin`, `/Worker`, and `/SaaSAdmin`; verify access is denied or redirected.

Use Stripe decline card `4000 0000 0000 9995` in a separate subscription attempt and verify no active subscription or credit is granted.
