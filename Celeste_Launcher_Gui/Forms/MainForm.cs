﻿#region Using directives

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.Timers;
using System.Windows.Forms;
using Celeste_User.Enum;
using Celeste_User.Remote;
using Timer = System.Timers.Timer;

#endregion

namespace Celeste_Launcher_Gui.Forms
{
    public partial class MainForm : Form
    {
        private static Timer _timer;
        private static bool _loginPassed;

        public MainForm()
        {
            InitializeComponent();
            //OnPropertyChanged
            Program.WebSocketClient.PropertyChanged += OnPropertyChanged;

            //Login
            if (Program.WebSocketClient.State != WebSocketClientState.Logging ||
                Program.WebSocketClient.State != WebSocketClientState.Logged)
                using (var form = new LoginForm())
                {
                    var dr = form.ShowDialog();

                    if (dr != DialogResult.OK)
                    {
                        Program.WebSocketClient.AgentWebSocket.Close();
                        Environment.Exit(0);
                    }
                }

            //User Info
            if (Program.RemoteUser != null)
                ExecuteUserInfoResultCommand(Program.RemoteUser);

            //Start xLiveBridgeServer
            Program.Server.Setup(Program.ServerConfig);
            Program.Server.Start();

            //Auto-Refresh User Info
            if (_timer != null) return;

            _timer = new Timer(1000 * 60); //60Sec
            _timer.Elapsed += DoUserInfo;
            _timer.AutoReset = true;
            _timer.Enabled = true;
            _timer.Enabled = true;
            _timer.Start();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            var pname = Process.GetProcessesByName("spartan");
            if (pname.Length > 0)
            {
                MessageBox.Show(@"You need to close the game first!");
                e.Cancel = true;
                return;
            }
            _timer.Stop();
            Program.WebSocketClient.AgentWebSocket.Close();
        }

        private void linklbl_ReportBug_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://github.com/ProjectCeleste/Celeste_Server/issues");
        }

