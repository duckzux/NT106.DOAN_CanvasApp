using System;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class RegisterForm : Form
    {
        public RegisterForm()
        {
            InitializeComponent();
            txtUsername.label = "Tài khoản";
            txtPassword.label = "Mật khẩu";
            txtPassword.isPassword = true;
            txtEmail.label = "Email";
            txtEmail.isPassword = false;
        }

        private async void btnReg_Click(object sender, EventArgs e)
        {
            string username = txtUsername.TextValue;
            string password = txtPassword.TextValue;
            string email = txtEmail.TextValue;

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(email))
            {
                MessageBox.Show("Vui lòng điền đầy đủ thông tin!", "Thông báo");
                return;
            }

            if (!email.Contains("@") || !email.Contains("."))
            {
                MessageBox.Show("Email không hợp lệ!", "Lỗi");
                return;
            }

            if (password.Length < 4)
            {
                MessageBox.Show("Mật khẩu phải >= 4 ký tự!", "Lỗi");
                return;
            }

            btnReg.Enabled = false;
            btnReg.Text = "Đang đăng ký...";

            var result = await AuthClient.RegisterAsync(username, password, email);

            btnReg.Enabled = true;
            btnReg.Text = "Đăng ký";

            if (!result.Success)
            {
                MessageBox.Show(result.Message, "Đăng ký thất bại",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("Đăng ký thành công! Vui lòng đăng nhập.",
                "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);

            var login = new LoginForm();
            login.Show();
            this.Close();
        }
    }
}
