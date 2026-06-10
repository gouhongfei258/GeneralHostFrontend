using GeneralHostFrontend.Core.Communication;

namespace GeneralHostFrontend.Infrastructure.Communication;

public sealed class CommunicationDriverFactory : ICommunicationDriverFactory
{
    public ICommunicationDriver Create(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        return endpoint.Kind switch
        {
            DriverKind.Simulator => new SimulatorCommunicationDriver(endpoint, options),
            DriverKind.ModbusTcp
                or DriverKind.ModbusUdp
                or DriverKind.ModbusRtu
                or DriverKind.SiemensS7
                or DriverKind.SiemensFetchWrite
                or DriverKind.OmronFins
                or DriverKind.OmronFinsUdp
                or DriverKind.OmronHostLinkOverTcp
                or DriverKind.OmronHostLinkCModeOverTcp
                or DriverKind.OmronCip
                or DriverKind.OmronConnectedCip
                or DriverKind.MelsecMc
                or DriverKind.MelsecMcUdp
                or DriverKind.MelsecMcAscii
                or DriverKind.MelsecMcAsciiUdp
                or DriverKind.MelsecMcR
                or DriverKind.MelsecA1E
                or DriverKind.MelsecA1EAscii
                or DriverKind.MelsecA3COverTcp
                or DriverKind.MelsecFxLinksOverTcp
                or DriverKind.MelsecFxSerialOverTcp
                or DriverKind.MelsecCip
                or DriverKind.KeyenceMc
                or DriverKind.KeyenceMcAscii
                or DriverKind.KeyenceNanoOverTcp
                or DriverKind.PanasonicMc
                or DriverKind.PanasonicMewtocolOverTcp
                or DriverKind.AllenBradleyCip
                or DriverKind.AllenBradleyConnectedCip
                or DriverKind.AllenBradleyPccc
                or DriverKind.AllenBradleySlc
                or DriverKind.BeckhoffAds
                or DriverKind.DeltaTcp
                or DriverKind.DeltaSerialOverTcp
                or DriverKind.DeltaSerialAsciiOverTcp
                or DriverKind.FatekProgramOverTcp
                or DriverKind.InovanceTcp
                or DriverKind.InovanceSerialOverTcp
                or DriverKind.InovanceEasy
                or DriverKind.InovanceConnectedCip
                or DriverKind.FujiSph
                or DriverKind.FujiSpbOverTcp
                or DriverKind.GeSrtp
                or DriverKind.LsFastEnet
                or DriverKind.LsCnetOverTcp
                or DriverKind.XinJeTcp
                or DriverKind.XinJeInternal
                or DriverKind.XinJeSerialOverTcp
                or DriverKind.YaskawaMemobusTcp
                or DriverKind.YaskawaMemobusUdp
                or DriverKind.MegMeetTcp
                or DriverKind.MegMeetSerialOverTcp
                or DriverKind.SiemensPpiOverTcp => HslCommunicationDriverFactory.Create(endpoint, options),
            DriverKind.Http => new HttpCommunicationDriver(endpoint, options),
            DriverKind.TcpClient => new TcpClientCommunicationDriver(endpoint, options),
            DriverKind.TcpServer => new TcpServerCommunicationDriver(endpoint, options),
            _ => throw new NotSupportedException($"Driver '{endpoint.Kind}' is not registered yet. Add an adapter in Infrastructure.")
        };
    }
}
