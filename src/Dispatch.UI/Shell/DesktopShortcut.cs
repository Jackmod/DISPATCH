using System.Runtime.InteropServices;
using System.Text;

namespace Dispatch.UI.Shell;

/// <summary>
/// Creates a Desktop shortcut to the launcher once setup is done.
/// </summary>
/// <remarks>
/// After the wizard finishes, Dispatch is a launcher rather than an installer, and
/// a shortcut is how a returning user opens it without going near the setup flow
/// again. The link points at this executable itself — on launch, a configured
/// profile sends the app straight to the launcher, so the shortcut lands exactly
/// where the name promises.
///
/// <para>
/// Built through the <c>IShellLink</c> COM interface rather than a
/// <c>dynamic</c> WScript.Shell call, because the shipped build is trimmed and
/// late-bound COM would not survive it. It is Windows-only and entirely
/// best-effort: any failure is swallowed, since a missing shortcut is a small
/// inconvenience and never a reason to interrupt a finished install.
/// </para>
/// </remarks>
internal static class DesktopShortcut
{
    /// <summary>Creates (or refreshes) the "Dispatch" shortcut on the Desktop.</summary>
    public static void TryCreate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var target = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(target) ||
                !string.Equals(Path.GetFileNameWithoutExtension(target), "Dispatch", StringComparison.OrdinalIgnoreCase))
            {
                // Running under `dotnet run` or a test host — the process is not the
                // shipped executable, so a shortcut to it would be meaningless.
                return;
            }

            var desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                return;
            }

            var linkPath = Path.Combine(desktop, "Dispatch.lnk");

            // Interface-to-coclass cast goes through object; the runtime resolves
            // it by COM QueryInterface.
            var link = (IShellLinkW)(object)new ShellLink();
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target) ?? string.Empty);
            link.SetDescription("Open the Dispatch launcher");
            link.SetIconLocation(target, 0);

            ((IPersistFile)link).Save(linkPath, false);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            // A shortcut is a courtesy; never fail the handoff over it.
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
