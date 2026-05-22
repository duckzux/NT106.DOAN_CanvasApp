using System.Windows.Forms;

namespace CanvasApp.Client
{
    public partial class CanvasForm : Form
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CanvasForm));
            this.pnlHeader = new Guna.UI2.WinForms.Guna2Panel();
            this.ctrlMinimize = new Guna.UI2.WinForms.Guna2ControlBox();
            this.ctrlClose = new Guna.UI2.WinForms.Guna2ControlBox();
            this.ctrlMaximize = new Guna.UI2.WinForms.Guna2ControlBox();
            this.guna2DragControl1 = new Guna.UI2.WinForms.Guna2DragControl(this.components);
            this.pnlRight = new Guna.UI2.WinForms.Guna2Panel();
            this.btnSendMessage = new Guna.UI2.WinForms.Guna2Button();
            this.txtMessageInput = new Guna.UI2.WinForms.Guna2TextBox();
            this.rtbChatHistory = new System.Windows.Forms.RichTextBox();
            this.pnlUserList = new Guna.UI2.WinForms.Guna2Panel();
            this.btnCopyCode = new System.Windows.Forms.Button();
            this.lblRoomCode = new System.Windows.Forms.Label();
            this.lblRoomName = new System.Windows.Forms.Label();
            this.canvasPanel = new System.Windows.Forms.Panel();
            this.pnlColorPalette = new System.Windows.Forms.Panel();
            this.btnAddColor = new Guna.UI2.WinForms.Guna2Button();
            this.flpRecentColors = new System.Windows.Forms.FlowLayoutPanel();
            this.pnlColorWhite = new System.Windows.Forms.Panel();
            this.pnlColorBrown = new System.Windows.Forms.Panel();
            this.pnlColorPink = new System.Windows.Forms.Panel();
            this.pnlColorPurple = new System.Windows.Forms.Panel();
            this.pnlColorCyan = new System.Windows.Forms.Panel();
            this.pnlColorBlue = new System.Windows.Forms.Panel();
            this.pnlColorGreen = new System.Windows.Forms.Panel();
            this.pnlColorYellow = new System.Windows.Forms.Panel();
            this.pnlColorOrange = new System.Windows.Forms.Panel();
            this.pnlColorRed = new System.Windows.Forms.Panel();
            this.pnlColorGray = new System.Windows.Forms.Panel();
            this.pnlColorBlack = new System.Windows.Forms.Panel();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.lblConnectionStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblPing = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblCoordinates = new System.Windows.Forms.ToolStripStatusLabel();
            this.btnPen = new System.Windows.Forms.ToolStripButton();
            this.btnEraser = new System.Windows.Forms.ToolStripButton();
            this.btnRectangle = new System.Windows.Forms.ToolStripButton();
            this.btnCircle = new System.Windows.Forms.ToolStripButton();
            this.btnLine = new System.Windows.Forms.ToolStripButton();
            this.btnArrow = new System.Windows.Forms.ToolStripButton();
            this.btnText = new System.Windows.Forms.ToolStripButton();
            this.btnColor = new System.Windows.Forms.ToolStripButton();
            this.btnUndo = new System.Windows.Forms.ToolStripButton();
            this.btnRedo = new System.Windows.Forms.ToolStripButton();
            this.btnClear = new System.Windows.Forms.ToolStripButton();
            this.btnExport = new System.Windows.Forms.ToolStripButton();
            this.chkFill = new System.Windows.Forms.ToolStripButton();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.tscbSize = new System.Windows.Forms.ToolStripComboBox();
            this.btnImportBg = new System.Windows.Forms.ToolStripButton();
            this.colorDialog1 = new System.Windows.Forms.ColorDialog();
            this.pnlHeader.SuspendLayout();
            this.pnlRight.SuspendLayout();
            this.canvasPanel.SuspendLayout();
            this.pnlColorPalette.SuspendLayout();
            this.statusStrip1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.SystemColors.Window;
            this.pnlHeader.Controls.Add(this.ctrlMinimize);
            this.pnlHeader.Controls.Add(this.ctrlClose);
            this.pnlHeader.Controls.Add(this.ctrlMaximize);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(173)))), ((int)(((byte)(255)))));
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(1200, 45);
            this.pnlHeader.TabIndex = 0;
            // 
            // ctrlMinimize
            // 
            this.ctrlMinimize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ctrlMinimize.BackColor = System.Drawing.Color.Transparent;
            this.ctrlMinimize.ControlBoxType = Guna.UI2.WinForms.Enums.ControlBoxType.MinimizeBox;
            this.ctrlMinimize.FillColor = System.Drawing.Color.Transparent;
            this.ctrlMinimize.IconColor = System.Drawing.Color.White;
            this.ctrlMinimize.Location = new System.Drawing.Point(1046, 8);
            this.ctrlMinimize.Name = "ctrlMinimize";
            this.ctrlMinimize.Size = new System.Drawing.Size(45, 29);
            this.ctrlMinimize.TabIndex = 3;
            // 
            // ctrlClose
            // 
            this.ctrlClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ctrlClose.BackColor = System.Drawing.Color.Transparent;
            this.ctrlClose.ControlBoxStyle = Guna.UI2.WinForms.Enums.ControlBoxStyle.Custom;
            this.ctrlClose.FillColor = System.Drawing.Color.Transparent;
            this.ctrlClose.IconColor = System.Drawing.Color.White;
            this.ctrlClose.Location = new System.Drawing.Point(1148, 8);
            this.ctrlClose.Name = "ctrlClose";
            this.ctrlClose.Size = new System.Drawing.Size(45, 29);
            this.ctrlClose.TabIndex = 1;
            // 
            // ctrlMaximize
            // 
            this.ctrlMaximize.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.ctrlMaximize.BackColor = System.Drawing.Color.Transparent;
            this.ctrlMaximize.ControlBoxStyle = Guna.UI2.WinForms.Enums.ControlBoxStyle.Custom;
            this.ctrlMaximize.ControlBoxType = Guna.UI2.WinForms.Enums.ControlBoxType.MaximizeBox;
            this.ctrlMaximize.FillColor = System.Drawing.Color.Transparent;
            this.ctrlMaximize.IconColor = System.Drawing.Color.White;
            this.ctrlMaximize.Location = new System.Drawing.Point(1097, 8);
            this.ctrlMaximize.Name = "ctrlMaximize";
            this.ctrlMaximize.Size = new System.Drawing.Size(45, 29);
            this.ctrlMaximize.TabIndex = 2;
            this.ctrlMaximize.Click += new System.EventHandler(this.guna2ControlBox2_Click);
            // 
            // guna2DragControl1
            // 
            this.guna2DragControl1.DockIndicatorTransparencyValue = 0.6D;
            this.guna2DragControl1.TargetControl = this.pnlHeader;
            this.guna2DragControl1.UseTransparentDrag = true;
            // 
            // pnlRight
            // 
            this.pnlRight.Controls.Add(this.btnSendMessage);
            this.pnlRight.Controls.Add(this.txtMessageInput);
            this.pnlRight.Controls.Add(this.rtbChatHistory);
            this.pnlRight.Controls.Add(this.pnlUserList);
            this.pnlRight.Controls.Add(this.btnCopyCode);
            this.pnlRight.Controls.Add(this.lblRoomCode);
            this.pnlRight.Controls.Add(this.lblRoomName);
            this.pnlRight.Dock = System.Windows.Forms.DockStyle.Right;
            this.pnlRight.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(224)))), ((int)(((byte)(255)))));
            this.pnlRight.Location = new System.Drawing.Point(950, 45);
            this.pnlRight.Name = "pnlRight";
            this.pnlRight.Padding = new System.Windows.Forms.Padding(10);
            this.pnlRight.Size = new System.Drawing.Size(250, 755);
            this.pnlRight.TabIndex = 2;
            // 
            // btnSendMessage
            // 
            this.btnSendMessage.Animated = true;
            this.btnSendMessage.BackColor = System.Drawing.Color.Transparent;
            this.btnSendMessage.BorderRadius = 20;
            this.btnSendMessage.DisabledState.BorderColor = System.Drawing.Color.DarkGray;
            this.btnSendMessage.DisabledState.CustomBorderColor = System.Drawing.Color.DarkGray;
            this.btnSendMessage.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(169)))), ((int)(((byte)(169)))));
            this.btnSendMessage.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(141)))), ((int)(((byte)(141)))), ((int)(((byte)(141)))));
            this.btnSendMessage.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(172)))), ((int)(((byte)(139)))), ((int)(((byte)(238)))));
            this.btnSendMessage.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnSendMessage.ForeColor = System.Drawing.Color.WhiteSmoke;
            this.btnSendMessage.Location = new System.Drawing.Point(194, 696);
            this.btnSendMessage.Name = "btnSendMessage";
            this.btnSendMessage.Size = new System.Drawing.Size(49, 45);
            this.btnSendMessage.TabIndex = 4;
            this.btnSendMessage.Text = " ➤";
            // 
            // txtMessageInput
            // 
            this.txtMessageInput.BackColor = System.Drawing.Color.Transparent;
            this.txtMessageInput.BorderRadius = 20;
            this.txtMessageInput.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.txtMessageInput.DefaultText = "";
            this.txtMessageInput.DisabledState.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(208)))), ((int)(((byte)(208)))));
            this.txtMessageInput.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(226)))), ((int)(((byte)(226)))));
            this.txtMessageInput.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(138)))), ((int)(((byte)(138)))), ((int)(((byte)(138)))));
            this.txtMessageInput.DisabledState.PlaceholderForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(138)))), ((int)(((byte)(138)))), ((int)(((byte)(138)))));
            this.txtMessageInput.FocusedState.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(94)))), ((int)(((byte)(148)))), ((int)(((byte)(255)))));
            this.txtMessageInput.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.txtMessageInput.HoverState.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(94)))), ((int)(((byte)(148)))), ((int)(((byte)(255)))));
            this.txtMessageInput.Location = new System.Drawing.Point(6, 693);
            this.txtMessageInput.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.txtMessageInput.Name = "txtMessageInput";
            this.txtMessageInput.PlaceholderText = "Nhập tin nhắn...";
            this.txtMessageInput.SelectedText = "";
            this.txtMessageInput.Size = new System.Drawing.Size(182, 48);
            this.txtMessageInput.TabIndex = 3;
            // 
            // rtbChatHistory
            // 
            this.rtbChatHistory.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbChatHistory.Location = new System.Drawing.Point(6, 265);
            this.rtbChatHistory.Name = "rtbChatHistory";
            this.rtbChatHistory.ReadOnly = true;
            this.rtbChatHistory.Size = new System.Drawing.Size(237, 410);
            this.rtbChatHistory.TabIndex = 2;
            this.rtbChatHistory.Text = "";
            // 
            // pnlUserList
            // 
            this.pnlUserList.AutoScroll = true;
            this.pnlUserList.BackColor = System.Drawing.Color.Transparent;
            this.pnlUserList.BorderRadius = 10;
            this.pnlUserList.FillColor = System.Drawing.Color.White;
            this.pnlUserList.Location = new System.Drawing.Point(6, 74);
            this.pnlUserList.Name = "pnlUserList";
            this.pnlUserList.Size = new System.Drawing.Size(237, 175);
            this.pnlUserList.TabIndex = 1;
            // 
            // btnCopyCode
            // 
            this.btnCopyCode.BackColor = System.Drawing.Color.Transparent;
            this.btnCopyCode.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCopyCode.FlatAppearance.BorderSize = 0;
            this.btnCopyCode.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(220)))), ((int)(((byte)(255)))));
            this.btnCopyCode.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCopyCode.Image = ((System.Drawing.Image)(resources.GetObject("btnCopyCode.Image")));
            this.btnCopyCode.Location = new System.Drawing.Point(198, 41);
            this.btnCopyCode.Name = "btnCopyCode";
            this.btnCopyCode.Size = new System.Drawing.Size(39, 26);
            this.btnCopyCode.TabIndex = 5;
            this.btnCopyCode.UseVisualStyleBackColor = false;
            this.btnCopyCode.Click += new System.EventHandler(this.btnCopyCode_Click);
            // 
            // lblRoomCode
            // 
            this.lblRoomCode.BackColor = System.Drawing.Color.Transparent;
            this.lblRoomCode.Font = new System.Drawing.Font("Microsoft Sans Serif", 10.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblRoomCode.Location = new System.Drawing.Point(10, 43);
            this.lblRoomCode.Name = "lblRoomCode";
            this.lblRoomCode.Size = new System.Drawing.Size(198, 24);
            this.lblRoomCode.TabIndex = 1;
            this.lblRoomCode.Text = "Mã: ABC123X";
            this.lblRoomCode.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // lblRoomName
            // 
            this.lblRoomName.BackColor = System.Drawing.Color.Transparent;
            this.lblRoomName.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblRoomName.Location = new System.Drawing.Point(10, 10);
            this.lblRoomName.Name = "lblRoomName";
            this.lblRoomName.Size = new System.Drawing.Size(230, 28);
            this.lblRoomName.TabIndex = 0;
            this.lblRoomName.Text = "Phòng vẽ: ID";
            this.lblRoomName.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // canvasPanel
            // 
            this.canvasPanel.Controls.Add(this.pnlColorPalette);
            this.canvasPanel.Controls.Add(this.statusStrip1);
            this.canvasPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.canvasPanel.Location = new System.Drawing.Point(50, 45);
            this.canvasPanel.Name = "canvasPanel";
            this.canvasPanel.Size = new System.Drawing.Size(900, 755);
            this.canvasPanel.TabIndex = 3;
            // 
            // pnlColorPalette
            // 
            this.pnlColorPalette.Controls.Add(this.btnAddColor);
            this.pnlColorPalette.Controls.Add(this.flpRecentColors);
            this.pnlColorPalette.Controls.Add(this.pnlColorWhite);
            this.pnlColorPalette.Controls.Add(this.pnlColorBrown);
            this.pnlColorPalette.Controls.Add(this.pnlColorPink);
            this.pnlColorPalette.Controls.Add(this.pnlColorPurple);
            this.pnlColorPalette.Controls.Add(this.pnlColorCyan);
            this.pnlColorPalette.Controls.Add(this.pnlColorBlue);
            this.pnlColorPalette.Controls.Add(this.pnlColorGreen);
            this.pnlColorPalette.Controls.Add(this.pnlColorYellow);
            this.pnlColorPalette.Controls.Add(this.pnlColorOrange);
            this.pnlColorPalette.Controls.Add(this.pnlColorRed);
            this.pnlColorPalette.Controls.Add(this.pnlColorGray);
            this.pnlColorPalette.Controls.Add(this.pnlColorBlack);
            this.pnlColorPalette.Location = new System.Drawing.Point(13, 285);
            this.pnlColorPalette.Name = "pnlColorPalette";
            this.pnlColorPalette.Size = new System.Drawing.Size(164, 167);
            this.pnlColorPalette.TabIndex = 1;
            this.pnlColorPalette.Visible = false;
            // 
            // btnAddColor
            // 
            this.btnAddColor.BorderRadius = 10;
            this.btnAddColor.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddColor.DisabledState.BorderColor = System.Drawing.Color.DarkGray;
            this.btnAddColor.DisabledState.CustomBorderColor = System.Drawing.Color.DarkGray;
            this.btnAddColor.DisabledState.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(169)))), ((int)(((byte)(169)))));
            this.btnAddColor.DisabledState.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(141)))), ((int)(((byte)(141)))), ((int)(((byte)(141)))));
            this.btnAddColor.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(192)))), ((int)(((byte)(255)))));
            this.btnAddColor.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnAddColor.ForeColor = System.Drawing.Color.Black;
            this.btnAddColor.Location = new System.Drawing.Point(70, 69);
            this.btnAddColor.Name = "btnAddColor";
            this.btnAddColor.Size = new System.Drawing.Size(56, 25);
            this.btnAddColor.TabIndex = 2;
            this.btnAddColor.Text = "+";
            // 
            // flpRecentColors
            // 
            this.flpRecentColors.Location = new System.Drawing.Point(8, 100);
            this.flpRecentColors.Name = "flpRecentColors";
            this.flpRecentColors.Size = new System.Drawing.Size(149, 60);
            this.flpRecentColors.TabIndex = 2;
            // 
            // pnlColorWhite
            // 
            this.pnlColorWhite.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorWhite.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorWhite.Location = new System.Drawing.Point(39, 69);
            this.pnlColorWhite.Name = "pnlColorWhite";
            this.pnlColorWhite.Size = new System.Drawing.Size(25, 25);
            this.pnlColorWhite.TabIndex = 3;
            // 
            // pnlColorBrown
            // 
            this.pnlColorBrown.BackColor = System.Drawing.Color.Brown;
            this.pnlColorBrown.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorBrown.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorBrown.Location = new System.Drawing.Point(8, 69);
            this.pnlColorBrown.Name = "pnlColorBrown";
            this.pnlColorBrown.Size = new System.Drawing.Size(25, 25);
            this.pnlColorBrown.TabIndex = 3;
            // 
            // pnlColorPink
            // 
            this.pnlColorPink.BackColor = System.Drawing.Color.Pink;
            this.pnlColorPink.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorPink.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorPink.Location = new System.Drawing.Point(132, 38);
            this.pnlColorPink.Name = "pnlColorPink";
            this.pnlColorPink.Size = new System.Drawing.Size(25, 25);
            this.pnlColorPink.TabIndex = 3;
            // 
            // pnlColorPurple
            // 
            this.pnlColorPurple.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(128)))), ((int)(((byte)(128)))), ((int)(((byte)(255)))));
            this.pnlColorPurple.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorPurple.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorPurple.Location = new System.Drawing.Point(101, 38);
            this.pnlColorPurple.Name = "pnlColorPurple";
            this.pnlColorPurple.Size = new System.Drawing.Size(25, 25);
            this.pnlColorPurple.TabIndex = 3;
            // 
            // pnlColorCyan
            // 
            this.pnlColorCyan.BackColor = System.Drawing.Color.Cyan;
            this.pnlColorCyan.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorCyan.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorCyan.Location = new System.Drawing.Point(70, 38);
            this.pnlColorCyan.Name = "pnlColorCyan";
            this.pnlColorCyan.Size = new System.Drawing.Size(25, 25);
            this.pnlColorCyan.TabIndex = 3;
            // 
            // pnlColorBlue
            // 
            this.pnlColorBlue.BackColor = System.Drawing.Color.Blue;
            this.pnlColorBlue.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorBlue.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorBlue.Location = new System.Drawing.Point(39, 38);
            this.pnlColorBlue.Name = "pnlColorBlue";
            this.pnlColorBlue.Size = new System.Drawing.Size(25, 25);
            this.pnlColorBlue.TabIndex = 3;
            // 
            // pnlColorGreen
            // 
            this.pnlColorGreen.BackColor = System.Drawing.Color.Green;
            this.pnlColorGreen.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorGreen.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorGreen.Location = new System.Drawing.Point(8, 38);
            this.pnlColorGreen.Name = "pnlColorGreen";
            this.pnlColorGreen.Size = new System.Drawing.Size(25, 25);
            this.pnlColorGreen.TabIndex = 3;
            // 
            // pnlColorYellow
            // 
            this.pnlColorYellow.BackColor = System.Drawing.Color.Yellow;
            this.pnlColorYellow.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorYellow.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorYellow.Location = new System.Drawing.Point(132, 7);
            this.pnlColorYellow.Name = "pnlColorYellow";
            this.pnlColorYellow.Size = new System.Drawing.Size(25, 25);
            this.pnlColorYellow.TabIndex = 3;
            // 
            // pnlColorOrange
            // 
            this.pnlColorOrange.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(0)))));
            this.pnlColorOrange.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorOrange.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorOrange.Location = new System.Drawing.Point(101, 7);
            this.pnlColorOrange.Name = "pnlColorOrange";
            this.pnlColorOrange.Size = new System.Drawing.Size(25, 25);
            this.pnlColorOrange.TabIndex = 3;
            // 
            // pnlColorRed
            // 
            this.pnlColorRed.BackColor = System.Drawing.Color.Red;
            this.pnlColorRed.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorRed.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorRed.Location = new System.Drawing.Point(70, 7);
            this.pnlColorRed.Name = "pnlColorRed";
            this.pnlColorRed.Size = new System.Drawing.Size(25, 25);
            this.pnlColorRed.TabIndex = 3;
            // 
            // pnlColorGray
            // 
            this.pnlColorGray.BackColor = System.Drawing.Color.Gray;
            this.pnlColorGray.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorGray.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorGray.Location = new System.Drawing.Point(39, 7);
            this.pnlColorGray.Name = "pnlColorGray";
            this.pnlColorGray.Size = new System.Drawing.Size(25, 25);
            this.pnlColorGray.TabIndex = 3;
            // 
            // pnlColorBlack
            // 
            this.pnlColorBlack.BackColor = System.Drawing.Color.Black;
            this.pnlColorBlack.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.pnlColorBlack.Cursor = System.Windows.Forms.Cursors.Hand;
            this.pnlColorBlack.Location = new System.Drawing.Point(8, 7);
            this.pnlColorBlack.Name = "pnlColorBlack";
            this.pnlColorBlack.Size = new System.Drawing.Size(25, 25);
            this.pnlColorBlack.TabIndex = 2;
            // 
            // statusStrip1
            // 
            this.statusStrip1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(173)))), ((int)(((byte)(255)))));
            this.statusStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblConnectionStatus,
            this.lblPing,
            this.lblStatus,
            this.lblCoordinates});
            this.statusStrip1.Location = new System.Drawing.Point(0, 723);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            this.statusStrip1.Size = new System.Drawing.Size(900, 32);
            this.statusStrip1.TabIndex = 0;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // lblConnectionStatus
            // 
            this.lblConnectionStatus.ForeColor = System.Drawing.Color.GreenYellow;
            this.lblConnectionStatus.Name = "lblConnectionStatus";
            this.lblConnectionStatus.Size = new System.Drawing.Size(93, 26);
            this.lblConnectionStatus.Text = "● Connected";
            // 
            // lblPing
            // 
            this.lblPing.Margin = new System.Windows.Forms.Padding(20, 4, 0, 2);
            this.lblPing.Name = "lblPing";
            this.lblPing.Size = new System.Drawing.Size(72, 26);
            this.lblPing.Text = "Ping: 0ms";
            // 
            // lblStatus
            // 
            this.lblStatus.Margin = new System.Windows.Forms.Padding(10, 4, 0, 2);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Padding = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this.lblStatus.Size = new System.Drawing.Size(617, 26);
            this.lblStatus.Spring = true;
            this.lblStatus.Text = "Sẵn sàng";
            this.lblStatus.Click += new System.EventHandler(this.toolStripStatusLabel1_Click);
            // 
            // lblCoordinates
            // 
            this.lblCoordinates.Margin = new System.Windows.Forms.Padding(10, 4, 0, 2);
            this.lblCoordinates.Name = "lblCoordinates";
            this.lblCoordinates.Size = new System.Drawing.Size(63, 26);
            this.lblCoordinates.Text = "X: 0, Y: 0";
            // 
            // btnPen
            // 
            this.btnPen.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnPen.Image = ((System.Drawing.Image)(resources.GetObject("btnPen.Image")));
            this.btnPen.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnPen.Name = "btnPen";
            this.btnPen.Padding = new System.Windows.Forms.Padding(3);
            this.btnPen.Size = new System.Drawing.Size(43, 34);
            this.btnPen.Text = "Bút vẽ";
            this.btnPen.ToolTipText = "Bút vẽ";
            // 
            // btnEraser
            // 
            this.btnEraser.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnEraser.Image = ((System.Drawing.Image)(resources.GetObject("btnEraser.Image")));
            this.btnEraser.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnEraser.Name = "btnEraser";
            this.btnEraser.Padding = new System.Windows.Forms.Padding(3);
            this.btnEraser.Size = new System.Drawing.Size(43, 34);
            this.btnEraser.Text = "Tẩy";
            // 
            // btnRectangle
            // 
            this.btnRectangle.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnRectangle.Image = ((System.Drawing.Image)(resources.GetObject("btnRectangle.Image")));
            this.btnRectangle.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnRectangle.Name = "btnRectangle";
            this.btnRectangle.Padding = new System.Windows.Forms.Padding(3);
            this.btnRectangle.Size = new System.Drawing.Size(43, 34);
            this.btnRectangle.Text = "Hình chữ nhật";
            // 
            // btnCircle
            // 
            this.btnCircle.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnCircle.Image = ((System.Drawing.Image)(resources.GetObject("btnCircle.Image")));
            this.btnCircle.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnCircle.Name = "btnCircle";
            this.btnCircle.Padding = new System.Windows.Forms.Padding(3);
            this.btnCircle.Size = new System.Drawing.Size(43, 34);
            this.btnCircle.Text = "Hình tròn";
            this.btnCircle.Click += new System.EventHandler(this.btnCircle_Click);
            // 
            // btnLine
            // 
            this.btnLine.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnLine.Image = ((System.Drawing.Image)(resources.GetObject("btnLine.Image")));
            this.btnLine.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnLine.Name = "btnLine";
            this.btnLine.Padding = new System.Windows.Forms.Padding(3);
            this.btnLine.Size = new System.Drawing.Size(43, 34);
            this.btnLine.Text = "Đường thẳng";
            // 
            // btnArrow
            // 
            this.btnArrow.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnArrow.Image = ((System.Drawing.Image)(resources.GetObject("btnArrow.Image")));
            this.btnArrow.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnArrow.Name = "btnArrow";
            this.btnArrow.Padding = new System.Windows.Forms.Padding(3);
            this.btnArrow.Size = new System.Drawing.Size(43, 34);
            this.btnArrow.Text = "Mũi tên";
            // 
            // btnText
            // 
            this.btnText.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnText.Image = ((System.Drawing.Image)(resources.GetObject("btnText.Image")));
            this.btnText.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnText.Name = "btnText";
            this.btnText.Padding = new System.Windows.Forms.Padding(3);
            this.btnText.Size = new System.Drawing.Size(43, 34);
            this.btnText.Text = "Văn bản";
            // 
            // btnColor
            // 
            this.btnColor.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnColor.Image = ((System.Drawing.Image)(resources.GetObject("btnColor.Image")));
            this.btnColor.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnColor.Name = "btnColor";
            this.btnColor.Padding = new System.Windows.Forms.Padding(3);
            this.btnColor.Size = new System.Drawing.Size(43, 34);
            this.btnColor.Text = "Màu sắc";
            // 
            // btnUndo
            // 
            this.btnUndo.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnUndo.Image = ((System.Drawing.Image)(resources.GetObject("btnUndo.Image")));
            this.btnUndo.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnUndo.Name = "btnUndo";
            this.btnUndo.Padding = new System.Windows.Forms.Padding(3);
            this.btnUndo.Size = new System.Drawing.Size(43, 34);
            this.btnUndo.Text = "Hoàn tác";
            // 
            // btnRedo
            // 
            this.btnRedo.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnRedo.Image = ((System.Drawing.Image)(resources.GetObject("btnRedo.Image")));
            this.btnRedo.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnRedo.Name = "btnRedo";
            this.btnRedo.Padding = new System.Windows.Forms.Padding(3);
            this.btnRedo.Size = new System.Drawing.Size(43, 34);
            this.btnRedo.Text = "Làm lại";
            // 
            // btnClear
            // 
            this.btnClear.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnClear.Image = ((System.Drawing.Image)(resources.GetObject("btnClear.Image")));
            this.btnClear.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnClear.Name = "btnClear";
            this.btnClear.Padding = new System.Windows.Forms.Padding(3);
            this.btnClear.Size = new System.Drawing.Size(43, 34);
            this.btnClear.Text = "Xóa hết";
            // 
            // btnExport
            // 
            this.btnExport.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnExport.Image = ((System.Drawing.Image)(resources.GetObject("btnExport.Image")));
            this.btnExport.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnExport.Name = "btnExport";
            this.btnExport.Padding = new System.Windows.Forms.Padding(3);
            this.btnExport.Size = new System.Drawing.Size(43, 34);
            this.btnExport.Text = "Xuất ảnh";
            // 
            // chkFill
            // 
            this.chkFill.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.chkFill.Image = ((System.Drawing.Image)(resources.GetObject("chkFill.Image")));
            this.chkFill.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.chkFill.Name = "chkFill";
            this.chkFill.Padding = new System.Windows.Forms.Padding(3);
            this.chkFill.Size = new System.Drawing.Size(43, 34);
            this.chkFill.Text = "Đổ màu";
            // 
            // toolStrip1
            // 
            this.toolStrip1.AutoSize = false;
            this.toolStrip1.Dock = System.Windows.Forms.DockStyle.Left;
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.btnPen,
            this.btnEraser,
            this.tscbSize,
            this.btnRectangle,
            this.btnCircle,
            this.btnLine,
            this.btnArrow,
            this.btnText,
            this.btnColor,
            this.chkFill,
            this.btnUndo,
            this.btnRedo,
            this.btnClear,
            this.btnImportBg,
            this.btnExport});
            this.toolStrip1.Location = new System.Drawing.Point(0, 45);
            this.toolStrip1.Margin = new System.Windows.Forms.Padding(0, 5, 0, 5);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Padding = new System.Windows.Forms.Padding(3);
            this.toolStrip1.Size = new System.Drawing.Size(50, 755);
            this.toolStrip1.TabIndex = 1;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // tscbSize
            // 
            this.tscbSize.Font = new System.Drawing.Font("Segoe UI", 7.2F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.tscbSize.Items.AddRange(new object[] {
            "1",
            "2",
            "3",
            "5",
            "8",
            "9",
            "10",
            "11",
            "12",
            "15",
            "20",
            "25",
            "30",
            "40",
            "50",
            "72"});
            this.tscbSize.Name = "tscbSize";
            this.tscbSize.Size = new System.Drawing.Size(41, 23);
            this.tscbSize.Text = " ⚫";
            this.tscbSize.TextChanged += new System.EventHandler(this.tscbSize_TextChanged);
            // 
            // btnImportBg
            // 
            this.btnImportBg.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.btnImportBg.Image = ((System.Drawing.Image)(resources.GetObject("btnImportBg.Image")));
            this.btnImportBg.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.btnImportBg.Name = "btnImportBg";
            this.btnImportBg.Size = new System.Drawing.Size(43, 28);
            this.btnImportBg.Text = "toolStripButton1";
            // 
            // colorDialog1
            // 
            this.colorDialog1.AnyColor = true;
            this.colorDialog1.FullOpen = true;
            // 
            // CanvasForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(1200, 800);
            this.Controls.Add(this.canvasPanel);
            this.Controls.Add(this.pnlRight);
            this.Controls.Add(this.toolStrip1);
            this.Controls.Add(this.pnlHeader);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "CanvasForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CanvasApp";
            this.pnlHeader.ResumeLayout(false);
            this.pnlRight.ResumeLayout(false);
            this.canvasPanel.ResumeLayout(false);
            this.canvasPanel.PerformLayout();
            this.pnlColorPalette.ResumeLayout(false);
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        // ── Guna2 controls ────────────────────────────────────────────────
        private Guna.UI2.WinForms.Guna2Panel pnlHeader;
        private Guna.UI2.WinForms.Guna2ControlBox ctrlMinimize;
        private Guna.UI2.WinForms.Guna2ControlBox ctrlClose;
        private Guna.UI2.WinForms.Guna2ControlBox ctrlMaximize;
        private Guna.UI2.WinForms.Guna2DragControl guna2DragControl1;
        private Guna.UI2.WinForms.Guna2Panel pnlRight;
        private Guna.UI2.WinForms.Guna2Panel pnlUserList;
        private Guna.UI2.WinForms.Guna2TextBox txtMessageInput;
        private Guna.UI2.WinForms.Guna2Button btnSendMessage;
        private Guna.UI2.WinForms.Guna2Button btnAddColor;

        // ── Standard WinForms controls ────────────────────────────────────
        private System.Windows.Forms.Panel canvasPanel;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.Label lblRoomCode;
        private System.Windows.Forms.Label lblRoomName;
        private System.Windows.Forms.RichTextBox rtbChatHistory;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblCoordinates;
        private System.Windows.Forms.ToolStripStatusLabel lblConnectionStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblPing;
        private System.Windows.Forms.ToolStripButton btnPen;
        private System.Windows.Forms.ToolStripButton btnEraser;
        private System.Windows.Forms.ToolStripButton btnRectangle;
        private System.Windows.Forms.ToolStripButton btnCircle;
        private System.Windows.Forms.ToolStripButton btnLine;
        private System.Windows.Forms.ToolStripButton btnArrow;
        private System.Windows.Forms.ToolStripButton btnText;
        private System.Windows.Forms.ToolStripButton btnColor;
        private System.Windows.Forms.ToolStripButton btnUndo;
        private System.Windows.Forms.ToolStripButton btnRedo;
        private System.Windows.Forms.ToolStripButton btnClear;
        private System.Windows.Forms.ToolStripButton btnExport;
        private System.Windows.Forms.ToolStripButton chkFill;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.Panel pnlColorPalette;
        private System.Windows.Forms.Panel pnlColorWhite;
        private System.Windows.Forms.Panel pnlColorBrown;
        private System.Windows.Forms.Panel pnlColorPink;
        private System.Windows.Forms.Panel pnlColorPurple;
        private System.Windows.Forms.Panel pnlColorCyan;
        private System.Windows.Forms.Panel pnlColorBlue;
        private System.Windows.Forms.Panel pnlColorGreen;
        private System.Windows.Forms.Panel pnlColorYellow;
        private System.Windows.Forms.Panel pnlColorOrange;
        private System.Windows.Forms.Panel pnlColorRed;
        private System.Windows.Forms.Panel pnlColorGray;
        private System.Windows.Forms.Panel pnlColorBlack;
        private System.Windows.Forms.FlowLayoutPanel flpRecentColors;
        private System.Windows.Forms.ColorDialog colorDialog1;
        private ToolStripButton btnImportBg;
        private ToolStripComboBox tscbSize;
        private System.Windows.Forms.Button btnCopyCode;
    }
}
