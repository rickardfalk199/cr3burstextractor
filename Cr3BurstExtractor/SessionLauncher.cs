using System.Runtime.InteropServices;

namespace Cr3BurstExtractor;

/// <summary>
/// Spawns a child process in the active interactive user session from a
/// LocalSystem-running service.
///
/// Background: a Windows Service runs in session 0, which is isolated from
/// interactive user sessions since Vista. Any UI the service tries to show
/// (toast, balloon, message box) goes to a desktop no human ever sees. The
/// fix is to grab a token for the user logged into the physical console
/// (<c>WTSQueryUserToken</c>) and use it to <c>CreateProcessAsUser</c> — the
/// child then runs in the user's session and its UI is visible.
///
/// If nobody is logged in, <see cref="LaunchInActiveSession"/> returns false
/// and the caller should fall back to logging.
/// </summary>
public static class SessionLauncher
{
    public static bool LaunchInActiveSession(string exePath, string args, out string error)
    {
        error = "";
        if (!OperatingSystem.IsWindows())
        {
            error = "Not supported on this OS.";
            return false;
        }

        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            error = "No active console session.";
            return false;
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            error = $"WTSQueryUserToken failed (Win32 error {Marshal.GetLastWin32Error()}); is anyone logged in?";
            return false;
        }

        IntPtr envBlock = IntPtr.Zero;
        try
        {
            if (!CreateEnvironmentBlock(out envBlock, userToken, false))
            {
                error = $"CreateEnvironmentBlock failed (Win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            var si = new STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
            };

            // Wrap the exe path in quotes so spaces in Program Files-style
            // install paths don't split into separate argv entries.
            string cmdLine = $"\"{exePath}\" {args}";

            if (!CreateProcessAsUser(
                    userToken,
                    null,
                    cmdLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                    envBlock,
                    null,
                    ref si,
                    out var pi))
            {
                error = $"CreateProcessAsUser failed (Win32 error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            // We don't need to wait for or interact with the child — let it
            // run in the user's session and clean itself up.
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            return true;
        }
        finally
        {
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            CloseHandle(userToken);
        }
    }

    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const uint CREATE_NO_WINDOW           = 0x08000000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, [MarshalAs(UnmanagedType.Bool)] bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint   dwProcessId;
        public uint   dwThreadId;
    }
}
