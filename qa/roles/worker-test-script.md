# Worker test script

Environment: `https://dev.boxdropaz.com`

## Credentials

Username: `worker@boxdropaz.com`

```powershell
$c = aws lambda get-function-configuration --function-name BoxDropAzWeb-dev --region us-west-2 --query Environment.Variables --output json | ConvertFrom-Json
$username = $c.SEED_WORKER_EMAIL
$password = $c.SEED_WORKER_PASSWORD
"Username: $username`nPassword: $password"
```

## Test

1. Sign in at `/Account/Login` and verify the worker is sent to `/Worker`.
2. Verify the manifest is limited to the worker's assigned region and selected date.
3. Open a delivery and verify customer contact, address, package quantities, window, and notes.
4. Mark the order delivered and verify its status changes once without creating duplicate history.
5. Open a pickup and enter returned tote/lid and custom-fit dolly quantities.
6. Report one damaged or missing item with a note, then mark the pickup complete.
7. Verify missing equipment reduces owned inventory and causes a restock task when it creates a projected shortage.
8. For an admin-created restock task, verify the exact tote and dolly quantities and Home Depot product links appear on the manifest.
9. Enter the quantities actually received and complete the restock task. Verify it disappears after refresh and the admin inventory totals increase once.
10. Verify the completed rental stop leaves the active manifest and its status remains correct after refresh.
11. Change the manifest date and verify only stops and due/overdue inventory tasks for that date are shown.
12. Attempt to open `/Admin/users`, `/SaaSAdmin`, `/Agent`, and another region's order URL; verify access is denied, redirected, or the record is not exposed.

Coordinate with an admin tester to prepare delivery and pickup orders before running destructive status steps.
