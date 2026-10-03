using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 主窗口：左栏图片上传区 + 右栏输入/输出文本区，七个按钮的事件编排、
    /// 额外指令 / Pony Mode 标签增删、状态栏刷新、窗口几何持久化。
    /// </summary>
    public class MainForm : Form
    {
        /// <summary>按钮统一宽度（七个按钮需在一行内排布，最小窗口下也不换行）。</summary>
        private const int ButtonWidth = 60;

        private TextBox _txtInput;
        private TextBox _txtOutput;

        /// <summary>按「字体大小」配置生成的文本区字体（两个文本框共用，替换时释放旧字体）。</summary>
        private Font _textFont;
        private CheckBox _chkExtra;
        private CheckBox _chkPony;
        private Button _btnConfig;
        private Button _btnGenerate;
        private Button _btnCopy;
        private Button _btnSave;
        private Button _btnView;
        private Button _btnClear;
        private Button _btnAbout;

        private Panel _imagePanel;
        private Panel _uploadBox;
        private PictureBox _picPreview;
        private Label _lblPlus;
        private Label _lblImageHint;

        private StatusStrip _status;
        private ToolStripStatusLabel _lblKey;
        private ToolStripStatusLabel _lblMode;
        private ToolStripStatusLabel _lblBalance;

        private System.Windows.Forms.Timer _geomTimer;
        private bool _geomDirty;
        private bool _restoringGeometry;
        private bool _busy;
        private bool _loadingBalance;
        private bool _suppressTagHandlers;
        private bool _hasImage;
        private string _balanceText;
        private string _balanceDetail;

        /// <summary>当前上传图片的原始路径（无图为空串）。</summary>
        private string _currentImagePath;

        /// <summary>当前上传图片的载荷（BMP 已转码，供生成时组装请求体）。</summary>
        private ImagePayload _imagePayload;

        /// <summary>当前图片的界面预览（与 _picPreview.Image 同源，需成对释放）。</summary>
        private ImagePreview _preview;

        /// <summary>发起生成时的图片路径快照，保存记录时写入 imagePath。</summary>
        private string _lastImagePath;

        /// <summary>发起生成时的 Pony 判定快照，保存记录时写入 isPony。</summary>
        private bool _lastIsPony;

        public MainForm()
        {
            _balanceText = "未配置 Key";
            _balanceDetail = string.Empty;
            _busy = false;
            _loadingBalance = false;
            _suppressTagHandlers = false;
            _hasImage = false;
            _currentImagePath = string.Empty;
            _imagePayload = null;
            _preview = null;
            _lastImagePath = string.Empty;
            _lastIsPony = false;

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
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            // 内容区：左栏固定宽度图片区 + 右栏文本区
            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 2;
            content.RowCount = 1;
            content.Padding = new Padding(10, 8, 10, 6);
            content.Margin = new Padding(0);
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Defaults.ImagePanelWidth));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            content.Controls.Add(BuildImagePanel(), 0, 0);
            content.Controls.Add(BuildTextPanel(), 1, 0);

            BuildStatusStrip();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.Margin = new Padding(0);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(content, 0, 0);
            root.Controls.Add(_status, 0, 1);

            Controls.Add(root);

            Resize += OnFormResizeOrMove;
            Move += OnFormResizeOrMove;

            ApplyTextFont();
        }

        /// <summary>
        /// 应用「字体大小」配置：只作用于「用户输入」「英文提示词」两个文本框，
        /// 其余控件与窗口尺寸保持窗体默认字号（9 磅）。
        /// </summary>
        private void ApplyTextFont()
        {
            Font font = new Font(this.Font.FontFamily,
                Defaults.NormalizeFontSize(Storage.Config.fontSize), FontStyle.Regular, GraphicsUnit.Point);
            Font previous = _textFont;
            _textFont = font;
            // 先把两个文本框切到新字体，再释放旧字体，避免绘制时引用已释放对象
            _txtInput.Font = font;
            _txtOutput.Font = font;
            if (previous != null)
            {
                previous.Dispose();
            }
        }

        /// <summary>左栏：固定 250×200 的图片上传框（窗口缩放不改变其尺寸）+ 下方提示文字。</summary>
        private Control BuildImagePanel()
        {
            _imagePanel = new Panel();
            _imagePanel.Dock = DockStyle.Fill;
            _imagePanel.Margin = new Padding(0, 0, 10, 0);
            _imagePanel.Resize += OnImagePanelResize;

            _uploadBox = new Panel();
            _uploadBox.Size = new Size(Defaults.UploadBoxWidth, Defaults.UploadBoxHeight);
            _uploadBox.BackColor = SystemColors.Window;
            _uploadBox.Cursor = Cursors.Hand;
            _uploadBox.Paint += OnUploadBoxPaint;
            _uploadBox.Click += OnUploadClick;
            AttachDropTarget(_uploadBox);

            _picPreview = new PictureBox();
            _picPreview.Dock = DockStyle.Fill;
            _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
            _picPreview.BackColor = SystemColors.Window;
            _picPreview.Cursor = Cursors.Hand;
            _picPreview.Visible = false;
            _picPreview.Click += OnUploadClick;
            AttachDropTarget(_picPreview);

            // 「＋」用固定尺寸的 Label（不 Dock=Fill，避免遮住父容器的虚线边框）
            _lblPlus = new Label();
            _lblPlus.Text = "＋";
            _lblPlus.Size = new Size(160, 160);
            _lblPlus.TextAlign = ContentAlignment.MiddleCenter;
            _lblPlus.BackColor = SystemColors.Window;
            _lblPlus.ForeColor = SystemColors.ControlDark;
            _lblPlus.Font = new Font("Microsoft YaHei UI", 40F, FontStyle.Regular, GraphicsUnit.Point);
            _lblPlus.Cursor = Cursors.Hand;
            _lblPlus.Click += OnUploadClick;
            AttachDropTarget(_lblPlus);

            _uploadBox.Controls.Add(_picPreview);
            _uploadBox.Controls.Add(_lblPlus);

            _lblImageHint = new Label();
            _lblImageHint.Text = "点击上传图片\r\n或将图片拖动至此";
            _lblImageHint.TextAlign = ContentAlignment.MiddleCenter;
            _lblImageHint.AutoSize = false;
            _lblImageHint.Cursor = Cursors.Hand;
            _lblImageHint.Click += OnUploadClick;
            AttachDropTarget(_lblImageHint);

            _imagePanel.Controls.Add(_uploadBox);
            _imagePanel.Controls.Add(_lblImageHint);
            return _imagePanel;
        }

        /// <summary>右栏：头部（用户输入 + 两个复选框）/ 输入 / 输出标题 / 输出 / 按钮行。</summary>
        private Control BuildTextPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 1;
            panel.RowCount = 5;
            panel.Margin = new Padding(0);
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlowLayoutPanel header = new FlowLayoutPanel();
            header.AutoSize = true;
            header.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            header.WrapContents = false;
            header.Margin = new Padding(0);
            header.Padding = new Padding(0);

            Label lblInput = new Label();
            lblInput.Text = "用户输入";
            lblInput.AutoSize = true;
            lblInput.Margin = new Padding(0, 6, 20, 0);

            _chkExtra = new CheckBox();
            _chkExtra.Text = "额外指令";
            _chkExtra.AutoSize = true;
            _chkExtra.Margin = new Padding(0, 4, 20, 0);
            _chkExtra.CheckedChanged += OnExtraCheckedChanged;

            _chkPony = new CheckBox();
            _chkPony.Text = "Pony Mode";
            _chkPony.AutoSize = true;
            _chkPony.Margin = new Padding(0, 4, 0, 0);
            _chkPony.CheckedChanged += OnPonyCheckedChanged;

            header.Controls.Add(lblInput);
            header.Controls.Add(_chkExtra);
            header.Controls.Add(_chkPony);

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
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);
            buttons.Padding = new Padding(0);

            _btnConfig = CreateButton("配置", OnConfigClick);
            _btnGenerate = CreateButton("生成", OnGenerateClick);
            _btnCopy = CreateButton("复制", OnCopyClick);
            _btnSave = CreateButton("保存", OnSaveClick);
            _btnView = CreateButton("查看", OnViewClick);
            _btnClear = CreateButton("清除", OnClearClick);
            _btnAbout = CreateButton("关于", OnAboutClick);

            buttons.Controls.Add(_btnConfig);
            buttons.Controls.Add(_btnGenerate);
            buttons.Controls.Add(_btnCopy);
            buttons.Controls.Add(_btnSave);
            buttons.Controls.Add(_btnView);
            buttons.Controls.Add(_btnClear);
            buttons.Controls.Add(_btnAbout);

            panel.Controls.Add(header, 0, 0);
            panel.Controls.Add(_txtInput, 0, 1);
            panel.Controls.Add(lblOutput, 0, 2);
            panel.Controls.Add(_txtOutput, 0, 3);
            panel.Controls.Add(buttons, 0, 4);
            return panel;
        }

        private static Button CreateButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = ButtonWidth;
            button.Height = 28;
            button.Margin = new Padding(0, 0, 4, 0);
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

        /// <summary>
        /// 图片区自适应：上传框恒为 250×200 并水平居中，整体在左栏内垂直居中；
        /// 窗口缩放只改变留白，不改变上传框尺寸。
        /// </summary>
        private void LayoutImageArea()
        {
            if (_imagePanel == null || _uploadBox == null || _lblImageHint == null)
            {
                return;
            }

            int boxWidth = Defaults.UploadBoxWidth;
            int boxHeight = Defaults.UploadBoxHeight;
            int gap = 12;
            int hintHeight = _lblImageHint.Font.Height * 2 + 8;

            int total = boxHeight + gap + hintHeight;
            int top = (_imagePanel.ClientSize.Height - total) / 2;
            if (top < 6)
            {
                top = 6;
            }
            int left = (_imagePanel.ClientSize.Width - boxWidth) / 2;
            if (left < 0)
            {
                left = 0;
            }

            _uploadBox.Location = new Point(left, top);
            _uploadBox.Size = new Size(boxWidth, boxHeight);
            _lblImageHint.Location = new Point(left, top + boxHeight + gap);
            _lblImageHint.Size = new Size(boxWidth, hintHeight);

            if (_lblPlus != null)
            {
                _lblPlus.Location = new Point(
                    (boxWidth - _lblPlus.Width) / 2,
                    (boxHeight - _lblPlus.Height) / 2);
            }
        }

        /// <summary>无图时绘制虚线边框；有图时由 PictureBox 覆盖整个上传框。</summary>
        private void OnUploadBoxPaint(object sender, PaintEventArgs e)
        {
            if (_hasImage)
            {
                return;
            }
            Rectangle rect = new Rectangle(0, 0, _uploadBox.Width - 1, _uploadBox.Height - 1);
            ControlPaint.DrawBorder(e.Graphics, rect, Color.Silver, ButtonBorderStyle.Dashed);
        }

        private void AttachDropTarget(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += OnDragEnter;
            control.DragDrop += OnDragDrop;
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
            DetachPreview();
        }

        #endregion

        #region 图片上传

        private void OnImagePanelResize(object sender, EventArgs e)
        {
            LayoutImageArea();
        }

        private void OnUploadClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择图片";
                dialog.Filter = ImageUtil.OpenFileFilter;
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                SetImage(dialog.FileName);
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
            {
                return;
            }
            if (ImageUtil.IsSupportedExtension(files[0]))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (_busy || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
            {
                return;
            }
            // 仅支持单张：多选时取第一个文件
            SetImage(files[0]);
        }

        /// <summary>加载图片（校验失败时提示并保持当前图片不变）。</summary>
        private void SetImage(string path)
        {
            ImagePayload payload;
            string error;
            if (!ImageUtil.LoadForUpload(path, out payload, out error))
            {
                MessageBox.Show(this, error, "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 预览解码失败（例如 GDI+ 不支持的 WebP）不影响上传与生成
            ImagePreview preview;
            string previewError;
            if (!ImageUtil.TryCreatePreview(payload, out preview, out previewError))
            {
                preview = null;
            }

            DetachPreview();

            _imagePayload = payload;
            _currentImagePath = path;
            _preview = preview;
            _hasImage = preview != null;

            if (_preview != null)
            {
                _picPreview.Image = _preview.Image;
                _picPreview.Visible = true;
                _lblPlus.Visible = false;
            }
            else
            {
                _picPreview.Image = null;
                _picPreview.Visible = false;
                _lblPlus.Visible = true;
            }
            _uploadBox.Invalidate();
        }

        /// <summary>清空图片（内存中的载荷、预览与路径）。</summary>
        private void ClearImage()
        {
            DetachPreview();
            _imagePayload = null;
            _currentImagePath = string.Empty;
            _hasImage = false;
            _picPreview.Visible = false;
            _lblPlus.Visible = true;
            _uploadBox.Invalidate();
        }

        /// <summary>释放预览图像；必须先断开 PictureBox 的引用再释放。</summary>
        private void DetachPreview()
        {
            if (_picPreview != null)
            {
                _picPreview.Image = null;
            }
            if (_preview != null)
            {
                _preview.Dispose();
                _preview = null;
            }
        }

        #endregion

        #region 事件处理

        private void OnFormLoad(object sender, EventArgs e)
        {
            LayoutImageArea();
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
                    // 只有落盘成功才应用新字号，避免「保存失败」后界面与配置文件不一致
                    ApplyTextFont();
                    RefreshBalance();
                }
                else
                {
                    UpdateBalanceText();
                }
            }
        }

        #region 额外指令 / Pony Mode 标签

        private void OnExtraCheckedChanged(object sender, EventArgs e)
        {
            if (_suppressTagHandlers)
            {
                return;
            }
            string text = _txtInput.Text;
            if (_chkExtra.Checked)
            {
                ApplyTagText(Defaults.AppendExtraInstruction(text));
            }
            else
            {
                ApplyTagText(Defaults.RemoveExtraInstruction(text));
            }
        }

        private void OnPonyCheckedChanged(object sender, EventArgs e)
        {
            if (_suppressTagHandlers)
            {
                return;
            }
            string text = _txtInput.Text;
            if (_chkPony.Checked)
            {
                ApplyTagText(Defaults.AppendPonyMode(text));
            }
            else
            {
                ApplyTagText(Defaults.RemovePonyMode(text));
            }
        }

        /// <summary>写入标签改动后的文本并把光标移到末尾（抑制复选框事件递归触发）。</summary>
        private void ApplyTagText(string text)
        {
            _suppressTagHandlers = true;
            try
            {
                _txtInput.Text = text == null ? string.Empty : text;
                _txtInput.SelectionStart = _txtInput.TextLength;
                _txtInput.SelectionLength = 0;
            }
            finally
            {
                _suppressTagHandlers = false;
            }
        }

        #endregion

        private void OnGenerateClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            string input = _txtInput.Text.Trim();
            bool hasImage = _imagePayload != null && _imagePayload.Length > 0;
            if (input.Length == 0 && !hasImage)
            {
                MessageBox.Show(this, "请先输入描述或上传图片。", "绘图提示词生成器",
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

            // 本次生成的图片与 Pony 属性在此快照，供「保存」写入记录
            _lastIsPony = Defaults.ContainsPonyMode(_txtInput.Text);
            _lastImagePath = hasImage ? _currentImagePath : string.Empty;
            ImagePayload payload = hasImage ? _imagePayload : null;

            DeepSeekClient client = new DeepSeekClient(cfg.apiKeyPlain, cfg.model, cfg.thinking);

            SetBusy(true);

            Task.Factory.StartNew(delegate
            {
                GenerateResult result = null;
                try
                {
                    result = client.Generate(systemPrompt, input, payload);
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
                _txtOutput.Text = Defaults.ToDisplayNewlines(result.Content);
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
            string text = Defaults.ToUnixNewlines(_txtOutput.Text);
            if (text == null || text.Trim().Length == 0)
            {
                MessageBox.Show(this, "没有可复制的内容，请先生成提示词。", "绘图提示词生成器",
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
            string content = Defaults.ToUnixNewlines(_txtOutput.Text);
            if (content == null || content.Trim().Length == 0)
            {
                MessageBox.Show(this, "没有可保存的内容，请先生成提示词。", "绘图提示词生成器",
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
                    // content 与 title 原样保存，<Pony> 前缀只在列表显示时按 isPony 派生
                    entry.content = content;
                    entry.source = _txtInput.Text.Trim();
                    entry.time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    entry.imagePath = _lastImagePath == null ? string.Empty : _lastImagePath;
                    entry.isPony = _lastIsPony;
                    entry.thumbFile = CreateThumbnail(entry.imagePath);

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

        /// <summary>
        /// 为带图记录生成缩略图（thumbs\&lt;guid&gt;.png）。
        /// 原图被移动/删除或解码失败时返回空串，保存流程照常完成。
        /// </summary>
        private static string CreateThumbnail(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                return string.Empty;
            }
            string fileName = ImageUtil.NewThumbFileName();
            string target = Path.Combine(Storage.ThumbsDir, fileName);
            if (ImageUtil.TryCreateThumbnail(imagePath, target))
            {
                return fileName;
            }
            return string.Empty;
        }

        /// <summary>清除图片、输入、输出与两个复选框的勾选状态（无二次确认）。</summary>
        private void OnClearClick(object sender, EventArgs e)
        {
            ClearImage();

            _suppressTagHandlers = true;
            try
            {
                _chkExtra.Checked = false;
                _chkPony.Checked = false;
            }
            finally
            {
                _suppressTagHandlers = false;
            }

            _txtInput.Clear();
            _txtOutput.Clear();
            _lastImagePath = string.Empty;
            _lastIsPony = false;
            _txtInput.Focus();
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
                    _txtOutput.Text = content == null ? string.Empty : Defaults.ToDisplayNewlines(content);
                    _txtOutput.SelectionStart = 0;
                    _txtOutput.SelectionLength = 0;
                    if (!string.IsNullOrEmpty(source))
                    {
                        _txtInput.Text = source;
                    }
                    // 回填不携带图片，同时按回填文本重算 Pony 属性，
                    // 使随后保存的记录与当前文本一致
                    _lastImagePath = string.Empty;
                    _lastIsPony = Defaults.ContainsPonyMode(_txtInput.Text);
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
            _btnClear.Enabled = !busy;
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

        /// <summary>窗口销毁时释放本窗口创建的文本区字体（必须在 base.Dispose 之后，避免拆除期重绘引用已释放字体）。</summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _textFont != null)
            {
                _textFont.Dispose();
                _textFont = null;
            }
        }
    }
}
