# Creates the Stripe products and recurring prices for the three realtor gifting plans, then prints
# a stripe-settings.<env>.json block to paste in. Run from the repo root with the Stripe CLI logged in.
#
#   .\create-stripe-prices.ps1 -Environment dev
#   .\create-stripe-prices.ps1 -Environment prod
#
# Amounts mirror RealtorPlan.cs. If you change one, change the other in the same commit or an agent
# will be billed a price that does not match the credit they are granted.

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "prod")]
    [string]$Environment,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$liveMode = $Environment -eq "prod"
$modeFlag = if ($liveMode) { "--live" } else { "--test" }

$settingsPath = "stripe-settings.$Environment.json"
if (Test-Path $settingsPath) {
    $existing = (Get-Content $settingsPath -Raw | ConvertFrom-Json).Stripe
    $apiKey = $existing.SecretKey
}

if (-not $apiKey) {
    Write-Error "No secret key found. Create $settingsPath with a Stripe.SecretKey value first."
}

$expectedPrefix = if ($liveMode) { "sk_live_" } else { "sk_test_" }
if ($apiKey -notmatch "^$expectedPrefix") {
    Write-Error "$settingsPath holds a key that is not $expectedPrefix*. Refusing to create $Environment prices with it."
}

$env:STRIPE_API_KEY = $apiKey

# Must match BoxDropAz.Core/Models/Realtors/RealtorPlan.cs.
$plans = @(
    @{ Key = "RealtorStarterMonthlyPriceId";      Name = "BoxDrop AZ Agent Gifting - Starter";      Amount = 5900;  Credit = 7500 }
    @{ Key = "RealtorProfessionalMonthlyPriceId"; Name = "BoxDrop AZ Agent Gifting - Professional"; Amount = 12900; Credit = 17500 }
    @{ Key = "RealtorBrokerageMonthlyPriceId";    Name = "BoxDrop AZ Agent Gifting - Brokerage";    Amount = 29900; Credit = 42500 }
)

function Invoke-Stripe {
    param([string[]]$Arguments)

    $output = & stripe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "stripe $($Arguments -join ' ') failed: $output"
    }
    return $output | ConvertFrom-Json
}

Write-Host "--- Creating $Environment Stripe prices ---" -ForegroundColor Cyan

$results = @{}
foreach ($plan in $plans) {
    $monthly = $plan.Amount / 100
    $credit = $plan.Credit / 100
    Write-Host "`n$($plan.Name): `$$monthly/mo granting `$$credit credit" -ForegroundColor Cyan

    if ($DryRun) {
        Write-Host "  [dry run] stripe products create --name=`"$($plan.Name)`" $modeFlag"
        Write-Host "  [dry run] stripe prices create --product=<id> --currency=usd --unit-amount=$($plan.Amount) --recurring.interval=month $modeFlag"
        $results[$plan.Key] = "price_DRYRUN_$($plan.Key)"
        continue
    }

    # The monthly credit is stamped on the product so the Stripe dashboard shows what the plan owes.
    $product = Invoke-Stripe @(
        "products", "create",
        "--name=$($plan.Name)",
        "--description=Monthly subscription granting `$$credit in closing gift credit",
        "--metadata[monthly_credit_cents]=$($plan.Credit)",
        $modeFlag
    )
    Write-Host "  product -> $($product.id)"

    $price = Invoke-Stripe @(
        "prices", "create",
        "--product=$($product.id)",
        "--currency=usd",
        "--unit-amount=$($plan.Amount)",
        "--recurring.interval=month",
        "--recurring.interval-count=1",
        $modeFlag
    )
    Write-Host "  price   -> $($price.id)" -ForegroundColor Green

    $results[$plan.Key] = $price.id
}

Write-Host "`n=== Paste into $settingsPath ===" -ForegroundColor Cyan
Write-Host '{'
Write-Host '  "Stripe": {'
Write-Host ('    "SecretKey": "' + $apiKey + '",')
Write-Host '    "PublishableKey": "pk_...",'
Write-Host '    "WebhookSecret": "whsec_...",'
foreach ($plan in $plans) {
    $comma = if ($plan.Key -eq $plans[-1].Key) { "" } else { "," }
    Write-Host ('    "' + $plan.Key + '": "' + $results[$plan.Key] + '"' + $comma)
}
Write-Host '  }'
Write-Host '}'
Write-Host ""
Write-Host "The webhook secret comes from the endpoint you register, either in the dashboard or via" -ForegroundColor Yellow
Write-Host "  stripe listen --forward-to https://localhost:7057/stripe/webhook" -ForegroundColor Yellow
