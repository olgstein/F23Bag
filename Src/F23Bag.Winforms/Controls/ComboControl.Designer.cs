﻿namespace F23Bag.Winforms.Controls
{
    partial class ComboControl
    {
        /// <summary> 
        /// Variable nécessaire au concepteur.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Nettoyage des ressources utilisées.
        /// </summary>
        /// <param name="disposing">true si les ressources managées doivent être supprimées ; sinon, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Code généré par le Concepteur de composants

        /// <summary> 
        /// Méthode requise pour la prise en charge du concepteur - ne modifiez pas 
        /// le contenu de cette méthode avec l'éditeur de code.
        /// </summary>
        private void InitializeComponent()
        {
            this.cbValue = new System.Windows.Forms.ComboBox();
            this.lblLabel = new System.Windows.Forms.Label();
            this._validationIcon = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this._validationIcon)).BeginInit();
            this.SuspendLayout();
            // 
            // cbValue
            // 
            this.cbValue.FormattingEnabled = true;
            this.cbValue.Location = new System.Drawing.Point(210, 4);
            this.cbValue.Name = "cbValue";
            this.cbValue.Size = new System.Drawing.Size(300, 21);
            this.cbValue.TabIndex = 4;
            // 
            // lblLabel
            // 
            this.lblLabel.Location = new System.Drawing.Point(4, 4);
            this.lblLabel.Name = "lblLabel";
            this.lblLabel.Size = new System.Drawing.Size(200, 23);
            this.lblLabel.TabIndex = 3;
            this.lblLabel.Text = ".....";
            // 
            // _validationIcon
            // 
            this._validationIcon.Location = new System.Drawing.Point(516, 6);
            this._validationIcon.Name = "_validationIcon";
            this._validationIcon.Size = new System.Drawing.Size(16, 16);
            this._validationIcon.TabIndex = 5;
            this._validationIcon.TabStop = false;
            // 
            // ComboControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._validationIcon);
            this.Controls.Add(this.cbValue);
            this.Controls.Add(this.lblLabel);
            this.Name = "ComboControl";
            this.Size = new System.Drawing.Size(540, 32);
            ((System.ComponentModel.ISupportInitialize)(this._validationIcon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox cbValue;
        private System.Windows.Forms.Label lblLabel;
        private System.Windows.Forms.PictureBox _validationIcon;
    }
}