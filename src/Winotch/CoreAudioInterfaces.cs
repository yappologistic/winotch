using System.Runtime.InteropServices;

namespace Winotch;

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumerator;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MMDeviceEnumeratorInterface
{
    [PreserveSig]
    int EnumAudioEndpoints(AudioDataFlow dataFlow, AudioDeviceState stateMask, out MMDeviceCollectionInterface devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(AudioDataFlow dataFlow, AudioRole role, out MMDeviceInterface device);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out MMDeviceInterface device);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MMDeviceCollectionInterface
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out MMDeviceInterface device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface MMDeviceInterface
{
    [PreserveSig]
    int Activate(ref Guid iid, int classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(int access, out PropertyStoreInterface properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out AudioDeviceState state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface PropertyStoreInterface
{
    [PreserveSig]
    int GetCount(out int propertyCount);

    [PreserveSig]
    int GetAt(int propertyIndex, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface AudioEndpointVolumeInterface
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr client);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr client);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float level, Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float level);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channelNumber, float level, Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channelNumber, float level, Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channelNumber, out float level);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channelNumber, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface AudioSessionManager2Interface
{
    [PreserveSig]
    int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags, out AudioSessionControlInterface sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint streamFlags, out SimpleAudioVolumeInterface audioVolume);

    [PreserveSig]
    int GetSessionEnumerator(out AudioSessionEnumeratorInterface sessionEnumerator);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface AudioSessionEnumeratorInterface
{
    [PreserveSig]
    int GetCount(out int sessionCount);

    [PreserveSig]
    int GetSession(int sessionIndex, out AudioSessionControlInterface sessionControl);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface AudioSessionControlInterface
{
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, Guid eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, Guid eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingId);

    [PreserveSig]
    int SetGroupingParam(Guid groupingId, Guid eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr notifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr notifications);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface AudioSessionControl2Interface
{
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, Guid eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, Guid eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingId);

    [PreserveSig]
    int SetGroupingParam(Guid groupingId, Guid eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr notifications);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr notifications);

    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceId);

    [PreserveSig]
    int GetProcessId(out uint processId);

    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface SimpleAudioVolumeInterface
{
    [PreserveSig]
    int SetMasterVolume(float level, Guid eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
}
