namespace Dispatch.Core.Infrastructure;

/// <summary>Free space on the drive a folder lives on.</summary>
public static class DiskSpace
{
    /// <summary>Free bytes on the drive containing <paramref name="path"/>, or null when it cannot be read.</summary>
    public static long? FreeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Formats a byte count as a short human string, e.g. "184 GB".</summary>
    public static string Format(long? bytes)
    {
        if (bytes is null)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit >= 3 ? $"{value:0.#} {units[unit]}" : $"{value:0} {units[unit]}";
    }
}