        private void linkLbl_ProjectCelesteCom_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://projectceleste.com");
        }

        private void linklbl_Wiki_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://ageofempiresonline.wikia.com/wiki/Age_of_Empires_Online_Wiki");
        }

        private void linkLabel3_LinkClicked_1(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://eso-community.net/");
        }

        private void linkLbl_aoeo4evernet_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://aoeo4ever.net");
        }

        private void linkLabel3_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var form = new UpgradeForm())
            {
                Hide();
                form.ShowDialog();
                Show();
            }
        }

        private void linkLbl_ChangePwd_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var form = new ChangePwdForm())
            {
                Hide();
                form.ShowDialog();
                Show();
            }
        }

        private void pb_Avatar_Click(object sender, EventArgs e)
        {
            //TODO
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            //TODO
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            //TODO
        }

        private void linkLbl_ReportUser_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            //TODO
        }

        private void btn_ManageInvite_Click(object sender, EventArgs e)
        {
            using (var form = new ManageInviteForm())
            {
                Hide();
                form.ShowDialog();
                Show();
            }
        }

        private static void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (e.PropertyName)
            {
                case "State":
                {
                    switch (Program.WebSocketClient.State)
                    {
                        case WebSocketClientState.Offline:
                        {
                            if (_timer != null && _timer.Enabled)
                            {
                                _timer.Enabled = false;
                                _timer.Stop();
                            }
                            if (_loginPassed)
                            {
                                MessageBox.Show(@"You have been disconnected from the server!", @"Project Celeste",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                                _loginPassed = false;

                                //Login
                                if (Program.WebSocketClient.State != WebSocketClientState.Logging ||
                                    Program.WebSocketClient.State != WebSocketClientState.Logged)
                                    using (var form = new LoginForm())
                                    {
                                        var dr = form.ShowDialog();

                                        if (dr != DialogResult.OK)
                                        {
                                            Program.WebSocketClient.AgentWebSocket.Close();
                                            Environment.Exit(0);
                                        }
                                    }
                            }
                            break;
                        }
                        case WebSocketClientState.Connecting:
                            break;
                        case WebSocketClientState.Logging:
                            break;
                        case WebSocketClientState.Logged:
                        {
                            if (_timer != null && !_timer.Enabled)
                            {
                                _timer.Enabled = true;
                                _timer.Start();
                            }
                            _loginPassed = true;
                            break;
                        }
                        case WebSocketClientState.Connected:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(WebSocketClient.State),
                                Program.WebSocketClient.State,
                                @"OnPropertyChanged()");
                    }
                    break;
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var pname = Process.GetProcessesByName("spartan");
            if (pname.Length > 0)
            {
                MessageBox.Show(@"Game already runing!");
                return;
            }

            var path = $"{AppDomain.CurrentDomain.BaseDirectory}spartan.exe";

            Process.Start(path);
        }

        #region "User Info"

        private UserInfoResult _userInfoResult;

        private delegate void UserInfoResult(RemoteUser remoteUser);

        private void DoUserInfo(object source, ElapsedEventArgs e)
        {
            DoUserInfo();
        }

        private void DoUserInfo()
        {
            if (Program.WebSocketClient.State != WebSocketClientState.Logged)
                return;

            dynamic getUserInfo = new ExpandoObject();
            getUserInfo.UserName = "";

            Program.WebSocketClient.AgentWebSocket?.Query<dynamic>("GETUSERINFO", (object) getUserInfo, OnUserInfo);
        }

        private void OnUserInfo(dynamic result)
        {
            if (result["Result"].ToObject<bool>())
            {
                Program.RemoteUser = result["RemoteUser"].ToObject<RemoteUser>();

                if (_userInfoResult == null)
                    _userInfoResult = ExecuteUserInfoResultCommand;
                try
                {
                    Invoke(_userInfoResult, Program.RemoteUser);
                }
                catch (Exception)
                {
                    //
                }
            }
            else
            {
                var str = result["Message"].ToObject<string>();
                MessageBox.Show($@"OnUserInfo(): {str}", @"Project Celeste",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExecuteUserInfoResultCommand(RemoteUser remoteUser)
        {
            lbl_Mail.Text = $@"Email: {remoteUser.Mail}";
            lbl_UserName.Text = $@"User Name: {remoteUser.ProfileName}";
            lbl_Rank.Text = $@"Rank: {remoteUser.Rank}";
            //
            if (!remoteUser.BannedGame && !remoteUser.BannedChat)
                lbl_Banned.Text = @"Banned: false";
            else if (remoteUser.BannedGame && remoteUser.BannedChat)
                lbl_Banned.Text = @"Banned: Game and Chat!";
            else if (remoteUser.BannedGame && !remoteUser.BannedChat)
                lbl_Banned.Text = @"Banned: Game!";
            else if (!remoteUser.BannedGame && remoteUser.BannedChat)
                lbl_Banned.Text = @"Banned: Chat!";
            //
            if (!remoteUser.IsConnectedGameServer && !remoteUser.IsConnectedCustomChatServer)
                lbl_Connected.Text = @"Connected: false";
            else if (remoteUser.IsConnectedGameServer && remoteUser.IsConnectedCustomChatServer)
                lbl_Connected.Text = @"Connected: Game and Chat!";
            else if (remoteUser.IsConnectedGameServer && !remoteUser.IsConnectedCustomChatServer)
                lbl_Connected.Text = @"Connected: Game";
            else if (!remoteUser.IsConnectedGameServer && remoteUser.IsConnectedCustomChatServer)
                lbl_Connected.Text = @"Connected: Chat";
            //
            comboBox1.Items.Clear();
            if (remoteUser.AllowedCiv.Count > 0)
                foreach (var civ in remoteUser.AllowedCiv)
                {
                    var strCiv = Enum.GetName(typeof(Civilization), civ);
                    if (string.IsNullOrEmpty(strCiv))
                        strCiv = "Unknow";
                    comboBox1.Items.Add(strCiv);
                }
            else
                comboBox1.Items.Add("None");

            comboBox1.SelectedIndex = 0;
        }

        #endregion
    }
}