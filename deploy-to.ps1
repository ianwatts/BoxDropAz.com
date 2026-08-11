# Deploys BoxDropAz to AWS. Run from the repo root.
#
#   .\deploy-to.ps1 -Environment dev
#   .\deploy-to.ps1 -Environment prod
#
# Stripe keys are read from stripe-settings.<env>.json, which is gitignored. Anything missing is
# passed as an empty string, so a deploy without that file still succeeds with payments disabled.

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "prod")]
    [string]$Environment,

    # Prod defaults to leaving tables alone. Pass this on a first-ever prod deploy.
    [switch]$CreateTables,

    # Skips the custom domain mapping step, which fails until the domain exists in API Gateway.
    [switch]$SkipDomainMapping
)

$ErrorActionPreference = "Stop"

$region = "us-west-2"
$stackName = "BoxDropAz-$Environment"
$stageName = $Environment
$tablePrefix = if ($Environment -eq "dev") { "BoxDropAz_Dev_" } else { "BoxDropAz_Prod_" }
# Canonical public hosts. Prod serves www; the apex is mapped as an alias when it exists in API Gateway.
$domainName = if ($Environment -eq "dev") { "dev.boxdropaz.com" } else { "www.boxdropaz.com" }
$siteBaseUrl = if ($Environment -eq "dev") { "https://dev.boxdropaz.com" } else { "https://www.boxdropaz.com" }

# First-ever prod stack has no tables yet, so create them automatically. Later prod deploys leave
# tables alone unless -CreateTables is passed explicitly.
if ($Environment -eq "prod" -and -not $CreateTables) {
    $ErrorActionPreference = "SilentlyContinue"
    aws cloudformation describe-stacks --stack-name $stackName --region $region 2>$null | Out-Null
    $stackMissing = $LASTEXITCODE -ne 0
    $ErrorActionPreference = "Stop"
    if ($stackMissing) {
        Write-Host "Prod stack does not exist yet; enabling table creation for the first deploy." -ForegroundColor Yellow
        $CreateTables = $true
    }
}

# Recreating tables on an existing prod stack would be catastrophic, so it is opt-in there.
# Dev tables also often already exist from local AutoCreateTables, and CloudFormation's
# early-validation hook refuses to create a table that is already there.
$manageTables = if ($Environment -eq "prod" -and -not $CreateTables) { "false" } else { "true" }

if ($manageTables -eq "true") {
    $ErrorActionPreference = "SilentlyContinue"
    $existingTables = aws dynamodb list-tables --region $region --output json 2>$null |
        ConvertFrom-Json |
        Select-Object -ExpandProperty TableNames |
        Where-Object { $_.StartsWith($tablePrefix) }
    $ErrorActionPreference = "Stop"

    if ($existingTables -and @($existingTables).Count -gt 0) {
        Write-Host "Found $(@($existingTables).Count) existing tables with prefix '$tablePrefix'; leaving ManageTables=false so CloudFormation does not try to recreate them." -ForegroundColor Yellow
        $manageTables = "false"
    }
}

$accountId = aws sts get-caller-identity --query "Account" --output text
if ($LASTEXITCODE -ne 0) { throw "Could not resolve the AWS account. Check your credentials." }

$s3Bucket = "boxdropaz-deploy-$Environment-$accountId"

$templateParams = @(
    "StageName=$stageName"
    "TablePrefix=$tablePrefix"
    "ManageTables=$manageTables"
    "SiteBaseUrl=$siteBaseUrl"
)

