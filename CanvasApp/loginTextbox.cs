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
    public partial class loginTextbox : UserControl
    {
        public loginTextbox()
        {
            InitializeComponent();
        }
        public string _label = "default value";
        public bool _isPassword = false;

        public string label
        {
            get { return _label; }
            set { _label = value;
                label1.Text = value;
            }
        }

        public bool isPassword
        {
            get { return _isPassword; }
            set 
            { 
                _isPassword = value;
                textBox1.UseSystemPasswordChar = value;
            }
        }
        public string TextValue
        {
            get { return textBox1.Text; }
            set { textBox1.Text = value; }
        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
