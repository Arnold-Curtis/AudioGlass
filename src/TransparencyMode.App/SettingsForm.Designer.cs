namespace TransparencyMode.App
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.lblInputDevice = new System.Windows.Forms.Label();
            this.cmbInputDevice = new System.Windows.Forms.ComboBox();
            this.lblOutputDevice = new System.Windows.Forms.Label();
            this.cmbOutputDevice = new System.Windows.Forms.ComboBox();
            this.lblVolume = new System.Windows.Forms.Label();
            this.trackVolume = new System.Windows.Forms.TrackBar();
            this.lblVolumeValue = new System.Windows.Forms.Label();
            this.chkEnabled = new System.Windows.Forms.CheckBox();
            this.lblBuffer = new System.Windows.Forms.Label();
            this.numBuffer = new System.Windows.Forms.NumericUpDown();
            this.chkLowLatency = new System.Windows.Forms.CheckBox();
            this.btnApply = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            this.lblWarning = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.trackVolume)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBuffer)).BeginInit();
            this.SuspendLayout();
            // 
            // lblInputDevice
            // 
            this.lblInputDevice.AutoSize = true;
            this.lblInputDevice.Location = new System.Drawing.Point(12, 15);
            this.lblInputDevice.Name = "lblInputDevice";
            this.lblInputDevice.Size = new System.Drawing.Size(106, 15);
            this.lblInputDevice.TabIndex = 0;
            this.lblInputDevice.Text = "Input (Microphone):";
            // 
            // cmbInputDevice
            // 
            this.cmbInputDevice.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbInputDevice.FormattingEnabled = true;
            this.cmbInputDevice.Location = new System.Drawing.Point(12, 33);
            this.cmbInputDevice.Name = "cmbInputDevice";
            this.cmbInputDevice.Size = new System.Drawing.Size(360, 23);
            this.cmbInputDevice.TabIndex = 1;
            // 
            // lblOutputDevice
            // 
            this.lblOutputDevice.AutoSize = true;
            this.lblOutputDevice.Location = new System.Drawing.Point(12, 65);
            this.lblOutputDevice.Name = "lblOutputDevice";
            this.lblOutputDevice.Size = new System.Drawing.Size(118, 15);
            this.lblOutputDevice.TabIndex = 2;
            this.lblOutputDevice.Text = "Output (Headphones):";
            // 
            // cmbOutputDevice
            // 
            this.cmbOutputDevice.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbOutputDevice.FormattingEnabled = true;
            this.cmbOutputDevice.Location = new System.Drawing.Point(12, 83);
            this.cmbOutputDevice.Name = "cmbOutputDevice";
            this.cmbOutputDevice.Size = new System.Drawing.Size(360, 23);
            this.cmbOutputDevice.TabIndex = 3;
            // 
            // lblVolume
            // 
            this.lblVolume.AutoSize = true;
            this.lblVolume.Location = new System.Drawing.Point(12, 115);
            this.lblVolume.Name = "lblVolume";
            this.lblVolume.Size = new System.Drawing.Size(110, 15);
            this.lblVolume.TabIndex = 4;
            this.lblVolume.Text = "Transparency Level:";
            // 
            // trackVolume
            // 
            this.trackVolume.Location = new System.Drawing.Point(12, 133);
            this.trackVolume.Maximum = 100;
            this.trackVolume.Name = "trackVolume";
            this.trackVolume.Size = new System.Drawing.Size(300, 45);
            this.trackVolume.TabIndex = 5;
            this.trackVolume.TickFrequency = 10;
            this.trackVolume.Value = 100;
            // 
            // lblVolumeValue
            // 
            this.lblVolumeValue.AutoSize = true;
            this.lblVolumeValue.Location = new System.Drawing.Point(318, 140);
            this.lblVolumeValue.Name = "lblVolumeValue";
            this.lblVolumeValue.Size = new System.Drawing.Size(33, 15);
            this.lblVolumeValue.TabIndex = 6;
            this.lblVolumeValue.Text = "100%";
            // 
            // chkEnabled
            // 
            this.chkEnabled.AutoSize = true;
            this.chkEnabled.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.chkEnabled.Location = new System.Drawing.Point(12, 184);
            this.chkEnabled.Name = "chkEnabled";
            this.chkEnabled.Size = new System.Drawing.Size(190, 23);
            this.chkEnabled.TabIndex = 7;
            this.chkEnabled.Text = "Enable Transparency Mode";
            this.chkEnabled.UseVisualStyleBackColor = true;
            // 
            // lblBuffer
            // 
            this.lblBuffer.AutoSize = true;
            this.lblBuffer.Location = new System.Drawing.Point(12, 220);
            this.lblBuffer.Name = "lblBuffer";
            this.lblBuffer.Size = new System.Drawing.Size(91, 15);
            this.lblBuffer.TabIndex = 8;
            this.lblBuffer.Text = "Buffer Size (ms):";
            // 
            // numBuffer
            // 
            this.numBuffer.Location = new System.Drawing.Point(109, 218);
            this.numBuffer.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numBuffer.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
            this.numBuffer.Name = "numBuffer";
            this.numBuffer.Size = new System.Drawing.Size(60, 23);
            this.numBuffer.TabIndex = 9;
            this.numBuffer.Value = new decimal(new int[] { 10, 0, 0, 0 });
            // 
            // chkLowLatency
            // 
            this.chkLowLatency.AutoSize = true;
            this.chkLowLatency.Checked = true;
            this.chkLowLatency.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkLowLatency.Location = new System.Drawing.Point(185, 219);
            this.chkLowLatency.Name = "chkLowLatency";
            this.chkLowLatency.Size = new System.Drawing.Size(116, 19);
            this.chkLowLatency.TabIndex = 10;
            this.chkLowLatency.Text = "Low Latency Mode";
            this.chkLowLatency.UseVisualStyleBackColor = true;
            // 
            // btnApply
            // 
            this.btnApply.Location = new System.Drawing.Point(216, 290);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(75, 30);
            this.btnApply.TabIndex = 11;
            this.btnApply.Text = "Apply";
            this.btnApply.UseVisualStyleBackColor = true;
            // 
            // btnClose
            // 
            this.btnClose.Location = new System.Drawing.Point(297, 290);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(75, 30);
            this.btnClose.TabIndex = 12;
            this.btnClose.Text = "Close";
            this.btnClose.UseVisualStyleBackColor = true;
            // 
            // lblWarning
            // 
            this.lblWarning.AutoSize = true;
            this.lblWarning.ForeColor = System.Drawing.Color.DarkOrange;
            this.lblWarning.Location = new System.Drawing.Point(12, 250);
            this.lblWarning.MaximumSize = new System.Drawing.Size(360, 0);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(0, 15);
            this.lblWarning.TabIndex = 13;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.ForeColor = System.Drawing.Color.Green;
            this.lblStatus.Location = new System.Drawing.Point(12, 298);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(0, 15);
            this.lblStatus.TabIndex = 14;
            // 
            // SettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(384, 332);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblWarning);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.chkLowLatency);
            this.Controls.Add(this.numBuffer);
            this.Controls.Add(this.lblBuffer);
            this.Controls.Add(this.chkEnabled);
            this.Controls.Add(this.lblVolumeValue);
            this.Controls.Add(this.trackVolume);
            this.Controls.Add(this.lblVolume);
            this.Controls.Add(this.cmbOutputDevice);
            this.Controls.Add(this.lblOutputDevice);
            this.Controls.Add(this.cmbInputDevice);
            this.Controls.Add(this.lblInputDevice);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SettingsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Transparency Mode Settings";
            ((System.ComponentModel.ISupportInitialize)(this.trackVolume)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBuffer)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblInputDevice;
        private System.Windows.Forms.ComboBox cmbInputDevice;
        private System.Windows.Forms.Label lblOutputDevice;
        private System.Windows.Forms.ComboBox cmbOutputDevice;
        private System.Windows.Forms.Label lblVolume;
        private System.Windows.Forms.TrackBar trackVolume;
        private System.Windows.Forms.Label lblVolumeValue;
        private System.Windows.Forms.CheckBox chkEnabled;
        private System.Windows.Forms.Label lblBuffer;
        private System.Windows.Forms.NumericUpDown numBuffer;
        private System.Windows.Forms.CheckBox chkLowLatency;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Label lblWarning;
        private System.Windows.Forms.Label lblStatus;
    }
}
