using System.ComponentModel.DataAnnotations;

namespace MockErp.Api.Contracts;

public sealed record OutboundOrderRequest
{
    [Required, MinLength(1)]
    public string CustomerId { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string FullName { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string OrderCode { get; init; } = string.Empty;

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal TotalAmount { get; init; }

    [Required, MinLength(1)]
    public IReadOnlyList<OutboundOrderItem>? Items { get; init; }
}

public sealed record OutboundOrderItem
{
    [Required, MinLength(1)]
    public string Code { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}
