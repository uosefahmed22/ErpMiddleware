using ErpMiddleware.Api.Clients;
using ErpMiddleware.Api.Contracts;
using ErpMiddleware.Api.Storage;

namespace ErpMiddleware.Api.Services;

public sealed class OrderProcessingService(
    IInboundRequestStore requestStore,
    RequestHasher requestHasher,
    OrderTransformer transformer,
    IErpOrderClient erpOrderClient,
    ILogger<OrderProcessingService> logger) : IOrderProcessingService
{
    public async Task<OrderProcessingResult> ProcessAsync(
        InboundOrderRequest request,
        string correlationId,
        string? simulationMode,
        CancellationToken cancellationToken)
    {
        var requestHash = requestHasher.ComputeHash(request);
        var decision = requestStore.TryBegin(request, requestHash, DateTimeOffset.UtcNow);

        if (decision.Type == IdempotencyDecisionType.Replay)
        {
            logger.LogInformation(
                "Returning stored result for replayed idempotency key {IdempotencyKey}",
                request.IdempotencyKey);

            return new OrderProcessingResult(
                decision.StoredResult!.Outcome,
                decision.StoredResult.Response,
                IsReplay: true);
        }

        if (decision.Type == IdempotencyDecisionType.Conflict)
        {
            logger.LogWarning(
                "Idempotency conflict for key {IdempotencyKey}: the payload differs from the original request",
                request.IdempotencyKey);

            return new OrderProcessingResult(
                OrderProcessingOutcome.Conflict,
                new ProcessingErrorResponse(
                    "error",
                    "IDEMPOTENCY_CONFLICT",
                    "The idempotency key was already used with a different request payload."));
        }

        if (decision.Type == IdempotencyDecisionType.InProgress)
        {
            logger.LogWarning(
                "A request with idempotency key {IdempotencyKey} is already being processed",
                request.IdempotencyKey);

            return new OrderProcessingResult(
                OrderProcessingOutcome.Conflict,
                new ProcessingErrorResponse(
                    "error",
                    "REQUEST_IN_PROGRESS",
                    "A request with this idempotency key is already being processed."));
        }

        try
        {
            var outboundOrder = transformer.Transform(request);

            logger.LogInformation(
                "Transformed inbound order {OrderNumber} with total amount {TotalAmount}",
                outboundOrder.OrderCode,
                outboundOrder.TotalAmount);

            var erpResponse = await erpOrderClient.SendAsync(
                outboundOrder,
                request.IdempotencyKey,
                correlationId,
                simulationMode,
                cancellationToken);

            var result = string.Equals(
                erpResponse.Status,
                "partial_success",
                StringComparison.OrdinalIgnoreCase)
                    ? CreatePartialSuccessResult(erpResponse)
                    : new OrderProcessingResult(
                        OrderProcessingOutcome.Success,
                        new ProcessingSuccessResponse(
                            "success",
                            erpResponse.ErpOrderId,
                            "Order processed and updated in ERP"));

            StoreCompletedResult(request.IdempotencyKey, result);
            return result;
        }
        catch (ErpTimeoutException exception)
        {
            logger.LogError(
                exception,
                "ERP timed out for idempotency key {IdempotencyKey}",
                request.IdempotencyKey);

            var result = new OrderProcessingResult(
                OrderProcessingOutcome.Timeout,
                new ProcessingErrorResponse(
                    "error",
                    "ERP_TIMEOUT",
                    $"ERP did not respond within {exception.TimeoutSeconds} {(exception.TimeoutSeconds == 1 ? "second" : "seconds")}"));

            StoreCompletedResult(request.IdempotencyKey, result);
            return result;
        }
        catch (ErpServerException exception)
        {
            logger.LogError(
                exception,
                "ERP server error for idempotency key {IdempotencyKey}",
                request.IdempotencyKey);

            var result = new OrderProcessingResult(
                OrderProcessingOutcome.UpstreamFailure,
                new ProcessingErrorResponse(
                    "error",
                    "ERP_SERVER_ERROR",
                    $"ERP returned a server error after {exception.Attempts} attempts"));

            StoreCompletedResult(request.IdempotencyKey, result);
            return result;
        }
        catch (ErpUnavailableException exception)
        {
            logger.LogError(
                exception,
                "ERP unavailable for idempotency key {IdempotencyKey}",
                request.IdempotencyKey);

            var result = new OrderProcessingResult(
                OrderProcessingOutcome.UpstreamFailure,
                new ProcessingErrorResponse(
                    "error",
                    "ERP_UNAVAILABLE",
                    $"ERP could not be reached after {exception.Attempts} attempts"));

            StoreCompletedResult(request.IdempotencyKey, result);
            return result;
        }
        catch (ErpRejectedException exception)
        {
            logger.LogError(
                exception,
                "ERP rejected the request for idempotency key {IdempotencyKey}",
                request.IdempotencyKey);

            var result = new OrderProcessingResult(
                OrderProcessingOutcome.UpstreamFailure,
                new ProcessingErrorResponse(
                    "error",
                    "ERP_REJECTED",
                    $"ERP rejected the request with status code {(int)exception.StatusCode}"));

            StoreCompletedResult(request.IdempotencyKey, result);
            return result;
        }
        catch (ErpInvalidResponseException exception)
        {
            logger.LogError(
                exception,
                "ERP returned an invalid response for idempotency key {IdempotencyKey}",
                request.IdempotencyKey);

            var result = new OrderProcessingResult(
                OrderProcessingOutcome.UpstreamFailure,
                new ProcessingErrorResponse(
                    "error",
                    "ERP_INVALID_RESPONSE",
                    "ERP returned an invalid response"));

            StoreCompletedResult(request.IdempotencyKey, result);
            return result;
        }
        catch
        {
            requestStore.Remove(request.IdempotencyKey);
            throw;
        }
    }

    private static OrderProcessingResult CreatePartialSuccessResult(
        ErpOrderAcknowledgement erpResponse)
    {
        var rejectedItems = erpResponse.RejectedItems?
            .Select(item => new RejectedOrderItemResponse(item.Code, item.Reason))
            .ToArray() ?? [];

        return new OrderProcessingResult(
            OrderProcessingOutcome.PartialSuccess,
            new ProcessingPartialSuccessResponse(
                "partial_success",
                erpResponse.ErpOrderId,
                "ERP_PARTIAL_SUCCESS",
                erpResponse.Message,
                erpResponse.AcceptedItems ?? [],
                rejectedItems));
    }

    private void StoreCompletedResult(string idempotencyKey, OrderProcessingResult result)
    {
        requestStore.Complete(
            idempotencyKey,
            new StoredProcessingResult(result.Outcome, result.Response));
    }
}
