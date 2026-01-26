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
            this.SystemTypePipe_label = new System.Windows.Forms.Label();
            this.SystemTypePipe = new System.Windows.Forms.ComboBox();
            this.CreateWarmFloor = new System.Windows.Forms.Button();
            this.SaveChanges = new System.Windows.Forms.CheckBox();
            this.label9 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.PipeLayingHeight_label = new System.Windows.Forms.Label();
            this.PipeLayingHeight = new System.Windows.Forms.ComboBox();
            this.PipeDiametr_label = new System.Windows.Forms.Label();
            this.PipeDiametr = new System.Windows.Forms.ComboBox();
            this.PipeSelection_label = new System.Windows.Forms.Label();
            this.PipeSelection = new System.Windows.Forms.ComboBox();
            this.SelectDrawingDWG_label = new System.Windows.Forms.Label();
            this.SelectDrawingDWG = new System.Windows.Forms.ComboBox();
            this.label5 = new System.Windows.Forms.Label();
            this.SelectLayerDWG = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // SystemTypePipe_label
            // 
            this.SystemTypePipe_label.AutoSize = true;
            this.SystemTypePipe_label.Location = new System.Drawing.Point(130, 88);
            this.SystemTypePipe_label.Name = "SystemTypePipe_label";
            this.SystemTypePipe_label.Size = new System.Drawing.Size(234, 20);
            this.SystemTypePipe_label.TabIndex = 34;
            this.SystemTypePipe_label.Text = "Выберете тип системы трубы";
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
            this.CreateWarmFloor.Location = new System.Drawing.Point(11, 334);
            this.CreateWarmFloor.Name = "CreateWarmFloor";
            this.CreateWarmFloor.Size = new System.Drawing.Size(460, 29);
            this.CreateWarmFloor.TabIndex = 32;
            this.CreateWarmFloor.Text = "Построить теплый пол";
            this.CreateWarmFloor.UseVisualStyleBackColor = true;
            this.CreateWarmFloor.Click += new System.EventHandler(this.CreateWarmFloor_Click);
            // 
            // SaveChanges
            // 
            this.SaveChanges.AutoSize = true;
            this.SaveChanges.Checked = true;
            this.SaveChanges.CheckState = System.Windows.Forms.CheckState.Checked;
            this.SaveChanges.Location = new System.Drawing.Point(11, 304);
            this.SaveChanges.Name = "SaveChanges";
            this.SaveChanges.Size = new System.Drawing.Size(388, 24);
            this.SaveChanges.TabIndex = 31;
            this.SaveChanges.Text = "Сохранить текущие значения при перезапуске";
            this.SaveChanges.UseVisualStyleBackColor = true;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(440, 278);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(31, 20);
            this.label9.TabIndex = 30;
            this.label9.Text = "мм";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(207, 278);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(31, 20);
            this.label10.TabIndex = 29;
            this.label10.Text = "мм";
            // 
            // PipeLayingHeight_label
            // 
            this.PipeLayingHeight_label.AutoSize = true;
            this.PipeLayingHeight_label.Location = new System.Drawing.Point(240, 238);
            this.PipeLayingHeight_label.Name = "PipeLayingHeight_label";
            this.PipeLayingHeight_label.Size = new System.Drawing.Size(207, 20);
            this.PipeLayingHeight_label.TabIndex = 28;
            this.PipeLayingHeight_label.Text = "Высота от у.ч.п до центра";
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
            this.PipeLayingHeight.Location = new System.Drawing.Point(244, 270);
            this.PipeLayingHeight.Name = "PipeLayingHeight";
            this.PipeLayingHeight.Size = new System.Drawing.Size(190, 28);
            this.PipeLayingHeight.TabIndex = 27;
            // 
            // PipeDiametr_label
            // 
            this.PipeDiametr_label.AutoSize = true;
            this.PipeDiametr_label.Location = new System.Drawing.Point(11, 238);
            this.PipeDiametr_label.Name = "PipeDiametr_label";
            this.PipeDiametr_label.Size = new System.Drawing.Size(190, 20);
            this.PipeDiametr_label.TabIndex = 26;
            this.PipeDiametr_label.Text = "Размер трубы (сечение)";
            // 
            // PipeDiametr
            // 
            this.PipeDiametr.FormattingEnabled = true;
            this.PipeDiametr.Items.AddRange(new object[] {
            "16",
            "20",
            "25",
            "32"});
            this.PipeDiametr.Location = new System.Drawing.Point(11, 270);
            this.PipeDiametr.Name = "PipeDiametr";
            this.PipeDiametr.Size = new System.Drawing.Size(190, 28);
            this.PipeDiametr.TabIndex = 25;
            // 
            // PipeSelection_label
            // 
            this.PipeSelection_label.AutoSize = true;
            this.PipeSelection_label.Location = new System.Drawing.Point(179, 9);
            this.PipeSelection_label.Name = "PipeSelection_label";
            this.PipeSelection_label.Size = new System.Drawing.Size(130, 20);
            this.PipeSelection_label.TabIndex = 24;
            this.PipeSelection_label.Text = "Выберете трубу";
            this.PipeSelection_label.Click += new System.EventHandler(this.PipeSelection_label_Click);
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
            // SelectDrawingDWG_label
            // 
            this.SelectDrawingDWG_label.AutoSize = true;
            this.SelectDrawingDWG_label.Location = new System.Drawing.Point(105, 164);
            this.SelectDrawingDWG_label.Name = "SelectDrawingDWG_label";
            this.SelectDrawingDWG_label.Size = new System.Drawing.Size(285, 20);
            this.SelectDrawingDWG_label.TabIndex = 36;
            this.SelectDrawingDWG_label.Text = "Выберете чертеж на данном уровне";
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
            this.label5.Location = new System.Drawing.Point(138, 239);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(216, 20);
            this.label5.TabIndex = 42;
            this.label5.Text = "Выберете слой на чертеже";
            this.label5.Visible = false;
            // 
            // SelectLayerDWG
            // 
            this.SelectLayerDWG.BackColor = System.Drawing.SystemColors.Window;
            this.SelectLayerDWG.FormattingEnabled = true;
            this.SelectLayerDWG.Location = new System.Drawing.Point(11, 270);
            this.SelectLayerDWG.Name = "SelectLayerDWG";
            this.SelectLayerDWG.Size = new System.Drawing.Size(460, 28);
            this.SelectLayerDWG.TabIndex = 41;
            this.SelectLayerDWG.Visible = false;
            // 
            // FormDWGtoUnderfloor
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(478, 374);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.SelectLayerDWG);
            this.Controls.Add(this.SelectDrawingDWG_label);
            this.Controls.Add(this.SelectDrawingDWG);
            this.Controls.Add(this.SystemTypePipe_label);
            this.Controls.Add(this.SystemTypePipe);
            this.Controls.Add(this.CreateWarmFloor);
            this.Controls.Add(this.SaveChanges);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.PipeLayingHeight_label);
            this.Controls.Add(this.PipeLayingHeight);
            this.Controls.Add(this.PipeDiametr_label);
            this.Controls.Add(this.PipeDiametr);
            this.Controls.Add(this.PipeSelection_label);
            this.Controls.Add(this.PipeSelection);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FormDWGtoUnderfloor";
            this.Text = "DWG в теплый пол";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label SystemTypePipe_label;
        private System.Windows.Forms.ComboBox SystemTypePipe;
        private System.Windows.Forms.Button CreateWarmFloor;
        private System.Windows.Forms.CheckBox SaveChanges;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Label PipeLayingHeight_label;
        private System.Windows.Forms.ComboBox PipeLayingHeight;
        private System.Windows.Forms.Label PipeDiametr_label;
        private System.Windows.Forms.ComboBox PipeDiametr;
        private System.Windows.Forms.ComboBox PipeSelection;
        private System.Windows.Forms.Label SelectDrawingDWG_label;
        private System.Windows.Forms.ComboBox SelectDrawingDWG;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox SelectLayerDWG;
        private System.Windows.Forms.Label PipeSelection_label;
    }
}