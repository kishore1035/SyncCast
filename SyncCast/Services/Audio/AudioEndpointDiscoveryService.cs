using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using SyncCast.Models;

namespace SyncCast.Services.Audio;

public interface IAudioEndpointDiscoveryService
{
    Task<IReadOnlyList<AudioEndpointInfo>> DiscoverEndpointsAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    EndpointActiveState GetCurrentDeviceState(string endpointId);
}

public class AudioEndpointDiscoveryService : IAudioEndpointDiscoveryService
{
    public EndpointActiveState GetCurrentDeviceState(string endpointId)
    {
        if (string.IsNullOrEmpty(endpointId)) return EndpointActiveState.NotPresent;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(endpointId);
            return MapDeviceState(device?.State ?? DeviceState.NotPresent);
        }
        catch
        {
            return EndpointActiveState.NotPresent;
        }
    }

    public Task<IReadOnlyList<AudioEndpointInfo>> DiscoverEndpointsAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<AudioEndpointInfo>();

            MMDeviceEnumerator? enumerator = null;
            try
            {
                enumerator = new MMDeviceEnumerator();

                string? defaultEndpointId = null;
                try
                {
                    var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    defaultEndpointId = defaultDevice?.ID;
                }
                catch
                {
                    // No default audio render device or Core Audio not initialized
                }

                var stateMask = includeInactive
                    ? DeviceState.Active | DeviceState.Disabled | DeviceState.NotPresent | DeviceState.Unplugged
                    : DeviceState.Active;

                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, stateMask);

                foreach (var device in devices)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string id = device.ID ?? string.Empty;
                    string friendlyName = string.Empty;
                    string deviceDesc = string.Empty;

                    try
                    {
                        friendlyName = device.FriendlyName ?? "Unknown Audio Endpoint";
                    }
                    catch
                    {
                        friendlyName = "Endpoint " + (id.Length > 8 ? id[..8] : id);
                    }

                    try
                    {
                        deviceDesc = device.DeviceFriendlyName ?? string.Empty;
                    }
                    catch
                    {
                        deviceDesc = string.Empty;
                    }

                    var state = MapDeviceState(device.State);
                    bool isDefault = !string.IsNullOrEmpty(defaultEndpointId) &&
                                     string.Equals(id, defaultEndpointId, StringComparison.OrdinalIgnoreCase);

                    int? sampleRate = null;
                    int? channels = null;
                    int? bitsPerSample = null;

                    if (state == EndpointActiveState.Active)
                    {
                        try
                        {
                            var mixFormat = device.AudioClient?.MixFormat;
                            if (mixFormat != null)
                            {
                                sampleRate = mixFormat.SampleRate;
                                channels = mixFormat.Channels;
                                bitsPerSample = mixFormat.BitsPerSample;
                            }
                        }
                        catch
                        {
                            // Some active virtual/hardware endpoints fail to query shared mix format
                        }
                    }

                    // Extract all property strings from property store for accurate PnP / Bus transport analysis
                    var (pnpId, propertiesSummary) = ExtractPropertyMetadata(device);

                    // Evaluate transport heuristics based on Windows Core Audio metadata
                    var (transport, reason) = ClassifyTransport(friendlyName, deviceDesc, pnpId, propertiesSummary);

                    results.Add(new AudioEndpointInfo
                    {
                        Id = id,
                        FriendlyName = friendlyName,
                        DeviceDescription = deviceDesc,
                        State = state,
                        Transport = transport,
                        TransportReason = reason,
                        IsDefaultEndpoint = isDefault,
                        SampleRate = sampleRate,
                        Channels = channels,
                        BitsPerSample = bitsPerSample,
                        PnpDeviceId = pnpId
                    });
                }
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException($"Windows Core Audio MMDevice COM error (0x{ex.ErrorCode:X8}): {ex.Message}", ex);
            }
            finally
            {
                enumerator?.Dispose();
            }

            // Order active devices first, with default at top, then alphabetical
            return (IReadOnlyList<AudioEndpointInfo>)results
                .OrderByDescending(d => d.IsDefaultEndpoint)
                .ThenBy(d => d.State != EndpointActiveState.Active)
                .ThenBy(d => d.FriendlyName)
                .ToList();
        }, cancellationToken);
    }

    private static EndpointActiveState MapDeviceState(DeviceState state) => state switch
    {
        DeviceState.Active => EndpointActiveState.Active,
        DeviceState.Disabled => EndpointActiveState.Disabled,
        DeviceState.NotPresent => EndpointActiveState.NotPresent,
        DeviceState.Unplugged => EndpointActiveState.Unplugged,
        _ => EndpointActiveState.Unknown
    };

    private static (string? PnpId, string CombinedMetadata) ExtractPropertyMetadata(MMDevice device)
    {
        string? pnpId = null;
        var metaStrings = new List<string>();

        try
        {
            var propStore = device.Properties;
            if (propStore != null)
            {
                for (int i = 0; i < propStore.Count; i++)
                {
                    try
                    {
                        var key = propStore.Get(i);
                        var propVal = propStore[key]?.Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(propVal))
                        {
                            metaStrings.Add(propVal);
                            if (pnpId == null && (propVal.StartsWith(@"SWD\MMDEVAPI\", StringComparison.OrdinalIgnoreCase) ||
                                                  propVal.Contains(@"BTHENUM\", StringComparison.OrdinalIgnoreCase) ||
                                                  propVal.Contains(@"USB\", StringComparison.OrdinalIgnoreCase) ||
                                                  propVal.Contains(@"HDAUDIO\", StringComparison.OrdinalIgnoreCase)))
                            {
                                pnpId = propVal;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual property access issues
                    }
                }
            }
        }
        catch
        {
            // Property store might be inaccessible
        }

        return (pnpId, string.Join(" | ", metaStrings));
    }

    /// <summary>
    /// Evaluates Windows metadata to categorize device transport without guessing or fabricating claims.
    /// Explicit rule: "If the transport cannot be determined reliably, label it Unknown instead of guessing."
    /// </summary>
    public static (DeviceTransportType Transport, string Reason) ClassifyTransport(
        string friendlyName,
        string deviceDesc,
        string? pnpId,
        string combinedProperties)
    {
        string searchCorpus = $"{friendlyName} {deviceDesc} {pnpId ?? string.Empty} {combinedProperties}".ToLowerInvariant();

        // 1. Check for concrete Bluetooth identifiers
        string[] bluetoothIndicators =
        {
            "bthenum",
            "bthhfenum",
            "btha2dp",
            "bluetooth",
            "bth\\",
            "a2dp",
            "hands-free ag audio",
            "hands-free audio",
            "wireless stereo",
            "le audio",
            "galaxy buds",
            "airpods",
            "wh-1000",
            "wf-1000",
            "pixel buds",
            "jabra",
            "bose quietcomfort",
            "soundcore",
            "earbuds"
        };

        foreach (var indicator in bluetoothIndicators)
        {
            if (searchCorpus.Contains(indicator))
            {
                return (DeviceTransportType.Bluetooth, $"Identified as Bluetooth audio output via metadata indicator '{indicator}'.");
            }
        }

        // 2. Check for concrete built-in or wired identifiers
        string[] wiredOrBuiltInIndicators =
        {
            "realtek",
            "high definition audio",
            "hdaudio",
            "internal speakers",
            "speakers (realtek",
            "headphones (realtek",
            "intel(r) display audio",
            "nvidia high definition",
            "amd high definition",
            "conexant",
            "synaptics audio",
            "cirrus logic",
            "usb audio",
            "usbaudio",
            "displayport",
            "hdmi"
        };

        foreach (var indicator in wiredOrBuiltInIndicators)
        {
            if (searchCorpus.Contains(indicator))
            {
                return (DeviceTransportType.BuiltInOrWired, $"Identified as Built-In / Wired output via metadata indicator '{indicator}'.");
            }
        }

        // 3. Honest reporting when indicators are absent
        return (DeviceTransportType.Unknown, "Transport interface could not be determined with high certainty from available Core Audio metadata.");
    }
}
