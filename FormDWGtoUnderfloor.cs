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
            //if (LevelID == null) this.Close();

            LoadComboBox(PipeSelection, true, () => pipeTypeService.GetPipeTypes());
            LoadComboBox(SystemTypePipe, true, () => pipeTypeService.GetSystemTypesPipe());
            LoadComboBox(SelectDrawingDWG, true, () => dwgConverter.GetDWGDrawings(LevelID));

            PipeSelection.SelectedIndex = 1;
            SystemTypePipe.SelectedIndex = 0;
            SelectDrawingDWG.SelectedIndex = 0;
            PipeLayingHeight.SelectedIndex = 0;
            PipeDiametr.SelectedIndex = 0;
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

        private bool CheckNullParamets(System.Windows.Forms.ComboBox[] boxItems)
        {


            return true;
        }

        private void CreateWarmFloor_Click(object sender, EventArgs e)
        {
            Element pipeType = pipeTypeService.GetPipeType(PipeSelection.SelectedIndex);
            Element pipeSystemType = pipeTypeService.GetSystemTypePipe(SystemTypePipe.SelectedIndex);
            ImportInstance dwg = dwgConverter.GetDWGDrawings(SelectDrawingDWG.SelectedIndex);
            ElementId LevelID = userParametrs.GetLevelId();

            double pipeDiametr = Convert.ToDouble(PipeDiametr.Text);
            double pipeHeightLayer = Convert.ToDouble(PipeLayingHeight.Text);

            Stopwatch stopwatch = new Stopwatch();

            //stopwatch.Start();
            IList<IList<XYZ>> points = dwgConverter.GetListVerticesFromDwg(dwg);
            //stopwatch.Stop();

            //MessageBox.Show($"Время выполнения: {stopwatch.ElapsedMilliseconds} мс");

            stopwatch.Start();
            foreach (IList<XYZ> point in points)
            {
                pipeTypeService.DrawPipes(point, pipeDiametr, pipeHeightLayer, pipeType.Id, LevelID, pipeSystemType.Id);
            }
            stopwatch.Stop(); 

            MessageBox.Show($"Время выполнения: {stopwatch.ElapsedMilliseconds} мс");
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
    }
}
