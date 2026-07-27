using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Web.Scripts;

public static class DynamoDbSetup
{
    private sealed record IndexSpec(string Name, string HashKey, string? RangeKey);

    public static async Task AutoCreateTablesAsync(IAmazonDynamoDB ddb, CancellationToken ct = default)
    {
        Console.WriteLine("DynamoDB: ensuring required tables exist...");

        // Identity tables
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.ApplicationUser), "Id", null, ct);
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.ApplicationRole), "Id", null, ct);
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.UserToRoles), "UserId", "RoleId", ct);
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.UserLogin), "Id", null, ct,
            new IndexSpec(DynamoDbTableNames.UserLoginByUserIndex, "UserId", null));

        // Catalog and geography
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.Region), "Id", null, ct);
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.CratePackage), "RegionId", "PackageId", ct);

        // Orders. The manifest and revenue views are all region scoped, so every access path is an
        // index query rather than a scan.
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.RentalOrder), "OrderId", null, ct,
            new IndexSpec(DynamoDbTableNames.RentalOrderByRegionAndDeliveryDateIndex, "RegionId", "DeliveryDate"),
            new IndexSpec(DynamoDbTableNames.RentalOrderByRegionAndPickupDateIndex, "RegionId", "PickupDate"),
            new IndexSpec(DynamoDbTableNames.RentalOrderByUserIndex, "UserId", "CreatedAtUtc"),
            new IndexSpec(DynamoDbTableNames.RentalOrderByRegionAndCreatedIndex, "RegionId", "CreatedAtUtc"));

        // Realtor gifting
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.RealtorSubscription), "UserId", null, ct);
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.GiftOrder), "GiftId", null, ct,
            new IndexSpec(DynamoDbTableNames.GiftOrderByRealtorIndex, "RealtorUserId", "CreatedAtUtc"),
            new IndexSpec(DynamoDbTableNames.GiftOrderByClaimTokenIndex, "ClaimToken", null));
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.CreditLedger), "UserId", "EntryId", ct);

        // Billing plumbing
        await EnsureTableAsync(ddb, Table(DynamoDbTableNames.StripeEvent), "EventId", null, ct);

        Console.WriteLine("DynamoDB: table check complete.");
    }

    private static string Table(string baseName) => DynamoDbTableNames.GetTableName(baseName);

    private static async Task EnsureTableAsync(
        IAmazonDynamoDB ddb,
        string tableName,
        string hashKeyName,
        string? rangeKeyName,
        CancellationToken ct,
        params IndexSpec[] indexes)
    {
        try
        {
            await ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct);
            return; // Exists
        }
        catch (ResourceNotFoundException)
        {
            // Create below
        }

        Console.WriteLine($"DynamoDB: creating table: {tableName}");

        // Every key attribute must be declared exactly once, including ones shared between indexes.
        var attributeNames = new HashSet<string>(StringComparer.Ordinal) { hashKeyName };
        if (!string.IsNullOrWhiteSpace(rangeKeyName))
        {
            attributeNames.Add(rangeKeyName);
        }

        var keySchema = new List<KeySchemaElement> { new(hashKeyName, KeyType.HASH) };
        if (!string.IsNullOrWhiteSpace(rangeKeyName))
        {
            keySchema.Add(new KeySchemaElement(rangeKeyName, KeyType.RANGE));
        }

        var gsis = new List<GlobalSecondaryIndex>();
        foreach (var index in indexes)
        {
            attributeNames.Add(index.HashKey);
            var indexSchema = new List<KeySchemaElement> { new(index.HashKey, KeyType.HASH) };
            if (!string.IsNullOrWhiteSpace(index.RangeKey))
            {
                attributeNames.Add(index.RangeKey);
                indexSchema.Add(new KeySchemaElement(index.RangeKey, KeyType.RANGE));
            }

            gsis.Add(new GlobalSecondaryIndex
            {
                IndexName = index.Name,
                KeySchema = indexSchema,
                Projection = new Projection { ProjectionType = ProjectionType.ALL }
            });
        }

        var attributeDefinitions = attributeNames
            .Select(name => new AttributeDefinition { AttributeName = name, AttributeType = ScalarAttributeType.S })
            .ToList();

        await ddb.CreateTableAsync(new CreateTableRequest
        {
            TableName = tableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = attributeDefinitions,
            KeySchema = keySchema,
            GlobalSecondaryIndexes = gsis.Count > 0 ? gsis : null
        }, ct);

        // Wait for ACTIVE
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            try
            {
                var described = await ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct);
                if (string.Equals(described.Table.TableStatus, TableStatus.ACTIVE, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"DynamoDB: table ACTIVE: {tableName}");
                    return;
                }
            }
            catch (ResourceNotFoundException)
            {
                // still propagating
            }
        }
    }
}
