using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;
using BoxDropAz.Core.Models.Inventory;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Data;

namespace BoxDropAz.Web.Services;

public sealed record InventoryProjectionRow(
    DateOnly Date,
    int TotesGoingOut,
    int TotesReturning,
    int DolliesGoingOut,
    int DolliesReturning,
    int IndexCardsGoingOut,
    int ProjectedTotes,
    int ProjectedDollies,
    int ProjectedIndexCards,
    int ToteShortage,
    int DollyShortage,
    int IndexCardShortage);

public sealed class InventoryAssessment
{
    public required InventoryRecord Inventory { get; init; }
    public int TotesInField { get; init; }
    public int DolliesInField { get; init; }
    public int TotesAvailableNow => Inventory.TotalTotes - TotesInField;
    public int DolliesAvailableNow => Inventory.TotalDollies - DolliesInField;
    public int IndexCardsAvailableNow => Inventory.TotalIndexCards;
    public List<InventoryProjectionRow> Projection { get; init; } = new();
    public List<InventoryRecord> OpenRestockTasks { get; set; } = new();

    /// <summary>
    /// False when the Inventory table is unreachable, which means totals and restock tasks are
    /// calculated but cannot be saved.
    /// </summary>
    public bool StorageReady { get; set; } = true;
}

public sealed class InventoryService
{
    public const int IndexCardsPerPack = 300;
    public const int CardHoldersPerPack = 56;

    public const string ToteProductUrl =
        "https://www.homedepot.com/p/327528802";

    public const string DollyProductUrl =
        "https://www.homedepot.com/p/336630042";

    public const string IndexCardProductUrl = "https://www.amazon.com/dp/B0FQNDBJSW";

    public const string CardHolderProductUrl = "https://www.amazon.com/dp/B0F1TC88CJ";

