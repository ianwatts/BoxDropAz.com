namespace BoxDropAz.Web.Services;

/// <summary>
/// Absolute links for email and for webhook code, which has no HttpContext to generate them from.
/// </summary>
public sealed class SiteUrls
{
    private readonly string _baseUrl;

    public SiteUrls(IConfiguration config)
    {
        _baseUrl = (config["Site:BaseUrl"] ?? "https://boxdropaz.com").TrimEnd('/');
    }

    public string BaseUrl => _baseUrl;

    public string Absolute(string relativePath)
        => $"{_baseUrl}/{relativePath.TrimStart('/')}";

    public string OrderDetail(string orderId) => Absolute($"dashboard/order/{orderId}");

    public string AdminOrder(string orderId) => Absolute($"admin/orders/{orderId}");

    public string AdminInventory(string regionId) => Absolute($"admin/inventory?region={regionId}");

    public string GiftClaim(string claimToken) => Absolute($"gift/claim/{claimToken}");

    public string AgentDashboard() => Absolute("agent/dashboard");

    public string Login() => Absolute("account/login");
}
