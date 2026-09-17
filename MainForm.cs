using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 主窗口：输入/输出控件、五个按钮的事件编排、状态栏刷新、窗口几何持久化。
    /// </summary>
    public class MainForm : Form
    {
        private TextBox _txtInput;
        private TextBox _txtOutput;
        private Button _btnConfig;
        private Button _btnGenerate;
        private Button _btnCopy;
        private Button _btnSave;
        private Button _btnView;
        private Button _btnAbout;

        private StatusStrip _status;
        private ToolStripStatusLabel _lblKey;
        private ToolStripStatusLabel _lblMode;
        private ToolStripStatusLabel _lblBalance;

        private System.Windows.Forms.Timer _geomTimer;
        private bool _geomDirty;
        private bool _restoringGeometry;
        private bool _busy;
        private bool _loadingBalance;
        private string _balanceText;
        private string _balanceDetail;

        public MainForm()
        {
            _balanceText = "未配置 Key";
            _balanceDetail = string.Empty;
            _busy = false;
            _loadingBalance = false;

            BuildUi();
            ApplyGeometry();

            _geomTimer = new System.Windows.Forms.Timer();
            _geomTimer.Interval = 800;
            _geomTimer.Tick += OnGeometryTimerTick;

            Load += OnFormLoad;
            FormClosing += OnFormClosing;
        }

        #region 界面构建

        private void BuildUi()
        {
            Text = "绘图提示词生成器";
            MinimumSize = new Size(Defaults.WindowMinWidth, Defaults.WindowMinHeight);
            Size = new Size(Defaults.WindowWidth, Defaults.WindowHeight);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 5;
            content.Padding = new Padding(10, 8, 10, 6);
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label lblInput = new Label();
            lblInput.Text = "中文描述";
            lblInput.AutoSize = true;
            lblInput.Margin = new Padding(0, 2, 0, 4);

            _txtInput = new TextBox();
            _txtInput.Multiline = true;
            _txtInput.ScrollBars = ScrollBars.Vertical;
            _txtInput.AcceptsReturn = true;
            _txtInput.WordWrap = true;
            _txtInput.Dock = DockStyle.Fill;
            _txtInput.Margin = new Padding(0, 0, 0, 8);
            _txtInput.KeyDown += OnInputKeyDown;

            Label lblOutput = new Label();
            lblOutput.Text = "英文提示词";
            lblOutput.AutoSize = true;
            lblOutput.Margin = new Padding(0, 2, 0, 4);

            _txtOutput = new TextBox();
            _txtOutput.Multiline = true;
            _txtOutput.ScrollBars = ScrollBars.Vertical;
            _txtOutput.ReadOnly = true;
            _txtOutput.WordWrap = true;
            _txtOutput.Dock = DockStyle.Fill;
            _txtOutput.BackColor = SystemColors.Window;
            _txtOutput.Margin = new Padding(0, 0, 0, 8);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);

            _btnConfig = CreateButton("配置", 84, OnConfigClick);
            _btnGenerate = CreateButton("生成", 84, OnGenerateClick);
            _btnCopy = CreateButton("复制", 84, OnCopyClick);
            _btnSave = CreateButton("保存", 84, OnSaveClick);
            _btnView = CreateButton("查看", 84, OnViewClick);
            _btnAbout = CreateButton("关于", 84, OnAboutClick);

            buttons.Controls.Add(_btnConfig);
            buttons.Controls.Add(_btnGenerate);
            buttons.Controls.Add(_btnCopy);
            buttons.Controls.Add(_btnSave);
            buttons.Controls.Add(_btnView);
            buttons.Controls.Add(_btnAbout);

            content.Controls.Add(lblInput, 0, 0);
            content.Controls.Add(_txtInput, 0, 1);
            content.Controls.Add(lblOutput, 0, 2);
            content.Controls.Add(_txtOutput, 0, 3);
            content.Controls.Add(buttons, 0, 4);

            BuildStatusStrip();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(content, 0, 0);
            root.Controls.Add(_status, 0, 1);

            Controls.Add(root);

            Resize += OnFormResizeOrMove;
            Move += OnFormResizeOrMove;
        }

        private Button CreateButton(string text, int width, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 28;
            button.Margin = new Padding(0, 0, 8, 0);
            button.Click += handler;
            return button;
        }

        private void BuildStatusStrip()
        {
            _lblKey = new ToolStripStatusLabel();
            _lblKey.Text = "密钥：" + Defaults.KeyName;
            _lblKey.ToolTipText = "点击刷新余额";
            _lblKey.Click += OnStatusClick;

            ToolStripStatusLabel sep1 = new ToolStripStatusLabel();
            sep1.Text = "｜";

            _lblMode = new ToolStripStatusLabel();
            _lblMode.Text = "思考：" + Defaults.ThinkingShortName(Defaults.ThinkingDefault);
            _lblMode.ToolTipText = "思考模式（在「配置」中修改）";
            _lblMode.Click += OnStatusClick;

            ToolStripStatusLabel sep2 = new ToolStripStatusLabel();
            sep2.Text = "｜";

            _lblBalance = new ToolStripStatusLabel();
            _lblBalance.Text = "余额：" + _balanceText;
            _lblBalance.ToolTipText = "点击刷新余额";
            _lblBalance.Click += OnStatusClick;

            _status = new StatusStrip();
            _status.SizingGrip = false;
            _status.Dock = DockStyle.Fill;
            _status.Items.Add(_lblKey);
            _status.Items.Add(sep1);
            _status.Items.Add(_lblMode);
            _status.Items.Add(sep2);
            _status.Items.Add(_lblBalance);
            _status.Click += OnStatusClick;
        }

        #endregion

        #region 窗口几何

        /// <summary>启动时恢复窗口几何；越界或未设置时居中。</summary>
        private void ApplyGeometry()
        {
            _restoringGeometry = true;
            try
            {
                WindowConfig w = Storage.Config.window;
                int width = w.width;
                int height = w.height;
                if (width < MinimumSize.Width)
                {
                    width = Defaults.WindowWidth;
                }
                if (height < MinimumSize.Height)
                {
                    height = Defaults.WindowHeight;
                }

                Size = new Size(width, height);

                if (w.HasPosition && IsRectangleVisible(w.x, w.y, width, height))
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(w.x, w.y);
                }
                else
                {
                    StartPosition = FormStartPosition.CenterScreen;
                }
            }
            catch (Exception)
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
            finally
            {
                _restoringGeometry = false;
                _geomDirty = false;
            }
        }

        private static bool IsRectangleVisible(int x, int y, int width, int height)
        {
            Rectangle rect;
            try
            {
                rect = new Rectangle(x, y, width, height);
            }
            catch (Exception)
            {
                return false;
            }
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                Rectangle area = screens[i].WorkingArea;
                Rectangle overlap = Rectangle.Intersect(area, rect);
                // 至少要有 80x40 的区域可见，保证标题栏可抓取
                if (overlap.Width >= 80 && overlap.Height >= 40)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnFormResizeOrMove(object sender, EventArgs e)
        {
            if (_restoringGeometry || !Visible)
            {
                return;
            }
            if (WindowState != FormWindowState.Normal)
            {
                // 最小化或最大化时不记录
                return;
            }
            _geomDirty = true;
            if (_geomTimer != null)
            {
                _geomTimer.Stop();
                _geomTimer.Start();
            }
        }

        private void OnGeometryTimerTick(object sender, EventArgs e)
        {
            _geomTimer.Stop();
            SaveGeometry();
        }

        private void SaveGeometry()
        {
            if (!_geomDirty || WindowState != FormWindowState.Normal)
            {
                return;
            }
            try
            {
                WindowConfig w = Storage.Config.window;
                w.x = Location.X;
                w.y = Location.Y;
                w.width = Width;
                w.height = Height;
                Storage.SaveConfig();
                _geomDirty = false;
            }
            catch (Exception)
            {
                // 几何保存失败不打扰用户
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_geomTimer != null)
            {
                _geomTimer.Stop();
                _geomTimer.Dispose();
                _geomTimer = null;
            }
            SaveGeometry();
        }

        #endregion

        #region 事件处理

        private void OnFormLoad(object sender, EventArgs e)
        {
            RefreshStatusLabels();
            RefreshBalance();

            if (Storage.DecryptFailed || Storage.ConfigLoadFailed)
            {
                MessageBox.Show(this, Storage.DecryptMessage, "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                OpenConfigDialog();
            }
        }

        private void OnStatusClick(object sender, EventArgs e)
        {
            RefreshStatusLabels();
            RefreshBalance();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Enter 快捷生成
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                OnGenerateClick(sender, EventArgs.Empty);
            }
        }

        private void OnConfigClick(object sender, EventArgs e)
        {
            OpenConfigDialog();
        }

        private void OpenConfigDialog()
        {
            using (ConfigForm form = new ConfigForm())
            {
                DialogResult result = form.ShowDialog(this);
                RefreshStatusLabels();
                if (result == DialogResult.OK)
                {
                    RefreshBalance();
                }
                else
                {
                    UpdateBalanceText();
                }
            }
        }

        private void OnGenerateClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            string input = _txtInput.Text.Trim();
            if (input.Length == 0)
            {
                MessageBox.Show(this, "请先输入中文描述。", "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtInput.Focus();
                return;
            }

            AppConfig cfg = Storage.Config;
            if (string.IsNullOrEmpty(cfg.apiKeyPlain))
            {
                DialogResult choice = MessageBox.Show(this,
                    "尚未配置 API Key，是否现在打开配置？", "绘图提示词生成器",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes)
                {
                    OpenConfigDialog();
                }
                return;
            }

            string systemPrompt;
            try
            {
                // 每次生成前重新读取 prompt.txt，改完即生效
                systemPrompt = Storage.ReadPrompt();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (systemPrompt.Trim().Length == 0)
            {
                DialogResult emptyChoice = MessageBox.Show(this,
                    "系统提示词为空，生成效果可能不可用。是否仍要继续？\r\n（可在「配置」中填写，或直接编辑 prompt.txt）",
                    "绘图提示词生成器", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (emptyChoice != DialogResult.Yes)
                {
                    return;
                }
            }

            DeepSeekClient client = new DeepSeekClient(cfg.apiKeyPlain, cfg.model, cfg.thinking);

            SetBusy(true);

            Task.Factory.StartNew(delegate
            {
                GenerateResult result = null;
                try
                {
                    result = client.Generate(systemPrompt, input);
                }
                catch (Exception ex)
                {
                    result = new GenerateResult();
                    result.Error = "生成失败：" + ex.Message;
                }
                SafeInvoke(delegate
                {
                    SetBusy(false);
                    HandleGenerateResult(result);
                });
            });
        }

        private void HandleGenerateResult(GenerateResult result)
        {
            if (result.Success)
            {
                _txtOutput.Text = result.Content;
                _txtOutput.SelectionStart = 0;
                _txtOutput.SelectionLength = 0;
                RefreshBalance();
                return;
            }

            string message = Storage.Sanitize(result.Error);
            MessageBox.Show(this, message, "生成失败", MessageBoxButtons.OK, MessageBoxIcon.Error);

            if (result.Unauthorized)
            {
                DialogResult choice = MessageBox.Show(this, "是否打开配置检查 API Key？", "绘图提示词生成器",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (choice == DialogResult.Yes)
                {
                    OpenConfigDialog();
                }
            }
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            string text = _txtOutput.Text;
            if (text == null || text.Trim().Length == 0)
            {
                MessageBox.Show(this, "没有可复制的内容，请先生成英文提示词。", "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (TrySetClipboard(text))
            {
                _balanceDetail = "已复制到剪贴板（" + text.Length + " 字符）";
                _lblBalance.ToolTipText = _balanceDetail;
            }
            else
            {
                MessageBox.Show(this, "复制失败：剪贴板被其他程序占用，请稍后重试。", "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool TrySetClipboard(string text)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (Exception)
                {
                    Thread.Sleep(80);
                }
            }
            return false;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            string content = _txtOutput.Text;
            if (content == null || content.Trim().Length == 0)
            {
                MessageBox.Show(this, "没有可保存的内容，请先生成英文提示词。", "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveDialog dialog = new SaveDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    bool loadFailed;
                    List<SavedEntry> entries = Storage.LoadSaved(out loadFailed);
                    if (loadFailed)
                    {
                        MessageBox.Show(this,
                            "读取 saved.json 失败：文件内容可能已损坏。\r\n为避免覆盖原有记录，本次保存已中止；原文件已备份为 saved.json.bad，请检查后再试。",
                            "绘图提示词生成器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    SavedEntry entry = new SavedEntry();
                    entry.title = dialog.EntryTitle == null ? string.Empty : dialog.EntryTitle;
                    entry.content = content;
                    entry.source = _txtInput.Text.Trim();
                    entry.time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    entries.Add(entry);
                    Storage.SaveSaved(entries);

                    MessageBox.Show(this, "已保存。", "绘图提示词生成器",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败：" + ex.Message, "绘图提示词生成器",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnAboutClick(object sender, EventArgs e)
        {
            string text =
                "绘图提示词生成器 v" + Defaults.AppVersion + "\r\n" +
                Defaults.CopyrightLine + "\r\n" +
                "MIT License（详见随附的 LICENSE 文件：" + Defaults.LicenseUrl + "）\r\n\r\n" +
                "本软件按“原样”提供，不附带任何明示或暗示的担保。";
            MessageBox.Show(this, text, "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnViewClick(object sender, EventArgs e)
        {
            try
            {
                using (ViewForm form = new ViewForm(delegate(string content, string source)
                {
                    _txtOutput.Text = content == null ? string.Empty : content;
                    _txtOutput.SelectionStart = 0;
                    _txtOutput.SelectionLength = 0;
                    if (!string.IsNullOrEmpty(source))
                    {
                        _txtInput.Text = source;
                    }
                    _txtOutput.Focus();
                }))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开查看窗口失败：" + ex.Message, "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region 状态栏

        private void RefreshStatusLabels()
        {
            AppConfig cfg = Storage.Config;
            _lblKey.Text = "密钥：" + cfg.keyName;
            _lblMode.Text = "思考：" + Defaults.ThinkingShortName(cfg.thinking);
            UpdateBalanceText();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _btnConfig.Enabled = !busy;
            _btnGenerate.Enabled = !busy;
            _btnCopy.Enabled = !busy;
            _btnSave.Enabled = !busy;
            _btnView.Enabled = !busy;
            _btnAbout.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnGenerate.Text = busy ? "生成中…" : "生成";
            UpdateBalanceText();
        }

        private void UpdateBalanceText()
        {
            if (_busy)
            {
                _lblBalance.Text = "生成中…";
                return;
            }
            if (_loadingBalance)
            {
                _lblBalance.Text = "余额：获取中…";
                return;
            }
            _lblBalance.Text = "余额：" + _balanceText;
            _lblBalance.ToolTipText = string.IsNullOrEmpty(_balanceDetail) ? "点击刷新余额" : _balanceDetail;
        }

        /// <summary>刷新余额：未配置 Key 时直接提示，否则后台查询。</summary>
        private void RefreshBalance()
        {
            if (_busy || _loadingBalance || IsDisposed)
            {
                return;
            }

            string key = Storage.Config.apiKeyPlain;
            if (string.IsNullOrEmpty(key))
            {
                _balanceText = "未配置 Key";
                _balanceDetail = "请在「配置」中填写 API Key";
                UpdateBalanceText();
                return;
            }

            _loadingBalance = true;
            UpdateBalanceText();

            Task.Factory.StartNew(delegate
            {
                BalanceResult result = null;
                try
                {
                    result = DeepSeekClient.QueryBalance(key);
                }
                catch (Exception ex)
                {
                    result = new BalanceResult();
                    result.Error = "余额查询失败：" + ex.Message;
                }
                SafeInvoke(delegate
                {
                    _loadingBalance = false;
                    if (result.Success)
                    {
                        _balanceText = result.Text;
                        _balanceDetail = "余额查询于 " + DateTime.Now.ToString("HH:mm:ss");
                    }
                    else
                    {
                        _balanceText = "获取失败";
                        _balanceDetail = Storage.Sanitize(result.Error);
                    }
                    UpdateBalanceText();
                });
            });
        }

        /// <summary>回主线程执行；窗口已销毁时静默忽略。</summary>
        private void SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke(action);
            }
            catch (Exception)
            {
                // 窗口关闭过程中忽略
            }
        }

        #endregion
    }
}
