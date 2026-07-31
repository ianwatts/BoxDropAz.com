# SaaS admin test script

Environment: `https://dev.boxdropaz.com`

## Credentials

Username: `admin@boxdropaz.com`

```powershell
$c = aws lambda get-function-configuration --function-name BoxDropAzWeb-dev --region us-west-2 --query Environment.Variables --output json | ConvertFrom-Json
$username = $c.SEED_ADMIN_EMAIL
$password = $c.SEED_ADMIN_PASSWORD
"Username: $username`nPassword: $password"
```

## Test

1. Sign in at `/Account/Login` and verify the SaaS admin is sent to `/SaaSAdmin`.
2. Verify platform totals span all configured regions.
3. Open regions, create or edit a clearly named QA region, and verify validation and persistence.
4. Create or edit a QA package, toggle its active state, and verify public package visibility follows the setting.
5. Open `/Admin/orders`, switch regions, and verify cross-region order access works.
6. Open `/admin/inventory`, switch regions, and verify each region has independent tote/dolly totals, projections, and restock tasks.
7. Open users and verify role, region, enable/disable, and permitted impersonation controls.
8. Impersonate a customer, realtor, worker, and regional admin in turn; verify the role home and **stop impersonating** behavior.
9. Open `/SaaSAdmin/stripe-events`; verify recent checkout, subscription, invoice, and failed-payment events show their processing outcomes.
10. Trigger a Stripe test checkout from a customer flow and confirm its webhook event becomes processed rather than failed.
11. Review browser navigation and direct URLs for accidental exposure of secrets, full card data, or cross-site checkout redirects.

Delete only QA records that were created by this script. Do not delete production-like regions, packages, users, or orders.
