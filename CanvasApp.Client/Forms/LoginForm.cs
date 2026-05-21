using System;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class LoginForm : Form
    {
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
            string username = txtUsername.TextValue;
            string password = txtPassword.TextValue;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Vui lòng nhập đầy đủ thông tin!", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Disable UI khi đang login
            btnLogin.Enabled = false;
            btnLogin.Text = "Đang đăng nhập...";

            var result = await AuthClient.LoginAsync(username, password);

            btnLogin.Enabled = true;
            btnLogin.Text = "Đăng nhập";

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
    }
}
