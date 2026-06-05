using GeneralHostFrontend.Core.Communication;

namespace GeneralHostFrontend.Infrastructure.Communication;

public sealed class CommunicationDriverFactory : ICommunicationDriverFactory
{
    public ICommunicationDriver Create(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        return endpoint.Kind switch
        {
            DriverKind.Simulator => new SimulatorCommunicationDriver(endpoint, options),
            _ => throw new NotSupportedException($"Driver '{endpoint.Kind}' is not registered yet. Add an adapter in Infrastructure.")
        };
    }
}
