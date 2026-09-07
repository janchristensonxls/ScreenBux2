using System.Runtime.InteropServices;
using System.Security.Principal;
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
        _logger.LogDebug("TryStartInActiveSession: active console session id is {SessionId}.", consoleSessionId);

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
            // SecurityIdentification only allows the resulting token to be used to identify the
            // caller, not to perform access checks. When CreateProcessAsUser launches a GUI
            // process, it must open the target window station/desktop ("winsta0\default")
            // under this token, which requires SecurityImpersonation - otherwise that access
            // check fails during USER32/GDI startup, which for a GUI app like the WPF Agent
            // manifests as an immediate exit with STATUS_DLL_INIT_FAILED (0xC0000142).
            if (!DuplicateTokenEx(
                    userTokenHandle,
                    TOKEN_ALL_ACCESS,
                    IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    TOKEN_TYPE.TokenPrimary,
                    out var primaryTokenHandle))
            {
                _logger.LogWarning("DuplicateTokenEx failed (Win32Error={Error}).", Marshal.GetLastWin32Error());
                return false;
            }

            using (primaryTokenHandle)
            {
                var environmentBlock = IntPtr.Zero;
                PROFILEINFO profileInfo = default;
                var profileLoaded = false;

                try
                {
                    // LoadUserProfile is required alongside CreateEnvironmentBlock: without it,
                    // the target user's HKEY_CURRENT_USER hive is never mapped into the new
                    // process, which causes GUI frameworks (WPF in the Agent's case) to fail
                    // during per-user COM/DLL initialization - observed as the process exiting
                    // almost immediately with STATUS_DLL_INIT_FAILED (0xC0000142). Loading the
                    // profile first fixes that.
                    if (!TryGetUserNameFromToken(primaryTokenHandle, out var userName))
                    {
                        _logger.LogWarning(
                            "Could not resolve the user name for the active session's token; proceeding without loading a user profile for {Executable}.",
                            executablePath);
                    }
                    else
                    {
                        profileInfo = new PROFILEINFO
                        {
                            dwSize = Marshal.SizeOf<PROFILEINFO>(),
                            lpUserName = userName
                        };

                        if (!LoadUserProfile(primaryTokenHandle, ref profileInfo))
                        {
                            _logger.LogWarning(
                                "LoadUserProfile failed for user {UserName} (Win32Error={Error}); {Executable} may fail to start correctly without the user's profile loaded.",
                                userName, Marshal.GetLastWin32Error(), executablePath);
                        }
                        else
                        {
                            profileLoaded = true;
                        }
                    }

                    if (!CreateEnvironmentBlock(out environmentBlock, primaryTokenHandle, false))
                    {
                        _logger.LogWarning(
                            "CreateEnvironmentBlock failed for {Executable} (Win32Error={Error}); continuing with a null environment block.",
                            executablePath, Marshal.GetLastWin32Error());
                        environmentBlock = IntPtr.Zero;
                    }

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

                    // CreateProcessAsUser succeeding only means the process was spawned - it
                    // says nothing about whether it stays running (missing runtime, startup
                    // exception, getting terminated by AV/EDR for the token-impersonation
                    // technique used here, etc.). Give it a moment and check whether it has
                    // already exited so that is visible in the log instead of silently
                    // reporting success for a process that immediately died.
                    const int PostLaunchCheckDelayMs = 1500;
                    Thread.Sleep(PostLaunchCheckDelayMs);
                    if (GetExitCodeProcess(processInformation.hProcess, out var exitCode) && exitCode != STILL_ACTIVE)
                    {
                        _logger.LogWarning(
                            "{Executable} was launched (PID={ProcessId}) but had already exited with code {ExitCode} within {DelayMs}ms of launch.",
                            executablePath, processInformation.dwProcessId, exitCode, PostLaunchCheckDelayMs);
                        CloseHandle(processInformation.hProcess);
                        CloseHandle(processInformation.hThread);
                        return false;
                    }

                    CloseHandle(processInformation.hProcess);
                    CloseHandle(processInformation.hThread);
                    _logger.LogInformation("Relaunched {Executable} (PID={ProcessId}) in session {SessionId}.", executablePath, processInformation.dwProcessId, consoleSessionId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while relaunching {Executable} in session {SessionId}.", executablePath, consoleSessionId);
                    return false;
                }
                finally
                {
                    if (environmentBlock != IntPtr.Zero)
                    {
                        DestroyEnvironmentBlock(environmentBlock);
                    }

                    if (profileLoaded && profileInfo.hProfile != IntPtr.Zero)
                    {
                        UnloadUserProfile(primaryTokenHandle, profileInfo.hProfile);
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

    // CharSet.Unicode (plus explicit LPWStr marshalling) matches CreateProcessAsUser's
    // CharSet.Unicode declaration; without it, the struct's string fields default to ANSI
    // marshalling and lpDesktop ("winsta0\default") can be read incorrectly by the OS.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpReserved;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDesktop;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitle;
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

    private const uint STILL_ACTIVE = 259;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    /// <summary>
    /// Resolves "DOMAIN\User" (or "MachineName\User" for a local account) for the given token,
    /// via LookupAccountSid, in the format LoadUserProfile's PROFILEINFO.lpUserName expects.
    /// </summary>
    private static bool TryGetUserNameFromToken(SafeAccessTokenHandle tokenHandle, out string userName)
    {
        userName = string.Empty;
        try
        {
            var identity = new WindowsIdentity(tokenHandle.DangerousGetHandle());
            var account = identity.Name; // "DOMAIN\User" or "MachineName\User"
            if (string.IsNullOrEmpty(account))
            {
                return false;
            }

            var backslashIndex = account.IndexOf('\\');
            userName = backslashIndex >= 0 ? account[(backslashIndex + 1)..] : account;
            return !string.IsNullOrEmpty(userName);
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROFILEINFO
    {
        public int dwSize;
        public int dwFlags;
        public string lpUserName;
        public string? lpProfilePath;
        public string? lpDefaultPath;
        public string? lpServerName;
        public string? lpPolicyPath;
        public IntPtr hProfile;
    }

    [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LoadUserProfile(SafeAccessTokenHandle hToken, ref PROFILEINFO lpProfileInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool UnloadUserProfile(SafeAccessTokenHandle hToken, IntPtr hProfile);
}
