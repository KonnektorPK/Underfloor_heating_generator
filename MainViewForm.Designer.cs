namespace Underfloor_heating_generator
{
    partial class MainViewForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainViewForm));
            this.PipeSelection = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.PipeDiametr = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.PipeLayingHeight = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.StepLaying = new System.Windows.Forms.ComboBox();
            this.label5 = new System.Windows.Forms.Label();
            this.DistanceFromWall = new System.Windows.Forms.ComboBox();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.SaveChanges = new System.Windows.Forms.CheckBox();
            this.ChangeRoom = new System.Windows.Forms.Button();
            this.DrawBorder = new System.Windows.Forms.Button();
            this.label11 = new System.Windows.Forms.Label();
            this.SystemTypePipe = new System.Windows.Forms.ComboBox();
            this.DistanceFromCollector = new System.Windows.Forms.ComboBox();
            this.SuspendLayout();
            // 
            // PipeSelection
            // 
            this.PipeSelection.BackColor = System.Drawing.SystemColors.Window;
            this.PipeSelection.FormattingEnabled = true;
            this.PipeSelection.Location = new System.Drawing.Point(12, 45);
            this.PipeSelection.Name = "PipeSelection";
            this.PipeSelection.Size = new System.Drawing.Size(460, 28);
            this.PipeSelection.TabIndex = 0;
            this.PipeSelection.SelectedIndexChanged += new System.EventHandler(this.PipeSelection_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(166, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(165, 20);
            this.label1.TabIndex = 1;
            this.label1.Text = "Выберете тип трубы";
            this.label1.Click += new System.EventHandler(this.label1_Click);
            // 
            // PipeDiametr
            // 
            this.PipeDiametr.FormattingEnabled = true;
            this.PipeDiametr.Items.AddRange(new object[] {
            "16",
            "20",
            "25",
            "32"});
            this.PipeDiametr.Location = new System.Drawing.Point(8, 195);
            this.PipeDiametr.Name = "PipeDiametr";
            this.PipeDiametr.Size = new System.Drawing.Size(190, 28);
            this.PipeDiametr.TabIndex = 2;
            this.PipeDiametr.SelectedIndexChanged += new System.EventHandler(this.PipeDiametr_SelectedIndexChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(8, 163);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(190, 20);
            this.label2.TabIndex = 3;
            this.label2.Text = "Размер трубы (сечение)";
            this.label2.Click += new System.EventHandler(this.label2_Click);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(237, 163);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(207, 20);
            this.label3.TabIndex = 5;
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
            this.PipeLayingHeight.Location = new System.Drawing.Point(241, 195);
            this.PipeLayingHeight.Name = "PipeLayingHeight";
            this.PipeLayingHeight.Size = new System.Drawing.Size(190, 28);
            this.PipeLayingHeight.TabIndex = 4;
            this.PipeLayingHeight.SelectedIndexChanged += new System.EventHandler(this.PipeLayingHeight_SelectedIndexChanged);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(237, 236);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(129, 20);
            this.label4.TabIndex = 9;
            this.label4.Text = "Шаг укладки ТП";
            this.label4.Click += new System.EventHandler(this.label4_Click);
            // 
            // StepLaying
            // 
            this.StepLaying.FormattingEnabled = true;
            this.StepLaying.Items.AddRange(new object[] {
            "75",
            "100",
            "150",
            "200",
            "300",
            "400"});
            this.StepLaying.Location = new System.Drawing.Point(241, 270);
            this.StepLaying.Name = "StepLaying";
            this.StepLaying.Size = new System.Drawing.Size(190, 28);
            this.StepLaying.TabIndex = 8;
            this.StepLaying.SelectedIndexChanged += new System.EventHandler(this.StepLaying_SelectedIndexChanged);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(8, 236);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(170, 20);
            this.label5.TabIndex = 7;
            this.label5.Text = "Расстояние от стены";
            this.label5.Click += new System.EventHandler(this.label5_Click);
            // 
            // DistanceFromWall
            // 
            this.DistanceFromWall.FormattingEnabled = true;
            this.DistanceFromWall.Items.AddRange(new object[] {
            "25",
            "50",
            "75",
            "100",
            "150",
            "200"});
            this.DistanceFromWall.Location = new System.Drawing.Point(8, 270);
            this.DistanceFromWall.Name = "DistanceFromWall";
            this.DistanceFromWall.Size = new System.Drawing.Size(190, 28);
            this.DistanceFromWall.TabIndex = 6;
            this.DistanceFromWall.SelectedIndexChanged += new System.EventHandler(this.DistanceFromWall_SelectedIndexChanged);
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(135, 323);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(214, 20);
            this.label6.TabIndex = 11;
            this.label6.Text = "Расстояния от коллектора";
            this.label6.Click += new System.EventHandler(this.label6_Click);
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(204, 278);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(31, 20);
            this.label7.TabIndex = 12;
            this.label7.Text = "мм";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(437, 278);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(31, 20);
            this.label8.TabIndex = 13;
            this.label8.Text = "мм";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(437, 203);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(31, 20);
            this.label9.TabIndex = 15;
            this.label9.Text = "мм";
            // 
            // label10
            // 
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(204, 203);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(31, 20);
            this.label10.TabIndex = 14;
            this.label10.Text = "мм";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(437, 356);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(31, 20);
            this.label12.TabIndex = 17;
            this.label12.Text = "мм";
            // 
            // SaveChanges
            // 
            this.SaveChanges.AutoSize = true;
            this.SaveChanges.Checked = true;
            this.SaveChanges.CheckState = System.Windows.Forms.CheckState.Checked;
            this.SaveChanges.Location = new System.Drawing.Point(8, 398);
            this.SaveChanges.Name = "SaveChanges";
            this.SaveChanges.Size = new System.Drawing.Size(388, 24);
            this.SaveChanges.TabIndex = 18;
            this.SaveChanges.Text = "Сохранить текущие значения при перезапуске";
            this.SaveChanges.UseVisualStyleBackColor = true;
            this.SaveChanges.CheckedChanged += new System.EventHandler(this.SaveChanges_CheckedChanged);
            // 
            // ChangeRoom
            // 
            this.ChangeRoom.Location = new System.Drawing.Point(8, 443);
            this.ChangeRoom.Name = "ChangeRoom";
            this.ChangeRoom.Size = new System.Drawing.Size(186, 29);
            this.ChangeRoom.TabIndex = 19;
            this.ChangeRoom.Text = "Выбрать помещение";
            this.ChangeRoom.UseVisualStyleBackColor = true;
            this.ChangeRoom.Click += new System.EventHandler(this.ChangeRoom_Click);
            // 
            // DrawBorder
            // 
            this.DrawBorder.Location = new System.Drawing.Point(278, 443);
            this.DrawBorder.Name = "DrawBorder";
            this.DrawBorder.Size = new System.Drawing.Size(190, 29);
            this.DrawBorder.TabIndex = 20;
            this.DrawBorder.Text = "Нарисовать границу";
            this.DrawBorder.UseVisualStyleBackColor = true;
            this.DrawBorder.Click += new System.EventHandler(this.DrawBorder_Click);
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(166, 87);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(165, 20);
            this.label11.TabIndex = 22;
            this.label11.Text = "Выберете тип трубы";
            this.label11.Click += new System.EventHandler(this.label11_Click);
            // 
            // SystemTypePipe
            // 
            this.SystemTypePipe.BackColor = System.Drawing.SystemColors.Window;
            this.SystemTypePipe.FormattingEnabled = true;
            this.SystemTypePipe.Location = new System.Drawing.Point(12, 123);
            this.SystemTypePipe.Name = "SystemTypePipe";
            this.SystemTypePipe.Size = new System.Drawing.Size(460, 28);
            this.SystemTypePipe.TabIndex = 21;
            this.SystemTypePipe.SelectedIndexChanged += new System.EventHandler(this.SystemTypePipe_SelectedIndexChanged);
            // 
            // DistanceFromCollector
            // 
            this.DistanceFromCollector.FormattingEnabled = true;
            this.DistanceFromCollector.Items.AddRange(new object[] {
            "1000",
            "2000",
            "5000",
            "7500",
            "10000",
            "12500",
            "15000",
            "17500",
            "20000",
            "25000",
            "30000",
            "35000",
            "40000",
            "45000",
            "50000"});
            this.DistanceFromCollector.Location = new System.Drawing.Point(8, 348);
            this.DistanceFromCollector.Name = "DistanceFromCollector";
            this.DistanceFromCollector.Size = new System.Drawing.Size(423, 28);
            this.DistanceFromCollector.TabIndex = 10;
            this.DistanceFromCollector.SelectedIndexChanged += new System.EventHandler(this.DistanceFromCollector_SelectedIndexChanged);
            // 
            // MainViewForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(478, 488);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.SystemTypePipe);
            this.Controls.Add(this.DrawBorder);
            this.Controls.Add(this.ChangeRoom);
            this.Controls.Add(this.SaveChanges);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.DistanceFromCollector);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.StepLaying);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.DistanceFromWall);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.PipeLayingHeight);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.PipeDiametr);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.PipeSelection);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainViewForm";
            this.Text = "Ввод данных теплого пола";
            this.Load += new System.EventHandler(this.MainViewForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox PipeSelection;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox PipeDiametr;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox PipeLayingHeight;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ComboBox StepLaying;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox DistanceFromWall;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.CheckBox SaveChanges;
        private System.Windows.Forms.Button ChangeRoom;
        private System.Windows.Forms.Button DrawBorder;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.ComboBox SystemTypePipe;
        private System.Windows.Forms.ComboBox DistanceFromCollector;
    }
}