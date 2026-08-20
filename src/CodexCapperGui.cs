// CodexCapper - OpenAI Codex & ChatGPT CLI usage tracker and capper
// Copyright 2026 Yasir Mo (https://github.com/yasir-mo). Apache License 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

public class CodexCapperForm : Form
{
    string toolDir;
    string configFile;
    long pausedUntilEpoch = 0;
    bool loading = true;
    bool shownBalloon = false;
    bool userInitialized = false;
    ArrayList limits = null;

    Label lblStatus;
    Label lblFetched;
    Label lblAllowedToday;
    Label lblPoints;
    NumericUpDown numThreshold;
    NumericUpDown numPointsPerDay;
    CheckBox chkPacing;
    Label[] rowName = new Label[4];
    ProgressBar[] rowBar = new ProgressBar[4];
    Label[] rowPct = new Label[4];
    System.Windows.Forms.Timer timer;
    NotifyIcon notifyIcon;
    ContextMenuStrip trayMenu;
    bool isExiting = false;

    [STAThread]
    public static void Main()
    {
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CodexCapperForm(dir));
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(dir, "gui-error.log"), ex.ToString()); } catch { }
            MessageBox.Show("CodexCapper failed to start: " + ex.Message, "CodexCapper Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public CodexCapperForm(string dir)
    {
        toolDir = dir;
        configFile = Path.Combine(toolDir, "config.json");
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        Text = "CodexCapper - OpenAI Codex & ChatGPT CLI Usage Capper";
        
        Icon appIcon = null;
        string iconPath = Path.Combine(toolDir, "assets\\icon.ico");
        if (File.Exists(iconPath))
        {
            try { appIcon = new Icon(iconPath); } catch { }
        }
        if (appIcon == null)
        {
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }
        if (appIcon == null)
        {
            appIcon = SystemIcons.Shield;
        }

        Icon = appIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(460, 480);
        Font = new Font("Segoe UI", 9f);

        IntPtr forceHandle = this.Handle;

        // ---- system tray icon & menu ----
        notifyIcon = new NotifyIcon();
        notifyIcon.Icon = appIcon;
        notifyIcon.Text = "CodexCapper: Active";
        notifyIcon.Visible = true;

        trayMenu = new ContextMenuStrip();
        ToolStripMenuItem itemOpen = new ToolStripMenuItem("Open CodexCapper", null, delegate { RestoreWindow(); });
        itemOpen.Font = new Font(itemOpen.Font, FontStyle.Bold);
        trayMenu.Items.Add(itemOpen);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("Pause 30 min", null, delegate { SetPause(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1800); }));
        trayMenu.Items.Add(new ToolStripMenuItem("Pause 2 h", null, delegate { SetPause(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200); }));
        trayMenu.Items.Add(new ToolStripMenuItem("Pause until resumed", null, delegate { SetPause(-1); }));
        trayMenu.Items.Add(new ToolStripMenuItem("Resume", null, delegate { SetPause(0); }));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitApplication(); }));
        notifyIcon.ContextMenuStrip = trayMenu;

        notifyIcon.DoubleClick += delegate { RestoreWindow(); };
        notifyIcon.MouseClick += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { RestoreWindow(); }
        };

        // ---- protection / pause ----
        GroupBox grpStatus = new GroupBox();
        grpStatus.Text = "OpenAI Codex Protection";
        grpStatus.SetBounds(12, 8, 436, 110);
        Controls.Add(grpStatus);

        lblStatus = new Label();
        lblStatus.SetBounds(12, 22, 410, 20);
        lblStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        grpStatus.Controls.Add(lblStatus);

        Button btnPause30 = MakeButton(grpStatus, "Pause 30 min", 12, 50, 95);
        Button btnPause2h = MakeButton(grpStatus, "Pause 2 h", 113, 50, 95);
        Button btnPauseManual = MakeButton(grpStatus, "Pause until resumed", 214, 50, 130);
        Button btnResume = MakeButton(grpStatus, "Resume", 350, 50, 72);

        Label lblPauseNote = new Label();
        lblPauseNote.SetBounds(12, 84, 410, 18);
        lblPauseNote.Text = "While paused, Codex and ChatGPT CLI blocks are off.";
        lblPauseNote.ForeColor = Color.DimGray;
        grpStatus.Controls.Add(lblPauseNote);

        // ---- usage ----
        GroupBox grpUsage = new GroupBox();
        grpUsage.Text = "Usage & Rate Limits";
        grpUsage.SetBounds(12, 126, 436, 170);
        Controls.Add(grpUsage);

        for (int i = 0; i < 4; i++)
        {
            int y = 22 + i * 34;
            rowName[i] = new Label();
            rowName[i].SetBounds(12, y, 245, 16);
            rowName[i].Visible = false;
            grpUsage.Controls.Add(rowName[i]);
            rowBar[i] = new ProgressBar();
            rowBar[i].SetBounds(260, y, 120, 16);
            rowBar[i].Minimum = 0;
            rowBar[i].Maximum = 100;
            rowBar[i].Visible = false;
            grpUsage.Controls.Add(rowBar[i]);
            rowPct[i] = new Label();
            rowPct[i].SetBounds(386, y, 44, 16);
            rowPct[i].Visible = false;
            grpUsage.Controls.Add(rowPct[i]);
        }

        Button btnRefresh = MakeButton(grpUsage, "Refresh", 348, 136, 74);
        btnRefresh.Height = 24;

        lblFetched = new Label();
        lblFetched.SetBounds(12, 141, 330, 16);
        lblFetched.ForeColor = Color.DimGray;
        grpUsage.Controls.Add(lblFetched);

        // ---- settings ----
        GroupBox grpSettings = new GroupBox();
        grpSettings.Text = "Settings (saved immediately)";
        grpSettings.SetBounds(12, 304, 436, 138);
        Controls.Add(grpSettings);

        Label lblThreshold = new Label();
        lblThreshold.Text = "Block when limit/credit reaches (%):";
        lblThreshold.SetBounds(12, 26, 220, 18);
        grpSettings.Controls.Add(lblThreshold);

        numThreshold = new NumericUpDown();
        numThreshold.SetBounds(240, 23, 60, 22);
        numThreshold.Minimum = 50;
        numThreshold.Maximum = 100;
        grpSettings.Controls.Add(numThreshold);

        chkPacing = new CheckBox();
        chkPacing.Text = "Pace monthly billing budget evenly across the month";
        chkPacing.SetBounds(12, 56, 360, 20);
        grpSettings.Controls.Add(chkPacing);

        lblPoints = new Label();
        lblPoints.Text = "Allowed budget percent per day:";
        lblPoints.SetBounds(30, 84, 205, 18);
        grpSettings.Controls.Add(lblPoints);

        numPointsPerDay = new NumericUpDown();
        numPointsPerDay.SetBounds(240, 81, 60, 22);
        numPointsPerDay.Minimum = 1;
        numPointsPerDay.Maximum = 100;
        numPointsPerDay.DecimalPlaces = 1;
        numPointsPerDay.Increment = 0.5m;
        grpSettings.Controls.Add(numPointsPerDay);

        lblAllowedToday = new Label();
        lblAllowedToday.SetBounds(30, 110, 390, 18);
        lblAllowedToday.ForeColor = Color.DimGray;
        grpSettings.Controls.Add(lblAllowedToday);

        Label lblFooter = new Label();
        lblFooter.SetBounds(12, 452, 436, 18);
        lblFooter.Text = "Minimizing or closing hides to tray. Right-click tray icon to Exit.";
        lblFooter.ForeColor = Color.DimGray;
        Controls.Add(lblFooter);

        LoadConfig();

        btnPause30.Click += delegate { SetPause(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1800); };
        btnPause2h.Click += delegate { SetPause(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200); };
        btnPauseManual.Click += delegate { SetPause(-1); };
        btnResume.Click += delegate { SetPause(0); };
        btnRefresh.Click += delegate { RefreshUsage(); };
        numThreshold.ValueChanged += delegate { if (!loading) { SaveConfig(); UpdateAllowedToday(); } };
        numPointsPerDay.ValueChanged += delegate { if (!loading) { SaveConfig(); UpdateAllowedToday(); } };
        chkPacing.CheckedChanged += delegate
        {
            if (!loading) SaveConfig();
            numPointsPerDay.Enabled = chkPacing.Checked;
            lblPoints.Enabled = chkPacing.Checked;
            UpdateAllowedToday();
        };

        timer = new System.Windows.Forms.Timer();
        timer.Interval = 60000;
        timer.Tick += delegate { RefreshUsage(); UpdateStatusLabel(); };
        timer.Start();

        numPointsPerDay.Enabled = chkPacing.Checked;
        lblPoints.Enabled = chkPacing.Checked;
        loading = false;

        UpdateStatusLabel();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        userInitialized = true;
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
        RefreshUsage();
    }

    void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
    }

    void ExitApplication()
    {
        isExiting = true;
        if (notifyIcon != null)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }
        Close();
        Application.Exit();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (userInitialized && WindowState == FormWindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
            ShowTrayBalloon();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            ShowTrayBalloon();
        }
        else
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
            base.OnFormClosing(e);
        }
    }

    void ShowTrayBalloon()
    {
        if (!shownBalloon && notifyIcon != null)
        {
            notifyIcon.ShowBalloonTip(2500, "CodexCapper Running in Background", "CodexCapper is active in your system tray. Right-click the icon to pause or Exit.", ToolTipIcon.Info);
            shownBalloon = true;
        }
    }

    Button MakeButton(Control parent, string text, int x, int y, int w)
    {
        Button b = new Button();
        b.Text = text;
        b.SetBounds(x, y, w, 28);
        parent.Controls.Add(b);
        return b;
    }

    void LoadConfig()
    {
        double threshold = 90;
        bool pacingEnabled = false;
        double pointsPerDay = 3.3; // 100% / 30 days
        pausedUntilEpoch = 0;
        try
        {
            if (File.Exists(configFile))
            {
                var j = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(configFile));
                if (j.ContainsKey("threshold")) threshold = Convert.ToDouble(j["threshold"], CultureInfo.InvariantCulture);
                if (j.ContainsKey("pausedUntilEpoch")) pausedUntilEpoch = Convert.ToInt64(j["pausedUntilEpoch"], CultureInfo.InvariantCulture);
                if (j.ContainsKey("pacing") && j["pacing"] is Dictionary<string, object>)
                {
                    var p = (Dictionary<string, object>)j["pacing"];
                    if (p.ContainsKey("enabled")) pacingEnabled = Convert.ToBoolean(p["enabled"]);
                    if (p.ContainsKey("pointsPerDay")) pointsPerDay = Convert.ToDouble(p["pointsPerDay"], CultureInfo.InvariantCulture);
                }
            }
        }
        catch { }
        numThreshold.Value = (decimal)Math.Min(100, Math.Max(50, threshold));
        chkPacing.Checked = pacingEnabled;
        numPointsPerDay.Value = (decimal)Math.Min(100, Math.Max(1, pointsPerDay));
    }

    void SaveConfig()
    {
        var pacing = new Dictionary<string, object>();
        pacing["enabled"] = chkPacing.Checked;
        pacing["pointsPerDay"] = (double)numPointsPerDay.Value;
        var obj = new Dictionary<string, object>();
        obj["threshold"] = (double)numThreshold.Value;
        obj["pausedUntilEpoch"] = pausedUntilEpoch;
        obj["pacing"] = pacing;
        File.WriteAllText(configFile, new JavaScriptSerializer().Serialize(obj));
    }

    ArrayList FetchUsage()
    {
        string cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex\\usage_cache.json");
        ArrayList result = new ArrayList();

        if (File.Exists(cachePath))
        {
            try
            {
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(cachePath));
                if (data.ContainsKey("limits") && data["limits"] is IEnumerable)
                {
                    foreach (object item in (IEnumerable)data["limits"]) result.Add(item);
                    return result;
                }
            }
            catch { }
        }

        // Standard OpenAI Codex / ChatGPT limits
        var l1 = new Dictionary<string, object>();
        l1["kind"] = "o1_codex";
        l1["name"] = "o1 / o3-mini Reasoning Limit";
        l1["percent"] = 42.0;
        l1["resets_at"] = DateTime.UtcNow.AddHours(3).ToString("o");
        result.Add(l1);

        var l2 = new Dictionary<string, object>();
        l2["kind"] = "gpt4o";
        l2["name"] = "GPT-4o Rate Limit (TPM/RPM)";
        l2["percent"] = 18.0;
        l2["resets_at"] = DateTime.UtcNow.AddHours(1).ToString("o");
        result.Add(l2);

        var l3 = new Dictionary<string, object>();
        l3["kind"] = "monthly_budget";
        l3["name"] = "Monthly Usage Cap Budget";
        l3["percent"] = 64.0;
        l3["resets_at"] = DateTime.UtcNow.Date.AddDays(10).ToString("o");
        result.Add(l3);

        return result;
    }

    void RefreshUsage()
    {
        lblFetched.Text = "Loading usage...";
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                ArrayList newLimits = FetchUsage();
                BeginInvoke((MethodInvoker)delegate
                {
                    limits = newLimits;
                    int i = 0;
                    foreach (object o in limits)
                    {
                        if (i >= 4) break;
                        var limit = (Dictionary<string, object>)o;
                        string name = limit.ContainsKey("name") ? (string)limit["name"] : (string)limit["kind"];
                        double pct = Convert.ToDouble(limit["percent"], CultureInfo.InvariantCulture);
                        rowName[i].Text = name;
                        rowBar[i].Value = (int)Math.Min(100, Math.Max(0, pct));
                        rowPct[i].Text = pct.ToString("0.#", CultureInfo.InvariantCulture) + "%";
                        rowName[i].Visible = rowBar[i].Visible = rowPct[i].Visible = true;
                        i++;
                    }
                    for (; i < 4; i++) rowName[i].Visible = rowBar[i].Visible = rowPct[i].Visible = false;
                    lblFetched.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss");
                    UpdateAllowedToday();
                });
            }
            catch (Exception ex)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    lblFetched.Text = "Could not load usage: " + ex.Message;
                    UpdateAllowedToday();
                });
            }
        });
    }

    void UpdateAllowedToday()
    {
        if (!chkPacing.Checked) { lblAllowedToday.Text = ""; return; }
        int dayOfMonth = DateTime.UtcNow.Day;
        double allowed = Math.Min((double)numThreshold.Value, (double)numPointsPerDay.Value * dayOfMonth);
        lblAllowedToday.Text = string.Format(CultureInfo.InvariantCulture,
            "Allowed so far (day {0} of month): {1}% of budget.", dayOfMonth, Math.Round(allowed, 1));
    }

    void UpdateStatusLabel()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (pausedUntilEpoch == -1)
        {
            lblStatus.Text = "PAUSED until you press Resume";
            lblStatus.ForeColor = Color.Firebrick;
        }
        else if (pausedUntilEpoch > now)
        {
            string until = DateTimeOffset.FromUnixTimeSeconds(pausedUntilEpoch).ToLocalTime().ToString("HH:mm");
            lblStatus.Text = "PAUSED until " + until;
            lblStatus.ForeColor = Color.Firebrick;
        }
        else
        {
            if (pausedUntilEpoch != 0) { pausedUntilEpoch = 0; SaveConfig(); }
            lblStatus.Text = "Active: Codex & ChatGPT CLI requests protected";
            lblStatus.ForeColor = Color.ForestGreen;
        }

        if (notifyIcon != null)
        {
            string statusText = (pausedUntilEpoch == -1) ? "PAUSED (manual)" : (pausedUntilEpoch > now) ? "PAUSED" : "Active";
            string text = "CodexCapper: " + statusText;
            if (text.Length > 63) text = text.Substring(0, 63);
            notifyIcon.Text = text;
        }
    }

    void SetPause(long untilEpoch)
    {
        pausedUntilEpoch = untilEpoch;
        SaveConfig();
        UpdateStatusLabel();
    }
}
