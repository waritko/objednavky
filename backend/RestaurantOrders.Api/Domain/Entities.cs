namespace RestaurantOrders.Api.Domain;

public enum AccountRole { Administrator, Operational }
public enum OrderState { Active, Closed }

public sealed class Account
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public AccountRole Role { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class RestaurantTable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class Category
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class Subcategory
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class MenuItem
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public int CategoryId { get; set; }
    public int? SubcategoryId { get; set; }
    public required string Name { get; set; }
    public decimal PriceBeforeVat { get; set; }
    public decimal VatRate { get; set; }
    public decimal Price { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class RestaurantOrder
{
    public int Id { get; set; }
    public int TableId { get; set; }
    public int? ActiveTableId { get; set; }
    public int CreatedByAccountId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public OrderState State { get; set; } = OrderState.Active;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public string? Note { get; set; }
    public string LineNotesJson { get; set; } = "{}";
    public List<OrderUnit> Units { get; set; } = [];
}

public sealed class OrderUnit
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Guid RequestToken { get; set; } = Guid.NewGuid();
    public int MenuItemId { get; set; }
    public string ItemCode { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public decimal UnitPriceBeforeVat { get; set; }
    public decimal VatRate { get; set; }
    public required string ItemName { get; set; }
    public required string CategoryName { get; set; }
    public string? SubcategoryName { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public int AddedByAccountId { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int? ProcessedByAccountId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public int? PaidByAccountId { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public int? RemovedByAccountId { get; set; }
}

public sealed class AuditEvent
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ActorAccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Action { get; set; }
    public int? UnitId { get; set; }
    public string? DetailsJson { get; set; }
}
