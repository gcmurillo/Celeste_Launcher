﻿#region Using directives

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Celeste_AOEO_Controls;
using Celeste_AOEO_Controls.MsgBox;
using Celeste_Launcher_Gui.Helpers;
using Celeste_Public_Api.GameScanner_Api;
using Celeste_Public_Api.Helpers;
using Open.Nat;

#endregion

namespace Celeste_Launcher_Gui.Forms
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();

            //
            lb_Ver.Text = $@"v{Assembly.GetEntryAssembly().GetName().Version}";
            UpdateDiagModeToolStripFromConfig();

            //
            panelManager1.SelectedPanel = managedPanel2;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                Program.WebSocketApi?.Disconnect();
                NatDiscoverer.ReleaseAll();
            }
            catch
            {
                //
            }
        }

        private async void Btn_Play_Click(object sender, EventArgs e)
        {
            btn_Play.Enabled = false;
            var pname = Process.GetProcessesByName("spartan");
            if (pname.Length > 0)
            {
                MsgBox.ShowMessage(@"Game already running!");
                btn_Play.Enabled = true;
                return;
            }

            //QuickGameScan
            try
            {
                var gameFilePath = !string.IsNullOrWhiteSpace(Program.UserConfig.GameFilesPath)
                    ? Program.UserConfig.GameFilesPath
                    : GameScannnerApi.GetGameFilesRootPath();

                var gameScannner = new GameScannnerApi(gameFilePath, Program.UserConfig.IsSteamVersion,
                    Program.UserConfig.IsLegacyXLive);

                retry:
                if (!await gameScannner.QuickScan())
                {
                    bool success;
                    using (var form =
                        new MsgBoxYesNo(
                            @"Error: Your game files are corrupted or outdated. Click ""Yes"" to run a ""Game Scan"" to fix your game files, or ""No"" to ignore the error (not recommended).")
                    )
                    {
                        var dr = form.ShowDialog();
                        if (dr == DialogResult.OK)
                            using (var form2 = new GameScan())
                            {
                                form2.ShowDialog();
                                success = false;
                            }
                        else
                            success = true;
                    }
                    if (!success)
                        goto retry;
                }
            }
            catch (Exception ex)
            {
                MsgBox.ShowMessage(
                    $"Warning: Error during quick scan. Error message: {ex.Message}",
                    @"Celeste Fan Project",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            //isSteam
            if (!Program.UserConfig.IsSteamVersion)
            {
                var steamApiDll = $"{Program.UserConfig.GameFilesPath}\\steam_api.dll";
                if (File.Exists(steamApiDll))
                    File.Delete(steamApiDll);
            }

            //MpSettings
            try
            {
                if (Program.UserConfig.MpSettings != null)
                    if (Program.UserConfig.MpSettings.IsOnline)
                    {
                        Program.UserConfig.MpSettings.PublicIp = Program.CurrentUser.Ip;

                        if (Program.UserConfig.MpSettings.AutoPortMapping)
                        {
                            var mapPortTask = OpenNat.MapPortTask(1000, 1000);
                            try
                            {
                                await mapPortTask;
                                NatDiscoverer.TraceSource.Close();
                            }
                            catch (AggregateException ex)
                            {
                                NatDiscoverer.TraceSource.Close();

                                if (!(ex.InnerException is NatDeviceNotFoundException)) throw;

                                MsgBox.ShowMessage(
                                    "Error: Upnp device not found! Set \"Port mapping\" to manual in \"Mp Settings\" and configure your router.",
                                    @"Celeste Fan Project",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                                btn_Play.Enabled = true;

                                return;
                            }
                        }
                    }
            }
            catch
            {
                MsgBox.ShowMessage(
                    "Error: Upnp device not found! Set \"Port mapping\" to manual in \"Mp Settings\" and configure your router.",
                    @"Celeste Fan Project",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                btn_Play.Enabled = true;

                return;
            }

            try
            {
                //Save UserConfig
                Program.UserConfig.Save(Program.UserConfigFilePath);
            }
            catch
            {
                //
            }

            try
            {
                //Launch Game
                var path = !string.IsNullOrEmpty(Program.UserConfig.GameFilesPath)
                    ? Program.UserConfig.GameFilesPath
                    : GameScannnerApi.GetGameFilesRootPath();

                if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    path += Path.DirectorySeparatorChar;

                var spartanPath = Path.Combine(path, "Spartan.exe");

                if (!File.Exists(spartanPath))
                    throw new FileNotFoundException("Spartan.exe not found!", spartanPath);

                string lang;
                switch (Program.UserConfig.GameLanguage)
                {
                    case GameLanguage.deDE:
                        lang = "de-DE";
                        break;
                    case GameLanguage.enUS:
                        lang = "en-US";
                        break;
                    case GameLanguage.esES:
                        lang = "es-ES";
                        break;
                    case GameLanguage.frFR:
                        lang = "fr-FR";
                        break;
                    case GameLanguage.itIT:
                        lang = "it-IT";
                        break;
                    case GameLanguage.zhCHT:
                        lang = "zh-CHT";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                try
                {
                    if (Program.UserConfig.IsDiagnosticMode)
                    {
                        var procdumpFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "procdump.exe");
                        const int maxNumOfCrashDumps = 30;
                        if (!File.Exists(procdumpFileName))
                            throw new FileNotFoundException("Diagonstic Mode requires procdump.exe (File not Found)",
                                procdumpFileName);

                        // First ensure that all directories are set
                        var pathToCrashDumpFolder =
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                @"Spartan\MiniDumps");

                        if (!Directory.Exists(pathToCrashDumpFolder))
                            Directory.CreateDirectory(pathToCrashDumpFolder);

                        // Check for cleanup
                        Directory.GetFiles(pathToCrashDumpFolder)
                            .OrderByDescending(File.GetLastWriteTime) // Sort by age --> old one last
                            .Skip(maxNumOfCrashDumps) // Skip max num crash dumps
                            .ToList()
                            .ForEach(File.Delete); // Remove the rest

                        var excludeExceptions = new[]
                        {
                            "E0434F4D.COM", // .NET native exception
                            "E06D7363.msc",
                            "E06D7363.PAVEEFileLoadException@@",
                            "E0434F4D.System.IO.FileNotFoundException" // .NET managed exception
                        };

                        var excludeExcpetionsCmd = string.Join(" ", excludeExceptions.Select(elem => "-fx " + elem));

                        var fullCmdArgs = "-accepteula -mm -e 1 -n 10 " + excludeExcpetionsCmd +
                                          " -g -w Spartan.exe \"" + pathToCrashDumpFolder + "\"";

                        // MsgBox.ShowMessage(fullCmd);
                        var startInfo = new ProcessStartInfo(procdumpFileName, fullCmdArgs)
                        {
                            WorkingDirectory = path,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true
                        };

                        Process.Start(startInfo);
                    }
                }
                catch (Exception exception)
                {
                    MsgBox.ShowMessage(
                        $"Warning: {exception.Message}",
                        @"Celeste Fan Project",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                var arg = Program.UserConfig?.MpSettings == null || Program.UserConfig.MpSettings.IsOnline
                    ? $"--email \"{Program.UserConfig.LoginInfo.Email}\"  --password \"{Program.UserConfig.LoginInfo.Password}\" --ignore_rest LauncherLang={lang} LauncherLocale=1033"
                    : $"--email \"{Program.UserConfig.LoginInfo.Email}\"  --password \"{Program.UserConfig.LoginInfo.Password}\" --online-ip \"{Program.UserConfig.MpSettings.PublicIp}\" --ignore_rest LauncherLang={lang} LauncherLocale=1033";

                Process.Start(new ProcessStartInfo(spartanPath, arg) {WorkingDirectory = path});

                WindowState = FormWindowState.Minimized;
            }
            catch (Exception exception)
            {
                MsgBox.ShowMessage(
                    $"Error: {exception.Message}",
                    @"Celeste Fan Project",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            btn_Play.Enabled = true;
        }

        private void PictureBoxButtonCustom3_Click(object sender, EventArgs e)
        {
            var btnSender = (PictureBoxButtonCustom) sender;
            var pt = new Point(btnSender.Bounds.Width + 1, btnSender.Bounds.Top);
            cMS_ProjectCelesteCom.Show(btnSender, pt);
        }

        private void PictureBoxButtonCustom1_Click(object sender, EventArgs e)
        {
            Process.Start("https://discord.gg/pkM2RAm");
        }

        private void PictureBoxButtonCustom2_Click(object sender, EventArgs e)
        {
            Process.Start("https://www.reddit.com/r/projectceleste/");
        }

        private void PictureBoxButtonCustom4_Click(object sender, EventArgs e)
        {
            Process.Start("http://aoedb.net/");
        }

        private void PictureBoxButtonCustom5_Click(object sender, EventArgs e)
        {
            var btnSender = (PictureBoxButtonCustom) sender;
            var pt = new Point(btnSender.Bounds.Width + 1, btnSender.Bounds.Top);
            cMS_Donate.Show(btnSender, pt);
        }

        private void CustomBtn1_Click(object sender, EventArgs e)
        {
            //Login
            using (var form = new LoginForm())
            {
                var dr = form.ShowDialog();

                if (dr == DialogResult.OK)
                {
                    if (form.CurrentUser == null)
                        return;

                    Program.CurrentUser = form.CurrentUser;

                    gamerCard1.UserName = $@"{Program.CurrentUser.ProfileName}";
                    gamerCard1.Rank = $@"{Program.CurrentUser.Rank}";

                    panelManager1.SelectedPanel = managedPanel1;
                }
                else
                {
                    panelManager1.SelectedPanel = managedPanel2;
                }
            }
        }

        private void LinkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://www.xbox.com/en-us/developers/rules");
        }

        private void PictureBoxButtonCustom8_Click(object sender, EventArgs e)
        {
            var btnSender = (PictureBoxButtonCustom) sender;
            var pt = new Point(btnSender.Bounds.Width + 1, btnSender.Bounds.Top);
            cMS_Tools.Show(btnSender, pt);
        }

        private void CustomBtn2_Click(object sender, EventArgs e)
        {
            //Register
            using (var form = new RegisterForm())
            {
                form.ShowDialog();
                if (form.DialogResult != DialogResult.OK)
                    return;

                //Save UserConfig
                if (Program.UserConfig == null)
                {
                    Program.UserConfig = new UserConfig
                    {
                        LoginInfo = new LoginInfo
                        {
                            Email = form.tb_Mail.Text,
                            Password = form.tb_Password.Text,
                            RememberMe = true
                        }
                    };
                }
                else
                {
                    Program.UserConfig.LoginInfo.Email = form.tb_Mail.Text;
                    Program.UserConfig.LoginInfo.Password = form.tb_Password.Text;
                    Program.UserConfig.LoginInfo.RememberMe = true;
                }
                try
                {
                    Program.UserConfig.Save(Program.UserConfigFilePath);
                }
                catch (Exception)
                {
                    //
                }
            }

            //Login
            using (var form = new LoginForm())
            {
                var dr = form.ShowDialog();

                if (dr == DialogResult.OK)
                {
                    if (form.CurrentUser == null)
                        return;

                    Program.CurrentUser = form.CurrentUser;

                    gamerCard1.UserName = $@"{Program.CurrentUser.ProfileName}";
                    gamerCard1.Rank = $@"{Program.CurrentUser.Rank}";

                    panelManager1.SelectedPanel = managedPanel1;
                }
                else
                {
                    panelManager1.SelectedPanel = managedPanel2;
                }
            }
        }

        private void UpdaterToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var pname = Process.GetProcessesByName("spartan");
            if (pname.Length > 0)
            {
                MsgBox.ShowMessage(@"Game is running, you need to close it first!");
                return;
            }

            using (var form = new UpdaterForm())
            {
                form.ShowDialog();
            }
        }

        private void ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            var pname = Process.GetProcessesByName("spartan");
            if (pname.Length > 0)
            {
                MsgBox.ShowMessage(@"Game is running, you need to close it first!");
                return;
            }

            using (var form = new GameScan())
            {
                form.ShowDialog();
            }
        }

        private void PictureBoxButtonCustom9_Click(object sender, EventArgs e)
        {
            var btnSender = (PictureBoxButtonCustom) sender;
            var pt = new Point(btnSender.Bounds.Width + 1, btnSender.Bounds.Top);
            cMS_Settings.Show(btnSender, pt);
        }

        private void PictureBoxButtonCustom7_Click(object sender, EventArgs e)
        {
            var btnSender = (PictureBoxButtonCustom) sender;
            var pt = new Point(btnSender.Bounds.Width + 1, btnSender.Bounds.Top);
            cMS_Account.Show(btnSender, pt);
        }

        private void ToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            using (var form = new LanguageChooser())
            {
                var dr = form.ShowDialog();

                if (dr != DialogResult.OK)
                    return;

                try
                {
                    if (Program.UserConfig != null)
                        Program.UserConfig.GameLanguage = form.SelectedLang;
                    else
                        Program.UserConfig = new UserConfig {GameLanguage = form.SelectedLang};
                    Program.UserConfig.Save(Program.UserConfigFilePath);
                }
                catch (Exception)
                {
                    //
                }
            }
        }

        private void ToolStripMenuItem3_Click(object sender, EventArgs e)
        {
            using (var form = new MpSettingForm(Program.UserConfig.MpSettings))
            {
                form.ShowDialog();
            }
        }

        private void ToolStripMenuItem4_Click(object sender, EventArgs e)
        {
            using (var form = new ChangePwdForm())
            {
                form.ShowDialog();
            }
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            //CleanUpFiles
            try
            {
                Misc.CleanUpFiles(Directory.GetCurrentDirectory(), "*.old");
            }
            catch (Exception ex)
            {
                MsgBox.ShowMessage(
                    $"Warning: Error during files clean-up. Error message: {ex.Message}",
                    @"Celeste Fan Project",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            //Update Check
            try
            {
                if (await UpdaterForm.GetGitHubVersion() > Assembly.GetExecutingAssembly().GetName().Version)
                    using (var form =
                        new MsgBoxYesNo(
                            @"An update is avalaible. Click ""Yes"" to install it, or ""No"" to ignore it (not recommended).")
                    )
                    {
                        var dr = form.ShowDialog();
                        if (dr == DialogResult.OK)
                            using (var form2 = new UpdaterForm())
                            {
                                form2.ShowDialog();
                            }
                    }
            }
            catch (Exception ex)
            {
                MsgBox.ShowMessage(
                    $"Warning: Error during update check. Error message: {ex.Message}",
                    @"Celeste Fan Project",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            //Auto Login
            if (Program.UserConfig?.LoginInfo == null)
                return;

            if (!Program.UserConfig.LoginInfo.AutoLogin)
                return;

            panelManager1.Enabled = false;
            try
            {
                var response = await Program.WebSocketApi.DoLogin(Program.UserConfig.LoginInfo.Email,
                    Program.UserConfig.LoginInfo.Password);

                if (response.Result)
                {
                    Program.CurrentUser = response.User;

                    gamerCard1.UserName = $@"{Program.CurrentUser.ProfileName}";
                    gamerCard1.Rank = $@"{Program.CurrentUser.Rank}";

                    panelManager1.SelectedPanel = managedPanel1;
                }
            }
            catch (Exception)
            {
                //
            }
            panelManager1.Enabled = true;
        }

        private void DonateWithPayPalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=EZ3SSAJRRUYFY");
        }

        private void MoreDonateOptionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://forums.projectceleste.com/donations/");
        }

        private void HomeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://projectceleste.com");
        }

        private void ForumsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://forums.projectceleste.com");
        }

        private void LogOffToolStripMenuItem_Click(object sender, EventArgs e)
        {
            panelManager1.Enabled = false;
            //
            try
            {
                Program.WebSocketApi?.Disconnect();
            }
            catch
            {
                //
            }
            //

            //Save UserConfig
            if (Program.UserConfig?.LoginInfo != null)
            {
                Program.UserConfig.LoginInfo.AutoLogin = false;

                try
                {
                    Program.UserConfig.Save(Program.UserConfigFilePath);
                }
                catch (Exception)
                {
                    //
                }
            }

            //
            panelManager1.SelectedPanel = managedPanel2;
            panelManager1.Enabled = true;
        }

        private void WindowsFeaturesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new WindowsFeaturesForm())
            {
                form.ShowDialog();
            }
        }

        private void SteamToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var form = new SteamForm())
            {
                form.ShowDialog();
            }
        }

        private void PictureBoxButtonCustom6_Click(object sender, EventArgs e)
        {
            using (var x = new FriendsForm())
            {
                x.ShowDialog();
            }
        }

        private void UpdateDiagModeToolStripFromConfig()
        {
            enableDiagnosticModeToolStripMenuItem.Text = Program.UserConfig.IsDiagnosticMode
                ? @"Disable Diagnostic Mode"
                : @"Enable Diagnostic Mode";
        }

        private void EnableDiagnosticModeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Program.UserConfig.IsDiagnosticMode = !Program.UserConfig.IsDiagnosticMode;

            UpdateDiagModeToolStripFromConfig();
        }
    }
}