using System.ComponentModel.DataAnnotations;

namespace ErpMiddleware.Api.Contracts;

public sealed record InboundOrderRequest
{
    [Required, MinLength(1)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [Required]
    public CustomerRequest? Customer { get; init; }

    [Required]
    public OrderRequest? Order { get; init; }
}

public sealed record CustomerRequest
{
    [Required, MinLength(1)]
    public string ErpCustomerId { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string Name { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;
}

public sealed record OrderRequest
{
    [Required, MinLength(1)]
    public string OrderNumber { get; init; } = string.Empty;

    [Required]
    public DateOnly? Date { get; init; }

    [Required, MinLength(1)]
    public IReadOnlyList<OrderLineRequest>? Lines { get; init; }
}

public sealed record OrderLineRequest
{
    [Required, MinLength(1)]
    public string Sku { get; init; } = string.Empty;

    [Required, Range(1, int.MaxValue)]
    public int? Qty { get; init; }

    [Required, Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? Price { get; init; }
}
