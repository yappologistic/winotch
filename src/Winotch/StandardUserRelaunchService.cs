using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winotch;

/// <summary>
/// Keeps the desktop overlay at the same integrity level as Explorer and browsers.
/// OLE drag/drop is blocked across a lower-to-higher integrity boundary, so an
/// instance started from an Administrator terminal must shed elevation first.
/// </summary>
internal static class StandardUserRelaunchService
{
    private const uint TokenQuery = 0x0008;
    private const uint TokenDuplicate = 0x0002;
    private const uint LogonWithProfile = 0x00000001;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint SeGroupIntegrity = 0x00000020;
    private const uint MaximumAllowed = 0x02000000;
    private const int TokenElevation = 20;
    private const int TokenLinkedToken = 19;

    public static bool TryRelaunchIfElevated()
    {
        if (!OperatingSystem.IsWindows() || !TryOpenCurrentToken(out var processToken))
        {
            return false;
        }

        using (processToken)
        {
            if (!IsElevated(processToken) || !TryGetStandardUserToken(processToken, out var standardUserToken))
            {
                return false;
            }

            using (standardUserToken)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    return false;
                }

                var startupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfo>()
                };

                if (!CreateProcessWithTokenW(
                        standardUserToken,
                        LogonWithProfile,
                        executable,
                        Environment.CommandLine,
                        CreateUnicodeEnvironment,
                        IntPtr.Zero,
                        Environment.CurrentDirectory,
                        ref startupInfo,
                        out var processInfo))
                {
                    Debug.WriteLine($"Unable to relaunch Winotch without elevation: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
                    return false;
                }

                CloseHandle(processInfo.Thread);
                CloseHandle(processInfo.Process);
                return true;
            }
        }
    }

    private static bool TryOpenCurrentToken(out SafeTokenHandle token) =>
        OpenProcessToken(Process.GetCurrentProcess().Handle, TokenQuery | TokenDuplicate, out token);

    private static bool IsElevated(SafeTokenHandle token)
    {
        var elevation = 0;
        return GetTokenInformation(
                   token,
                   TokenElevation,
                   ref elevation,
                   sizeof(int),
                   out _) &&
               elevation != 0;
    }

    private static bool TryGetLinkedToken(SafeTokenHandle token, out SafeTokenHandle linkedToken) =>
        GetTokenInformation(
            token,
            TokenLinkedToken,
            out linkedToken,
            IntPtr.Size,
            out _);

    private static bool TryGetStandardUserToken(SafeTokenHandle token, out SafeTokenHandle standardUserToken)
    {
        if (TryGetLinkedToken(token, out standardUserToken))
        {
            return true;
        }

        // UAC-disabled machines do not expose TokenLinkedToken. Duplicate the
        // current primary token and lower only its mandatory integrity label.
        if (!DuplicateTokenEx(
                token,
                MaximumAllowed,
                IntPtr.Zero,
                2,
                1,
                out standardUserToken))
        {
            return false;
        }

        if (TrySetMediumIntegrity(standardUserToken))
        {
            return true;
        }

        standardUserToken.Dispose();
        standardUserToken = new SafeTokenHandle();
        return false;
    }

    private static bool TrySetMediumIntegrity(SafeTokenHandle token)
    {
        if (!ConvertStringSidToSidW("S-1-16-8192", out var mediumSid))
        {
            return false;
        }

        try
        {
            var label = new TokenMandatoryLabel
            {
                Label = new SidAndAttributes
                {
                    Sid = mediumSid,
                    Attributes = SeGroupIntegrity
                }
            };
            var labelSize = Marshal.SizeOf<TokenMandatoryLabel>() + GetLengthSid(mediumSid);
            var buffer = Marshal.AllocHGlobal(labelSize);
            try
            {
                Marshal.StructureToPtr(label, buffer, fDeleteOld: false);
                return SetTokenInformation(token, 25, buffer, labelSize);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = LocalFree(mediumSid);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr ReservedPointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    private sealed class SafeTokenHandle : SafeHandle
    {
        public SafeTokenHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeTokenHandle tokenHandle,
        int tokenInformationClass,
        ref int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeTokenHandle tokenHandle,
        int tokenInformationClass,
        out SafeTokenHandle tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(
        SafeTokenHandle token,
        uint logonFlags,
        string applicationName,
        string commandLine,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSidToSidW(string stringSid, out IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int GetLengthSid(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(
        SafeTokenHandle tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        SafeTokenHandle existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out SafeTokenHandle newToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
