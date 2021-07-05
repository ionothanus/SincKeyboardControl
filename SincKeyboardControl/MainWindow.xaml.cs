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
using SincKeyboardControl.SincHid;

// TODO: 
// - system tray icon?
// - autoconnect on start?
// - better error handling?
// - once implemented in controller - status notifications when layer changed on keyboard?

namespace SincKeyboardControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SincHidController controller;

        public MainWindow()
        {
            controller = new SincHidController();
            Initialized += OnInit;
            InitializeComponent();
        }

        private void OnInit(object sender, EventArgs e)
        {
            lblCurrentState.DataContext = controller;
            lblConnected.DataContext = controller;
        }

        private async void btnRequestLayer_Click(object sender, RoutedEventArgs e)
        {
            var test = comboBox.SelectedItem;
            if (!await controller.RequestLayer((SincLayerState)test))
            {
                MessageBox.Show("Failed to refresh status");
            }
        }

        private async void btnRequestRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!await controller.UpdateLayerStatus())
            {
                MessageBox.Show("Failed to refresh status");
            }
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!await controller.OpenDevice())
            {
                MessageBox.Show("Failed to open device");
            }
        }
    }
}
