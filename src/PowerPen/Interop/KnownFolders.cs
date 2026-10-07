using System.IO;
using System.Runtime.InteropServices;

namespace PowerPen.Interop;

/// <summary>Finds special Windows folders where they really are.</summary>
public static class KnownFolders
{
    /// <summary>
    /// The user's Downloads folder. Asks Windows rather than assuming %USERPROFILE%\Downloads,
    /// because company PCs often redirect it (e.g. into OneDrive).
    /// </summary>
    public static string Downloads
    {
        get
        {
            if (NativeMethods.SHGetKnownFolderPath(NativeMethods.FOLDERID_Downloads, 0, IntPtr.Zero, out IntPtr pathPointer) == 0)
            {
                try
                {
                    string? path = Marshal.PtrToStringUni(pathPointer);
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
                finally
                {
                    NativeMethods.CoTaskMemFree(pathPointer);
                }
            }

            // Very unlikely, but don't fail a screenshot over it: fall back to the usual location.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }
}
