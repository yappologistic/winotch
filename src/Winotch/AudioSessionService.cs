using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace Winotch;

public sealed record AudioSessionRow(
    string Id,
    string Name,
    ImageSource? Icon,
    float Volume,
    bool IsMuted);

public sealed class AudioSessionService
{
    public async Task<IReadOnlyList<AudioSessionRow>> GetSessionsAsync()
    {
        var rows = new List<AudioSessionRowData>();
        WithSessionEnumerator((enumerator, count) =>
        {
            for (var index = 0; index < count; index++)
            {
                AudioSessionControlInterface? session = null;
                try
                {
                    if (!CoreAudioInterop.Succeeded(enumerator.GetSession(index, out session)))
                    {
                        continue;
                    }

                    var row = TryReadRowData(session);
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                }
                finally
                {
                    CoreAudioInterop.Release(session);
                }
            }
        });

        var sessions = rows
            .GroupBy(row => row.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<AudioSessionRow>(sessions.Count);
        foreach (var session in sessions)
        {
            result.Add(new AudioSessionRow(
                session.Id,
                session.Name,
                await ShellIconService.LoadSmallIconAsync(session.ModulePath),
                session.Volume,
                session.IsMuted));
        }

        return result;
    }

    public void SetSessionVolume(string sessionId, float percent) =>
        WithSessionVolume(sessionId, volume => volume.SetMasterVolume(Math.Clamp(percent / 100, 0, 1), Guid.Empty));

    public void SetSessionMuted(string sessionId, bool isMuted) =>
        WithSessionVolume(sessionId, volume => volume.SetMute(isMuted, Guid.Empty));

    private static AudioSessionRowData? TryReadRowData(AudioSessionControlInterface session)
    {
        try
        {
            if (!CoreAudioInterop.Succeeded(session.GetState(out var state)) || state != AudioSessionState.Active)
            {
                return null;
            }

            var session2 = (AudioSessionControl2Interface)session;
            var volume = (SimpleAudioVolumeInterface)session;
            var id = ReadSessionId(session2);
            if (string.IsNullOrWhiteSpace(id) ||
                !CoreAudioInterop.Succeeded(volume.GetMasterVolume(out var level)) ||
                !CoreAudioInterop.Succeeded(volume.GetMute(out var isMuted)))
            {
                return null;
            }

            var process = ReadProcessInfo(session2);
            var name = AudioSessionNaming.Resolve(
                process.FileDescription,
                process.ProductName,
                ReadSessionDisplayName(session),
                process.ProcessName,
                session2.IsSystemSoundsSession() == 0);

            return new AudioSessionRowData(
                id,
                name,
                Math.Clamp(level * 100, 0, 100),
                isMuted,
                process.ModulePath);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadSessionId(AudioSessionControl2Interface session)
    {
        if (CoreAudioInterop.Succeeded(session.GetSessionIdentifier(out var id)) && !string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return CoreAudioInterop.Succeeded(session.GetSessionInstanceIdentifier(out var instanceId)) ? instanceId : null;
    }

    private static string? ReadSessionDisplayName(AudioSessionControlInterface session) =>
        CoreAudioInterop.Succeeded(session.GetDisplayName(out var displayName)) ? displayName : null;

    private static SessionProcessInfo ReadProcessInfo(AudioSessionControl2Interface session)
    {
        if (!CoreAudioInterop.Succeeded(session.GetProcessId(out var processId)) || processId == 0)
        {
            return SessionProcessInfo.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            try
            {
                var module = process.MainModule;
                var version = module?.FileVersionInfo;
                return new SessionProcessInfo(
                    version?.FileDescription,
                    version?.ProductName,
                    processName,
                    module?.FileName);
            }
            catch
            {
                return new SessionProcessInfo(null, null, processName, null);
            }
        }
        catch
        {
            return SessionProcessInfo.Empty;
        }
    }

    private static void WithSessionVolume(string sessionId, Action<SimpleAudioVolumeInterface> action) =>
        WithSessionEnumerator((enumerator, count) =>
        {
            for (var index = 0; index < count; index++)
            {
                AudioSessionControlInterface? session = null;
                try
                {
                    if (!CoreAudioInterop.Succeeded(enumerator.GetSession(index, out session)))
                    {
                        continue;
                    }

                    var session2 = (AudioSessionControl2Interface)session;
                    if (StringComparer.Ordinal.Equals(ReadSessionId(session2), sessionId))
                    {
                        action((SimpleAudioVolumeInterface)session);
                        return;
                    }
                }
                catch
                {
                }
                finally
                {
                    CoreAudioInterop.Release(session);
                }
            }
        });

    private static void WithSessionEnumerator(Action<AudioSessionEnumeratorInterface, int> action)
    {
        MMDeviceEnumeratorInterface? deviceEnumerator = null;
        MMDeviceInterface? device = null;
        AudioSessionManager2Interface? manager = null;
        AudioSessionEnumeratorInterface? sessionEnumerator = null;
        try
        {
            deviceEnumerator = CoreAudioInterop.CreateEnumerator();
            if (!CoreAudioInterop.Succeeded(deviceEnumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, AudioRole.Multimedia, out device)))
            {
                return;
            }

            manager = CoreAudioInterop.Activate<AudioSessionManager2Interface>(device);
            if (manager is null ||
                !CoreAudioInterop.Succeeded(manager.GetSessionEnumerator(out sessionEnumerator)) ||
                !CoreAudioInterop.Succeeded(sessionEnumerator.GetCount(out var count)))
            {
                return;
            }

            action(sessionEnumerator, count);
        }
        catch
        {
        }
        finally
        {
            CoreAudioInterop.Release(sessionEnumerator);
            CoreAudioInterop.Release(manager);
            CoreAudioInterop.Release(device);
            CoreAudioInterop.Release(deviceEnumerator);
        }
    }

    private sealed record SessionProcessInfo(
        string? FileDescription,
        string? ProductName,
        string? ProcessName,
        string? ModulePath)
    {
        public static readonly SessionProcessInfo Empty = new(null, null, null, null);
    }

    private sealed record AudioSessionRowData(
        string Id,
        string Name,
        float Volume,
        bool IsMuted,
        string? ModulePath);
}
