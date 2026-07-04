namespace Winotch;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioOutputDevice> GetRenderDevices()
    {
        var devices = new List<AudioOutputDevice>();
        MMDeviceEnumeratorInterface? enumerator = null;
        MMDeviceCollectionInterface? collection = null;
        MMDeviceInterface? defaultDevice = null;
        try
        {
            enumerator = CoreAudioInterop.CreateEnumerator();
            var defaultId = CoreAudioInterop.Succeeded(enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, AudioRole.Multimedia, out defaultDevice))
                ? CoreAudioInterop.ReadDeviceId(defaultDevice)
                : null;

            if (!CoreAudioInterop.Succeeded(enumerator.EnumAudioEndpoints(AudioDataFlow.Render, AudioDeviceState.Active, out collection)) ||
                !CoreAudioInterop.Succeeded(collection.GetCount(out var count)))
            {
                return [];
            }

            for (uint index = 0; index < count; index++)
            {
                MMDeviceInterface? device = null;
                try
                {
                    if (!CoreAudioInterop.Succeeded(collection.Item(index, out device)))
                    {
                        continue;
                    }

                    var id = CoreAudioInterop.ReadDeviceId(device);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    devices.Add(new AudioOutputDevice(
                        id,
                        CoreAudioInterop.ReadDeviceFriendlyName(device),
                        StringComparer.Ordinal.Equals(id, defaultId)));
                }
                finally
                {
                    CoreAudioInterop.Release(device);
                }
            }

            return AudioDeviceOrdering.DefaultFirst(devices);
        }
        catch
        {
            return [];
        }
        finally
        {
            CoreAudioInterop.Release(defaultDevice);
            CoreAudioInterop.Release(collection);
            CoreAudioInterop.Release(enumerator);
        }
    }

    public void SetDefaultRenderDevice(string deviceId)
    {
        PolicyConfigInterface? policy = null;
        try
        {
            policy = (PolicyConfigInterface)(object)new PolicyConfigClient();
            policy.SetDefaultEndpoint(deviceId, AudioRole.Console);
            policy.SetDefaultEndpoint(deviceId, AudioRole.Multimedia);
            policy.SetDefaultEndpoint(deviceId, AudioRole.Communications);
        }
        catch
        {
        }
        finally
        {
            CoreAudioInterop.Release(policy);
        }
    }
}
