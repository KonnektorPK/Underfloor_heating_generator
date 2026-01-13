using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;
using RevitView = Autodesk.Revit.DB.View;
using Autodesk.Revit.DB.Plumbing;

namespace Underfloor_heating_generator
{
    public partial class MainViewForm : System.Windows.Forms.Form
    {
        private PipeTypeService pipeTypeService;
        private UserFormParametrs userParametrs;
        private DwgConverter dwgConverter;

        public MainViewForm(
            PipeTypeService _pipeTypeService, 
            UserFormParametrs _userParametrs,
            DwgConverter _dwgConverter)
        {
            InitializeComponent();
            pipeTypeService = _pipeTypeService;
            userParametrs = _userParametrs;
            dwgConverter = _dwgConverter;

            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
        }

        private void MainViewForm_Load(object sender, EventArgs e)
        {
            SystemTypePipe.DropDownStyle = ComboBoxStyle.DropDownList;
            PipeSelection.DropDownStyle = ComboBoxStyle.DropDownList;

            PipeTypes_load_in_ComboBox(); //загрузка типов труб в ComboBox
            SystemTypePipe_LoadInCombobox();

            PipeSelection.SelectedIndex = 1;
            SystemTypePipe.SelectedIndex = 0;
            PipeDiametr.SelectedIndex = 0;
            PipeLayingHeight.SelectedIndex = 1;
            DistanceFromWall.SelectedIndex = 0;
            StepLaying.SelectedIndex = 0;
            DistanceFromCollector.SelectedIndex = 0;
        }

        private void PipeSelection_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (PipeSelection.SelectedItem != null)
                PipeSelection.BackColor = System.Drawing.Color.White;
        }