# Site contact info — always pass explicitly so stack updates don't keep a stale SupportPhone
# (CloudFormation retains existing parameter values when omitted from deploy parameters).
$siteSettingsFile = if (Test-Path "BoxDropAz.Web/appsettings.json") {
    "BoxDropAz.Web/appsettings.json"
} else {
    "appsettings.example.json"
}
if (Test-Path $siteSettingsFile) {
    $appSettings = Get-Content $siteSettingsFile -Raw | ConvertFrom-Json
    $site = $appSettings.Site
    if ($site.SupportPhone) { $templateParams += "SupportPhone=$($site.SupportPhone)" }
    if ($site.SupportEmail) { $templateParams += "SupportEmail=$($site.SupportEmail)" }
    if ($site.AdminEmail) { $templateParams += "AdminEmail=$($site.AdminEmail)" }
    $seo = $appSettings.Seo
    if ($seo -and $seo.GoogleAdsId) { $templateParams += "GoogleAdsId=$($seo.GoogleAdsId)" }
    if ($seo -and $seo.GoogleAdsPurchaseLabel) { $templateParams += "GoogleAdsPurchaseLabel=$($seo.GoogleAdsPurchaseLabel)" }
}

# Seed accounts (optional). Prefer Seed section from appsettings.Development.json so both
# environments get a known SaaS admin on first boot without committing secrets to the template.
$seedFile = "BoxDropAz.Web/appsettings.Development.json"
if (Test-Path $seedFile) {
    $seed = (Get-Content $seedFile -Raw | ConvertFrom-Json).Seed
    if ($seed) {
        $templateParams += "SeedAdminEmail=$($seed.AdminEmail)"
        $templateParams += "SeedAdminPassword=$($seed.AdminPassword)"
        $templateParams += "SeedRegionalAdminEmail=$($seed.RegionalAdminEmail)"
        $templateParams += "SeedRegionalAdminPassword=$($seed.RegionalAdminPassword)"
        $templateParams += "SeedWorkerEmail=$($seed.WorkerEmail)"
        $templateParams += "SeedWorkerPassword=$($seed.WorkerPassword)"
        $templateParams += "SeedRealtorEmail=$($seed.RealtorEmail)"
        $templateParams += "SeedRealtorPassword=$($seed.RealtorPassword)"
        $templateParams += "SeedCustomerEmail=$($seed.CustomerEmail)"
        $templateParams += "SeedCustomerPassword=$($seed.CustomerPassword)"
    }
}

$stripeSettingsFile = "stripe-settings.$Environment.json"
if (Test-Path $stripeSettingsFile) {
    $stripe = (Get-Content $stripeSettingsFile -Raw | ConvertFrom-Json).Stripe

    $templateParams += "StripeSecretKey=$($stripe.SecretKey)"
    $templateParams += "StripePublishableKey=$($stripe.PublishableKey)"
    $templateParams += "StripeWebhookSecret=$($stripe.WebhookSecret)"
    $templateParams += "StripeRealtorStarterMonthlyPriceId=$($stripe.RealtorStarterMonthlyPriceId)"
    $templateParams += "StripeRealtorProfessionalMonthlyPriceId=$($stripe.RealtorProfessionalMonthlyPriceId)"
    $templateParams += "StripeRealtorBrokerageMonthlyPriceId=$($stripe.RealtorBrokerageMonthlyPriceId)"
    $collectTax = if ($null -ne $stripe.PSObject.Properties["CollectTax"] -and $stripe.CollectTax -eq $true) { "true" } else { "false" }
    $templateParams += "StripeCollectTax=$collectTax"

    if ($Environment -eq "prod" -and $stripe.SecretKey -notmatch "^sk_live_") {
        throw "stripe-settings.prod.json must hold a live key (sk_live_...). Refusing to deploy prod against Stripe test mode."
    }
    if ($Environment -eq "dev" -and $stripe.SecretKey -match "^sk_live_") {
        throw "stripe-settings.dev.json holds a live key. Dev must use Stripe test/sandbox keys (sk_test_...)."
    }
    if ($Environment -eq "dev" -and $stripe.SecretKey -and $stripe.SecretKey -notmatch "^sk_test_") {
        Write-Warning "stripe-settings.dev.json SecretKey does not look like sk_test_*. Double-check before relying on sandbox."
    }
}
else {
    Write-Warning "$stripeSettingsFile not found. Deploying without Stripe keys; checkout will not work."
}

