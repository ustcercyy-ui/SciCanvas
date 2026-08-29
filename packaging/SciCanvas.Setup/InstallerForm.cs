using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace SciCanvas.Setup;

internal sealed class InstallerForm : Form
{
    private static readonly Color Navy = Color.FromArgb(23, 37, 61);
    private static readonly Color Accent = Color.FromArgb(31, 111, 235);
    private static readonly Color AccentHover = Color.FromArgb(25, 90, 190);
    private static readonly Color Canvas = Color.FromArgb(246, 248, 251);
    private static readonly Color Ink = Color.FromArgb(31, 41, 55);
    private static readonly Color Muted = Color.FromArgb(103, 116, 137);
    private static readonly Color Border = Color.FromArgb(218, 224, 232);

    private readonly Panel _configurationPanel;
    private readonly Panel _completionPanel;
    private readonly TextBox _installPathTextBox;
    private readonly Button _browseButton;
    private readonly CheckBox _startMenuCheckBox;
    private readonly CheckBox _desktopCheckBox;
    private readonly CheckBox _launchCheckBox;
    private readonly CheckBox _completionLaunchCheckBox;
    private readonly Label _spaceLabel;
    private readonly Label _statusLabel;
    private readonly Label _completionPathLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;
    private readonly Button _primaryButton;
    private readonly Label[] _stepLabels;
    private bool _installationInProgress;
    private bool _installationSucceeded;
    private string _installedDirectory = string.Empty;

    public InstallerForm(InstallerOptions initialOptions)
    {
        Text = "SciCanvas 安装程序";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ClientSize = new Size(760, 670);
        BackColor = Canvas;
        ForeColor = Ink;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

        TableLayoutPanel rootLayout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
        Controls.Add(rootLayout);

        Panel header = CreateHeader(out _stepLabels);
        rootLayout.Controls.Add(header, 0, 0);

        Panel footer = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(36, 18, 36, 18),
            Margin = Padding.Empty,
        };
        footer.Paint += (_, eventArgs) =>
            eventArgs.Graphics.DrawLine(new Pen(Border), 0, 0, footer.Width, 0);

        _primaryButton = CreatePrimaryButton("开始安装");
        _primaryButton.Location = new Point(586, 18);
        _primaryButton.Click += PrimaryButton_Click;
        footer.Controls.Add(_primaryButton);

        _cancelButton = CreateSecondaryButton("取消");
        _cancelButton.Location = new Point(474, 18);
        _cancelButton.Click += (_, _) => Close();
        footer.Controls.Add(_cancelButton);
        rootLayout.Controls.Add(footer, 0, 2);

