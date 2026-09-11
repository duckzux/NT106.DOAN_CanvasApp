using System;
using System.Windows.Forms;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Forgot-password flow in one modal: user enters email → server emails an OTP →
    /// user fills in the OTP + new password (twice) → server verifies + updates.
    /// The OTP / new-password rows stay disabled until a code is successfully sent
    /// so it's obvious the email step has to happen first.
    /// </summary>
    public partial class ForgotPasswordForm : Form
    {
        private string _otpToken;

        public ForgotPasswordForm()
        {
            InitializeComponent();
            btnSendOtp.Click += BtnSendOtp_Click;
            btnReset.Click += BtnReset_Click;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        }

        private async void BtnSendOtp_Click(object sender, EventArgs e)
        {
            var email = (txtEmail.Text ?? "").Trim();
            if (!email.Contains("@") || !email.Contains("."))
            {
                MessageBox.Show("Email không hợp lệ.", "Quên mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnSendOtp.Enabled = false;
            var originalText = btnSendOtp.Text;
            btnSendOtp.Text = "Đang gửi mã...";

            ForgotPasswordSendOtpResult res;
            try { res = await AuthClient.SendForgotPasswordOtpAsync(email); }
            finally
            {
                btnSendOtp.Enabled = true;
                btnSendOtp.Text = originalText;
            }

            if (!res.Success)
            {
                MessageBox.Show(res.Message ?? "Không gửi được mã.", "Quên mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _otpToken = res.OtpToken;
            txtCode.Enabled = true;
            txtNewPassword.Enabled = true;
            txtNewPassword2.Enabled = true;
            btnReset.Enabled = true;
            txtCode.Focus();

            MessageBox.Show("Đã gửi mã đặt lại mật khẩu đến " + email + ".",
                "Quên mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void BtnReset_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_otpToken))
            {
                MessageBox.Show("Hãy gửi mã xác thực trước.", "Quên mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var code = (txtCode.Text ?? "").Trim();
            if (code.Length != 6 || !ulong.TryParse(code, out _))
            {
                MessageBox.Show("Mã xác thực gồm 6 chữ số.", "Quên mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newPw = txtNewPassword.Text ?? "";
            var newPw2 = txtNewPassword2.Text ?? "";
            if (newPw.Length < 6)
            {
                MessageBox.Show("Mật khẩu mới phải có ít nhất 6 ký tự.", "Quên mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (newPw != newPw2)
            {
                MessageBox.Show("Hai lần nhập mật khẩu không khớp.", "Quên mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var email = (txtEmail.Text ?? "").Trim();

            btnReset.Enabled = false;
            var originalText = btnReset.Text;
            btnReset.Text = "Đang xử lý...";

            ResetPasswordResult res;
            try { res = await AuthClient.ResetPasswordAsync(email, newPw, _otpToken, code); }
            finally
            {
                btnReset.Enabled = true;
                btnReset.Text = originalText;
            }

            if (!res.Success)
            {
                MessageBox.Show(res.Message ?? "Đặt lại mật khẩu thất bại.", "Quên mật khẩu",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show("Đặt lại mật khẩu thành công! Hãy đăng nhập lại.",
                "Quên mật khẩu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