$authSettingsFile = "auth-settings.$Environment.json"
if (Test-Path $authSettingsFile) {
    $auth = (Get-Content $authSettingsFile -Raw | ConvertFrom-Json).Authentication
    if ($auth.Google) {
        $templateParams += "GoogleClientId=$($auth.Google.ClientId)"
        $templateParams += "GoogleClientSecret=$($auth.Google.ClientSecret)"
    }
    if ($auth.Facebook) {
        $templateParams += "FacebookAppId=$($auth.Facebook.AppId)"
        $templateParams += "FacebookAppSecret=$($auth.Facebook.AppSecret)"
    }
}
else {
    Write-Warning "$authSettingsFile not found. Deploying without Google/Facebook login credentials; social buttons will be hidden."
}

Write-Host "--- Deploying BoxDropAz to $Environment ---" -ForegroundColor Cyan
Write-Host "Stack:        $stackName"
Write-Host "Stage:        $stageName"
Write-Host "Table prefix: $tablePrefix"
Write-Host "ManageTables: $manageTables"
Write-Host "Domain:       $domainName"
Write-Host "Artifacts:    s3://$s3Bucket"

# Ensure the deployment bucket exists. "already owned by you" is normal on a re-deploy.
$previousPreference = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
aws s3api head-bucket --bucket $s3Bucket --region $region 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    aws s3 mb "s3://$s3Bucket" --region $region 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to create the deployment bucket $s3Bucket" }
    Write-Host "Created deployment bucket: $s3Bucket"
}
else {
    Write-Host "Deployment bucket already exists."
}
$ErrorActionPreference = $previousPreference

Write-Host "`nPublishing and deploying the stack..." -ForegroundColor Cyan
dotnet lambda deploy-serverless `
    --stack-name $stackName `
    --template serverless.template `
    --template-parameters ($templateParams -join ";") `
    --s3-bucket $s3Bucket `
    --region $region

if ($LASTEXITCODE -ne 0) {
    Write-Error "Deployment failed."
    exit $LASTEXITCODE
}

Write-Host "`nReading stack outputs..." -ForegroundColor Cyan
$stack = aws cloudformation describe-stacks --stack-name $stackName --region $region | ConvertFrom-Json
$outputs = $stack.Stacks[0].Outputs
$apiUrl = ($outputs | Where-Object { $_.OutputKey -eq "ApiUrl" }).OutputValue
$webhookUrl = ($outputs | Where-Object { $_.OutputKey -eq "StripeWebhookUrl" }).OutputValue

# https://{api-id}.execute-api.{region}.amazonaws.com/{stage}/
$apiId = $apiUrl.Split('/')[2].Split('.')[0]

Write-Host "API id:  $apiId"
Write-Host "API url: $apiUrl"