        private void PipeDiametr_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (PipeDiametr.SelectedItem != null)
                PipeDiametr.BackColor = System.Drawing.Color.White;
        }

        private void PipeLayingHeight_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (PipeLayingHeight.SelectedItem != null)
                PipeLayingHeight.BackColor = System.Drawing.Color.White;
        }

        private void DistanceFromWall_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (DistanceFromWall.SelectedItem != null)
                DistanceFromWall.BackColor = System.Drawing.Color.White;
        }

        private void StepLaying_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (StepLaying.SelectedItem != null)
                StepLaying.BackColor = System.Drawing.Color.White;
        }

        private void DistanceFromCollector_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (DistanceFromCollector.SelectedItem != null)
                DistanceFromCollector.BackColor = System.Drawing.Color.White;
        }

        private void SystemTypePipe_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SystemTypePipe.SelectedItem != null)
                SystemTypePipe.BackColor = System.Drawing.Color.White;
        }
        private void ChangeRoom_Click(object sender, EventArgs e)
        {
            if (CheckParameters())
            {
                try
                {
                    userParametrs.SetParametrs(
                        pipeTypeService.GetPipeType(PipeSelection.SelectedIndex),
                        pipeTypeService.GetSystemTypePipe(SystemTypePipe.SelectedIndex),
                        Convert.ToDouble(PipeDiametr.Text),
                        Convert.ToDouble(PipeLayingHeight.Text),
                        Convert.ToDouble(DistanceFromWall.Text),
                        Convert.ToDouble(StepLaying.Text),
                        Convert.ToDouble(DistanceFromCollector.Text));
                }
                catch (FormatException ex)
                {
                    MessageBox.Show($"Ошибка ввода числа: {ex.Message}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }

                ImportInstance instance = dwgConverter.PickDwgFile();
                IList<XYZ> points = dwgConverter.GetVerticesFromDwg(instance);
                
                //points = points.Select(p => new XYZ(p.X * 100, p.Y * 100, p.Z)).ToList();


                //IList<XYZ> points = new List<XYZ>() { new XYZ(0, 0, 0), new XYZ(10, 250, 0), new XYZ(20, 500, 0)};
                pipeTypeService.DrawPipes(points, userParametrs.PipeDiametr, userParametrs.PipeLayingHeight, userParametrs.PipeType.Id, userParametrs.GetLevelId(), userParametrs.SystemTypePipe.Id);
            }
            else
            {
                MessageBox.Show("Проверьте введенные значения", "Обнаружена ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DrawBorder_Click(object sender, EventArgs e)
        {
            
        }
        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void label6_Click(object sender, EventArgs e)
        {

        }
        private void label11_Click(object sender, EventArgs e)
        {

        }

        private void SaveChanges_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void PipeTypes_load_in_ComboBox()
        {
            PipeSelection.Items.Clear();

            foreach (Element element in pipeTypeService.GetPipeTypes())
            {
                PipeSelection.Items.Add(element.Name);
            }
        }

        private void SystemTypePipe_LoadInCombobox()
        {
            SystemTypePipe.Items.Clear();

            foreach(Element element in pipeTypeService.GetSystemTypesPipe())
            {
                SystemTypePipe.Items.Add(element.Name);
            }
        }
        private void ResetColor()
        {
            PipeDiametr.BackColor = System.Drawing.Color.White;
            PipeLayingHeight.BackColor = System.Drawing.Color.White;
            DistanceFromWall.BackColor = System.Drawing.Color.White;
            StepLaying.BackColor = System.Drawing.Color.White;
            SystemTypePipe.BackColor = System.Drawing.Color.White;
        }

        private bool CheckParameters()
        {
            bool isValid = true;

            ResetColor();

            if (SystemTypePipe.Text == null || string.IsNullOrEmpty(SystemTypePipe.Text.ToString()))
            {
                SystemTypePipe.BackColor = System.Drawing.Color.Orange;
                isValid = false;
            }

            if (PipeSelection.SelectedItem == null || string.IsNullOrEmpty(PipeSelection.SelectedItem.ToString()))
            {
                PipeSelection.BackColor = System.Drawing.Color.Orange;
                isValid = false;
            }

            if (PipeDiametr.Text == null || string.IsNullOrEmpty(PipeDiametr.Text.ToString()))
            {
                PipeDiametr.BackColor = System.Drawing.Color.Orange;
                isValid = false;
            }
            else if (!double.TryParse(PipeDiametr.Text.ToString(), out double diameter) || diameter <= 0)
            {
                PipeDiametr.BackColor = System.Drawing.Color.Red;
                isValid = false;
            }

            if (PipeLayingHeight.Text == null || string.IsNullOrEmpty(PipeLayingHeight.Text.ToString()))
            {
                PipeLayingHeight.BackColor = System.Drawing.Color.Orange;
                isValid = false;
            }
            else if (!double.TryParse(PipeLayingHeight.Text.ToString(), out double layingHeight))
            {
                PipeLayingHeight.BackColor = System.Drawing.Color.Red;
                isValid = false;
            }

            if (DistanceFromWall.Text == null || string.IsNullOrEmpty(DistanceFromWall.Text.ToString()))
            {
                DistanceFromWall.BackColor = System.Drawing.Color.Orange;
                isValid = false;
            }
            else if (!double.TryParse(DistanceFromWall.Text.ToString(), out double wallDistance) || wallDistance < 0)
            {
                DistanceFromWall.BackColor = System.Drawing.Color.Red;
                isValid = false;
            }

            if (StepLaying.Text == null || string.IsNullOrEmpty(StepLaying.Text.ToString()))
            {
                StepLaying.BackColor = System.Drawing.Color.Orange;
                isValid = false;
            }
            else if (!double.TryParse(StepLaying.Text.ToString(), out double step) || step <= 0)
            {
                StepLaying.BackColor = System.Drawing.Color.Red;
                isValid = false;
            }

            if (DistanceFromCollector.Text == null || string.IsNullOrEmpty(DistanceFromCollector.Text.ToString()))
            {
                DistanceFromCollector.BackColor = System.Drawing.Color.Orange;
                isValid = false;
            }
            else if (!double.TryParse(DistanceFromCollector.Text.ToString(), out double collectorDistance) || collectorDistance < 0)
            {
                DistanceFromCollector.BackColor = System.Drawing.Color.Red;
                isValid = false;
            }

            return isValid;
        }
    }
}