        Panel body = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            Padding = new Padding(40, 24, 40, 20),
            Margin = Padding.Empty,
        };
        rootLayout.Controls.Add(body, 0, 1);

        _configurationPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
        };
        body.Controls.Add(_configurationPanel);

        Label pageTitle = CreateLabel(
            "选择安装设置",
            new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
            Ink,
            new Point(0, 0),
            new Size(680, 42));
        _configurationPanel.Controls.Add(pageTitle);

        _configurationPanel.Controls.Add(CreateLabel(
            "可以使用推荐设置，也可以按需要更改安装位置和快捷方式。",
            Font,
            Muted,
            new Point(1, 43),
            new Size(679, 28)));

        _configurationPanel.Controls.Add(CreateLabel(
            "安装位置",
            new Font(Font, FontStyle.Bold),
            Ink,
            new Point(1, 82),
            new Size(200, 24)));

        _installPathTextBox = new TextBox
        {
            Location = new Point(1, 109),
            Size = new Size(560, 32),
            Text = initialOptions.InstallDirectory,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "安装位置",
        };
        _installPathTextBox.TextChanged += (_, _) => UpdateSpaceLabel();
        _configurationPanel.Controls.Add(_installPathTextBox);

        _browseButton = CreateSecondaryButton("浏览…");
        _browseButton.Location = new Point(573, 106);
        _browseButton.Size = new Size(106, 36);
        _browseButton.Click += BrowseButton_Click;
        _configurationPanel.Controls.Add(_browseButton);

        _spaceLabel = CreateLabel(
            string.Empty,
            new Font("Microsoft YaHei UI", 8.5F),
            Muted,
            new Point(1, 149),
            new Size(678, 22));
        _configurationPanel.Controls.Add(_spaceLabel);

        Panel optionCard = new()
        {
            Location = new Point(1, 183),
            Size = new Size(678, 130),
            BackColor = Color.White,
            Padding = new Padding(18),
        };
        optionCard.Paint += (_, eventArgs) =>
            eventArgs.Graphics.DrawRectangle(new Pen(Border), 0, 0, optionCard.Width - 1, optionCard.Height - 1);
        _configurationPanel.Controls.Add(optionCard);

        optionCard.Controls.Add(CreateLabel(
            "附加选项",
            new Font(Font, FontStyle.Bold),
            Ink,
            new Point(17, 13),
            new Size(180, 32)));

        _startMenuCheckBox = CreateCheckBox(
            "创建开始菜单快捷方式",
            initialOptions.CreateStartMenuShortcut,
            new Point(20, 46));
        optionCard.Controls.Add(_startMenuCheckBox);

        _desktopCheckBox = CreateCheckBox(
            "创建桌面快捷方式",
            initialOptions.CreateDesktopShortcut,
            new Point(344, 46));
        optionCard.Controls.Add(_desktopCheckBox);

        _launchCheckBox = CreateCheckBox(
            "安装完成后启动 SciCanvas",
            initialOptions.LaunchAfterInstall,
            new Point(20, 78));
        optionCard.Controls.Add(_launchCheckBox);

        Panel privacyNote = new()
        {
            Location = new Point(1, 318),
            Size = new Size(678, 48),
            BackColor = Color.FromArgb(235, 244, 255),
        };
        privacyNote.Controls.Add(CreateLabel(
            "✓  当前用户安装 · 无需管理员权限 · 不会改动原始科研图像",
            Font,
            Color.FromArgb(31, 78, 121),
            new Point(16, 9),
            new Size(646, 30)));
        _configurationPanel.Controls.Add(privacyNote);

        _progressBar = new ProgressBar
        {
            Location = new Point(1, 382),
            Size = new Size(678, 10),
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };
        _configurationPanel.Controls.Add(_progressBar);

        _statusLabel = CreateLabel(
            string.Empty,
            Font,
            Muted,
            new Point(1, 398),
            new Size(678, 24));
        _statusLabel.Visible = false;
        _configurationPanel.Controls.Add(_statusLabel);

        _completionPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            Visible = false,
        };
        body.Controls.Add(_completionPanel);

        Label completionMark = CreateLabel(
            "✓",
            new Font("Segoe UI", 28F, FontStyle.Bold, GraphicsUnit.Point),
            Color.White,
            new Point(305, 38),
            new Size(72, 72));
        completionMark.BackColor = Color.FromArgb(26, 153, 104);
        completionMark.TextAlign = ContentAlignment.MiddleCenter;
        _completionPanel.Controls.Add(completionMark);

        Label completionTitle = CreateLabel(
            "SciCanvas 已安装完成",
            new Font("Microsoft YaHei UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
            Ink,
            new Point(0, 134),
            new Size(680, 42));
        completionTitle.TextAlign = ContentAlignment.MiddleCenter;
        _completionPanel.Controls.Add(completionTitle);

        Label completionDescription = CreateLabel(
            "现在可以开始整理、分析和导出科研图像。",
            Font,
            Muted,
            new Point(0, 183),
            new Size(680, 26));
        completionDescription.TextAlign = ContentAlignment.MiddleCenter;
        _completionPanel.Controls.Add(completionDescription);

        _completionPathLabel = CreateLabel(
            string.Empty,
            new Font("Microsoft YaHei UI", 8.5F),
            Muted,
            new Point(40, 231),
            new Size(600, 48));
        _completionPathLabel.TextAlign = ContentAlignment.MiddleCenter;
        _completionPanel.Controls.Add(_completionPathLabel);

        _completionLaunchCheckBox = CreateCheckBox(
            "完成后启动 SciCanvas",
            initialOptions.LaunchAfterInstall,
            new Point(248, 308));
        _completionLaunchCheckBox.AutoSize = true;
        _completionPanel.Controls.Add(_completionLaunchCheckBox);

        AcceptButton = _primaryButton;
        CancelButton = _cancelButton;
        FormClosing += InstallerForm_FormClosing;

        UpdateSpaceLabel();
        SetStep(0);
    }

    public int ExitCode { get; private set; }

    private Panel CreateHeader(out Label[] stepLabels)
    {
        Panel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Navy,
            Margin = Padding.Empty,
        };

        Label mark = CreateLabel(
            "S",
            new Font("Segoe UI", 22F, FontStyle.Bold, GraphicsUnit.Point),
            Color.White,
            new Point(36, 26),
            new Size(58, 58));
        mark.BackColor = Accent;
        mark.TextAlign = ContentAlignment.MiddleCenter;
        header.Controls.Add(mark);

        header.Controls.Add(CreateLabel(
            "SciCanvas",
            new Font("Microsoft YaHei UI", 18F, FontStyle.Bold, GraphicsUnit.Point),
            Color.White,
            new Point(112, 24),
            new Size(280, 48)));
        header.Controls.Add(CreateLabel(
            $"科研组图工作台  ·  x64  ·  {Application.ProductVersion}",
            new Font("Microsoft YaHei UI", 8.5F),
            Color.FromArgb(182, 197, 219),
            new Point(114, 78),
            new Size(320, 24)));

        string[] steps = ["1  安装设置", "2  正在安装", "3  完成"];
        stepLabels = new Label[steps.Length];
        for (int index = 0; index < steps.Length; index++)
        {
            Label step = CreateLabel(
                steps[index],
                new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                Color.FromArgb(128, 149, 180),
                new Point(430 + index * 110, 48),
                new Size(110, 32));
            step.TextAlign = ContentAlignment.MiddleCenter;
            header.Controls.Add(step);
            stepLabels[index] = step;
        }

        return header;
    }

    private static Label CreateLabel(
        string text,
        Font font,
        Color color,
        Point location,
        Size size) => new()
        {
            AutoSize = false,
            Text = text,
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent,
            Location = location,
            Size = size,
        };

    private static CheckBox CreateCheckBox(string text, bool isChecked, Point location) => new()
    {
        AutoSize = true,
        Text = text,
        Checked = isChecked,
        Location = location,
        Size = new Size(300, 34),
        ForeColor = Ink,
        BackColor = Color.Transparent,
        UseVisualStyleBackColor = true,
    };

    private static Button CreatePrimaryButton(string text)
    {
        Button button = new()
        {
            Text = text,
            Size = new Size(138, 42),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = AccentHover;
        return button;
    }

    private static Button CreateSecondaryButton(string text)
    {
        Button button = new()
        {
            Text = text,
            Size = new Size(98, 42),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Ink,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(243, 246, 250);
        return button;
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 SciCanvas 安装位置",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_installPathTextBox.Text)
                ? _installPathTextBox.Text
                : InstallerEngine.DefaultInstallDirectory,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void PrimaryButton_Click(object? sender, EventArgs e)
    {
        if (_installationSucceeded)
        {
            FinishInstallation();
            return;
        }

        if (_installationInProgress)
        {
            return;
        }

        InstallerOptions options;
        try
        {
            string normalizedPath = InstallerEngine.NormalizeInstallDirectory(_installPathTextBox.Text);
            InstallerEngine.EnsureEnoughDiskSpace(
                normalizedPath,
                InstallerEngine.PayloadUncompressedSize);
            if (!ConfirmNonEmptyDestination(normalizedPath))
            {
                return;
            }

            _installPathTextBox.Text = normalizedPath;
            options = new InstallerOptions(
                normalizedPath,
                _startMenuCheckBox.Checked,
                _desktopCheckBox.Checked,
                _launchCheckBox.Checked);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                exception.Message,
                "请检查安装设置",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _installPathTextBox.Focus();
            return;
        }

        BeginInstallationUi();
        var progress = new Progress<InstallerProgress>(UpdateProgress);
        try
        {
            await Task.Run(() => InstallerEngine.Install(options, progress));
            ShowCompletion(options);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            ExitCode = 1;
            RestoreConfigurationUi();
            MessageBox.Show(
                $"SciCanvas 安装失败：{exception.Message}",
                "SciCanvas 安装程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private bool ConfirmNonEmptyDestination(string normalizedPath)
    {
        if (!Directory.Exists(normalizedPath) ||
            !Directory.EnumerateFileSystemEntries(normalizedPath).Any() ||
            File.Exists(Path.Combine(normalizedPath, "SciCanvas.App.exe")))
        {
            return true;
        }

        return MessageBox.Show(
            "所选文件夹不是空文件夹。安装可能覆盖其中的同名文件，是否继续？",
            "确认安装位置",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void BeginInstallationUi()
    {
        _installationInProgress = true;
        SetStep(1);
        _installPathTextBox.Enabled = false;
        _browseButton.Enabled = false;
        _startMenuCheckBox.Enabled = false;
        _desktopCheckBox.Enabled = false;
        _launchCheckBox.Enabled = false;
        _cancelButton.Enabled = false;
        _primaryButton.Enabled = false;
        _primaryButton.Text = "正在安装…";
        _progressBar.Value = 0;
        _progressBar.Visible = true;
        _statusLabel.Text = "正在准备安装…";
        _statusLabel.Visible = true;
    }

    private void UpdateProgress(InstallerProgress progress)
    {
        _progressBar.Value = Math.Clamp(progress.Percentage, 0, 100);
        _statusLabel.Text = progress.Message;
    }

    private void RestoreConfigurationUi()
    {
        _installationInProgress = false;
        SetStep(0);
        _installPathTextBox.Enabled = true;
        _browseButton.Enabled = true;
        _startMenuCheckBox.Enabled = true;
        _desktopCheckBox.Enabled = true;
        _launchCheckBox.Enabled = true;
        _cancelButton.Enabled = true;
        _primaryButton.Enabled = true;
        _primaryButton.Text = "重试安装";
        _statusLabel.Text = "安装未完成，请检查提示后重试。";
    }

    private void ShowCompletion(InstallerOptions options)
    {
        ExitCode = 0;
        _installationInProgress = false;
        _installationSucceeded = true;
        _installedDirectory = options.InstallDirectory;
        _configurationPanel.Visible = false;
        _completionPathLabel.Text = $"安装位置\n{options.InstallDirectory}";
        _completionLaunchCheckBox.Checked = options.LaunchAfterInstall;
        _completionPanel.Visible = true;
        _completionPanel.BringToFront();
        _cancelButton.Visible = false;
        _primaryButton.Enabled = true;
        _primaryButton.Text = "完成";
        SetStep(2);
    }

    private void FinishInstallation()
    {
        if (_completionLaunchCheckBox.Checked)
        {
            try
            {
                InstallerEngine.LaunchApplication(_installedDirectory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException or
                    ArgumentException or System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    $"SciCanvas 已安装，但暂时无法启动：{exception.Message}",
                    "SciCanvas 安装程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        Close();
    }

    private void UpdateSpaceLabel()
    {
        try
        {
            long payloadBytes = InstallerEngine.PayloadUncompressedSize;
            string normalizedPath = InstallerEngine.NormalizeInstallDirectory(_installPathTextBox.Text);
            string? root = Path.GetPathRoot(normalizedPath);
            string availability = string.Empty;
            if (!string.IsNullOrWhiteSpace(root) &&
                !root.StartsWith("\\\\", StringComparison.Ordinal))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    availability = $"，{drive.Name} 可用 {InstallerEngine.FormatBytes(drive.AvailableFreeSpace)}";
                }
            }

            _spaceLabel.Text = $"需要约 {InstallerEngine.FormatBytes(payloadBytes)} 空间{availability}";
            _spaceLabel.ForeColor = Muted;
        }
        catch
        {
            _spaceLabel.Text = "输入完整路径，或点击“浏览”选择文件夹";
            _spaceLabel.ForeColor = Color.FromArgb(184, 91, 22);
        }
    }

    private void SetStep(int activeIndex)
    {
        for (int index = 0; index < _stepLabels.Length; index++)
        {
            _stepLabels[index].ForeColor = index == activeIndex
                ? Color.White
                : Color.FromArgb(128, 149, 180);
        }
    }

    private void InstallerForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_installationInProgress)
        {
            e.Cancel = true;
            SystemSounds.Beep.Play();
            _statusLabel.Text = "正在写入应用文件，请等待安装完成。";
        }
    }
}
