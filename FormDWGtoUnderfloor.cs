using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Menu;

namespace Underfloor_heating_generator
{
    public partial class FormDWGtoUnderfloor : System.Windows.Forms.Form
    {
        private PipeTypeService pipeTypeService;
        private UserFormParametrs userParametrs;
        private DwgConverter dwgConverter;

        public FormDWGtoUnderfloor(
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

            ElementId LevelID = userParametrs.GetLevelId();
            if (LevelID == null)
            {
                this.Shown += (sender, e) => this.Close();
            }
            else
            {
                LoadComboBox(PipeSelection, true, () => pipeTypeService.GetPipeTypes());
                LoadComboBox(SystemTypePipe, true, () => pipeTypeService.GetSystemTypesPipe());
                LoadComboBox(SelectDrawingDWG, true, () => dwgConverter.GetDWGDrawings(LevelID));
            }

            //PipeSelection.SelectedIndex = 1;
            //SystemTypePipe.SelectedIndex = 0;
            //SelectDrawingDWG.SelectedIndex = 0;
            //PipeLayingHeight.SelectedIndex = 0;
            //PipeDiametr.SelectedIndex = 0;
        }

        private void LoadComboBox(System.Windows.Forms.ComboBox boxItem, bool allowUserInput, Func<IList<Element>> getElement)
        {
            boxItem.Items.Clear();

            foreach (Element element in getElement())
            {
                boxItem.Items.Add(
                    element is ImportInstance importInstance
                    ? importInstance.get_Parameter(BuiltInParameter.IMPORT_SYMBOL_NAME).AsString()
                    : element.Name
                );
            }

            if (allowUserInput) boxItem.DropDownStyle = ComboBoxStyle.DropDownList;
            if (boxItem.Items.Count == 1) boxItem.SelectedIndex = 0;
        }

        private void CreateWarmFloor_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.ComboBox[] comboBoxesIsNotNull = new System.Windows.Forms.ComboBox[] 
            {
                PipeSelection,
                SystemTypePipe,
                SelectDrawingDWG,
                PipeLayingHeight,
                PipeDiametr
            };

            System.Windows.Forms.ComboBox[] comboBoxesIsNumber = new System.Windows.Forms.ComboBox[]
            {
                PipeLayingHeight,
                PipeDiametr
            };

            System.Windows.Forms.ComboBox[] comboBoxesIsNegativeNumber = new System.Windows.Forms.ComboBox[]
            {
                PipeDiametr
            };

            if (CheckNullParamets(comboBoxesIsNotNull) 
                && CheckForNumber(comboBoxesIsNumber) 
                && CheckIsNegativeNumber(comboBoxesIsNegativeNumber))
            {
                Element pipeType = pipeTypeService.GetPipeType(PipeSelection.SelectedIndex);
                Element pipeSystemType = pipeTypeService.GetSystemTypePipe(SystemTypePipe.SelectedIndex);
                ImportInstance dwg = dwgConverter.GetDWGDrawings(SelectDrawingDWG.SelectedIndex);
                ElementId LevelID = userParametrs.GetLevelId();

                double pipeDiametr = Convert.ToDouble(PipeDiametr.Text);
                double pipeHeightLayer = Convert.ToDouble(PipeLayingHeight.Text);

                IList<IList<XYZ>> splinePoints = dwgConverter.GetListVerticesFromDwg(dwg);

                //Stopwatch stopwatch = new Stopwatch();
                //stopwatch.Start();
                foreach (IList<XYZ> spline in splinePoints)
                {
                    pipeTypeService.DrawPipes(spline, pipeDiametr, pipeHeightLayer, pipeType.Id, LevelID, pipeSystemType.Id);
                }
                //stopwatch.Stop();

                //System.Windows.MessageBox.Show($"Время выполнения: {stopwatch.ElapsedMilliseconds} мс");
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("Ошибка валидации данных, проверьте значения!",
                    "Обнаружена ошибка!",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void PipeSelection_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void SystemTypePipe_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void SelectDrawingDWG_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void SelectLayerDWG_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void PipeSelection_label_Click(object sender, EventArgs e)
        {

        }

        private bool CheckNullParamets(System.Windows.Forms.ComboBox[] boxItems)
        {
            bool result = true;

            foreach (var item in boxItems)
            {
                if (item.Text == null || string.IsNullOrEmpty(item.Text.ToString()))
                {
                    string labelName = item.Name + "_label";
                    System.Windows.Forms.Label label = this.Controls.Find(labelName, true)[0] as System.Windows.Forms.Label;

                    label.BackColor = System.Drawing.Color.Orange;

                    result = false;
                }
                else
                {
                    string labelName = item.Name + "_label";
                    System.Windows.Forms.Label label = this.Controls.Find(labelName, true)[0] as System.Windows.Forms.Label;

                    label.BackColor = System.Drawing.SystemColors.Control;
                }
            }

            return result;
        }

        private bool CheckForNumber(System.Windows.Forms.ComboBox[] boxItems)
        {
            bool result = true;

            foreach (var item in boxItems)
            {
                if (!double.TryParse(item.Text.ToString(), out double diameter))
                {
                    string labelName = item.Name + "_label";
                    System.Windows.Forms.Label label = this.Controls.Find(labelName, true)[0] as System.Windows.Forms.Label;

                    label.BackColor = System.Drawing.Color.Orange;

                    result = false;
                }
                else
                {
                    string labelName = item.Name + "_label";
                    System.Windows.Forms.Label label = this.Controls.Find(labelName, true)[0] as System.Windows.Forms.Label;

                    label.BackColor = System.Drawing.SystemColors.Control;
                }
            }

            return result;
        }

        private bool CheckIsNegativeNumber(System.Windows.Forms.ComboBox[] boxItems)
        {
            bool result = true;

            foreach (var item in boxItems)
            {
                if (!double.TryParse(item.Text.ToString(), out double diameter) || diameter <= 0)
                {
                    string labelName = item.Name + "_label";
                    System.Windows.Forms.Label label = this.Controls.Find(labelName, true)[0] as System.Windows.Forms.Label;

                    label.BackColor = System.Drawing.Color.Orange;

                    result = false;
                }
                else
                {
                    string labelName = item.Name + "_label";
                    System.Windows.Forms.Label label = this.Controls.Find(labelName, true)[0] as System.Windows.Forms.Label;

                    label.BackColor = System.Drawing.SystemColors.Control;
                }
            }

            return result;
        }
    }
}
