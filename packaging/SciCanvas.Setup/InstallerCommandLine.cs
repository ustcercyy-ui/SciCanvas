namespace SciCanvas.Setup;

internal sealed record InstallerOptions(
    string InstallDirectory,
    bool CreateStartMenuShortcut,
    bool CreateDesktopShortcut,
    bool LaunchAfterInstall)
{
    public static InstallerOptions CreateDefault() => new(
        InstallerEngine.DefaultInstallDirectory,
        CreateStartMenuShortcut: true,
        CreateDesktopShortcut: false,
        LaunchAfterInstall: true);
}

internal sealed record InstallerLaunchOptions(
    InstallerOptions Options,
    bool Silent,
    bool ShowHelp);

internal static class InstallerCommandLine
{
    public const string HelpText =
        "双击安装程序可打开安装向导。\n\n" +
        "可选命令行参数：\n" +
        "  /S 或 --silent                 静默安装\n" +
        "  /D=路径 或 --install-dir 路径  指定安装位置\n" +
        "  --desktop-shortcut             创建桌面快捷方式\n" +
        "  --no-start-menu-shortcut       不创建开始菜单快捷方式\n" +
        "  --launch / --no-launch         安装后启动 / 不启动";

    public static InstallerLaunchOptions Parse(IReadOnlyList<string> args)
    {
        InstallerOptions defaults = InstallerOptions.CreateDefault();
        string installDirectory = defaults.InstallDirectory;
        bool createStartMenuShortcut = defaults.CreateStartMenuShortcut;
        bool createDesktopShortcut = defaults.CreateDesktopShortcut;
        bool launchAfterInstall = defaults.LaunchAfterInstall;
        bool launchOptionSpecified = false;
        bool silent = false;
        bool showHelp = false;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--silent", StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
            }
            else if (argument.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
            {
                installDirectory = RequireValue(argument[3..], "/D");
            }
            else if (argument.StartsWith("--install-dir=", StringComparison.OrdinalIgnoreCase))
            {
                installDirectory = RequireValue(argument[14..], "--install-dir");
            }
            else if (argument.Equals("--install-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Count)
                {
                    throw new ArgumentException("--install-dir 后需要提供安装路径。");
                }

                installDirectory = RequireValue(args[index], "--install-dir");
            }
            else if (argument.Equals("--desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                createDesktopShortcut = true;
            }
            else if (argument.Equals("--no-desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                createDesktopShortcut = false;
            }
            else if (argument.Equals("--no-start-menu-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                createStartMenuShortcut = false;
            }
            else if (argument.Equals("--start-menu-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                createStartMenuShortcut = true;
            }
            else if (argument.Equals("--launch", StringComparison.OrdinalIgnoreCase))
            {
                launchAfterInstall = true;
                launchOptionSpecified = true;
            }
            else if (argument.Equals("--no-launch", StringComparison.OrdinalIgnoreCase))
            {
                launchAfterInstall = false;
                launchOptionSpecified = true;
            }
            else if (argument is "/?" or "-?" ||
                     argument.Equals("--help", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else
            {
                throw new ArgumentException($"无法识别安装参数：{argument}");
            }
        }

        if (silent && !launchOptionSpecified)
        {
            launchAfterInstall = false;
        }

        return new InstallerLaunchOptions(
            new InstallerOptions(
                installDirectory,
                createStartMenuShortcut,
                createDesktopShortcut,
                launchAfterInstall),
            silent,
            showHelp);
    }

    private static string RequireValue(string value, string optionName)
    {
        string trimmed = value.Trim().Trim('"');
        return trimmed.Length > 0
            ? trimmed
            : throw new ArgumentException($"{optionName} 后需要提供安装路径。");
    }
}
