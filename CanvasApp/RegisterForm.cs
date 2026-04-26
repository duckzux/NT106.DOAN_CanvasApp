using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CanvasApp
{
    public partial class RegisterForm : Form
    {
        public RegisterForm()
        {
            InitializeComponent();
            txtUsername.label = "Username";
            txtPassword.label = "Password";
            txtPassword.isPassword = true;
            txtEmail.label = "Email Address";
            txtEmail.isPassword = false;
        }

        private void btnReg_Click(object sender, EventArgs e)
        {
            string username = txtUsername.TextValue;
            string password = txtPassword.TextValue;
            string email = txtEmail.TextValue;

            if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(email))
            {
                MessageBox.Show("Vui lòng điền đầy đủ các trường thông tin!", "Thông báo");
                return;
            }

            if (!email.Contains("@") || !email.Contains("."))
            {
                MessageBox.Show("Định dạng Email không hợp lệ!", "Lỗi");
                return;
            }

            LoginForm login = new LoginForm();
            login.Show();
            this.Close();
        }
    }
}
