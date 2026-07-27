namespace BoxDropAz.Core.Models.Orders;

public sealed class OrderNote
{
    public string NoteId { get; set; } = Guid.NewGuid().ToString("N");

    public string Body { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public string AuthorUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
