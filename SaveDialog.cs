using System;
using System.Drawing;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 保存对话框：输入标题（允许留空）。
    /// </summary>
    public class SaveDialog : Form
    {
        private TextBox _txtTitle;

        /// <summary>用户输入的标题（可为空字符串）。</summary>
        public string EntryTitle
        {
            get { return _txtTitle.Text.Trim(); }
        }

        public SaveDialog()
        {
            Text = "保存提示词";
            ClientSize = new Size(440, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Label label = new Label();
            label.Text = "标题（可留空）";
            label.AutoSize = true;
            label.Location = new Point(14, 18);

            _txtTitle = new TextBox();
            _txtTitle.Location = new Point(14, 44);
            _txtTitle.Size = new Size(412, 24);

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
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
