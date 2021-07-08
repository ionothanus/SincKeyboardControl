using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
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
// - clean up from cancellation token experiments - probably some unnecessary cancels here now
// - minimize to system tray icon
// - change tray icon to reflect keyboard state?
// - show keyboard state in tooltip?
// - better error handling?

namespace SincKeyboardControl
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SincHidController controller;
        private CancellationTokenSource cts;
        // private System.Drawing.Icon icon;

        public MainWindow()
        {
            controller = new SincHidController();

            Initialized += OnInit;
            Closing += OnClosing;
            InitializeComponent();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            cts?.Cancel();
            taskbarIcon.Dispose();
        }

        private void ShowError(string text)
        {
            taskbarIcon.ShowBalloonTip("Error", text, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
        }

        private async void OnInit(object sender, EventArgs e)
        {
            lblCurrentState.DataContext = controller;
            lblConnected.DataContext = controller;
            
            // this successfully loads the icon, it just isn't good enough for ShowBalloonTip it seems
            // var stream = Application.GetResourceStream(new Uri("pack://application:,,,/option.ico")).Stream;
            // icon = new Icon(stream);

            controller.PropertyChanged += Controller_PropertyChanged;
            controller.DeviceConnected += Controller_DeviceConnected;
            controller.DeviceDisconnected += Controller_DeviceDisconnected;

            if (!Connect())
            {
                ShowError("Unable to connect to the device");
            }
        }

        private void Controller_DeviceDisconnected(object sender, EventArgs e)
        {
            cts?.Cancel();
        }

        private async void Controller_DeviceConnected(object sender, EventArgs e)
        {
            cts = new CancellationTokenSource();
            _ = controller.StartPolling(cts);

            if (!await RequestRefresh())
            {
                ShowError("Unable to request a refresh from the device");
            }
        }

        private void Controller_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(controller.DriverConnected):
                    if (controller.DriverConnected)
                    {
                        taskbarIcon.ShowBalloonTip("Connected", "Connected to keyboard", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.None);
                    }
                    else
                    {
                        taskbarIcon.ShowBalloonTip("Disconnected", "Disconnected from keyboard", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
                    }
                    break;
                case nameof(controller.LastState):
                    taskbarIcon.ShowBalloonTip($"{controller.LastState}", $"Keyboard is in {controller.LastState} mode", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.None);
                    break;
                default:
                    MessageBox.Show("Unknown element");
                    break;
            }

        }

        private bool Connect()
        {
            return controller.OpenDevice();
        }

        private async Task<bool> RequestRefresh()
        {
            return await controller.UpdateLayerStatusPolling();
        }

        private async Task<bool> RequestLayer(SincLayerState layerState)
        {
            return await controller.RequestLayerPolling(layerState);
        }

        private async void btnRequestLayer_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = (SincLayerState)(comboBox.SelectedItem ?? SincLayerState.Unknown);

            if (selectedItem != SincLayerState.Unknown)
            {
                if (!await RequestLayer(selectedItem))
                {
                    MessageBox.Show("Failed to refresh status");
                }
            }
        }

        private async void btnRequestRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!await RequestRefresh())
            {
                MessageBox.Show("Failed to refresh status");
            }
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!Connect())
            {
                MessageBox.Show("Failed to open device");
            }
        }

        private void btnStopPolling_Click(object sender, RoutedEventArgs e)
        {
            cts?.Cancel();
        }
    }
}
