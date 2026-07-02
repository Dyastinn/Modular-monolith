using Erp.Application.Abstractions.Messaging;

namespace Erp.Application.Orders.Ping;

public sealed record PingQuery : IRequest<string>;
