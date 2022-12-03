using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace commonAreas
{
    /// <summary>
    /// Interaction logic for calculationInterface.xaml
    /// </summary>
    public partial class calculationInterface : Window
    {
        public calculationInterface(List<string> levelNames)
        {
            InitializeComponent();
            Levels.ItemsSource = levelNames;
            if (levelNames.Count != 0) { 
                Levels.SelectedIndex = 0;
            }
            
            //Levels.Text = "Select a level";
            // els.sel

        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
        private void AcceptMg_Click(object sender, RoutedEventArgs e)
        {
            //MaxSpace.AFRYcommand.valueLength = Convert.ToDouble(truckLength.Text);
            //MaxSpace.AFRYcommand.valueWidth = Convert.ToDouble(truckWidth.Text);
            
            commonAreas.Calculations calc = new commonAreas.Calculations();
            if(Levels.SelectedItem != null)
            {
                calc.levelInput = Levels.SelectedItem.ToString();

            }
            

            this.DialogResult = true;

        }
    }
}
