using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class RequirePassword : UserControl
    {
        public Action<string> OnSubmit;
        public Action OnCancel;
        public RequirePassword()
        {
            InitializeComponent();
            btnOK.Click += BtnOK_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            OnSubmit?.Invoke(txtPassword.Text);
            txtPassword.Clear();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            txtPassword.Clear();
            OnCancel?.Invoke();
        }

        private void guna2Panel1_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
