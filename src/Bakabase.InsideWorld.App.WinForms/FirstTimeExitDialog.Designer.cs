namespace Bakabase.InsideWorld.App.WinForms
{
    partial class FirstTimeExitDialog
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
            this.minimizeBtn = new System.Windows.Forms.Button();
            this.exitBtn = new System.Windows.Forms.Button();
            this.rememberCheckBox = new System.Windows.Forms.CheckBox();
            this.tip = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // button1
            // 
            this.minimizeBtn.Location = new System.Drawing.Point(25, 67);
            this.minimizeBtn.Name = "MinimizeButton";
            this.minimizeBtn.TabIndex = 1;
            this.minimizeBtn.Text = "Minimize";
            this.minimizeBtn.UseVisualStyleBackColor = true;
            // 
            // button2
            // 
            this.exitBtn.Location = new System.Drawing.Point(139, 67);
            this.exitBtn.Name = "ExitButton";
            this.exitBtn.TabIndex = 2;
            this.exitBtn.Text = "Exit";
            this.exitBtn.UseVisualStyleBackColor = true;
            // 
            // checkBox1
            // 
            this.rememberCheckBox.AutoSize = true;
            this.rememberCheckBox.Location = new System.Drawing.Point(35, 36);
            this.rememberCheckBox.Name = "RememberCheckBox";
            this.rememberCheckBox.Size = new System.Drawing.Size(142, 19);
            this.rememberCheckBox.TabIndex = 3;
            this.rememberCheckBox.Text = "Remember my choice";
            this.rememberCheckBox.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.tip.AutoSize = true;
            this.tip.Location = new System.Drawing.Point(35, 9);
            this.tip.Name = "Tip";
            this.tip.Size = new System.Drawing.Size(166, 15);
            this.tip.TabIndex = 4;
            this.tip.Text = "Are you sure you want to exit?";
            // 
            // FirstTimeExitDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(241, 102);
            this.Controls.Add(this.tip);
            this.Controls.Add(this.rememberCheckBox);
            this.Controls.Add(this.exitBtn);
            this.Controls.Add(this.minimizeBtn);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "FirstTimeExitDialog";
            this.Text = "Exiting";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private Button minimizeBtn;
        private Button exitBtn;
        private CheckBox rememberCheckBox;
        private Label tip;
    }
}