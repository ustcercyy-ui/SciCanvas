using System.Windows.Forms;

namespace SciCanvas.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        InstallerLaunchOptions launchOptions;
        try
        {
            launchOptions = InstallerCommandLine.Parse(args);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                $"{exception.Message}\n\n{InstallerCommandLine.HelpText}",
                "SciCanvas 安装程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 2;
        }

        if (launchOptions.ShowHelp)
        {
            MessageBox.Show(
                InstallerCommandLine.HelpText,
                "SciCanvas 安装程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        if (launchOptions.Silent)
        {
            try
            {
                InstallerEngine.Install(launchOptions.Options);
                if (launchOptions.Options.LaunchAfterInstall)
                {
                    InstallerEngine.LaunchApplication(launchOptions.Options.InstallDirectory);
                }

                return 0;
            }
            catch (Exception exception)
            {
                InstallerEngine.TryWriteFailureLog(exception);
                return 1;
            }
        }

        using var installerForm = new InstallerForm(launchOptions.Options);
        Application.Run(installerForm);
        return installerForm.ExitCode;
    }
}
