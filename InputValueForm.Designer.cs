namespace VibrationAnalysis.UI
{
    partial class InputValueForm
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
            lblError = new System.Windows.Forms.Label();
            lblRange = new System.Windows.Forms.Label();
            nudValue = new System.Windows.Forms.NumericUpDown();
            btnAccept = new System.Windows.Forms.Button();
            btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)nudValue).BeginInit();
            SuspendLayout();
            // 
            // lblError
            // 
            lblError.AutoSize = true;
            lblError.Location = new System.Drawing.Point(12, 9);
            lblError.MaximumSize = new System.Drawing.Size(360, 0);
            lblError.Name = "lblError";
            lblError.Size = new System.Drawing.Size(32, 15);
            lblError.TabIndex = 0;
            lblError.Text = "Error";
            // 
            // lblRange
            // 
            lblRange.AutoSize = true;
            lblRange.Location = new System.Drawing.Point(12, 39);
            lblRange.Name = "lblRange";
            lblRange.Size = new System.Drawing.Size(41, 15);
            lblRange.TabIndex = 1;
            lblRange.Text = "Rango";
            // 
            // nudValue
            // 
            nudValue.DecimalPlaces = 3;
            nudValue.Increment = new decimal(new int[] { 1, 0, 0, 196608 });
            nudValue.Location = new System.Drawing.Point(12, 59);
            nudValue.Name = "nudValue";
            nudValue.Size = new System.Drawing.Size(150, 23);
            nudValue.TabIndex = 2;
            // 
            // btnAccept
            // 
            btnAccept.Location = new System.Drawing.Point(172, 59);
            btnAccept.Name = "btnAccept";
            btnAccept.Size = new System.Drawing.Size(80, 23);
            btnAccept.TabIndex = 3;
            btnAccept.Text = "Aceptar";
            btnAccept.UseVisualStyleBackColor = true;
            btnAccept.Click += BtnAccept_Click;
            // 
            // btnCancel
            // 
            btnCancel.Location = new System.Drawing.Point(262, 59);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new System.Drawing.Size(80, 23);
            btnCancel.TabIndex = 4;
            btnCancel.Text = "Cancelar";
            btnCancel.UseVisualStyleBackColor = true;
            btnCancel.Click += BtnCancel_Click;
            // 
            // InputValueForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(348, 107);
            Controls.Add(btnCancel);
            Controls.Add(btnAccept);
            Controls.Add(nudValue);
            Controls.Add(lblRange);
            Controls.Add(lblError);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "InputValueForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Corregir Valor";
            ((System.ComponentModel.ISupportInitialize)nudValue).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblError;
        private System.Windows.Forms.Label lblRange;
        private System.Windows.Forms.NumericUpDown nudValue;
        private System.Windows.Forms.Button btnAccept;
        private System.Windows.Forms.Button btnCancel;
    }
}