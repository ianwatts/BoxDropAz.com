using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;

namespace BoxDropAz.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API") != null)
        {
            // Running as a Lambda with custom runtime (provided.al2023)
            var lambdaEntryPoint = new LambdaEntryPoint();
            using var bootstrap = LambdaBootstrapBuilder.Create<APIGatewayProxyRequest, APIGatewayProxyResponse>(
                lambdaEntryPoint.FunctionHandlerAsync, new DefaultLambdaJsonSerializer())
                .Build();
            await bootstrap.RunAsync();
        }
        else
        {
            // Running as a standard web application
            CreateHostBuilder(args).Build().Run();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                var env = hostingContext.HostingEnvironment;
                var rootPath = Path.Combine(env.ContentRootPath, "..");

                string stripeFile = env.IsDevelopment()
                    ? "stripe-settings.dev.json"
                    : "stripe-settings.prod.json";

                // Try to load from project root or parent root (repo root)
                config.AddJsonFile(stripeFile, optional: true, reloadOnChange: true);
                config.AddJsonFile(Path.Combine(rootPath, stripeFile), optional: true, reloadOnChange: true);

                string authFile = env.IsDevelopment()
                    ? "auth-settings.dev.json"
                    : "auth-settings.prod.json";

                config.AddJsonFile(authFile, optional: true, reloadOnChange: true);
                config.AddJsonFile(Path.Combine(rootPath, authFile), optional: true, reloadOnChange: true);

                config.Build();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                webBuilder.ConfigureKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = 20 * 1024 * 1024;
                    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2);
                    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
                });
            });
}
