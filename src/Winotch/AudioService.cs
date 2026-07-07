namespace Winotch;

public sealed class AudioService : IDisposable
{
    private readonly EndpointVolumeCache _render = new(AudioDataFlow.Render);
    private readonly EndpointVolumeCache _capture = new(AudioDataFlow.Capture);

    public float GetVolume()
    {
        var endpoint = _render.Get();
        if (endpoint is null || !CoreAudioInterop.Succeeded(endpoint.GetMasterVolumeLevelScalar(out var value)))
        {
            return 0;
        }

        return Math.Clamp(value * 100, 0, 100);
    }

    public void SetVolume(float value)
    {
        var endpoint = _render.Get();
        endpoint?.SetMasterVolumeLevelScalar(Math.Clamp(value / 100, 0, 1), Guid.Empty);
    }

    public bool GetMuted()
    {
        var endpoint = _render.Get();
        return endpoint is not null &&
            CoreAudioInterop.Succeeded(endpoint.GetMute(out var isMuted)) &&
            isMuted;
    }

    public void SetMuted(bool isMuted)
    {
        var endpoint = _render.Get();
        endpoint?.SetMute(isMuted, Guid.Empty);
    }

    public bool GetCaptureMuted()
    {
        var endpoint = _capture.Get();
        return endpoint is not null &&
            CoreAudioInterop.Succeeded(endpoint.GetMute(out var isMuted)) &&
            isMuted;
    }

    public void SetCaptureMuted(bool isMuted)
    {
        var endpoint = _capture.Get();
        endpoint?.SetMute(isMuted, Guid.Empty);
    }

    public void RefreshDefaultEndpoints()
    {
        _render.Clear();
        _capture.Clear();
    }

    public void Dispose()
    {
        _render.Clear();
        _capture.Clear();
    }

    private sealed class EndpointVolumeCache
    {
        private readonly AudioDataFlow _dataFlow;
        private string? _deviceId;
        private AudioEndpointVolumeInterface? _endpoint;

        public EndpointVolumeCache(AudioDataFlow dataFlow)
        {
            _dataFlow = dataFlow;
        }

        public AudioEndpointVolumeInterface? Get()
        {
            MMDeviceEnumeratorInterface? enumerator = null;
            MMDeviceInterface? device = null;
            try
            {
                enumerator = CoreAudioInterop.CreateEnumerator();
                if (!CoreAudioInterop.Succeeded(enumerator.GetDefaultAudioEndpoint(_dataFlow, AudioRole.Multimedia, out device)))
                {
                    Clear();
                    return null;
                }

                var currentId = CoreAudioInterop.ReadDeviceId(device);
                if (string.IsNullOrWhiteSpace(currentId))
                {
                    Clear();
                    return null;
                }

                if (_endpoint is not null && StringComparer.Ordinal.Equals(_deviceId, currentId))
                {
                    return _endpoint;
                }

                Clear();
                _endpoint = CoreAudioInterop.Activate<AudioEndpointVolumeInterface>(device);
                _deviceId = _endpoint is null ? null : currentId;
                return _endpoint;
            }
            catch
            {
                Clear();
                return null;
            }
            finally
            {
                CoreAudioInterop.Release(device);
                CoreAudioInterop.Release(enumerator);
            }
        }

        public void Clear()
        {
            CoreAudioInterop.Release(_endpoint);
            _endpoint = null;
            _deviceId = null;
        }
    }
}
