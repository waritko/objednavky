namespace RestaurantOrders.Api.Domain;

public enum AccountRole { Administrator, Operational }
public enum OrderState { Active, Closed }

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public AccountRole Role { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class RestaurantTable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class Subcategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class MenuItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public Guid CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public required string Name { get; set; }
    public decimal PriceBeforeVat { get; set; }
    public decimal VatRate { get; set; }
    public decimal Price { get; set; }
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class RestaurantOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }
    public Guid? ActiveTableId { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public OrderState State { get; set; } = OrderState.Active;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public List<OrderUnit> Units { get; set; } = [];
}

public sealed class OrderUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid MenuItemId { get; set; }
    public required string ItemName { get; set; }
    public required string CategoryName { get; set; }
    public string? SubcategoryName { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public Guid AddedByAccountId { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public Guid? ProcessedByAccountId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PaidByAccountId { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedByAccountId { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ActorAccountId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string Action { get; set; }
    public Guid? UnitId { get; set; }
    public string? DetailsJson { get; set; }
}
