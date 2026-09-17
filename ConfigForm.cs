using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 配置对话框：密钥名称、API Key、模型、思考模式、系统提示词。
    /// </summary>
    public class ConfigForm : Form
    {
        private TextBox _txtKeyName;
        private TextBox _txtApiKey;
        private CheckBox _chkShowKey;
        private TextBox _txtModel;
        private ComboBox _cboThinking;
        private TextBox _txtPrompt;
        private Label _lblHint;

        private Button _btnNotepad;
        private Button _btnRestore;
        private Button _btnTest;
        private Button _btnSave;
        private Button _btnCancel;

        private bool _testing;
        private bool _awaitNotepadReload;
        private string _promptTextSnapshot;

        /// <summary>思考模式下拉项。</summary>
        private class ThinkingOption
        {
            public string Value;
            public string Text;

            public ThinkingOption(string value, string text)
            {
                Value = value;
                Text = text;
            }

            public override string ToString()
            {
                return Text;
            }
        }

        public ConfigForm()
        {
            _promptTextSnapshot = string.Empty;
            BuildUi();
            LoadValues();
            Activated += OnFormActivated;
        }

        private void BuildUi()
        {
            Text = "配置";
            ClientSize = new Size(640, 620);
            MinimumSize = new Size(560, 480);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Label lblKeyName = MakeLabel("密钥名称", 12, 15);
            _txtKeyName = new TextBox();
            _txtKeyName.Location = new Point(96, 12);
            _txtKeyName.Size = new Size(300, 24);
            _txtKeyName.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            Label lblApiKey = MakeLabel("API Key", 12, 49);
            _txtApiKey = new TextBox();
            _txtApiKey.Location = new Point(96, 46);
            _txtApiKey.Size = new Size(380, 24);
            _txtApiKey.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtApiKey.UseSystemPasswordChar = true;

            _chkShowKey = new CheckBox();
            _chkShowKey.Text = "显示";
            _chkShowKey.Location = new Point(486, 47);
            _chkShowKey.Size = new Size(70, 22);
            _chkShowKey.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _chkShowKey.CheckedChanged += OnShowKeyChanged;

            Label lblModel = MakeLabel("模型", 12, 83);
            _txtModel = new TextBox();
            _txtModel.Location = new Point(96, 80);
            _txtModel.Size = new Size(300, 24);
            _txtModel.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            Label lblThinking = MakeLabel("思考模式", 12, 117);
            _cboThinking = new ComboBox();
            _cboThinking.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboThinking.Location = new Point(96, 114);
            _cboThinking.Size = new Size(300, 24);
            _cboThinking.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _cboThinking.Items.Add(new ThinkingOption(Defaults.ThinkingDisabled, "关闭（响应最快，费用最低）"));
            _cboThinking.Items.Add(new ThinkingOption(Defaults.ThinkingLow, "低（low）"));
            _cboThinking.Items.Add(new ThinkingOption(Defaults.ThinkingHigh, "高（high）"));
            _cboThinking.Items.Add(new ThinkingOption(Defaults.ThinkingMax, "最高（max）"));

            Label lblPrompt = MakeLabel("系统提示词", 12, 151);
            lblPrompt.AutoSize = true;

            _lblHint = new Label();
            _lblHint.Text = "思考模式开启时采样参数不生效；提示词内容由用户自定义，程序不做过滤。";
            _lblHint.ForeColor = SystemColors.GrayText;
            _lblHint.AutoSize = true;
            _lblHint.Location = new Point(96, 153);
            _lblHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _txtPrompt = new TextBox();
            _txtPrompt.Multiline = true;
            _txtPrompt.ScrollBars = ScrollBars.Vertical;
            _txtPrompt.WordWrap = true;
            _txtPrompt.AcceptsReturn = true;
            _txtPrompt.Location = new Point(12, 176);
            _txtPrompt.Size = new Size(616, 388);
            _txtPrompt.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _btnNotepad = MakeButton("用记事本打开", 12, 576, 120);
            _btnNotepad.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnNotepad.Click += OnNotepadClick;

            _btnRestore = MakeButton("恢复默认", 140, 576, 90);
            _btnRestore.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnRestore.Click += OnRestoreClick;

            _btnTest = MakeButton("测试连接", 238, 576, 90);
            _btnTest.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnTest.Click += OnTestClick;

            _btnSave = MakeButton("保存", 460, 576, 80);
            _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnSave.Click += OnSaveClick;

            _btnCancel = MakeButton("取消", 548, 576, 80);
            _btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.Click += OnCancelClick;

            Controls.Add(lblKeyName);
            Controls.Add(_txtKeyName);
            Controls.Add(lblApiKey);
            Controls.Add(_txtApiKey);
            Controls.Add(_chkShowKey);
            Controls.Add(lblModel);
            Controls.Add(_txtModel);
            Controls.Add(lblThinking);
            Controls.Add(_cboThinking);
            Controls.Add(lblPrompt);
            Controls.Add(_lblHint);
            Controls.Add(_txtPrompt);
            Controls.Add(_btnNotepad);
            Controls.Add(_btnRestore);
            Controls.Add(_btnTest);
            Controls.Add(_btnSave);
            Controls.Add(_btnCancel);

            AcceptButton = _btnSave;
            CancelButton = _btnCancel;
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y + 3);
            return label;
        }

        private static Button MakeButton(string text, int x, int y, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 28);
            return button;
        }

        private void LoadValues()
        {
            AppConfig cfg = Storage.Config;
            _txtKeyName.Text = cfg.keyName;
            _txtApiKey.Text = cfg.apiKeyPlain;
            _txtModel.Text = cfg.model;

            string thinking = Defaults.NormalizeThinking(cfg.thinking);
            for (int i = 0; i < _cboThinking.Items.Count; i++)
            {
                ThinkingOption option = _cboThinking.Items[i] as ThinkingOption;
                if (option != null && option.Value == thinking)
                {
                    _cboThinking.SelectedIndex = i;
                    break;
                }
            }
            if (_cboThinking.SelectedIndex < 0)
            {
                _cboThinking.SelectedIndex = 0;
            }

            try
            {
                _txtPrompt.Text = Storage.ReadPrompt();
            }
            catch (Exception ex)
            {
                _txtPrompt.Text = Defaults.DefaultSystemPrompt;
                MessageBox.Show(this, ex.Message + "\r\n已载入内置默认提示词。", "配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _chkShowKey.Checked = false;
            _txtApiKey.UseSystemPasswordChar = true;
            _promptTextSnapshot = _txtPrompt.Text;
        }

        private string SelectedThinking()
        {
            ThinkingOption option = _cboThinking.SelectedItem as ThinkingOption;
            if (option == null)
            {
                return Defaults.ThinkingDefault;
            }
            return option.Value;
        }

        private void OnShowKeyChanged(object sender, EventArgs e)
        {
            _txtApiKey.UseSystemPasswordChar = !_chkShowKey.Checked;
        }

        private void OnNotepadClick(object sender, EventArgs e)
        {
            try
            {
                // 先把当前内容落盘，再用记事本编辑
                Storage.WritePrompt(_txtPrompt.Text);
                _promptTextSnapshot = _txtPrompt.Text;

                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "notepad.exe";
                info.Arguments = "\"" + Storage.PromptPath + "\"";
                info.UseShellExecute = false;
                Process process = Process.Start(info);
                if (process == null)
                {
                    MessageBox.Show(this, "无法启动记事本。", "配置",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 记事本可能把请求交给已有实例后立即退出，因此除等待进程外，
                // 还在本窗口重新激活时再回读一次磁盘内容
                _awaitNotepadReload = true;

                Cursor = Cursors.WaitCursor;
                process.WaitForExit();
                process.Dispose();
                Cursor = Cursors.Default;

                ReloadPromptFromDisk();
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, "调用记事本失败：" + ex.Message, "配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 窗口重新激活时，若刚用过记事本且磁盘内容已变，则回读 prompt.txt。
        /// 用内容比较而非时间戳，避免“记事本把请求交给已有实例后立即退出”导致回读旧内容，
        /// 也避免时间戳粒度/时钟问题造成的漏读。
        /// </summary>
        private void OnFormActivated(object sender, EventArgs e)
        {
            if (!_awaitNotepadReload)
            {
                return;
            }

            string onDisk;
            try
            {
                if (!File.Exists(Storage.PromptPath))
                {
                    return;
                }
                onDisk = Storage.ReadPrompt();
            }
            catch (Exception)
            {
                return;
            }

            // 记事本没有改动：结束等待，避免以后误覆盖用户在文本框里的编辑
            if (onDisk == _promptTextSnapshot)
            {
                _awaitNotepadReload = false;
                return;
            }

            _awaitNotepadReload = false;

            // 用户在文本框另有编辑时不静默覆盖
            if (_txtPrompt.Text != _promptTextSnapshot)
            {
                DialogResult choice = MessageBox.Show(this,
                    "prompt.txt 已在外部被修改，是否用文件内容覆盖当前编辑？", "配置",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (choice != DialogResult.Yes)
                {
                    _promptTextSnapshot = onDisk;
                    return;
                }
            }

            _txtPrompt.Text = onDisk;
            _promptTextSnapshot = onDisk;
        }

        private void ReloadPromptFromDisk()
        {
            try
            {
                string text = Storage.ReadPrompt();
                _txtPrompt.Text = text;
                _promptTextSnapshot = text;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "重新读取系统提示词失败：" + ex.Message, "配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnRestoreClick(object sender, EventArgs e)
        {
            DialogResult choice = MessageBox.Show(this,
                "把系统提示词重置为内置默认内容？（需点「保存」后才写入文件）", "配置",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes)
            {
                _txtPrompt.Text = Defaults.DefaultSystemPrompt;
            }        }

        private void OnTestClick(object sender, EventArgs e)
        {
            if (_testing)
            {
                return;
            }
            string key = _txtApiKey.Text.Trim();
            if (key.Length == 0)
            {
                MessageBox.Show(this, "请先填写 API Key。", "测试连接",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _testing = true;
            _btnTest.Enabled = false;
            _btnTest.Text = "测试中…";
            Cursor = Cursors.WaitCursor;

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
                    result.Error = "测试失败：" + ex.Message;
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _testing = false;
                        _btnTest.Enabled = true;
                        _btnTest.Text = "测试连接";
                        Cursor = Cursors.Default;

                        if (result.Success)
                        {
                            MessageBox.Show(this, "连接成功，当前余额：" + result.Text, "测试连接",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(this, Storage.Sanitize(result.Error), "测试连接失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }));
                }
                catch (Exception)
                {
                    // 对话框已关闭
                }
            });
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            AppConfig cfg = Storage.Config;
            cfg.keyName = _txtKeyName.Text.Trim();
            if (cfg.keyName.Length == 0)
            {
                cfg.keyName = Defaults.KeyName;
            }
            cfg.model = _txtModel.Text.Trim();
            if (cfg.model.Length == 0)
            {
                cfg.model = Defaults.Model;
            }
            cfg.thinking = SelectedThinking();

            try
            {
                Storage.WritePrompt(_txtPrompt.Text);
                // SetApiKey 内部会写回 config.json（含密钥密文）
                Storage.SetApiKey(_txtApiKey.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存配置失败：" + ex.Message, "配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
