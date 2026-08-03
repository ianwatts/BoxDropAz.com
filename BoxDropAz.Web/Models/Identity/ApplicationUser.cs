using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Models.Identity;

[DynamoDBTable(DynamoDbTableNames.ApplicationUser)]
public class ApplicationUser : IdentityUser
{
    [DynamoDBHashKey]
    [DynamoDBProperty("Id")]
    public override string Id { get; set; } = Guid.NewGuid().ToString();

    [DynamoDBProperty("UserName")]
    public override string? UserName { get; set; }

    [DynamoDBProperty("NormalizedUserName")]
    public override string? NormalizedUserName { get; set; }

    [DynamoDBProperty("Email")]
    public override string? Email { get; set; }

    [DynamoDBProperty("NormalizedEmail")]
    public override string? NormalizedEmail { get; set; }

    [DynamoDBProperty("EmailConfirmed")]
    public override bool EmailConfirmed { get; set; }

    [DynamoDBProperty("PasswordHash")]
    public override string? PasswordHash { get; set; }

    [DynamoDBProperty("SecurityStamp")]
    public override string? SecurityStamp { get; set; }

    [DynamoDBProperty("ConcurrencyStamp")]
    public override string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();

    [DynamoDBProperty("PhoneNumber")]
    public override string? PhoneNumber { get; set; }

    [DynamoDBProperty("FullName")]
    public string? FullName { get; set; }

    /// <summary>Brokerage or company name, shown on co-branded inserts.</summary>
    [DynamoDBProperty("CompanyName")]
    public string? CompanyName { get; set; }

    /// <summary>Scopes everything a regional admin or worker can see. Empty means SaaS-wide.</summary>
    [DynamoDBProperty("RegionId")]
    public string? RegionId { get; set; }

    [DynamoDBProperty("StripeCustomerId")]
    public string? StripeCustomerId { get; set; }

    /// <summary>Card kept on file for extensions and damage charges, per the rental agreement.</summary>
    [DynamoDBProperty("DefaultPaymentMethodId")]
    public string? DefaultPaymentMethodId { get; set; }

    [DynamoDBProperty("CardBrand")]
    public string? CardBrand { get; set; }

    [DynamoDBProperty("CardLast4")]
    public string? CardLast4 { get; set; }

    [DynamoDBProperty("IsDisabled")]
    public bool IsDisabled { get; set; }

    [DynamoDBProperty("CreatedAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("LastLoginAtUtc")]
    public DateTime? LastLoginAtUtc { get; set; }

    [DynamoDBIgnore]
    public string DisplayName => !string.IsNullOrWhiteSpace(FullName) ? FullName : (Email ?? UserName ?? Id);
}
