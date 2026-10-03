using System;
using System.Drawing;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 标题输入对话框：保存提示词与修改已保存标题共用同一套外观，
    /// 区别只在窗口标题、说明文字与是否预填原标题。
    /// </summary>
    public class SaveDialog : Form
    {
        private TextBox _txtTitle;

        /// <summary>用户输入的标题（可为空字符串）。</summary>
        public string EntryTitle
        {
            get { return _txtTitle.Text.Trim(); }
        }

        /// <summary>保存提示词时使用：空标题、无附加提示。</summary>
        public SaveDialog()
            : this("保存提示词", "标题（可留空）", string.Empty, string.Empty)
        {
        }

        /// <summary>
        /// 自定义文案的标题输入对话框。
        /// </summary>
        /// <param name="caption">窗口标题。</param>
        /// <param name="labelText">输入框上方的说明文字。</param>
        /// <param name="hintText">输入框下方的灰色提示；空字符串表示不显示。</param>
        /// <param name="initialTitle">预填入输入框的标题。</param>
        public SaveDialog(string caption, string labelText, string hintText, string initialTitle)
        {
            Text = caption;
            ClientSize = new Size(440, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Label label = new Label();
            label.Text = labelText == null ? string.Empty : labelText;
            label.AutoSize = true;
            label.Location = new Point(14, 18);

            _txtTitle = new TextBox();
            _txtTitle.Location = new Point(14, 44);
            _txtTitle.Size = new Size(412, 24);
            _txtTitle.Text = initialTitle == null ? string.Empty : initialTitle;

            Label hint = new Label();
            hint.Text = hintText == null ? string.Empty : hintText;
            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Location = new Point(14, 72);
            hint.Visible = hint.Text.Length > 0;

            Button ok = new Button();
            ok.Text = "确定";
            ok.Location = new Point(264, 96);
            ok.Size = new Size(78, 28);
            ok.DialogResult = DialogResult.OK;
            ok.Click += OnOkClick;

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Location = new Point(350, 96);
            cancel.Size = new Size(76, 28);
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(label);
            Controls.Add(_txtTitle);
            Controls.Add(hint);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>
        /// 对话框显示后把光标停在文本末尾：构造阶段句柄尚未创建，这里设置的选择可能被
        /// 控件获得焦点时的默认行为（全选）覆盖，因此必须在 Shown 之后重设一次。
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _txtTitle.SelectionStart = _txtTitle.TextLength;
            _txtTitle.SelectionLength = 0;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
