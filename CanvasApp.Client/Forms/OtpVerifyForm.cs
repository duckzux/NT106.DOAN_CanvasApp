using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using CanvasApp.Common;

namespace CanvasApp.Client
{
    /// <summary>
    /// Modal dialog that collects a 6-digit OTP. Caller sets <see cref="Email"/>,
    /// <see cref="Username"/> and an initial <see cref="OtpToken"/> then ShowDialog().
    /// On confirm, exposes <see cref="EnteredCode"/> + the (possibly-refreshed)
    /// <see cref="OtpToken"/>. "Gửi lại mã" reissues a new token in place.
    /// </summary>
    public partial class OtpVerifyForm : Form
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string OtpToken { get; set; }
        public string EnteredCode { get; private set; }

        public OtpVerifyForm()
        {
            InitializeComponent();
            btnOK.Click += BtnOK_Click;
            btnCancel.Click += BtnCancel_Click;
            btnResend.Click += BtnResend_Click;
            Shown += (s, e) => { lblEmail.Text = "Mã đã gửi tới: " + (Email ?? ""); txtCode.Focus(); };
            txtCode.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { BtnOK_Click(s, e); e.SuppressKeyPress = true; }
            };
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            var code = (txtCode.Text ?? "").Trim();
            if (code.Length != 6 || !ulong.TryParse(code, out _))
            {
                MessageBox.Show("Mã gồm 6 chữ số.", "OTP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            EnteredCode = code;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private async void BtnResend_Click(object sender, EventArgs e)
        {
            btnResend.Enabled = false;
            var original = btnResend.Text;
            btnResend.Text = "Đang gửi...";
            try
            {
                var res = await AuthClient.SendOtpAsync(Username, Email);
                if (res.Success)
                {
                    OtpToken = res.OtpToken;
                    txtCode.Clear();
                    MessageBox.Show("Đã gửi mã mới đến " + Email, "OTP",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(res.Message ?? "Không gửi được mã.", "OTP",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                btnResend.Text = original;
                StartCooldown();
            }
        }

        // Brief cooldown after a resend so users don't hammer the SMTP endpoint.
        private async void StartCooldown()
        {
            for (int s = 15; s > 0; s--)
            {
                btnResend.Text = $"Gửi lại ({s}s)";
                await Task.Delay(1000);
            }
            btnResend.Text = "Gửi lại mã";
            btnResend.Enabled = true;
        }
    }
}
