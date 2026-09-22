using ErpMiddleware.Api.Contracts;

namespace ErpMiddleware.Api.Services;

public sealed class OrderTransformer
{
    public OutboundOrderRequest Transform(InboundOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Customer);
        ArgumentNullException.ThrowIfNull(request.Order);
        ArgumentNullException.ThrowIfNull(request.Order.Lines);

        var items = request.Order.Lines
            .Select(line => new OutboundOrderItem(line.Sku, line.Qty!.Value))
            .ToArray();

        var totalAmount = request.Order.Lines.Sum(line => line.Qty!.Value * line.Price!.Value);

        return new OutboundOrderRequest(
            request.Customer.ErpCustomerId,
            request.Customer.Name,
            request.Order.OrderNumber,
            totalAmount,
            items);
    }
}
