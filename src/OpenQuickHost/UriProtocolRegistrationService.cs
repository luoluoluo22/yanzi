using System.IO;
using Microsoft.Win32;

namespace OpenQuickHost;

public static class UriProtocolRegistrationService
{
    public static void EnsureRegistered(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return;
        }

        using var schemeKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\yanzi");
        schemeKey?.SetValue(string.Empty, "URL:Yanzi Protocol");
        schemeKey?.SetValue("URL Protocol", string.Empty);

        using var defaultIconKey = schemeKey?.CreateSubKey("DefaultIcon");
        defaultIconKey?.SetValue(string.Empty, $"\"{executablePath}\",0");

        using var commandKey = schemeKey?.CreateSubKey(@"shell\open\command");
        commandKey?.SetValue(string.Empty, $"\"{executablePath}\" \"%1\"");
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\yanzi", throwOnMissingSubKey: false);
        }
        catch { }
    }
}
