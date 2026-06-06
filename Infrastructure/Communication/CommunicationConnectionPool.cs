using GeneralHostFrontend.Core.Communication;

namespace GeneralHostFrontend.Infrastructure.Communication;

public sealed class CommunicationConnectionPool : ICommunicationConnectionPool
{
    private readonly ICommunicationDriverFactory _factory;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Dictionary<string, DriverRegistration> _drivers = new(StringComparer.OrdinalIgnoreCase);

    public CommunicationConnectionPool(ICommunicationDriverFactory factory)
    {
        _factory = factory;
    }

    public async Task<ICommunicationDriver> GetOrCreateAsync(
        CommunicationEndpoint endpoint,
        CommunicationOptions options,
        CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!_drivers.TryGetValue(endpoint.DeviceId, out var registration))
            {
                registration = CreateRegistration(endpoint, options);
                _drivers[endpoint.DeviceId] = registration;
            }
            else if (!EndpointEquals(registration.Endpoint, endpoint)
                || !EqualityComparer<CommunicationOptions>.Default.Equals(registration.Options, options))
            {
                await registration.Driver.DisposeAsync();
                registration = CreateRegistration(endpoint, options);
                _drivers[endpoint.DeviceId] = registration;
            }

            var driver = registration.Driver;
            if (driver.Status.State is not DriverState.Connected and not DriverState.Connecting)
            {
                await driver.ConnectAsync(cancellationToken);
            }

            return driver;
        }
        finally
        {
            _sync.Release();
        }
    }

    public IReadOnlyCollection<DriverStatus> GetStatuses()
    {
        _sync.Wait();
        try
        {
            return _drivers.Values.Select(registration => registration.Driver.Status).ToArray();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sync.WaitAsync();
        try
        {
            foreach (var registration in _drivers.Values)
            {
                await registration.Driver.DisposeAsync();
            }

            _drivers.Clear();
        }
        finally
        {
            _sync.Release();
            _sync.Dispose();
        }
    }

    private DriverRegistration CreateRegistration(CommunicationEndpoint endpoint, CommunicationOptions options)
        => new(endpoint, options, _factory.Create(endpoint, options));

    private static bool EndpointEquals(CommunicationEndpoint left, CommunicationEndpoint right)
    {
        return left.Kind == right.Kind
            && string.Equals(left.Address, right.Address, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port
            && ParametersEqual(left.Parameters, right.Parameters);
    }

    private static bool ParametersEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var item in left)
        {
            var hasMatch = right.Any(candidate =>
                string.Equals(candidate.Key, item.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Value, item.Value, StringComparison.OrdinalIgnoreCase));
            if (!hasMatch)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record DriverRegistration(
        CommunicationEndpoint Endpoint,
        CommunicationOptions Options,
        ICommunicationDriver Driver);
}
