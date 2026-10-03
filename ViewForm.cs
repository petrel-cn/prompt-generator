using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 查看对话框：左栏为记录列表 + 缩略图，右栏上下分栏显示「用户输入」（含原始图片路径）
    /// 与「英文提示词」；支持复制、删除、双击回填。
    /// 几何与分栏位置在关闭时写入 config.json 的 viewWindow。
    /// </summary>
    public class ViewForm : Form
    {
        private ListBox _lstEntries;
        private TextBox _txtSource;
        private TextBox _txtImagePath;
        private TextBox _txtContent;
        private PictureBox _picThumb;
        private Label _lblNoThumb;
        private Panel _thumbHost;
        private SplitContainer _splitOuter;
        private SplitContainer _split;
        private Button _btnCopy;
        private Button _btnRename;
        private Button _btnDelete;
        private Button _btnClose;

        /// <summary>按「字体大小」配置生成的文本区字体（仅用于「用户输入」「英文提示词」两个框）。</summary>
        private Font _textFont;

        private List<SavedEntry> _entries;
        private ImagePreview _thumbPreview;
        private readonly Action<string, string> _onLoadToMain;

        public ViewForm(Action<string, string> onLoadToMain)
        {
            _onLoadToMain = onLoadToMain;
            _entries = new List<SavedEntry>();
            _thumbPreview = null;
            BuildUi();
            ApplyGeometry();
            ReloadList();
        }

        private void BuildUi()
        {
            Text = "已保存的提示词";
            MinimumSize = new Size(Defaults.ViewWindowMinWidth, Defaults.ViewWindowMinHeight);
            Size = new Size(Defaults.ViewWindowWidth, Defaults.ViewWindowHeight);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 左栏上半：标题列表（纵向滚动条由 ListBox 自动显示；横向滚动条由 HorizontalExtent 控制）
            _lstEntries = new ListBox();
            _lstEntries.Dock = DockStyle.Fill;
            _lstEntries.IntegralHeight = false;
            _lstEntries.HorizontalScrollbar = true;
            _lstEntries.SelectedIndexChanged += OnSelectionChanged;
            _lstEntries.DoubleClick += OnListDoubleClick;

            // 左栏下半：缩略图显示区（PictureBox 恒为 250×200，四周留白随窗口变化）
            _picThumb = new PictureBox();
            _picThumb.Size = new Size(Defaults.ThumbMaxWidth, Defaults.ThumbMaxHeight);
            _picThumb.SizeMode = PictureBoxSizeMode.Zoom;
            _picThumb.Visible = false;

            _lblNoThumb = new Label();
            _lblNoThumb.Text = "（无缩略图）";
            _lblNoThumb.Dock = DockStyle.Fill;
            _lblNoThumb.TextAlign = ContentAlignment.MiddleCenter;
            _lblNoThumb.ForeColor = SystemColors.GrayText;

            _thumbHost = new Panel();
            _thumbHost.Dock = DockStyle.Fill;
            _thumbHost.Resize += OnThumbHostResize;
            _thumbHost.Controls.Add(_picThumb);
            _thumbHost.Controls.Add(_lblNoThumb);

            // 缩略图区只保留边框，不加标题，避免标题占高导致 250×200 的缩略图被裁切
            GroupBox thumbGroup = new GroupBox();
            thumbGroup.Text = string.Empty;
            thumbGroup.Dock = DockStyle.Fill;
            thumbGroup.Padding = new Padding(6, 2, 6, 6);
            thumbGroup.Controls.Add(_thumbHost);

            TableLayoutPanel leftPanel = new TableLayoutPanel();
            leftPanel.Dock = DockStyle.Fill;
            leftPanel.ColumnCount = 1;
            leftPanel.RowCount = 2;
            leftPanel.Margin = new Padding(0);
            leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, Defaults.ViewThumbAreaHeight));
            leftPanel.Controls.Add(WrapInGroup("已保存的标题", _lstEntries), 0, 0);
            leftPanel.Controls.Add(thumbGroup, 0, 1);

            // 右栏：上下分栏
            _split = new SplitContainer();
            _split.Orientation = Orientation.Horizontal;
            // 先给足初始尺寸再设面板最小值：SplitContainer 在自身尺寸不足以容纳
            // Panel1MinSize + Panel2MinSize + SplitterWidth 时，设置面板最小值会抛
            // InvalidOperationException（内部 SplitterDistance 越界）
            _split.Size = new Size(Defaults.ViewWindowWidth, Defaults.ViewWindowHeight);
            _split.SplitterWidth = 6;
            _split.Panel1MinSize = 60;
            _split.Panel2MinSize = 60;
            _split.Dock = DockStyle.Fill;

            _txtSource = CreateReadOnlyBox();
            _txtImagePath = new TextBox();
            _txtImagePath.ReadOnly = true;
            _txtImagePath.Dock = DockStyle.Fill;
            _txtImagePath.Margin = new Padding(0, 4, 0, 0);
            _txtImagePath.BackColor = SystemColors.Window;

            TableLayoutPanel sourcePanel = new TableLayoutPanel();
            sourcePanel.Dock = DockStyle.Fill;
            sourcePanel.ColumnCount = 1;
            sourcePanel.RowCount = 2;
            sourcePanel.Margin = new Padding(0);
            sourcePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            sourcePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            sourcePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sourcePanel.Controls.Add(_txtSource, 0, 0);
            sourcePanel.Controls.Add(_txtImagePath, 0, 1);

            _split.Panel1.Controls.Add(WrapInGroup("用户输入", sourcePanel));
            _split.Panel1.Padding = new Padding(0, 0, 0, 3);

            _txtContent = CreateReadOnlyBox();

            // 「字体大小」只作用于右侧两个文本框，其余控件（含图片路径框）保持窗体默认字号
            _textFont = new Font(this.Font.FontFamily,
                Defaults.NormalizeFontSize(Storage.Config.fontSize), FontStyle.Regular, GraphicsUnit.Point);
            _txtSource.Font = _textFont;
            _txtContent.Font = _textFont;

            _split.Panel2.Controls.Add(WrapInGroup("英文提示词", _txtContent));
            _split.Panel2.Padding = new Padding(0, 3, 0, 0);

            // 左右分栏：分隔线可拖动
            _splitOuter = new SplitContainer();
            _splitOuter.Orientation = Orientation.Vertical;
            // 同上：先给足初始尺寸再设面板最小值
            _splitOuter.Size = new Size(Defaults.ViewWindowWidth, Defaults.ViewWindowHeight);
            _splitOuter.SplitterWidth = 6;
            _splitOuter.Panel1MinSize = 140;
            _splitOuter.Panel2MinSize = 260;
            _splitOuter.Dock = DockStyle.Fill;
            _splitOuter.Panel1.Padding = new Padding(0, 0, 3, 0);
            _splitOuter.Panel2.Padding = new Padding(3, 0, 0, 0);
            _splitOuter.Panel1.Controls.Add(leftPanel);
            _splitOuter.Panel2.Controls.Add(_split);

            _btnCopy = new Button();
            _btnCopy.Text = "复制英文";
            _btnCopy.Location = new Point(0, 4);
            _btnCopy.Size = new Size(96, 28);
            _btnCopy.Click += OnCopyClick;

            _btnRename = new Button();
            _btnRename.Text = "修改标题";
            _btnRename.Location = new Point(104, 4);
            _btnRename.Size = new Size(84, 28);
            _btnRename.Click += OnRenameClick;

            _btnDelete = new Button();
            _btnDelete.Text = "删除";
            _btnDelete.Location = new Point(196, 4);
            _btnDelete.Size = new Size(84, 28);
            _btnDelete.Click += OnDeleteClick;

            _btnClose = new Button();
            _btnClose.Text = "关闭";
            _btnClose.Location = new Point(0, 4);
            _btnClose.Size = new Size(84, 28);
            _btnClose.DialogResult = DialogResult.Cancel;
            _btnClose.Click += OnCloseClick;

            Panel buttonsPanel = new Panel();
            buttonsPanel.Dock = DockStyle.Fill;
            buttonsPanel.Margin = new Padding(0);
            buttonsPanel.Controls.Add(_btnCopy);
            buttonsPanel.Controls.Add(_btnRename);
            buttonsPanel.Controls.Add(_btnDelete);
            buttonsPanel.Controls.Add(_btnClose);
            buttonsPanel.Resize += OnButtonsPanelResize;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.Margin = new Padding(0);
            root.Padding = new Padding(12, 12, 12, 12);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.Controls.Add(_splitOuter, 0, 0);
            root.Controls.Add(buttonsPanel, 0, 1);

            Controls.Add(root);

            CancelButton = _btnClose;
            Shown += OnFormShown;
            FormClosing += OnFormClosing;
        }

        private static TextBox CreateReadOnlyBox()
        {
            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Both;
            box.WordWrap = true;
            box.BackColor = SystemColors.Window;
            box.Dock = DockStyle.Fill;
            return box;
        }

        private static GroupBox WrapInGroup(string caption, Control content)
        {
            GroupBox group = new GroupBox();
            group.Text = caption;
            group.Dock = DockStyle.Fill;
            group.Padding = new Padding(6, 4, 6, 6);
            content.Dock = DockStyle.Fill;
            group.Controls.Add(content);
            return group;
        }

        #region 窗口几何与分栏

        /// <summary>恢复窗口几何；坐标越界或未设置时居中于父窗口。</summary>
        private void ApplyGeometry()
        {
            try
            {
                ViewConfig v = Storage.Config.viewWindow;
                int width = v.width;
                int height = v.height;
                if (width < MinimumSize.Width)
                {
                    width = Defaults.ViewWindowWidth;
                }
                if (height < MinimumSize.Height)
                {
                    height = Defaults.ViewWindowHeight;
                }

                Size = new Size(width, height);

                if (v.HasPosition && IsRectangleVisible(v.x, v.y, width, height))
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(v.x, v.y);
                }
                else
                {
                    StartPosition = FormStartPosition.CenterParent;
                }
            }
            catch (Exception)
            {
                StartPosition = FormStartPosition.CenterParent;
            }
        }

        /// <summary>与主窗口同款可见性校验：至少 80×40 落在某个显示器工作区内。</summary>
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
                Rectangle overlap = Rectangle.Intersect(screens[i].WorkingArea, rect);
                if (overlap.Width >= 80 && overlap.Height >= 40)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            // 分栏位置必须在布局完成后设置，并按面板最小尺寸钳制
            try
            {
                ViewConfig v = Storage.Config.viewWindow;

                int usableOuter = _splitOuter.Width - _splitOuter.SplitterWidth;
                int leftWidth = v.listWidth;
                if (leftWidth < _splitOuter.Panel1MinSize)
                {
                    leftWidth = _splitOuter.Panel1MinSize;
                }
                int maxLeft = usableOuter - _splitOuter.Panel2MinSize;
                if (leftWidth > maxLeft)
                {
                    leftWidth = maxLeft;
                }
                if (leftWidth > 0 && maxLeft > 0)
                {
                    _splitOuter.SplitterDistance = leftWidth;
                }
            }
            catch (Exception)
            {
                // 尺寸过小时忽略，保持默认比例
            }

            try
            {
                ViewConfig v = Storage.Config.viewWindow;

                int usable = _split.Height - _split.SplitterWidth;
                int sourceHeight = v.sourceHeight;
                if (sourceHeight < _split.Panel1MinSize)
                {
                    sourceHeight = _split.Panel1MinSize;
                }
                int maxSource = usable - _split.Panel2MinSize;
                if (sourceHeight > maxSource)
                {
                    sourceHeight = maxSource;
                }
                if (sourceHeight > 0 && maxSource > 0)
                {
                    _split.SplitterDistance = sourceHeight;
                }
            }
            catch (Exception)
            {
                // 尺寸过小时忽略，保持默认比例
            }
        }

        private void OnButtonsPanelResize(object sender, EventArgs e)
        {
            Panel panel = sender as Panel;
            if (panel == null || _btnClose == null)
            {
                return;
            }
            int left = panel.ClientSize.Width - _btnClose.Width;
            if (left < 0)
            {
                left = 0;
            }
            _btnClose.Location = new Point(left, 4);
        }

        /// <summary>关闭时直接写入 viewWindow（窗口生命周期短，无需去抖）。</summary>
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // 最小化/最大化状态下的像素值不代表用户想要的布局（与主窗口同一口径），
                // 此时保留上次正常状态下的配置
                if (WindowState == FormWindowState.Normal)
                {
                    ViewConfig v = Storage.Config.viewWindow;
                    v.x = Location.X;
                    v.y = Location.Y;
                    v.width = Width;
                    v.height = Height;
                    v.listWidth = _splitOuter.SplitterDistance;
                    v.sourceHeight = _split.SplitterDistance;
                    Storage.SaveConfig();
                }
            }
            catch (Exception)
            {
                // 几何保存失败不打扰用户
            }

            ClearThumbnail();
        }

        #endregion

        #region 列表与缩略图

        private void ReloadList()
        {
            int keep = _lstEntries.SelectedIndex;

            bool loadFailed;
            _entries = Storage.LoadSaved(out loadFailed);

            _lstEntries.BeginUpdate();
            _lstEntries.Items.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                _lstEntries.Items.Add(_entries[i].Display);
            }
            _lstEntries.EndUpdate();
            UpdateListHorizontalExtent();

            if (loadFailed)
            {
                _txtSource.Text = string.Empty;
                _txtImagePath.Text = string.Empty;
                _txtContent.Text = "（saved.json 读取失败：文件内容可能已损坏，原文件已备份为 saved.json.bad）";
                ClearThumbnail();
                MessageBox.Show(this,
                    "读取 saved.json 失败：文件内容可能已损坏。\r\n为避免覆盖原有记录，请先修复或移除该文件；原文件已备份为 saved.json.bad。",
                    "已保存的提示词", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_entries.Count == 0)
            {
                _txtSource.Text = string.Empty;
                _txtImagePath.Text = string.Empty;
                _txtContent.Text = "（暂无已保存的提示词）";
                ClearThumbnail();
                return;
            }

            if (keep < 0 || keep >= _entries.Count)
            {
                keep = 0;
            }
            _lstEntries.SelectedIndex = keep;
        }

        /// <summary>
        /// 按最长标题重算列表的横向滚动范围，标题超出可见宽度时 ListBox 会显示横向滚动条。
        /// </summary>
        private void UpdateListHorizontalExtent()
        {
            int maxWidth = 0;
            for (int i = 0; i < _lstEntries.Items.Count; i++)
            {
                object item = _lstEntries.Items[i];
                string text = item == null ? string.Empty : item.ToString();
                if (text.Length == 0)
                {
                    continue;
                }
                Size size = TextRenderer.MeasureText(text, _lstEntries.Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (size.Width > maxWidth)
                {
                    maxWidth = size.Width;
                }
            }
            _lstEntries.HorizontalExtent = maxWidth + 16;
        }

        private SavedEntry SelectedEntry()
        {
            int index = _lstEntries.SelectedIndex;
            if (index < 0 || index >= _entries.Count)
            {
                return null;
            }
            return _entries[index];
        }

        private void OnThumbHostResize(object sender, EventArgs e)
        {
            if (_thumbHost == null || _picThumb == null)
            {
                return;
            }
            int left = (_thumbHost.ClientSize.Width - _picThumb.Width) / 2;
            int top = (_thumbHost.ClientSize.Height - _picThumb.Height) / 2;
            if (left < 0)
            {
                left = 0;
            }
            if (top < 0)
            {
                top = 0;
            }
            _picThumb.Location = new Point(left, top);
        }

        /// <summary>缩略图文件路径（thumbFile 只应是文件名，这里再净化一次）。</summary>
        private static string ThumbPath(SavedEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.thumbFile))
            {
                return null;
            }
            string name = Path.GetFileName(entry.thumbFile);
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            return Path.Combine(Storage.ThumbsDir, name);
        }

        private void ShowThumbnail(SavedEntry entry)
        {
            ClearThumbnail();

            string path = ThumbPath(entry);
            if (path == null || !File.Exists(path))
            {
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0)
                {
                    return;
                }
                ImagePreview preview;
                string error;
                if (!ImageUtil.TryCreatePreview(new ImagePayload(bytes, "image/png"), out preview, out error))
                {
                    return;
                }
                _thumbPreview = preview;
                _picThumb.Image = _thumbPreview.Image;
                _picThumb.Visible = true;
                _lblNoThumb.Visible = false;
            }
            catch (Exception)
            {
                // 缩略图损坏时显示占位，不影响其它信息
            }
        }

        private void ClearThumbnail()
        {
            if (_picThumb != null)
            {
                _picThumb.Image = null;
                _picThumb.Visible = false;
            }
            if (_lblNoThumb != null)
            {
                _lblNoThumb.Visible = true;
            }
            if (_thumbPreview != null)
            {
                _thumbPreview.Dispose();
                _thumbPreview = null;
            }
        }

        #endregion

        #region 交互

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            SavedEntry entry = SelectedEntry();
            if (entry == null)
            {
                _txtSource.Text = string.Empty;
                _txtImagePath.Text = string.Empty;
                _txtContent.Text = string.Empty;
                ClearThumbnail();
                return;
            }

            if (string.IsNullOrEmpty(entry.source))
            {
                _txtSource.Text = "（该记录保存于旧版本，未保存用户输入）";
            }
            else
            {
                _txtSource.Text = Defaults.ToDisplayNewlines(entry.source);
            }
            _txtSource.SelectionStart = 0;
            _txtSource.SelectionLength = 0;

            _txtImagePath.Text = string.IsNullOrEmpty(entry.imagePath)
                ? "（该记录未关联图片）"
                : entry.imagePath;
            _txtImagePath.SelectionStart = 0;
            _txtImagePath.SelectionLength = 0;

            _txtContent.Text = Defaults.ToDisplayNewlines(entry.content);
            _txtContent.SelectionStart = 0;
            _txtContent.SelectionLength = 0;

            ShowThumbnail(entry);
        }

        private void OnListDoubleClick(object sender, EventArgs e)
        {
            SavedEntry entry = SelectedEntry();
            if (entry == null)
            {
                return;
            }
            if (_onLoadToMain != null)
            {
                // 不回填图片：原始图可能已不存在
                _onLoadToMain(entry.content, entry.source);
            }
            Close();
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            SavedEntry entry = SelectedEntry();
            if (entry == null)
            {
                MessageBox.Show(this, "请先在左侧选择一条记录。", "已保存的提示词",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (entry.content == null || entry.content.Trim().Length == 0)
            {
                MessageBox.Show(this, "该记录内容为空。", "已保存的提示词",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (TrySetClipboard(entry.content))
            {
                MessageBox.Show(this, "已复制到剪贴板。", "已保存的提示词",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, "复制失败：剪贴板被其他程序占用，请稍后重试。", "已保存的提示词",
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

        private void OnRenameClick(object sender, EventArgs e)
        {
            SavedEntry entry = SelectedEntry();
            if (entry == null)
            {
                MessageBox.Show(this, "请先在左侧选择一条记录。", "已保存的提示词",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 标题只存用户自己输入的部分：<Pony> 前缀与保存时间在列表显示时派生，
            // 因此这里能改到（且只能改到）用户输入的那段文字
            using (SaveDialog dialog = new SaveDialog("修改标题", "标题（可留空）",
                "列表显示时自动附加的 <Pony> 前缀与保存时间不在此处修改。", entry.EditableTitle))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string oldTitle = entry.title;
                entry.title = dialog.EntryTitle == null ? string.Empty : dialog.EntryTitle;
                try
                {
                    Storage.SaveSaved(_entries);
                }
                catch (Exception ex)
                {
                    entry.title = oldTitle;
                    MessageBox.Show(this, "修改标题失败：" + ex.Message, "已保存的提示词",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                ReloadList();
            }
        }

        private void OnDeleteClick(object sender, EventArgs e)
        {
            int index = _lstEntries.SelectedIndex;
            SavedEntry entry = SelectedEntry();
            if (entry == null)
            {
                MessageBox.Show(this, "请先在左侧选择一条记录。", "已保存的提示词",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = string.IsNullOrEmpty(entry.title) ? "无标题" : entry.title;
            DialogResult choice = MessageBox.Show(this, "确定删除「" + name + "」？", "已保存的提示词",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes)
            {
                return;
            }

            string thumbFile = entry.thumbFile;

            // 先算新列表、写盘成功才提交（TryRemoveEntryAt 内部保证顺序）：
            // 若先改内存再写盘，写盘失败时 ListBox 行号与 _entries 下标会错位一格，
            // 此后「复制英文」「修改标题」「删除」都会作用到相邻记录
            List<SavedEntry> remaining;
            string error;
            if (!Storage.TryRemoveEntryAt(_entries, index, Storage.SaveSaved, out remaining, out error))
            {
                MessageBox.Show(this, "删除失败：" + error, "已保存的提示词",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _entries = remaining;

            // 记录已移除，缩略图 best-effort 删除（失败仅残留孤儿文件）
            ImageUtil.TryDeleteThumb(thumbFile);
            ReloadList();
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
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
