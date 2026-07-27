using System.Net;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Amazon.SimpleEmail;
using BoxDropAz.Core.Data;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Data;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Scripts;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web;

public sealed class Startup
{
    private readonly IWebHostEnvironment _env;

    public Startup(IConfiguration configuration, IWebHostEnvironment env)
    {
        Configuration = configuration;
        _env = env;
    }

    public IConfiguration Configuration { get; }

    private static bool IsLambdaEnvironment()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_EXECUTION_ENV"))
           || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LAMBDA_TASK_ROOT"));

    public void ConfigureServices(IServiceCollection services)
    {
        // DynamoDB table prefix
        var tablePrefix =
            Environment.GetEnvironmentVariable("DYNAMODB_TABLE_PREFIX")
            ?? Configuration.GetValue<string>("DynamoDB:TablePrefix", "BoxDropAz_Dev_");

        if (IsLambdaEnvironment())
        {
            DynamoDbTableNames.SetTablePrefix(tablePrefix!);
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            }
        }
        else
        {
            DynamoDbTableNames.Initialize(Configuration);
        }

        // AWS configuration
        var awsRegion = Configuration.GetValue<string>("AWS:Region", "us-west-2")!;
        var awsOptions = new AWSOptions { Region = RegionEndpoint.GetBySystemName(awsRegion) };
        services.AddDefaultAWSOptions(awsOptions);

