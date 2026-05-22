using System;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class LoginForm : Form
    {
        // Re-entrance guard. btnLogin.Enabled=false during the async login already prevents
        // most double-clicks, but rapid sequential clicks can still queue both events in
        // WinForms' message pump before the first handler runs Enable=false. This flag is
        // the belt-and-braces safety net so we never open two LobbyForms.
        private bool _loginInProgress;

        public LoginForm()
        {
            InitializeComponent();
            txtUsername.label = "Tài khoản";
            txtPassword.label = "Mật khẩu";
            txtPassword.isPassword = true;
        }

        private void Form1_Load(object sender, EventArgs e) { }
        private void label2_Click(object sender, EventArgs e) { }
        private void label3_Click(object sender, EventArgs e) { }
        private void label4_Click(object sender, EventArgs e) { }
        private void loginTextbox2_Load(object sender, EventArgs e) { }

        private void lbReg_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var regForm = new RegisterForm();
            regForm.Show();
            this.Hide();
        }

        private void lbForgot_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var dlg = new ForgotPasswordForm())
            {
                dlg.ShowDialog(this);
            }
        }

        private async void btnLogin_Click_1(object sender, EventArgs e)
        {
            if (_loginInProgress) return;

            string username = txtUsername.TextValue;
            string password = txtPassword.TextValue;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ thông tin!", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _loginInProgress = true;
            // Disable UI khi đang login
            btnLogin.Enabled = false;
            btnLogin.Text = "Đang đăng nhập...";

            try
            {
                var result = await AuthClient.LoginAsync(username, password);

                if (!result.Success)
                {
                    MessageBox.Show(result.Message ?? "Đăng nhập thất bại", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Lưu session
                Session.CurrentUser = result.User;
                Session.Token = result.Token;

                // ✅ KHÔNG connect Canvas Server ở đây
                // Connection sẽ được thực hiện khi user bấm Join phòng

                var lobby = new LobbyForm();
                lobby.Show();
                this.Hide();
            }
            finally
            {
                // Restore UI even if the login or LobbyForm construction threw, so the user
                // isn't left with a permanently-disabled "Đang đăng nhập..." button.
                _loginInProgress = false;
                if (!this.IsDisposed)
                {
                    btnLogin.Enabled = true;
                    btnLogin.Text = "Đăng nhập";
                }
            }
        }
    }
}
