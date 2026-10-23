using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RoseBuild
{
    /// <summary>Panel URL, webhook, license key and output folder.</summary>
    public sealed class SettingsForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(12, 10, 14);
        private static readonly Color Card = Color.FromArgb(22, 18, 27);
        private static readonly Color CardAlt = Color.FromArgb(28, 23, 34);
        private static readonly Color Border = Color.FromArgb(40, 33, 48);
        private static readonly Color Ink = Color.FromArgb(236, 232, 241);
        private static readonly Color Muted = Color.FromArgb(146, 138, 156);
        private static readonly Color Pink = Color.FromArgb(232, 90, 150);
        private static readonly Color Ok = Color.FromArgb(90, 224, 148);
        private static readonly Color Bad = Color.FromArgb(236, 74, 96);

        private static readonly Font FUi = new Font("Segoe UI", 9f);
        private static readonly Font FSmall = new Font("Segoe UI", 8f);
        private static readonly Font FMono = new Font("Consolas", 8.5f);

        private readonly AppConfig _cfg;

        private TextBox _panel;
        private TextBox _webhook;
        private TextBox _license;
        private TextBox _outDir;
        private Label _status;

        public SettingsForm(AppConfig cfg)
        {
            _cfg = cfg;

            Text = "Rose - Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 400);
            BackColor = Bg;
            Font = FUi;

            Label title = new Label();
            title.Text = "Account & panel";
            title.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            title.ForeColor = Ink;
            title.Bounds = new Rectangle(20, 16, 300, 24);
            Controls.Add(title);

            AddLabel("Panel URL", 20, 52);
            _panel = AddField(20, 70, 520);

            AddLabel("Discord webhook (optional)", 20, 104);
            _webhook = AddField(20, 122, 520);

            AddLabel("License key", 20, 156);
            _license = AddField(20, 174, 520);

            AddLabel("Build output folder", 20, 208);
            _outDir = AddField(20, 226, 430);

            Button browse = new Button();
            browse.Text = "Browse";
            browse.Bounds = new Rectangle(458, 225, 82, 26);
            browse.FlatStyle = FlatStyle.Flat;
            browse.FlatAppearance.BorderColor = Border;
            browse.FlatAppearance.BorderSize = 1;
            browse.BackColor = CardAlt;
            browse.ForeColor = Ink;
            browse.Font = FSmall;
            browse.Cursor = Cursors.Hand;
            browse.Click += delegate { BrowseOutDir(); };
            Controls.Add(browse);

            _status = new Label();
            _status.Text = "";
            _status.Font = FSmall;
            _status.ForeColor = Muted;
            _status.Bounds = new Rectangle(20, 262, 520, 36);
            Controls.Add(_status);

            Button save = new Button();
            save.Text = "Save Settings";
            save.Bounds = new Rectangle(20, 310, 250, 40);
            save.FlatStyle = FlatStyle.Flat;
            save.FlatAppearance.BorderSize = 0;
            save.BackColor = Pink;
            save.ForeColor = Color.White;
            save.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            save.Cursor = Cursors.Hand;
            save.Click += delegate { SaveSettings(); };
            Controls.Add(save);

            Button test = new Button();
            test.Text = "Test webhook";
            test.Bounds = new Rectangle(290, 310, 250, 40);
            test.FlatStyle = FlatStyle.Flat;
            test.FlatAppearance.BorderColor = Border;
            test.FlatAppearance.BorderSize = 1;
            test.BackColor = CardAlt;
            test.ForeColor = Ink;
            test.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            test.Cursor = Cursors.Hand;
            test.Click += delegate { TestWebhook(); };
            Controls.Add(test);

            LinkLabel hwid = new LinkLabel();
            hwid.Text = "device " + cfg.Hwid + "  -  copy";
            hwid.Font = FSmall;
            hwid.LinkColor = Muted;
            hwid.ActiveLinkColor = Pink;
            hwid.LinkBehavior = LinkBehavior.HoverUnderline;
            hwid.AutoSize = true;
            hwid.Location = new Point(20, 360);
            hwid.LinkClicked += delegate
            {
                try { Clipboard.SetText(_cfg.Hwid); _status.ForeColor = Ok; _status.Text = "device id copied."; }
                catch { }
            };
            Controls.Add(hwid);

            _panel.Text = cfg.PanelUrl;
            _webhook.Text = cfg.WebhookUrl;
            _license.Text = cfg.LicenseKey;
            _outDir.Text = cfg.OutDir;
        }

        private void AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FSmall;
            l.ForeColor = Muted;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            Controls.Add(l);
        }

        private TextBox AddField(int x, int y, int width)
        {
            TextBox box = new TextBox();
            box.Bounds = new Rectangle(x, y, width, 24);
            box.BackColor = CardAlt;
            box.ForeColor = Ink;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = FMono;
            Controls.Add(box);
            return box;
        }

        private void BrowseOutDir()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.ShowNewFolderButton = true;
                dlg.SelectedPath = _outDir.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _outDir.Text = dlg.SelectedPath;
            }
        }

        private void SaveSettings()
        {
            string panel = _panel.Text.Trim().TrimEnd('/');
            string hook = _webhook.Text.Trim();

            if (panel.Length > 0 && !IsHttps(panel))
            {
                _status.ForeColor = Bad;
                _status.Text = "panel URL must start with https://";
                return;
            }

            if (hook.Length > 0 && !IsHttps(hook))
            {
                _status.ForeColor = Bad;
                _status.Text = "webhook must start with https://";
                return;
            }

            _cfg.PanelUrl = panel;
            _cfg.WebhookUrl = hook;
            _cfg.LicenseKey = _license.Text.Trim();
            _cfg.OutDir = _outDir.Text.Trim();
            _cfg.Save();

            _status.ForeColor = Ok;
            _status.Text = "saved to " + AppConfig.Path_;
        }

        private void TestWebhook()
        {
            string hook = _webhook.Text.Trim();
            if (hook.Length == 0 || !IsHttps(hook))
            {
                _status.ForeColor = Bad;
                _status.Text = "enter an https webhook first.";
                return;
            }

            _status.ForeColor = Muted;
            _status.Text = "sending test...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                string outcome;
                try
                {
                    string body = "{\"username\":\"Rose\",\"allowed_mentions\":{\"parse\":[]}," +
                                  "\"content\":\"panel test ok | " + _cfg.Hwid + "\"}";
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(hook);
                    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
                    catch { }
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.Timeout = 8000;
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = bytes.Length;
                    using (Stream s = req.GetRequestStream())
                        s.Write(bytes, 0, bytes.Length);
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                        outcome = "delivered (" + (int)resp.StatusCode + ")";
                }
                catch (Exception ex)
                {
                    outcome = "failed: " + ex.Message;
                }

                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _status.ForeColor = outcome.StartsWith("delivered") ? Ok : Bad;
                        _status.Text = outcome;
                    }));
                }
                catch { }
            });
        }

        private static bool IsHttps(string url)
        {
            Uri parsed;
            return Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == Uri.UriSchemeHttps;
        }
    }
}
