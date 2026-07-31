# Regional admin test script

Environment: `https://dev.boxdropaz.com`

## Credentials

Username: `phoenix.admin@boxdropaz.com`

```powershell
$c = aws lambda get-function-configuration --function-name BoxDropAzWeb-dev --region us-west-2 --query Environment.Variables --output json | ConvertFrom-Json
$username = $c.SEED_REGIONALADMIN_EMAIL
$password = $c.SEED_REGIONALADMIN_PASSWORD
"Username: $username`nPassword: $password"
```

## Test

1. Sign in at `/Account/Login` and verify the regional admin is sent to `/Admin`.
2. Verify dashboard totals and order lists contain only the assigned region.
3. Filter orders by status/date and open an order.
4. Edit an operational field, add an internal note, and verify both persist after refresh.
5. Advance an order through an allowed status transition and verify its history.
6. Add a damage line, waive it with a reason, and verify no Stripe charge is created.
7. Open `/admin/schedule`, set minimum notice to 3 days, confirm weekday windows are after 5 PM and weekend daytime windows remain, then mark one future date/window unavailable and verify booking hides it.
8. Open `/admin/inventory`, record the original totals, and verify owned, in-field, available, projected tote/dolly counts, and colored index-card stock.
9. Set inventory below a scheduled delivery's requirements. Verify the shortage date, 3-day purchase lead, tote/dolly/card-holder/card-pack quantities, and one restock task without duplicates after refresh.
10. Restore the original totals and verify the obsolete open restock task is cancelled automatically.
11. Open users, filter by role, assign a non-admin role, and set a user region.
12. Disable and re-enable a non-admin test user.
13. Open the worker view and verify regional fulfillment and inventory-task access works.
14. Attempt to manage regions/packages in `/SaaSAdmin`, assign `SaaSAdmin` or `RegionalAdmin`, impersonate an admin, and access another region's direct order URL; verify each restricted action is blocked.

Do not run actual damage charges unless the target order is explicitly designated for Stripe test-mode charging.
