using System.Runtime.InteropServices;
using SyncCast.Models;

namespace SyncCast.Services.Diagnostics;

public interface IDiagnosticsService
{
    DiagnosticInfo GetDiagnostics(
        IReadOnlyList<AudioEndpointInfo> endpoints,
        int selectedCount,
        int testedCount = 0,
        string? customDisclaimer = null);
}

public class DiagnosticsService : IDiagnosticsService
{
    public DiagnosticInfo GetDiagnostics(
        IReadOnlyList<AudioEndpointInfo> endpoints,
        int selectedCount,
        int testedCount = 0,
        string? customDisclaimer = null)
    {
        var activeCount = endpoints.Count(e => e.State == EndpointActiveState.Active);
        var btCount = endpoints.Count(e => e.Transport == DeviceTransportType.Bluetooth && e.State == EndpointActiveState.Active);

        var osDesc = RuntimeInformation.OSDescription;
        var dotNetDesc = RuntimeInformation.FrameworkDescription;

        string defaultDisclaimer = testedCount > 0
            ? $"Live verification active: {testedCount} endpoint stream(s) verified. Real Bluetooth multi-streaming concurrency remains hardware- and adapter-dependent."
            : "Notice: Bluetooth 5.x does not guarantee 5 concurrent audio connections. Endpoint discovery alone does not verify simultaneous audio playback. Capability depends on your adapter hardware and driver capabilities.";

        return new DiagnosticInfo
        {
            WindowsVersion = osDesc,
            DotNetVersion = dotNetDesc,
            ActiveRenderEndpointsCount = activeCount,
            SelectedEndpointsCount = selectedCount,
            LikelyBluetoothEndpointsCount = btCount,
            TestedEndpointsCount = testedCount,
            BluetoothDisclaimer = customDisclaimer ?? defaultDisclaimer
        };
    }
}
