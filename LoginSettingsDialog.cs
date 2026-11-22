using System.ComponentModel;

namespace VideoWall115;

internal sealed class LoginSettingsDialog : Form
{
    private readonly TextBox _account = new() { Dock = DockStyle.Fill, MaxLength = 128 };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, MaxLength = 256, UseSystemPasswordChar = true };
    private readonly CheckBox _consent = new()
    {
        Text = "自动登录时，勾选 115 官方页面的「我已阅读并同意」",
        AutoSize = true, Dock = DockStyle.Fill,
    };

    internal bool Changed { get; private set; }

    public LoginSettingsDialog(WindowsLoginStore store, string? savedAccount)
    {
        Text = "115 自动登录";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(490, 255);
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9);
        _account.Text = savedAccount ?? "";

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(16),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 50, 33, 33, 32, 31, 43 })
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        var intro = new Label
        {
            Dock = DockStyle.Fill,
            Text = "账号和密码保存在本机 Windows 凭据管理器中。115 要求验证码、滑块或其他验证时，请在网页版手动完成。",
        };
        form.Controls.Add(intro, 0, 0);
        form.SetColumnSpan(intro, 2);
        AddField("账号", _account, 1);
        AddField("密码", _password, 2);
        form.Controls.Add(_consent, 0, 3);
        form.SetColumnSpan(_consent, 2);
        form.Controls.Add(new Label
        {
            Text = "请先在 115 登录页阅读用户服务协议和隐私政策。", Dock = DockStyle.Fill,
        }, 0, 4);
        form.SetColumnSpan(form.GetControlFromPosition(0, 4)!, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "保存并启用", AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var forget = new Button { Text = "忘记账号和密码", AutoSize = true, Enabled = savedAccount != null };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(forget);
        form.Controls.Add(buttons, 0, 5);
        form.SetColumnSpan(buttons, 2);
        Controls.Add(form);
        AcceptButton = save;
        CancelButton = cancel;

        save.Click += (_, _) =>
        {
            if (!_consent.Checked)
            {
                MessageBox.Show(this, "请确认已阅读协议，并同意自动勾选。", Text);
                return;
            }
            try
            {
                store.Save(new LoginDetails(_account.Text.Trim(), _password.Text));
                Changed = true;
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException error)
            {
                MessageBox.Show(this, error.Message, Text);
            }
            catch (Win32Exception)
            {
                MessageBox.Show(this, "Windows 未能保存 115 凭据。", Text);
            }
        };
        forget.Click += (_, _) =>
        {
            try
            {
                store.Forget();
                Changed = true;
                DialogResult = DialogResult.OK;
            }
            catch (Win32Exception)
            {
                MessageBox.Show(this, "Windows 未能删除 115 凭据。", Text);
            }
        };
        FormClosed += (_, _) => _password.Clear();

        void AddField(string caption, Control input, int row)
        {
            form.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 5, 4, 0) }, 0, row);
            form.Controls.Add(input, 1, row);
        }
    }
}
