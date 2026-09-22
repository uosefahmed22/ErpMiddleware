# ERP Middleware Integration Assessment

An ASP.NET Core middleware service that receives customer and order data from a source ERP, validates and transforms the payload, sends it to a second ERP endpoint, and returns a structured result. The implementation includes correlation tracing, temporary storage, idempotency, bounded retries, exponential backoff, timeouts, failure simulations, structured logging, and automated tests.

## Architecture

```text
Source ERP / HTTP Client
        |
        | POST /erp/inbound/order
        v
ErpMiddleware.Api
        |-- validation
        |-- correlation ID
        |-- idempotency check
        |-- temporary in-memory storage
        |-- payload transformation
        |-- retry / timeout policy
        |
        | POST /erp/outbound/order
        v
MockErp.Api
        |
        v
Structured response to the source ERP
```

The source ERP is represented by any HTTP client, such as the included `.http` file. `MockErp.Api` is a small simulator for the external ERP; it is not a complete ERP system.

## Solution Structure

```text
ErpMiddleware.slnx
|
|-- src/
|   |-- ErpMiddleware.Api/       Main middleware API
|   `-- MockErp.Api/             External ERP simulator
|
|-- tests/
|   `-- ErpMiddleware.Tests/     Seven endpoint integration tests
|
`-- example-logs.txt             Representative logs from verified scenarios
```

The solution intentionally uses a small folder-based architecture instead of CQRS, MediatR, or a multi-project Clean Architecture. The current problem has one main use case, so those patterns would add complexity without a concrete benefit.

## Requirements

- .NET 10 SDK
- No database or external infrastructure is required

## Running the Solution

Start the mock ERP in the first terminal:

```powershell
dotnet run --project src/MockErp.Api/MockErp.Api.csproj
```

Start the middleware in a second terminal:

```powershell
dotnet run --project src/ErpMiddleware.Api/ErpMiddleware.Api.csproj
```

Default addresses:

```text
Middleware API: http://localhost:5091
Mock ERP API:   http://localhost:5092
```

OpenAPI documents are available in Development:

```text
http://localhost:5091/openapi/v1.json
http://localhost:5092/openapi/v1.json
```

Ready-to-run requests for all scenarios are available in:

```text
src/ErpMiddleware.Api/ErpMiddleware.Api.http
```

## API 1 - Receive Data From ERP

```http
POST /erp/inbound/order
Content-Type: application/json
X-Correlation-ID: optional-correlation-id
X-ERP-Simulation: optional-demo-mode
```

Sample request:

```json
{
  "idempotencyKey": "abc-123-xyz",
  "customer": {
    "erpCustomerId": "C1001",
    "name": "John Doe",
    "email": "john@example.com"
  },
  "order": {
    "orderNumber": "SO-5001",
    "date": "2026-09-21",
    "lines": [
      { "sku": "P100", "qty": 2, "price": 50 },
      { "sku": "P200", "qty": 1, "price": 100 }
    ]
  }
}
```

Validation covers:

- Required idempotency key, customer, order, and line fields
- Email format
- Date type
- Empty order-line arrays
- Missing customer or order objects
- Quantities greater than zero
- Non-negative prices

Invalid input returns `400 Bad Request` with validation details and never calls the outbound ERP.

## Transformation

The inbound payload is mapped to:

```json
{
  "customerId": "C1001",
  "fullName": "John Doe",
  "orderCode": "SO-5001",
  "totalAmount": 200,
  "items": [
    { "code": "P100", "quantity": 2 },
    { "code": "P200", "quantity": 1 }
  ]
}
```

`totalAmount` is calculated as:

```text
sum(quantity * price)
```

## API 2 - Mock ERP Endpoint

```http
POST /erp/outbound/order
Content-Type: application/json
Idempotency-Key: abc-123-xyz
X-Correlation-ID: correlation-id
```

The middleware sends the transformed payload, the original idempotency key, and the same correlation ID to this endpoint.

### Mock ERP Idempotency

The mock ERP independently enforces the `Idempotency-Key` contract instead of only accepting the header:

| Condition | Result |
|---|---|
| New key | Atomically reserve the key and process the order |
| Same key and same payload after completion | Return the stored response with `Idempotency-Replayed: true` |
| Same key and different payload | `409 IDEMPOTENCY_CONFLICT` |
| Same key while the original request is processing | `409 REQUEST_IN_PROGRESS` |

Only completed success and partial-success responses are stored. Transient `500` responses, timeouts, and cancelled requests release the reservation so the middleware can retry them. The store is in memory and is intended only for this assessment simulator.

## Correlation IDs

The middleware reads `X-Correlation-ID` from the inbound request. If it is missing, it generates a new value. The ID is:

- Added to the response headers
- Included in middleware logs
- Forwarded to the mock ERP
- Included in the mock ERP logs

## Temporary Storage and Idempotency

Inbound requests are temporarily stored in memory with:

- Idempotency key
- SHA-256 request hash
- Original request
- Received timestamp
- Processing state
- Final response

Behavior:

| Condition | Result |
|---|---|
| New key | Start processing |
| Same key and same payload after completion | Return the stored response without calling ERP again |
| Same key and different payload | `409 IDEMPOTENCY_CONFLICT` |
| Same key while the original request is processing | `409 REQUEST_IN_PROGRESS` |

A replayed response includes:

```http
Idempotency-Replayed: true
```

The store uses an application-level lock so the check-and-start operation is atomic within one process.

### Production Limitation

The in-memory implementation is intentionally scoped to the assessment requirements. It is cleared on restart and is not shared across application instances. A production implementation should use a database or distributed store with a unique constraint, expiry policy, processing state, request hash, and stored response.

## Retry Logic

The outbound client performs a maximum of three total attempts.

It retries only transient failures:

- ERP `5xx`
- Timeout
- Network or connection errors

It does not retry:

- `4xx` responses
- Invalid ERP response bodies
- Partial success
- Caller cancellation

Default exponential-backoff sequence:

```text
Attempt 1 fails -> wait 1 second
Attempt 2 fails -> wait 2 seconds
Attempt 3 fails -> return the mapped error
```

Every retry sends a new HTTP request with the same idempotency key and correlation ID.

## Timeout Handling

Each outbound attempt has a configurable timeout of 10 seconds. After all attempts time out, the middleware returns:

```http
504 Gateway Timeout
```

```json
{
  "status": "error",
  "errorCode": "ERP_TIMEOUT",
  "message": "ERP did not respond within 10 seconds"
}
```

An outbound timeout does not prove that the remote system did not process the request. Reusing the same idempotency key across retries protects against duplicate effects when the real ERP supports that contract.

## Partial Failure Handling

The mock ERP can return item-level partial success. The middleware returns `207 Multi-Status` with accepted and rejected items:

```json
{
  "status": "partial_success",
  "erpOrderId": "SO-5001",
  "errorCode": "ERP_PARTIAL_SUCCESS",
  "message": "ERP processed some order items and rejected others",
  "acceptedItems": ["P100"],
  "rejectedItems": [
    {
      "code": "P200",
      "reason": "Simulated item rejection"
    }
  ]
}
```

The middleware does not automatically retry the complete order after partial success because that could duplicate already accepted items. A production retry would require item-level idempotency or an ERP contract that supports retrying only rejected items.

## Error Simulation

For demonstration and manual testing, the inbound endpoint accepts an optional header:

```http
X-ERP-Simulation: <mode>
```

The header is forwarded to `MockErp.Api`. It is an assessment/demo feature and should be disabled or removed in a production deployment.

| Mode | Result |
|---|---|
| `server-error` | Mock ERP always returns `500` |
| `timeout` | Mock ERP responds after the middleware timeout |
| `slow-response` | Mock ERP responds slowly but before the timeout |
| `partial-success` | Mock ERP returns `207` with accepted and rejected items |

An unknown mode returns `400 INVALID_SIMULATION_MODE`. The automated tests set the fake ERP behavior directly instead of depending on this header.

## Response Mapping

| Scenario | HTTP status | Error code |
|---|---:|---|
| Success | 200 | - |
| Validation error | 400 | Validation details |
| Invalid simulation mode | 400 | `INVALID_SIMULATION_MODE` |
| Idempotency conflict | 409 | `IDEMPOTENCY_CONFLICT` |
| Matching request still processing | 409 | `REQUEST_IN_PROGRESS` |
| Partial success | 207 | `ERP_PARTIAL_SUCCESS` |
| ERP server error after retries | 502 | `ERP_SERVER_ERROR` |
| ERP unavailable after retries | 502 | `ERP_UNAVAILABLE` |
| ERP rejected the request | 502 | `ERP_REJECTED` |
| Invalid ERP response | 502 | `ERP_INVALID_RESPONSE` |
| ERP timeout after retries | 504 | `ERP_TIMEOUT` |

## Logging Strategy

Logs contain operational identifiers rather than complete customer payloads:

- Correlation ID
- Idempotency key
- Order code
- Attempt number and maximum attempts
- Response status code
- Elapsed milliseconds
- Retry delay and reason
- Simulation mode in the mock ERP
- Final mapped failure

Customer email and full inbound payloads are not written to application logs. Representative output is provided in `example-logs.txt`.

## Automated Tests

Run all tests:

```powershell
dotnet test ErpMiddleware.slnx --configuration Release
```

The test suite contains the seven scenarios required by the assessment:

1. Valid request
2. Missing fields
3. Duplicate request
4. ERP timeout
5. ERP 500 error
6. ERP slow response
7. ERP partial success

The endpoint tests run the middleware API in memory and replace only the external HTTP connection with a small fake handler. The real `ErpOrderClient` still runs, so retry counts, timeout handling, transformation, headers, and response mapping are verified without starting a second API inside the test process. The real `MockErp.Api` remains available for manual end-to-end verification.

## Verification Commands

```powershell
dotnet format ErpMiddleware.slnx --verify-no-changes --no-restore
dotnet build ErpMiddleware.slnx --configuration Release --no-restore
dotnet test ErpMiddleware.slnx --configuration Release --no-build
dotnet list ErpMiddleware.slnx package --vulnerable --include-transitive
```
