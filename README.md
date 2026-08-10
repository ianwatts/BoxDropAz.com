# BoxDropAz.com

Reusable moving crate rental for the Phoenix East Valley and Pinal County, with a B2B realtor
closing-gift portal. Serverless ASP.NET Core MVC on AWS Lambda, DynamoDB, and Stripe.

## Projects

- `BoxDropAz.Web` - ASP.NET Core MVC app (public site, customer booking, realtor portal, worker
  manifest, regional admin, SaaS admin). Runs locally under Kestrel or in AWS Lambda via
  `Amazon.Lambda.AspNetCoreServer`.
- `BoxDropAz.Core` - shared models, DynamoDB table naming, pricing and zone services.

## Roles

| Role | Entry point | Purpose |
| --- | --- | --- |
| `Customer` | `/Dashboard` | Books crate bundles, extends rentals, manages card on file |
| `Realtor` | `/Agent` | Subscribes, tracks gift credits, gifts crates to closing clients |
| `Worker` | `/Worker` | Daily delivery and pickup manifest, damage reporting |
| `RegionalAdmin` | `/Admin` | Revenue graphs, order and user management for one region |
| `SaaSAdmin` | `/SaaSAdmin` | Everything, plus region management across all regions |

## Local run

```powershell
dotnet run --project .\BoxDropAz.Web\BoxDropAz.Web.csproj
```

Then browse to <http://localhost:5021>.

You need AWS credentials configured for DynamoDB (region `us-west-2` by default). With
`DynamoDB:AutoCreateTables = true` the app creates every table it needs on first boot using the
`BoxDropAz_Dev_` prefix, then seeds regions, crate packages, roles, and one test user per role from
the `Seed:*` settings.

Stripe is optional locally. With an empty `Stripe:SecretKey` the site still runs and every page is
browsable; checkout actions surface a "payments not configured" notice instead of calling Stripe.

### Configuration

`BoxDropAz.Web/appsettings.json` is gitignored because it holds secrets. Copy
`appsettings.example.json` over it and fill in your values. Environment variables win over
config in every case, following these names:

- `AWS_REGION`, `DYNAMODB_TABLE_PREFIX`, `DATA_PROTECTION_BUCKET`
- `STRIPE_SECRET_KEY`, `STRIPE_PUBLISHABLE_KEY`, `STRIPE_WEBHOOK_SECRET`
- `STRIPE_COLLECTTAX` (`true` / `false`; defaults to paused / `false`)
- `STRIPE_REALTORSTARTERMONTHLYPRICEID` and the other `STRIPE_<KEY>` price id overrides
- `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD` and the other `SEED_*` pairs

### Stripe setup

Create `stripe-settings.dev.json` at the repo root with at least your test secret key, then create
the three realtor subscription products and prices:

```powershell
.\create-stripe-prices.ps1 -Environment dev
```

The script prints a complete `stripe-settings.dev.json` block to paste back in. Pass `-DryRun` first
if you want to see the Stripe CLI calls without creating anything. The app loads that file
automatically in Development, and `deploy-to.ps1` passes the same values into CloudFormation.

For webhooks locally:

```powershell
stripe listen --forward-to http://localhost:5021/stripe/webhook
```

The `whsec_...` value that command prints goes in `WebhookSecret`. Without it the webhook endpoint
rejects every delivery, since it verifies the signature before doing anything.

## Deployment (AWS Lambda / SAM)

`serverless.template` drives API Gateway, DynamoDB, S3, and the web Lambda. Deploy with
Amazon.Lambda.Tools:

```powershell
dotnet tool install -g Amazon.Lambda.Tools
.\deploy-dev.bat
.\deploy-prod.bat
```

Or call the script directly:

```powershell
.\deploy-to.ps1 -Environment dev
.\deploy-to.ps1 -Environment prod
```

- **Handler**: `bootstrap` on `provided.al2023`, arm64, 1024 MB, 900 s timeout
- **dev**: stack `BoxDropAz-dev`, stage `dev`, prefix `BoxDropAz_Dev_`, site `https://dev.boxdropaz.com`, Stripe **test/sandbox** via `stripe-settings.dev.json`
- **prod**: stack `BoxDropAz-prod`, stage `prod`, prefix `BoxDropAz_Prod_`, site `https://www.boxdropaz.com`, Stripe **live** via `stripe-settings.prod.json`. `ManageTables=false` after the first deploy so CloudFormation never touches live tables

On a first-ever prod deploy the tables do not exist yet; `deploy-to.ps1` detects a missing stack and enables table creation automatically. Every table also carries `DeletionPolicy: Retain`, so deleting the stack leaves order
history behind rather than destroying it.

Custom domains are mapped after the stack deploys via `aws apigatewayv2 create-api-mapping`; the
domain itself must already exist in API Gateway with an ACM certificate (`dev.boxdropaz.com` for
dev, `www.boxdropaz.com` and optionally `boxdropaz.com` for prod). Use `-SkipDomainMapping`
until it does.

The stack outputs `StripeWebhookUrl`. Register it in the Stripe dashboard for
`checkout.session.completed`, `invoice.paid`, `customer.subscription.updated`,
`customer.subscription.deleted`, and `payment_intent.payment_failed`, then put the resulting signing
secret in `stripe-settings.<env>.json` and re-deploy.

## Pricing model

Base packages are a 7 day rental including free delivery and pickup in Zone A:

| Package | Crates | Dollies | Base | Extra week |
| --- | --- | --- | --- | --- |
| Studio / Apartment | 20 | 1 | $89 | $45 |
| 1-2 Bedroom | 35 | 2 | $129 | $65 |
| 2-3 Bedroom | 50 | 2 | $169 | $85 |
| 3-4 Bedroom | 75 | 3 | $219 | $110 |
| 4-5 Bedroom | 100 | 4 | $299 | $150 |

Damage fees charged to the card on file: $40 per crate, $95 per dolly, $25 missed pickup, $3 per
crate requiring deep cleaning. Realtor plans are $59/mo for $75 monthly gift credit, $129/mo for
$175, and $299/mo for $425 with five agent seats. Unused credit rolls over up to three months.

All of this is data, not code: pricing lives in the `CratePackage` table per region and is editable
from the SaaS admin area.
