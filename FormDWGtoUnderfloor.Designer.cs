namespace Underfloor_heating_generator
{
    partial class FormDWGtoUnderfloor
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormDWGtoUnderfloor));
            this.label11 = new System.Windows.Forms.Label();
            this.SystemTypePipe = new System.Windows.Forms.ComboBox();
            this.CreateWarmFloor = new System.Windows.Forms.Button();
            this.SaveChanges = new System.Windows.Forms.CheckBox();
            this.label9 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.PipeLayingHeight = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.PipeDiametr = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.PipeSelection = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.SelectDrawingDWG = new System.Windows.Forms.ComboBox();
            this.label5 = new System.Windows.Forms.Label();
            this.SelectLayerDWG = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(165, 87);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(165, 20);
            this.label11.TabIndex = 34;
            this.label11.Text = "Выберете тип трубы";
            // 
            // SystemTypePipe
            // 
            this.SystemTypePipe.BackColor = System.Drawing.SystemColors.Window;
            this.SystemTypePipe.FormattingEnabled = true;
            this.SystemTypePipe.Location = new System.Drawing.Point(11, 123);
            this.SystemTypePipe.Name = "SystemTypePipe";
            this.SystemTypePipe.Size = new System.Drawing.Size(460, 28);
            this.SystemTypePipe.TabIndex = 33;
            this.SystemTypePipe.SelectedIndexChanged += new System.EventHandler(this.SystemTypePipe_SelectedIndexChanged);
            // 
            // CreateWarmFloor
            // 
            this.CreateWarmFloor.Location = new System.Drawing.Point(11, 422);
            this.CreateWarmFloor.Name = "CreateWarmFloor";
            this.CreateWarmFloor.Size = new System.Drawing.Size(460, 29);
            this.CreateWarmFloor.TabIndex = 32;
            this.CreateWarmFloor.Text = "Построить теплый пол";
            this.CreateWarmFloor.UseVisualStyleBackColor = true;
            // 
            // SaveChanges
            // 
            this.SaveChanges.AutoSize = true;
            this.SaveChanges.Checked = true;
            this.SaveChanges.CheckState = System.Windows.Forms.CheckState.Checked;
            this.SaveChanges.Location = new System.Drawing.Point(11, 388);
            this.SaveChanges.Name = "SaveChanges";
            this.SaveChanges.Size = new System.Drawing.Size(388, 24);
            this.SaveChanges.TabIndex = 31;
            this.SaveChanges.Text = "Сохранить текущие значения при перезапуске";
            this.SaveChanges.UseVisualStyleBackColor = true;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(440, 347);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(31, 20);
            this.label9.TabIndex = 30;
            this.label9.Text = "мм";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(207, 347);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(31, 20);
            this.label10.TabIndex = 29;
            this.label10.Text = "мм";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(240, 307);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(207, 20);
            this.label3.TabIndex = 28;
            this.label3.Text = "Высота от у.ч.п до центра";
            // 
            // PipeLayingHeight
            // 
            this.PipeLayingHeight.FormattingEnabled = true;
            this.PipeLayingHeight.Items.AddRange(new object[] {
            "0",
            "-20",
            "-30",
            "-45",
            "-77",
            "-90",
            "-100",
            "-110",
            "-125"});
            this.PipeLayingHeight.Location = new System.Drawing.Point(244, 339);
            this.PipeLayingHeight.Name = "PipeLayingHeight";
            this.PipeLayingHeight.Size = new System.Drawing.Size(190, 28);
            this.PipeLayingHeight.TabIndex = 27;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(11, 307);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(190, 20);
            this.label2.TabIndex = 26;
            this.label2.Text = "Размер трубы (сечение)";
            // 
            // PipeDiametr
            // 
            this.PipeDiametr.FormattingEnabled = true;
            this.PipeDiametr.Items.AddRange(new object[] {
            "16",
            "20",
            "25",
            "32"});
            this.PipeDiametr.Location = new System.Drawing.Point(11, 339);
            this.PipeDiametr.Name = "PipeDiametr";
            this.PipeDiametr.Size = new System.Drawing.Size(190, 28);
            this.PipeDiametr.TabIndex = 25;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(165, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(165, 20);
            this.label1.TabIndex = 24;
            this.label1.Text = "Выберете тип трубы";
            // 
            // PipeSelection
            // 
            this.PipeSelection.BackColor = System.Drawing.SystemColors.Window;
            this.PipeSelection.FormattingEnabled = true;
            this.PipeSelection.Location = new System.Drawing.Point(11, 45);
            this.PipeSelection.Name = "PipeSelection";
            this.PipeSelection.Size = new System.Drawing.Size(460, 28);
            this.PipeSelection.TabIndex = 23;
            this.PipeSelection.SelectedIndexChanged += new System.EventHandler(this.PipeSelection_SelectedIndexChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(13, 166);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(458, 20);
            this.label4.TabIndex = 36;
            this.label4.Text = "Выберете чертеж по которому будет строится теплый пол";
            // 
            // SelectDrawingDWG
            // 
            this.SelectDrawingDWG.BackColor = System.Drawing.SystemColors.Window;
            this.SelectDrawingDWG.FormattingEnabled = true;
            this.SelectDrawingDWG.Location = new System.Drawing.Point(11, 198);
            this.SelectDrawingDWG.Name = "SelectDrawingDWG";
            this.SelectDrawingDWG.Size = new System.Drawing.Size(460, 28);
            this.SelectDrawingDWG.TabIndex = 35;
            this.SelectDrawingDWG.SelectedIndexChanged += new System.EventHandler(this.SelectDrawingDWG_SelectedIndexChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(138, 238);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(216, 20);
            this.label5.TabIndex = 38;
            this.label5.Text = "Выберете слой на чертеже";
            // 
            // SelectLayerDWG
            // 
            this.SelectLayerDWG.BackColor = System.Drawing.SystemColors.Window;
            this.SelectLayerDWG.FormattingEnabled = true;
            this.SelectLayerDWG.Location = new System.Drawing.Point(11, 269);
            this.SelectLayerDWG.Name = "SelectLayerDWG";
            this.SelectLayerDWG.Size = new System.Drawing.Size(460, 28);
            this.SelectLayerDWG.TabIndex = 37;
            this.SelectLayerDWG.SelectedIndexChanged += new System.EventHandler(this.SelectLayerDWG_SelectedIndexChanged);
            // 
            // FormDWGtoUnderfloor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(478, 464);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.SelectLayerDWG);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.SelectDrawingDWG);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.SystemTypePipe);
            this.Controls.Add(this.CreateWarmFloor);
            this.Controls.Add(this.SaveChanges);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.PipeLayingHeight);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.PipeDiametr);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.PipeSelection);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FormDWGtoUnderfloor";
            this.Text = "DWG в теплый пол";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.ComboBox SystemTypePipe;
        private System.Windows.Forms.Button CreateWarmFloor;
        private System.Windows.Forms.CheckBox SaveChanges;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox PipeLayingHeight;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ComboBox PipeDiametr;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox PipeSelection;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ComboBox SelectDrawingDWG;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox SelectLayerDWG;
    }
}