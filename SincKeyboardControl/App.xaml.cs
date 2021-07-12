using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Drawing;
using System.Threading;
using System.Windows;

namespace SincKeyboardControl
{
    using SincHid;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private TaskbarIcon taskbarIcon;
        private SincHidController controller;
        private CancellationTokenSource cts;

        private Icon windowsIcon;
        private Icon optionIcon;
        private Icon commandIcon;

        private NotifyIconViewModel viewModel;

        private const string toolTipBase = "Sinc keyboard status: ";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            controller = new SincHidController();
            viewModel = new NotifyIconViewModel(controller);

            taskbarIcon = (TaskbarIcon)Current.Resources["NotifyIcon"];
            taskbarIcon.DataContext = viewModel;

            windowsIcon = new Icon(GetResourceStream(new Uri("pack://application:,,,/noun_windows_3936274.ico")).Stream);
            optionIcon = new Icon(GetResourceStream(new Uri("pack://application:,,,/option.ico")).Stream);
            commandIcon = new Icon(GetResourceStream(new Uri("pack://application:,,,/command.ico")).Stream);

            controller.PropertyChanged += Controller_PropertyChanged;
            controller.DeviceConnected += Controller_DeviceConnected;
            controller.DeviceDisconnected += Controller_DeviceDisconnected;

            if (!Connect())
            {
                ShowError("Unable to connect to the device");
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            // we'll skip the command here to ensure this completes before we quit
            await viewModel.Controller.SetMacroKeyPolling(SincMacroKeyState.Enabled);

            cts?.Cancel();
            taskbarIcon.Dispose();
            base.OnExit(e);
        }

        private bool Connect()
        {
            return controller.OpenDevice();
        }

        private void ShowError(string text)
        {
            taskbarIcon.ShowBalloonTip("Error", text, BalloonIcon.Warning);
        }

        private void Controller_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(controller.DriverConnected):
                    if (!controller.DriverConnected)
                    {
                        taskbarIcon.ShowBalloonTip("Disconnected", "Disconnected from keyboard", BalloonIcon.Warning);
                    }
                    break;
                case nameof(controller.LastState):
                    string toolTip;

                    if (controller.LastState is null)
                    {
                        taskbarIcon.Icon = optionIcon;
                        toolTip = "Unknown";
                    }
                    else if (controller.LastState == SincLayerState.Windows)
                    {
                        taskbarIcon.Icon = windowsIcon;
                        toolTip = controller.LastState.ToString();
                    }
                    else if (controller.LastState == SincLayerState.Mac)
                    {
                        taskbarIcon.Icon = commandIcon;
                        toolTip = controller.LastState.ToString();
                    }
                    else
                    {
                        taskbarIcon.Icon = optionIcon;
                        toolTip = controller.LastState.ToString();
                    }

                    this.Dispatcher.Invoke(() => { taskbarIcon.ToolTipText = toolTipBase + toolTip; });

                    if (controller.LastState != null)
                    {
                        taskbarIcon.ShowBalloonTip($"{controller.LastState}", $"Keyboard is in {controller.LastState} mode", BalloonIcon.None);
                    }

                    break;
            }

        }

        private void Controller_DeviceDisconnected(object sender, EventArgs e)
        {
            cts?.Cancel();
        }

        private void Controller_DeviceConnected(object sender, EventArgs e)
        {
            cts = new CancellationTokenSource();
            _ = controller.CreatePollingTask(cts);

            var command = viewModel.RequestRefreshCommand;

            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            var command2 = viewModel.SetMacroKeyCommand;

            if (command2.CanExecute(true))
            {
                command2.Execute(true);
            }
        }
    }
}