    private readonly DynamoDbDataHelper _data;
    private readonly IOrderService _orders;
    private readonly IRegionService _regions;
    private readonly StaffNotifier _staff;
    private readonly SiteUrls _urls;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        DynamoDbDataHelper data,
        IOrderService orders,
        IRegionService regions,
        StaffNotifier staff,
        SiteUrls urls,
        ILogger<InventoryService> logger)
    {
        _data = data;
        _orders = orders;
        _regions = regions;
        _staff = staff;
        _urls = urls;
        _logger = logger;
    }

    public async Task<InventoryAssessment> GetAssessmentAsync(
        string regionId,
        bool reconcileTasks = true,
        CancellationToken ct = default)
    {
        var (records, storageReady) = await GetRecordsAsync(regionId, ct);
        var storedInventory = records.FirstOrDefault(r => r.RecordId == InventoryRecord.SummaryRecordId);
        var needsSummarySave = storedInventory is null || !storedInventory.IsConfigured;
        var inventory = storedInventory ?? NewSummary(regionId);
        var canPersist = storageReady;
        if (!inventory.IsConfigured)
        {
            inventory.IsConfigured = true;
        }

        if (needsSummarySave && canPersist)
        {
            try
            {
                using var context = _data.CreateContext();
                await context.SaveAsync(inventory, ct);
            }
            catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException)
            {
                canPersist = false;
                _logger.LogError(
                    "Inventory table {Table} is missing, so inventory for region {RegionId} cannot be saved",
                    DynamoDbTableNames.GetTableName(DynamoDbTableNames.Inventory),
                    regionId);
            }
        }

        var tasks = records
            .Where(r => r.RecordType == InventoryRecord.RestockType
                        && r.Status == InventoryTaskStatus.Open)
            .OrderBy(r => r.NeededByDate)
            .ToList();

        var orders = await _orders.GetRecentForRegionAsync(regionId, 0, ct);
        var assessment = BuildAssessment(inventory, tasks, orders);
        assessment.StorageReady = canPersist;

        if (reconcileTasks && canPersist)
        {
            var region = await _regions.GetByIdAsync(regionId, ct);
            await ReconcileTasksAsync(
                assessment,
                region?.Scheduling?.MinimumNoticeDays ?? 3,
                ct);
        }

        return assessment;
    }

    public async Task SetTotalsAsync(
        string regionId,
        int totalTotes,
        int totalDollies,
        int totalIndexCards,
        int totalCardHolders,
        CancellationToken ct = default)
    {
        using var context = _data.CreateContext();
        var inventory = await context.LoadAsync<InventoryRecord>(
                            regionId, InventoryRecord.SummaryRecordId, ct)
                        ?? NewSummary(regionId);

        inventory.TotalTotes = Math.Max(0, totalTotes);
        inventory.TotalDollies = Math.Max(0, totalDollies);
        inventory.TotalIndexCards = Math.Max(0, totalIndexCards);
        inventory.TotalCardHolders = Math.Max(0, totalCardHolders);
        inventory.IsConfigured = true;
        inventory.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveAsync(inventory, ct);

        await GetAssessmentAsync(regionId, reconcileTasks: true, ct);
    }

    public async Task<List<InventoryRecord>> GetRestockTasksAsync(
        string regionId,
        DateOnly throughDate,
        CancellationToken ct = default)
    {
        var assessment = await GetAssessmentAsync(regionId, reconcileTasks: true, ct);
        return assessment.OpenRestockTasks
            .Where(r => r.RecordType == InventoryRecord.RestockType
                        && r.Status == InventoryTaskStatus.Open
                        && DateOnly.TryParse(r.ActionByDate ?? r.NeededByDate, out var due)
                        && due <= throughDate)
            .OrderBy(r => r.NeededByDate)
            .ToList();
    }

    public async Task<bool> CompleteRestockTaskAsync(
        string regionId,
        string taskId,
        int totesReceived,
        int dolliesReceived,
        int cardHolderPacksReceived,
        int cardPacksReceived,
        string userId,
        string userName,
        CancellationToken ct = default)
    {
        using var context = _data.CreateContext();
        var task = await context.LoadAsync<InventoryRecord>(regionId, taskId, ct);
        if (task is null || task.RecordType != InventoryRecord.RestockType
                         || task.Status != InventoryTaskStatus.Open)
        {
            return false;
        }

        var inventory = await context.LoadAsync<InventoryRecord>(
                            regionId, InventoryRecord.SummaryRecordId, ct)
                        ?? NewSummary(regionId);

        // A newly purchased tote is usable only after its adhesive card holder is attached.
        var holdersAvailable = inventory.TotalCardHolders
                               + Math.Max(0, cardHolderPacksReceived) * CardHoldersPerPack;
        var readyTotes = Math.Min(Math.Max(0, totesReceived), holdersAvailable);
        inventory.TotalTotes += readyTotes;
        inventory.TotalCardHolders = holdersAvailable - readyTotes;
        inventory.TotalDollies += Math.Max(0, dolliesReceived);
        inventory.TotalIndexCards += Math.Max(0, cardPacksReceived) * IndexCardsPerPack;
        inventory.IsConfigured = true;
        inventory.UpdatedAtUtc = DateTime.UtcNow;

        task.Status = InventoryTaskStatus.Completed;
        task.CompletedAtUtc = DateTime.UtcNow;
        task.CompletedByUserId = userId;
        task.CompletedByName = userName;
        task.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveAsync(inventory, ct);
        await context.SaveAsync(task, ct);
        await GetAssessmentAsync(regionId, reconcileTasks: true, ct);
        return true;
    }

    public async Task<bool> ConsumeIndexCardAsync(string regionId, CancellationToken ct = default)
    {
        using var context = _data.CreateContext();
        var inventory = await context.LoadAsync<InventoryRecord>(
            regionId, InventoryRecord.SummaryRecordId, ct);
        if (inventory is null || !inventory.IsConfigured
            || inventory.TotalIndexCards < IndexCardsPerPack)
        {
            return false;
        }

        inventory.TotalIndexCards -= IndexCardsPerPack;
        inventory.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveAsync(inventory, ct);
        return true;
    }

    public async Task RecordMissingAssetsAsync(
        string regionId,
        int missingTotes,
        int missingDollies,
        CancellationToken ct = default)
    {
        if (missingTotes <= 0 && missingDollies <= 0)
        {
            return;
        }

        using var context = _data.CreateContext();
        var inventory = await context.LoadAsync<InventoryRecord>(
            regionId, InventoryRecord.SummaryRecordId, ct);
        if (inventory is null || !inventory.IsConfigured)
        {
            return;
        }

        inventory.TotalTotes = Math.Max(0, inventory.TotalTotes - Math.Max(0, missingTotes));
        inventory.TotalDollies = Math.Max(0, inventory.TotalDollies - Math.Max(0, missingDollies));
        inventory.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveAsync(inventory, ct);
        await GetAssessmentAsync(regionId, reconcileTasks: true, ct);
    }

    private async Task<(List<InventoryRecord> Records, bool StorageReady)> GetRecordsAsync(
        string regionId,
        CancellationToken ct)
    {
        try
        {
            using var context = _data.CreateContext();
            var records = await context.QueryAsync<InventoryRecord>(regionId).GetRemainingAsync(ct);
            return (records, true);
        }
        catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException)
        {
            // Local/dev can hit this before the Inventory table is provisioned.
            _logger.LogError(
                "Inventory table {Table} does not exist, so region {RegionId} has no tracked stock or restock tasks",
                DynamoDbTableNames.GetTableName(DynamoDbTableNames.Inventory),
                regionId);
            return (new List<InventoryRecord>(), false);
        }
    }

    private static InventoryAssessment BuildAssessment(
        InventoryRecord inventory,
        List<InventoryRecord> tasks,
        List<RentalOrder> orders)
    {
        var today = DeliveryWindows.TodayInArizona();
        var billable = orders
            .Where(o => o.Status is not OrderStatus.PendingPayment and not OrderStatus.Cancelled)
            .ToList();
        var inField = billable
            .Where(o => o.DeliveredAtUtc is not null && o.PickedUpAtUtc is null)
            .ToList();

        var totes = inventory.TotalTotes - inField.Sum(o => o.CrateCount);
        var dollies = inventory.TotalDollies - inField.Sum(o => o.DollyCount);
        var indexCards = inventory.TotalIndexCards;
        var events = new SortedDictionary<
            DateOnly,
            (int TotesOut, int TotesBack, int DolliesOut, int DolliesBack, int IndexCardsOut)>();

        foreach (var order in billable)
        {
            if (order.DeliveredAtUtc is null
                && DateOnly.TryParse(order.DeliveryDate, out var deliveryDate)
                && deliveryDate >= today)
            {
                AddEvent(
                    deliveryDate,
                    order.CrateCount,
                    0,
                    order.DollyCount,
                    0,
                    order.RequiresIndexCard && order.IndexCardIssuedAtUtc is null ? IndexCardsPerPack : 0);
            }

            if (order.PickedUpAtUtc is null
                && DateOnly.TryParse(order.PickupDate, out var pickupDate)
                && pickupDate >= today)
            {
                AddEvent(pickupDate, 0, order.CrateCount, 0, order.DollyCount, 0);
            }
        }

        var projection = new List<InventoryProjectionRow>();
        foreach (var (date, movement) in events)
        {
            // Deliveries are treated as leaving before same-day returns arrive. This conservative
            // ordering prevents a morning shortage from being hidden by an afternoon pickup.
            var minimumTotes = totes - movement.TotesOut;
            var minimumDollies = dollies - movement.DolliesOut;
            var minimumIndexCards = indexCards - movement.IndexCardsOut;
            var toteShortage = Math.Max(0, -minimumTotes);
            var dollyShortage = Math.Max(0, -minimumDollies);
            var indexCardShortage = Math.Max(0, -minimumIndexCards);
            totes = minimumTotes + movement.TotesBack;
            dollies = minimumDollies + movement.DolliesBack;
            indexCards = minimumIndexCards;

            projection.Add(new InventoryProjectionRow(
                date,
                movement.TotesOut,
                movement.TotesBack,
                movement.DolliesOut,
                movement.DolliesBack,
                movement.IndexCardsOut,
                totes,
                dollies,
                indexCards,
                toteShortage,
                dollyShortage,
                indexCardShortage));
        }

        return new InventoryAssessment
        {
            Inventory = inventory,
            TotesInField = inField.Sum(o => o.CrateCount),
            DolliesInField = inField.Sum(o => o.DollyCount),
            Projection = projection,
            OpenRestockTasks = tasks
        };

        void AddEvent(
            DateOnly date,
            int totesOut,
            int totesBack,
            int dolliesOut,
            int dolliesBack,
            int indexCardsOut)
        {
            events.TryGetValue(date, out var current);
            events[date] = (
                current.TotesOut + totesOut,
                current.TotesBack + totesBack,
                current.DolliesOut + dolliesOut,
                current.DolliesBack + dolliesBack,
                current.IndexCardsOut + indexCardsOut);
        }
    }

    private async Task ReconcileTasksAsync(
        InventoryAssessment assessment,
        int procurementLeadDays,
        CancellationToken ct)
    {
        var firstShortage = assessment.Projection
            .FirstOrDefault(p => p.ToteShortage > 0 || p.DollyShortage > 0 || p.IndexCardShortage > 0);
        var requiredTotes = assessment.Projection.Count == 0
            ? Math.Max(0, -assessment.TotesAvailableNow)
            : assessment.Projection.Max(p => p.ToteShortage);
        var requiredDollies = assessment.Projection.Count == 0
            ? Math.Max(0, -assessment.DolliesAvailableNow)
            : assessment.Projection.Max(p => p.DollyShortage);
        var requiredIndexCards = assessment.Projection.Count == 0
            ? 0
            : assessment.Projection.Max(p => p.IndexCardShortage);
        var requiredCardPacks = (int)Math.Ceiling(requiredIndexCards / (double)IndexCardsPerPack);
        var requiredCardHolders = Math.Max(0, requiredTotes - assessment.Inventory.TotalCardHolders);
        var requiredCardHolderPacks =
            (int)Math.Ceiling(requiredCardHolders / (double)CardHoldersPerPack);

        using var context = _data.CreateContext();
        if (requiredTotes == 0 && requiredDollies == 0 && requiredCardPacks == 0)
        {
            foreach (var task in assessment.OpenRestockTasks)
            {
                task.Status = InventoryTaskStatus.Cancelled;
                task.Reason = "Projected shortage resolved.";
                task.UpdatedAtUtc = DateTime.UtcNow;
                await context.SaveAsync(task, ct);
            }

            assessment.OpenRestockTasks = new List<InventoryRecord>();
            return;
        }

        var taskToUpdate = assessment.OpenRestockTasks.FirstOrDefault();
        var isNewTask = taskToUpdate is null;
        if (taskToUpdate is null)
        {
            taskToUpdate = new InventoryRecord
            {
                RegionId = assessment.Inventory.RegionId,
                RecordId = $"RESTOCK#{Guid.NewGuid():N}",
                RecordType = InventoryRecord.RestockType,
                Status = InventoryTaskStatus.Open,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        taskToUpdate.RequestedTotes = requiredTotes;
        taskToUpdate.RequestedDollies = requiredDollies;
        taskToUpdate.RequestedCardHolders = requiredCardHolders;
        taskToUpdate.RequestedCardHolderPacks = requiredCardHolderPacks;
        taskToUpdate.RequestedCardPacks = requiredCardPacks;
        taskToUpdate.NeededByDate = (firstShortage?.Date ?? DeliveryWindows.TodayInArizona())
            .ToString("yyyy-MM-dd");
        taskToUpdate.ActionByDate = DateOnly.Parse(taskToUpdate.NeededByDate)
            .AddDays(-Math.Max(0, procurementLeadDays))
            .ToString("yyyy-MM-dd");
        taskToUpdate.Reason =
            $"Projected shortage: {requiredTotes} tote(s), {requiredDollies} dolly/dollies, " +
            $"{requiredCardHolderPacks} holder pack(s), {requiredCardPacks} index-card pack(s).";
        taskToUpdate.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveAsync(taskToUpdate, ct);

        foreach (var duplicate in assessment.OpenRestockTasks.Where(t => t.RecordId != taskToUpdate.RecordId))
        {
            duplicate.Status = InventoryTaskStatus.Cancelled;
            duplicate.Reason = "Replaced by current inventory projection.";
            duplicate.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveAsync(duplicate, ct);
        }

        assessment.OpenRestockTasks = new List<InventoryRecord> { taskToUpdate };

        _logger.LogWarning(
            "Inventory shortage for region {RegionId}: {Totes} totes, {Dollies} dollies, and {CardPacks} card packs needed by {Date}",
            assessment.Inventory.RegionId,
            requiredTotes,
            requiredDollies,
            requiredCardPacks,
            taskToUpdate.NeededByDate);

        if (isNewTask)
        {
            await _staff.NotifyRegionAsync(
                assessment.Inventory.RegionId,
                NotificationTypes.InventoryRestock,
                $"Inventory restock needed — {assessment.Inventory.RegionId}",
                EmailTemplates.Wrap(
                    "Inventory restock needed",
                    EmailTemplates.DetailRows(
                        ("Region", assessment.Inventory.RegionId),
                        ("Totes", requiredTotes.ToString()),
                        ("Dollies", requiredDollies.ToString()),
                        ("Card holder packs", requiredCardHolderPacks.ToString()),
                        ("Index-card packs", requiredCardPacks.ToString()),
                        ("Needed by", taskToUpdate.NeededByDate),
                        ("Act by", taskToUpdate.ActionByDate ?? string.Empty)),
                    "Open inventory",
                    _urls.AdminInventory(assessment.Inventory.RegionId)),
                ct);
        }
    }

    private static InventoryRecord NewSummary(string regionId) => new()
    {
        RegionId = regionId,
        RecordId = InventoryRecord.SummaryRecordId,
        RecordType = InventoryRecord.SummaryType,
        Status = InventoryTaskStatus.Completed,
        IsConfigured = true
    };
}
