using System.Collections.Concurrent;
using GeneralHostFrontend.Core.Communication;

namespace GeneralHostFrontend.Infrastructure.Communication;

public sealed class CommunicationConnectionPool : ICommunicationConnectionPool
{
    private readonly ICommunicationDriverFactory _factory;
    private readonly ConcurrentDictionary<string, ICommunicationDriver> _drivers = new();

    public CommunicationConnectionPool(ICommunicationDriverFactory factory)
    {
        _factory = factory;
    }

    public async Task<ICommunicationDriver> GetOrCreateAsync(
        CommunicationEndpoint endpoint,
        CommunicationOptions options,
        CancellationToken cancellationToken = default)
    {
        var driver = _drivers.GetOrAdd(endpoint.DeviceId, _ => _factory.Create(endpoint, options));
        if (driver.Status.State is not DriverState.Connected and not DriverState.Connecting)
        {
            await driver.ConnectAsync(cancellationToken);
        }

        return driver;
    }

    public IReadOnlyCollection<DriverStatus> GetStatuses()
        => _drivers.Values.Select(driver => driver.Status).ToArray();

    public async ValueTask DisposeAsync()
    {
        foreach (var driver in _drivers.Values)
        {
            await driver.DisposeAsync();
        }

        _drivers.Clear();
    }
}