        services.AddSingleton<IAmazonDynamoDB>(_ =>
        {
            var config = new AmazonDynamoDBConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(awsRegion),
                Timeout = TimeSpan.FromMinutes(5)
            };
            return new AmazonDynamoDBClient(config);
        });

        services.AddAWSService<IAmazonS3>();
        services.AddAWSService<IAmazonSimpleEmailService>();

        // Stripe
        var stripeKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? Configuration["Stripe:SecretKey"];
        if (!string.IsNullOrWhiteSpace(stripeKey))
        {
            Stripe.StripeConfiguration.ApiKey = stripeKey;
        }

        // Allow overriding Price IDs via environment variables for dev/prod flexibility
        var stripeSection = Configuration.GetSection("Stripe");
        foreach (var child in stripeSection.GetChildren())
        {
            var envVarName = $"STRIPE_{child.Key.ToUpperInvariant()}";
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                Configuration[$"Stripe:{child.Key}"] = envValue;
            }
        }

        // Data protection (S3 in Lambda, filesystem locally)
        if (IsLambdaEnvironment())
        {
            var bucketName = Environment.GetEnvironmentVariable("DATA_PROTECTION_BUCKET")
                             ?? Configuration.GetValue<string>("AWS:S3:DataProtectionBucket", "");
            var keyPrefix = "dataprotection-keys";

            services.AddDataProtection()
                .SetApplicationName("BoxDropAz");
            services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
                .Configure<IAmazonS3>((options, s3) =>
                {
                    options.XmlRepository = new S3XmlRepository(s3, bucketName!, keyPrefix);
                });

            services.Configure<AntiforgeryOptions>(options =>
            {
                options.Cookie.SecurePolicy = CookieSecurePolicy.None;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Name = ".AspNetCore.Antiforgery";
            });
        }
        else
        {
            // Use ContentRootPath for stable keys across dotnet run / VS / different outputs
            var keysPath = Path.Combine(_env.ContentRootPath, "DataProtection-Keys");
            Directory.CreateDirectory(keysPath);
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
                .SetApplicationName("BoxDropAz");
        }

        // App services
        services.AddMemoryCache();
        services.AddScoped<DynamoDbDataHelper>();
        services.AddScoped<IRegionService, RegionService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IGiftService, GiftService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IStripeGateway, StripeGateway>();
        services.AddScoped<IStripeEventStore, StripeEventStore>();
        services.AddScoped<OrderNotifier>();
        services.AddScoped<GiftNotifier>();
        services.AddScoped<OrderCheckoutService>();
        services.AddScoped<RentalExtensionService>();
        services.AddScoped<DamageChargeService>();
        services.AddSingleton<SiteUrls>();
        services.AddSingleton<PricingService>();
        services.AddScoped<RoleStore>();
        services.AddScoped<UserStore>();
        services.AddScoped<IUserStore<ApplicationUser>>(sp => sp.GetRequiredService<UserStore>());
        services.AddScoped<IRoleStore<ApplicationRole>>(sp => sp.GetRequiredService<RoleStore>());

        services.AddScoped<IEmailService, SesEmailService>();
        services.AddScoped<IEmailSender<ApplicationUser>, SesEmailService>();

        // Identity
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
            })
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.SlidingExpiration = true;
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("SaaSAdmin", p => p.RequireRole(Roles.SaaSAdmin));
            options.AddPolicy("AnyAdmin", p => p.RequireRole(Roles.RegionalAdmin, Roles.SaaSAdmin));
            options.AddPolicy("Fulfillment", p => p.RequireRole(Roles.Worker, Roles.RegionalAdmin, Roles.SaaSAdmin));
        });

        services.AddControllersWithViews();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            // ASP.NET Core 8+ ignores X-Forwarded-* from unknown proxies. Behind API Gateway we must
            // trust the proxy so cookies and redirect URLs use the public scheme/host.
            if (IsLambdaEnvironment())
            {
                options.KnownProxies.Add(IPAddress.IPv6Any);
            }
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IAmazonDynamoDB ddb)
    {
        app.UseForwardedHeaders();

        // Force HTTPS scheme when behind API Gateway so generated links and cookies are correct
        if (IsLambdaEnvironment())
        {
            app.Use((context, next) =>
            {
                if (string.Equals(context.Request.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                {
                    context.Request.Scheme = "https";
                }
                return next();
            });
        }

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var feature = context.Features.Get<IExceptionHandlerPathFeature>();
                    var ex = feature?.Error;
                    if (ex != null)
                    {
                        var logger = context.RequestServices.GetRequiredService<ILogger<Startup>>();
                        logger.LogError(ex, "Unhandled exception at {Path}: {Message}", context.Request.Path, ex.Message);
                    }
                    context.Response.Redirect("/Home/Error");
                    await Task.CompletedTask;
                });
            });
            app.UseHsts();
        }

        if (!IsLambdaEnvironment() && !env.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        if (IsLambdaEnvironment())
        {
            app.Use((context, next) =>
            {
                context.Request.PathBase = string.Empty;
                return next();
            });
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                var headers = ctx.Context.Response.GetTypedHeaders();
                headers.CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
                {
                    Public = true,
                    MaxAge = TimeSpan.FromDays(365)
                };
            }
        });

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");
        });

        // Optional: auto-create DynamoDB tables for local/dev
        try
        {
            var autoCreate =
                Environment.GetEnvironmentVariable("DynamoDB__AutoCreateTables") == "true"
                || Configuration.GetValue<bool>("DynamoDB:AutoCreateTables", false);

            if (autoCreate)
            {
                Console.WriteLine($"DynamoDB auto-create enabled. Prefix='{DynamoDbTableNames.GetTablePrefix()}' Region='{Configuration.GetValue<string>("AWS:Region", "us-west-2")}'");
                DynamoDbSetup.AutoCreateTablesAsync(ddb).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: DynamoDB auto-create failed: {ex.Message}");
        }

        // Seed roles, test users, regions and the crate catalog
        try
        {
            using var scope = app.ApplicationServices.CreateScope();
            CatalogSeeder.SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
            IdentitySeeder.SeedAsync(scope.ServiceProvider, Configuration).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // In Lambda, seeding failures shouldn't crash the app
            Console.WriteLine($"Warning: identity seeding failed (non-critical): {ex.Message}");
            if (env.IsDevelopment())
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
