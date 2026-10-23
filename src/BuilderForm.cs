using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RoseBuild
{
    public sealed class BuilderForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(12, 10, 14);
        private static readonly Color Rail = Color.FromArgb(16, 13, 19);
        private static readonly Color Card = Color.FromArgb(22, 18, 27);
        private static readonly Color CardAlt = Color.FromArgb(28, 23, 34);
        private static readonly Color Border = Color.FromArgb(40, 33, 48);
        private static readonly Color Ink = Color.FromArgb(236, 232, 241);
        private static readonly Color Muted = Color.FromArgb(146, 138, 156);
        private static readonly Color Pink = Color.FromArgb(232, 90, 150);
        private static readonly Color PinkDim = Color.FromArgb(122, 42, 74);
        private static readonly Color Ok = Color.FromArgb(90, 224, 148);
        private static readonly Color Bad = Color.FromArgb(236, 74, 96);

        private static readonly Font FUi = new Font("Segoe UI", 9f);
        private static readonly Font FSmall = new Font("Segoe UI", 8f);
        private static readonly Font FBold = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        private static readonly Font FMono = new Font("Consolas", 8.5f);

        private readonly AppConfig _cfg;

        private TextBox _name;
        private TextBox _ext;
        private TextBox _icon;
        private TextBox _description;
        private TextBox _product;
        private TextBox _company;
        private TextBox _fileVersion;
        private TextBox _productVersion;
        private TextBox _copyright;
        private TextBox _log;
        private Button _build;
        private Label _licenseLabel;
        private Label _deviceLabel;
        private Label _webhookLabel;
        private Label _railBuild;
        private Label _railAccount;
        private bool _busy;

        public BuilderForm(AppConfig cfg)
        {
            _cfg = cfg;

            Text = "Rose";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(880, 680);
            MinimumSize = new Size(880, 680);
            BackColor = Bg;
            DoubleBuffered = true;
            Font = FUi;

            BuildUi();
            LoadSettings();
            Shown += delegate { RoundCorners(); CheckLicense(); };
            Resize += delegate { RoundCorners(); };
        }

        private void BuildUi()
        {
            Panel rail = new Panel();
            rail.Bounds = new Rectangle(0, 0, 180, ClientSize.Height);
            rail.BackColor = Rail;
            rail.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(rail);

            Label mark = new Label();
            mark.Text = "\u25C6  Rose";
            mark.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
            mark.ForeColor = Ink;
            mark.Bounds = new Rectangle(20, 18, 140, 26);
            mark.MouseDown += DragWindow;
            rail.Controls.Add(mark);

            _railAccount = RailItem("Account", 90);
            _railAccount.Click += delegate { OpenSettings(); };
            rail.Controls.Add(_railAccount);

            _railBuild = RailItem("Builder", 132);
            _railBuild.BackColor = PinkDim;
            _railBuild.ForeColor = Ink;
            rail.Controls.Add(_railBuild);

            Label version = new Label();
            version.Text = "v1.0.0 \u2014 Rose Software";
            version.Font = FSmall;
            version.ForeColor = Muted;
            version.Bounds = new Rectangle(20, ClientSize.Height - 32, 150, 18);
            version.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            rail.Controls.Add(version);

            Label close = WindowButton("\u2715");
            close.Bounds = new Rectangle(ClientSize.Width - 44, 12, 30, 26);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Label min = WindowButton("\u2014");
            min.Bounds = new Rectangle(ClientSize.Width - 80, 12, 30, 26);
            min.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            min.Click += delegate { WindowState = FormWindowState.Minimized; };
            Controls.Add(min);

            Label title = new Label();
            title.Text = "Builder";
            title.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            title.ForeColor = Ink;
            title.Bounds = new Rectangle(196, 16, 300, 24);
            title.MouseDown += DragWindow;
            Controls.Add(title);

            // account strip
            Panel strip = CardPanel(new Rectangle(196, 52, 660, 64));
            Controls.Add(strip);

            _licenseLabel = StripChip(strip, "LICENSE", 18, Muted);
            _deviceLabel = StripChip(strip, "DEVICE", 240, Muted);
            _webhookLabel = StripChip(strip, "WEBHOOK", 470, Muted);

            // customization card
            Panel custom = CardPanel(new Rectangle(196, 128, 660, 176));
            Controls.Add(custom);
            Section(custom, "+ Executable customization", 18, 14);

            Label nameLabel = FieldLabel("Executable name", 18, 40);
            custom.Controls.Add(nameLabel);
            _name = Field(custom, 18, 58, 360);

            Label extLabel = FieldLabel("Extension", 398, 40);
            custom.Controls.Add(extLabel);
            _ext = Field(custom, 398, 58, 80);

            Label iconLabel = FieldLabel("Executable icon", 18, 100);
            custom.Controls.Add(iconLabel);
            _icon = Field(custom, 18, 118, 460);
            _icon.ReadOnly = true;

            Button choose = GhostButton("Choose icon");
            choose.Bounds = new Rectangle(490, 118, 150, 26);
            choose.Click += delegate { ChooseIcon(); };
            custom.Controls.Add(choose);

            // information card
            Panel info = CardPanel(new Rectangle(196, 316, 660, 176));
            Controls.Add(info);
            Section(info, "+ Executable information", 18, 14);

            info.Controls.Add(FieldLabel("File description", 18, 40));
            _description = Field(info, 18, 58, 190);

            info.Controls.Add(FieldLabel("Product name", 226, 40));
            _product = Field(info, 226, 58, 190);

            info.Controls.Add(FieldLabel("Company", 434, 40));
            _company = Field(info, 434, 58, 190);

            info.Controls.Add(FieldLabel("File version", 18, 100));
            _fileVersion = Field(info, 18, 118, 190);

            info.Controls.Add(FieldLabel("Product version", 226, 100));
            _productVersion = Field(info, 226, 118, 190);

            info.Controls.Add(FieldLabel("Copyright", 434, 100));
            _copyright = Field(info, 434, 118, 190);

            _build = new Button();
            _build.Text = "Start Builder";
            _build.Bounds = new Rectangle(196, 508, 660, 46);
            _build.FlatStyle = FlatStyle.Flat;
            _build.FlatAppearance.BorderSize = 0;
            _build.BackColor = Pink;
            _build.ForeColor = Color.White;
            _build.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
            _build.Cursor = Cursors.Hand;
            _build.Click += delegate { StartBuild(); };
            Controls.Add(_build);

            Panel logCard = CardPanel(new Rectangle(196, 568, 660, 92));
            Controls.Add(logCard);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Color.FromArgb(16, 13, 20);
            _log.ForeColor = Color.FromArgb(198, 192, 208);
            _log.Font = FMono;
            _log.Bounds = new Rectangle(14, 12, 632, 68);
            logCard.Controls.Add(_log);
        }

        private Label RailItem(string text, int y)
        {
            Label l = new Label();
            l.Text = "   " + text;
            l.Font = FUi;
            l.ForeColor = Muted;
            l.BackColor = Rail;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Bounds = new Rectangle(16, y, 148, 34);
            l.Cursor = Cursors.Hand;
            return l;
        }

        private Label WindowButton(string glyph)
        {
            Label l = new Label();
            l.Text = glyph;
            l.Font = new Font("Segoe UI", 10f);
            l.ForeColor = Muted;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Cursor = Cursors.Hand;
            l.MouseEnter += delegate { l.ForeColor = Ink; };
            l.MouseLeave += delegate { l.ForeColor = Muted; };
            return l;
        }

        private Panel CardPanel(Rectangle bounds)
        {
            Panel p = new Panel();
            p.Bounds = bounds;
            p.BackColor = Card;
            p.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        private void Section(Panel parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FBold;
            l.ForeColor = Ink;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            parent.Controls.Add(l);
        }

        private Label FieldLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = FSmall;
            l.ForeColor = Muted;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        private TextBox Field(Control parent, int x, int y, int width)
        {
            TextBox box = new TextBox();
            box.Bounds = new Rectangle(x, y, width, 24);
            box.BackColor = CardAlt;
            box.ForeColor = Ink;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = FMono;
            parent.Controls.Add(box);
            return box;
        }

        private Button GhostButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = CardAlt;
            b.ForeColor = Ink;
            b.Font = FSmall;
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { b.BackColor = Color.FromArgb(38, 31, 46); };
            b.MouseLeave += delegate { b.BackColor = CardAlt; };
            return b;
        }

        private Label StripChip(Control parent, string caption, int x, Color color)
        {
            Label l = new Label();
            l.Text = "\u25CF " + caption + "\n-";
            l.Font = FSmall;
            l.ForeColor = color;
            l.Bounds = new Rectangle(x, 14, 200, 38);
            parent.Controls.Add(l);
            return l;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void RoundCorners()
        {
            int radius = 12;
            using (GraphicsPath path = new GraphicsPath())
            {
                Rectangle r = new Rectangle(0, 0, Width, Height);
                int d = radius * 2;
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }
        }

        private void LoadSettings()
        {
            _name.Text = _cfg.LastName;
            _ext.Text = "exe";
            _icon.Text = _cfg.LastIcon;
            _fileVersion.Text = "1.0.0.0";
            _productVersion.Text = "1.0.0.0";
        }

        private void ChooseIcon()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Select icon";
                dlg.Filter = "Icon (*.ico)|*.ico";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                _icon.Text = dlg.FileName;
                _cfg.LastIcon = dlg.FileName;
                _cfg.Save();
                Append("icon set: " + Path.GetFileName(dlg.FileName));
            }
        }

        private void OpenSettings()
        {
            using (SettingsForm dlg = new SettingsForm(_cfg))
                dlg.ShowDialog(this);

            CheckLicense();
        }

        private void CheckLicense()
        {
            Append("panel: " + (string.IsNullOrEmpty(_cfg.PanelUrl) ? "not configured" : _cfg.PanelUrl));
            _deviceLabel.Text = "\u25CF DEVICE\n" + _cfg.Hwid;

            if (string.IsNullOrEmpty(_cfg.PanelUrl))
            {
                _licenseLabel.ForeColor = Muted;
                _webhookLabel.Text = "\u25CF WEBHOOK\n" + (string.IsNullOrEmpty(_cfg.WebhookUrl) ? "not set" : "configured");
                return;
            }

            _licenseLabel.Text = "\u25CF LICENSE\nchecking...";
            Api.CheckLicenseAsync(_cfg.PanelUrl, _cfg.LicenseKey, _cfg.Hwid, delegate(LicenseState state)
            {
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action<LicenseState>(delegate(LicenseState s)
                    {
                        _licenseLabel.ForeColor = s.Active ? Ok : Bad;
                        _licenseLabel.Text = "\u25CF LICENSE\n" + (s.Active ? "Active" : s.Message) +
                                             (s.Active ? " \u00B7 " + s.Plan : "");
                        _webhookLabel.Text = "\u25CF WEBHOOK\n" + (string.IsNullOrEmpty(_cfg.WebhookUrl) ? "not set" : "connected");
                        Append("license: " + s.Message);
                    }), state);
                }
                catch
                {
                }
            });
        }

        private void StartBuild()
        {
            if (_busy) return;

            BuildSpec spec = new BuildSpec();
            spec.Name = _name.Text.Trim();
            spec.Extension = _ext.Text.Trim();
            spec.IconPath = _icon.Text.Trim();
            spec.FileDescription = _description.Text.Trim();
            spec.ProductName = _product.Text.Trim();
            spec.Company = _company.Text.Trim();
            spec.FileVersion = _fileVersion.Text.Trim();
            spec.ProductVersion = _productVersion.Text.Trim();
            spec.Copyright = _copyright.Text.Trim();
            spec.PanelUrl = _cfg.PanelUrl;
            spec.WebhookUrl = _cfg.WebhookUrl;
            spec.Tag = "r" + DateTime.UtcNow.ToString("yyMMddHHmmss");
            spec.DropName = spec.Name + ".txt";

            var errors = spec.Validate();
            if (errors.Count > 0)
            {
                foreach (string e in errors) Append("error: " + e);
                return;
            }

            _busy = true;
            _build.Enabled = false;
            _build.Text = "Working...";
            Append("--- build " + spec.FileName + " ---");

            _cfg.LastName = spec.Name;
            _cfg.Save();

            ThreadPool.QueueUserWorkItem(delegate
            {
                BuildResult result = StubCompiler.Build(spec, _cfg);
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action<BuildResult>(delegate(BuildResult r)
                    {
                        _busy = false;
                        _build.Enabled = true;
                        _build.Text = "Start Builder";

                        foreach (string line in r.Log.Split('\n'))
                        {
                            string trimmed = line.Trim();
                            if (trimmed.Length > 0) Append(trimmed);
                        }

                        if (r.Success)
                        {
                            FileInfo info = new FileInfo(r.OutputPath);
                            Append("ok: " + info.FullName);
                            Api.LogBuildAsync(_cfg.PanelUrl, spec, _cfg.Hwid, r.OutputPath, info.Length);
                            Api.NotifyAsync(_cfg.WebhookUrl, "Rose",
                                "build.created | " + info.Name + " | " + _cfg.Hwid);
                        }
                        else
                        {
                            Append("failed: " + r.Error);
                        }
                    }), result);
                }
                catch
                {
                }
            });
        }

        private void Append(string message)
        {
            if (_log == null) return;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }
    }
}
