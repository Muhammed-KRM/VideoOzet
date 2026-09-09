using MassTransit;

namespace VideoOzet.Business.Infrastructure.Messaging;

public class DummyPublishEndpoint : IPublishEndpoint
{
    public ConnectHandle ConnectPublishObserver(IPublishObserver observer)
    {
        return new DummyConnectHandle();
    }

    public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        // Dummy implementation - hiçbir şey yapmaz
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        return Task.CompletedTask;
    }

    public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        return Task.CompletedTask;
    }

    public Task Publish(object message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
    {
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        return Task.CompletedTask;
    }

    public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
    {
        return Task.CompletedTask;
    }
}

public class DummyConnectHandle : ConnectHandle
{
    public void Disconnect()
    {
        // Dummy implementation
    }

    public void Dispose()
    {
        // Dummy implementation
    }
}
