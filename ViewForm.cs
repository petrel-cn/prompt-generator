using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 查看对话框：左侧记录列表；右侧上下分栏显示「中文原文」与「英文提示词」；
    /// 支持复制、删除、双击加载回主窗口。
    /// </summary>
    public class ViewForm : Form
    {
        private ListBox _lstEntries;
        private TextBox _txtSource;
        private TextBox _txtContent;
        private SplitContainer _splitOuter;
        private SplitContainer _split;
        private Button _btnCopy;
        private Button _btnDelete;
        private Button _btnClose;

        private List<SavedEntry> _entries;
        private readonly Action<string, string> _onLoadToMain;

        public ViewForm(Action<string, string> onLoadToMain)
        {
            _onLoadToMain = onLoadToMain;
            _entries = new List<SavedEntry>();
            BuildUi();
            ReloadList();
        }

        private void BuildUi()
        {
            Text = "已保存的提示词";
            ClientSize = new Size(820, 500);
            MinimumSize = new Size(620, 380);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // 左侧标题列表：纵向滚动条由 ListBox 自动显示（IntegralHeight=false），
            // 横向滚动条由 HorizontalExtent 控制（标题过长时出现）
            _lstEntries = new ListBox();
            _lstEntries.Dock = DockStyle.Fill;
            _lstEntries.IntegralHeight = false;
            _lstEntries.HorizontalScrollbar = true;
            _lstEntries.SelectedIndexChanged += OnSelectionChanged;
            _lstEntries.DoubleClick += OnListDoubleClick;

            // 右侧上下分栏：上=中文原文，下=英文提示词
            _split = new SplitContainer();
            _split.Orientation = Orientation.Horizontal;
            _split.Dock = DockStyle.Fill;
            _split.SplitterWidth = 6;
            _split.Panel1MinSize = 60;
            _split.Panel2MinSize = 60;

            _txtSource = CreateReadOnlyBox();
            _split.Panel1.Controls.Add(WrapInGroup("中文原文", _txtSource));
            _split.Panel1.Padding = new Padding(0, 0, 0, 3);

            _txtContent = CreateReadOnlyBox();
            _split.Panel2.Controls.Add(WrapInGroup("英文提示词", _txtContent));
            _split.Panel2.Padding = new Padding(0, 3, 0, 0);

            // 左右分栏：分隔线可拖动，用于调整标题列表宽度
            _splitOuter = new SplitContainer();
            _splitOuter.Orientation = Orientation.Vertical;
            _splitOuter.Location = new Point(12, 12);
            _splitOuter.Size = new Size(796, 432);
            _splitOuter.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _splitOuter.SplitterWidth = 6;
            _splitOuter.Panel1MinSize = 140;
            _splitOuter.Panel2MinSize = 260;
            _splitOuter.Panel1.Padding = new Padding(0, 0, 3, 0);
            _splitOuter.Panel2.Padding = new Padding(3, 0, 0, 0);
            _splitOuter.Panel1.Controls.Add(WrapInGroup("已保存的标题", _lstEntries));
            _splitOuter.Panel2.Controls.Add(_split);

            _btnCopy = new Button();
            _btnCopy.Text = "复制英文";
            _btnCopy.Location = new Point(304, 456);
            _btnCopy.Size = new Size(96, 28);
            _btnCopy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnCopy.Click += OnCopyClick;

            _btnDelete = new Button();
            _btnDelete.Text = "删除";
            _btnDelete.Location = new Point(408, 456);
            _btnDelete.Size = new Size(84, 28);
            _btnDelete.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnDelete.Click += OnDeleteClick;

            _btnClose = new Button();
            _btnClose.Text = "关闭";
            _btnClose.Location = new Point(724, 456);
            _btnClose.Size = new Size(84, 28);
            _btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _btnClose.DialogResult = DialogResult.Cancel;
            _btnClose.Click += OnCloseClick;

            Controls.Add(_splitOuter);
            Controls.Add(_btnCopy);
            Controls.Add(_btnDelete);
            Controls.Add(_btnClose);

            CancelButton = _btnClose;
            Shown += OnFormShown;
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

        private void OnFormShown(object sender, EventArgs e)
        {
            // 左右分栏初始宽度：标题列表 288 px，右栏占其余空间
            try
            {
                int usableOuter = _splitOuter.Width - _splitOuter.SplitterWidth;
                int leftWidth = 288;
                if (leftWidth < _splitOuter.Panel1MinSize)
                {
                    leftWidth = _splitOuter.Panel1MinSize;
                }
                if (leftWidth > usableOuter - _splitOuter.Panel2MinSize)
                {
                    leftWidth = usableOuter - _splitOuter.Panel2MinSize;
                }
                if (leftWidth > 0)
                {
                    _splitOuter.SplitterDistance = leftWidth;
                }
            }
            catch (Exception)
            {
                // 尺寸过小时忽略，保持默认比例
            }

            // 初始分栏比例：中文原文占 35%
            try
            {
                int usable = _split.Height - _split.SplitterWidth;
                int distance = (int)(usable * 0.35);
                if (distance < _split.Panel1MinSize)
                {
                    distance = _split.Panel1MinSize;
                }
                if (distance > usable - _split.Panel2MinSize)
                {
                    distance = usable - _split.Panel2MinSize;
                }
                if (distance > 0)
                {
                    _split.SplitterDistance = distance;
                }
            }
            catch (Exception)
            {
                // 尺寸过小时忽略，保持默认比例
            }
        }

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
                _txtContent.Text = "（saved.json 读取失败：文件内容可能已损坏，原文件已备份为 saved.json.bad）";
                MessageBox.Show(this,
                    "读取 saved.json 失败：文件内容可能已损坏。\r\n为避免覆盖原有记录，请先修复或移除该文件；原文件已备份为 saved.json.bad。",
                    "已保存的提示词", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_entries.Count == 0)
            {
                _txtSource.Text = string.Empty;
                _txtContent.Text = "（暂无已保存的提示词）";
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

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            SavedEntry entry = SelectedEntry();
            if (entry == null)
            {
                _txtSource.Text = string.Empty;
                _txtContent.Text = string.Empty;
                return;
            }

            if (string.IsNullOrEmpty(entry.source))
            {
                _txtSource.Text = "（该记录保存于旧版本，未保存中文原文）";
            }
            else
            {
                _txtSource.Text = entry.source;
            }
            _txtSource.SelectionStart = 0;
            _txtSource.SelectionLength = 0;

            _txtContent.Text = entry.content;
            _txtContent.SelectionStart = 0;
            _txtContent.SelectionLength = 0;
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

            try
            {
                _entries.RemoveAt(index);
                Storage.SaveSaved(_entries);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "删除失败：" + ex.Message, "已保存的提示词",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            ReloadList();
        }

        private void OnCloseClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