if (-not $SkipDomainMapping) {
    $hostedZoneId = "Z050630219I3UBXJNGEWZ"
    $certArn = aws acm list-certificates --region $region `
        --query "CertificateSummaryList[?DomainName=='boxdropaz.com' && Status=='ISSUED'].CertificateArn | [0]" `
        --output text

    if ([string]::IsNullOrWhiteSpace($certArn) -or $certArn -eq "None") {
        Write-Warning "No ISSUED ACM certificate for boxdropaz.com in $region. Skipping custom domain setup."
    }
    else {
        # Dev is one host. Prod prefers www, and also maps the apex when present.
        $domainsToMap = @($domainName)
        if ($Environment -eq "prod") {
            $domainsToMap += "boxdropaz.com"
        }

        foreach ($targetDomain in $domainsToMap) {
            Write-Host "`nEnsuring custom domain $targetDomain..." -ForegroundColor Cyan

            $ErrorActionPreference = "SilentlyContinue"
            $domainJson = aws apigatewayv2 get-domain-name --domain-name $targetDomain --region $region 2>$null
            $domainExists = $LASTEXITCODE -eq 0 -and $domainJson
            $ErrorActionPreference = $previousPreference

            if (-not $domainExists) {
                aws apigatewayv2 create-domain-name `
                    --domain-name $targetDomain `
                    --domain-name-configurations "CertificateArn=$certArn,EndpointType=REGIONAL,SecurityPolicy=TLS_1_2" `
                    --region $region | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Write-Warning "Could not create API Gateway domain $targetDomain."
                    continue
                }
                Write-Host "Created API Gateway custom domain $targetDomain." -ForegroundColor Green
                Start-Sleep -Seconds 3
                $domainJson = aws apigatewayv2 get-domain-name --domain-name $targetDomain --region $region
            }
            else {
                Write-Host "API Gateway custom domain already exists." -ForegroundColor Yellow
            }

            $domainInfo = ($domainJson | ConvertFrom-Json).DomainNameConfigurations[0]
            $apiGwDomain = $domainInfo.ApiGatewayDomainName
            $apiGwZone = $domainInfo.HostedZoneId

            # Alias A record in Route 53 so the hostname resolves.
            $aliasBatch = @{
                Changes = @(
                    @{
                        Action = "UPSERT"
                        ResourceRecordSet = @{
                            Name = "$targetDomain."
                            Type = "A"
                            AliasTarget = @{
                                HostedZoneId = $apiGwZone
                                DNSName = $apiGwDomain
                                EvaluateTargetHealth = $false
                            }
                        }
                    }
                )
            }
            $aliasPath = Join-Path $env:TEMP "bd-alias-$targetDomain.json"
            [System.IO.File]::WriteAllText(
                $aliasPath,
                ($aliasBatch | ConvertTo-Json -Depth 8 -Compress),
                (New-Object System.Text.UTF8Encoding $false))
            aws route53 change-resource-record-sets `
                --hosted-zone-id $hostedZoneId `
                --change-batch "file://$aliasPath" | Out-Null
            Write-Host "Route 53 alias upserted for $targetDomain -> $apiGwDomain"

            $ErrorActionPreference = "SilentlyContinue"
            $mappingsJson = aws apigatewayv2 get-api-mappings --domain-name $targetDomain --region $region 2>$null
            $ErrorActionPreference = $previousPreference

            $existing = $null
            if ($mappingsJson) {
                $existing = ($mappingsJson | ConvertFrom-Json).Items |
                    Where-Object { $_.ApiId -eq $apiId -and $_.Stage -eq $stageName }
            }

            if ($null -eq $existing) {
                aws apigatewayv2 create-api-mapping `
                    --domain-name $targetDomain `
                    --api-id $apiId `
                    --stage $stageName `
                    --region $region | Out-Null
                Write-Host "API mapping created for $targetDomain -> $apiId/$stageName." -ForegroundColor Green
            }
            else {
                Write-Host "API mapping already exists for $targetDomain." -ForegroundColor Yellow
            }
        }
    }
}

$stripeMode = if ($Environment -eq "prod") { "live" } else { "test (sandbox)" }

Write-Host "`nDeployment complete." -ForegroundColor Green
Write-Host "Site:           $siteBaseUrl" -ForegroundColor Yellow
Write-Host "Stripe mode:    $stripeMode" -ForegroundColor Yellow
Write-Host "Stripe webhook: $webhookUrl" -ForegroundColor Yellow
Write-Host ""
Write-Host "Register that webhook URL in the Stripe $stripeMode dashboard for these events:" -ForegroundColor Yellow
Write-Host "  checkout.session.completed, invoice.paid, customer.subscription.updated," -ForegroundColor Yellow
Write-Host "  customer.subscription.deleted, payment_intent.payment_failed" -ForegroundColor Yellow
