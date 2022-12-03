using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RtView = Autodesk.Revit.DB.View;

namespace commonAreas
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]

    internal class TairloBirdAplication : IExternalApplication
    {
        /// <TransactionMode.Manual>you may use combinations of Transactions as you please</TransactionMode.Manual>
        /// <RegenerationOption.Manual>you may want to regenerate manually</RegenerationOption.Manual>
        /// <IExternalApplication> c) CREATE A CLASS THAT INHERITE IExternalApplication. 2 main methods: OnStartUp() & OnShutDown() </IExternalApplication>
        /// <UIControlledApplication> d). This parameter allows customization of ribbon panels and controls and the addition of ribbon tabs upon start up.</UIControlledApplication>

        public Result OnStartup(UIControlledApplication application)

        {
            //01) Create or lookup the appropriate Tab
            string tabName = "Tailorbird";
            try { application.CreateRibbonTab(tabName); }
            catch { }

            //02) Create or lookup the appropriate panel on previos Tab
            #region quantities and automated modeling
            RibbonPanel automatedModelPanel = null;
            string automatedModelPanelName = "Quantities and Automated Modeling";
            List<RibbonPanel> panelList = application.GetRibbonPanels(tabName);
            foreach (RibbonPanel rpanel in panelList)
            {
                if (rpanel.Name == automatedModelPanelName) { automatedModelPanel = rpanel; break; }
            }

            if (automatedModelPanel == null) { automatedModelPanel = application.CreateRibbonPanel(tabName, automatedModelPanelName); }
            #endregion

            #region Various

            RibbonPanel variousPanel = null;
            string variousPanelName = "Various";

            foreach (RibbonPanel rpanel in panelList)
            {
                if (rpanel.Name == variousPanelName) { variousPanel = rpanel; break; }
            }

            if (variousPanel == null) { variousPanel = application.CreateRibbonPanel(tabName, variousPanelName); }

            #endregion

            //03) Add buttons with icons to the selected panel
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            //string assembly = @"C:\ProgramData\Autodesk\Revit\Addins\2023\" + "TailorBirdAddin" + ".dll"; //set it on project properties
            //string iconPath = @"C:\ProgramData\Autodesk\Revit\Addins\2023\" + "TailorBirdIcon" + ".png";


            //3.1) query button icons
            #region a) Calculations
            Image calcImage = Properties.Resources.prison32px;
            ImageSource calcSource = GetImageSource(calcImage);

            PushButtonData calcButton = new PushButtonData("Model elements and calculate data", "Common areas" + "\n" + "calculations", assemblyPath, "commonAreas.Calculations");
            PushButton calcButtonPush = automatedModelPanel.AddItem(calcButton) as PushButton;
            calcButtonPush.ToolTip = "Model baseboard, floors, ceilings, moldings for each room. Besides that, calculate wall areas for rooms and set the room name for the location parameter";
            calcButtonPush.Image = calcSource;
            calcButtonPush.LargeImage = calcSource;

            #endregion

            #region b) Updater
            Image updaterImage = Properties.Resources.refresh32px;
            ImageSource updaterSource = GetImageSource(updaterImage);

            PushButtonData updaterButton = new PushButtonData("Find update for Common areas plugins", "Common areas" +"\n" + "updates", assemblyPath, "commonAreas.tailorbirdUpdater");
            PushButton updaterButtonPush = variousPanel.AddItem(updaterButton) as PushButton;
            updaterButtonPush.ToolTip = "Find updates for the Common areas Revit Plugin, if new version exist it will automatically update all the files. Afterwards, It's required to close and run Revit again";
            updaterButtonPush.Image = updaterSource;
            updaterButtonPush.LargeImage = updaterSource;

            #endregion


            //f). Revit keeps all changes made by the external command
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
        private BitmapSource GetImageSource(Image img)
        {
            BitmapImage bmp = new BitmapImage();

            using (MemoryStream ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = null;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            return bmp;
        }
    }
}
