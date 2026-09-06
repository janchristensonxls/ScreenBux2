using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace ScreenBux.Shared.Services;

/// <summary>
/// Launches a process into the currently active interactive user session from a Session-0
/// service process. Originally built for ScreenBux.Updater (to relaunch the Agent after it
/// has been updated on disk, since the Updater service runs in Session 0 and has no desktop
/// of its own); also used by ScreenBux.Service's AgentWatchdogService to relaunch the Agent
/// if it is not running/reporting while a user is logged in.
///
/// Handles the edge cases that make this "just some technicalities": no interactive session
/// logged in yet (returns false, caller should retry later), and using the *active console*
/// session rather than an arbitrary disconnected/RDP session so the launched process lands in
/// front of whoever is actually sitting at the machine.
/// </summary>
public class SessionLauncher
{
    private readonly ILogger<SessionLauncher> _logger;

    public SessionLauncher(ILogger<SessionLauncher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to start <paramref name="executablePath"/> in the active console session.
    /// Returns false (without throwing) if there is currently no interactive user logged in,
    /// or if the launch fails for any other recoverable reason - callers should treat that as
    /// "try again on the next check interval" rather than a fatal error.
    /// </summary>
    public bool TryStartInActiveSession(string executablePath, string? arguments = null)
    {
        var consoleSessionId = WTSGetActiveConsoleSessionId();
        if (consoleSessionId == 0xFFFFFFFF)
        {
            _logger.LogInformation("No active console session; skipping relaunch of {Executable}.", executablePath);
            return false;
        }

        if (!WTSQueryUserToken(consoleSessionId, out var userTokenHandle))
        {
            _logger.LogWarning("WTSQueryUserToken failed for session {SessionId} (Win32Error={Error}).", consoleSessionId, Marshal.GetLastWin32Error());
            return false;
        }

        using (userTokenHandle)
        {
            if (!DuplicateTokenEx(
                    userTokenHandle,
                    TOKEN_ALL_ACCESS,
                    IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification,
                    TOKEN_TYPE.TokenPrimary,
                    out var primaryTokenHandle))
            {
                _logger.LogWarning("DuplicateTokenEx failed (Win32Error={Error}).", Marshal.GetLastWin32Error());
                return false;
            }

            using (primaryTokenHandle)
            {
                var environmentBlock = IntPtr.Zero;
                try
                {
                    CreateEnvironmentBlock(out environmentBlock, primaryTokenHandle, false);

                    var startupInfo = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf<STARTUPINFO>(),
                        lpDesktop = "winsta0\\default"
                    };

                    var commandLine = arguments is null ? $"\"{executablePath}\"" : $"\"{executablePath}\" {arguments}";

                    var created = CreateProcessAsUser(
                        primaryTokenHandle,
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE,
                        environmentBlock,
                        Path.GetDirectoryName(executablePath),
                        ref startupInfo,
                        out var processInformation);

                    if (!created)
                    {
                        _logger.LogWarning("CreateProcessAsUser failed for {Executable} (Win32Error={Error}).", executablePath, Marshal.GetLastWin32Error());
                        return false;
                    }

                    CloseHandle(processInformation.hProcess);
                    CloseHandle(processInformation.hThread);
                    _logger.LogInformation("Relaunched {Executable} in session {SessionId}.", executablePath, consoleSessionId);
                    return true;
                }
                finally
                {
                    if (environmentBlock != IntPtr.Zero)
                    {
                        DestroyEnvironmentBlock(environmentBlock);
                    }
                }
            }
        }
    }

    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out SafeAccessTokenHandle phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, SafeAccessTokenHandle hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
