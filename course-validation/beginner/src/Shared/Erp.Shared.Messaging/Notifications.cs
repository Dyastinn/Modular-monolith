using Microsoft.Extensions.DependencyInjection;

namespace Erp.Shared.Messaging;

public interface INotification { }   // domain events implement this
public interface INotificationHandler<in T> where T : INotification
{ Task Handle(T notification, CancellationToken ct); }

public interface IPublisher
{
    Task Publish(INotification notification, CancellationToken ct = default);
}

// Mirrors Sender: resolve-by-type reflection, but loops all handlers instead of wrapping one.
public sealed class Publisher(IServiceProvider provider) : IPublisher
{
    public async Task Publish(INotification notification, CancellationToken ct = default)
    {
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notification.GetType());
        var method = handlerType.GetMethod("Handle")!;
        foreach (var handler in provider.GetServices(handlerType))
            await (Task)method.Invoke(handler, new object?[] { notification, ct })!;
    }
}
