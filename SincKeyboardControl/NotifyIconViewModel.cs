// Derived from hardcodet/wpf-notifyicon
// https://github.com/hardcodet/wpf-notifyicon/blob/b198c4597acaf657e05ad7225bbdb00908f7b00d/src/NotifyIconWpf.Sample.Windowless/NotifyIconViewModel.cs
// under the CPOL 1.02
// modifications:
// - namespace
// - addition of commands
// - addition of SincHidController and constructor to set it
// - addition of parameter for CommandActions

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace SincKeyboardControl
{
    using SincHid;

    class NotifyIconViewModel
    {
        private SincHidController controller;

        public SincHidController Controller { get => controller; }

        public NotifyIconViewModel(SincHidController controller)
        {
            this.controller = controller;
        }

        /// <summary>
        /// Shows a window, if none is already open.
        /// </summary>
        public ICommand ShowWindowCommand
        {
            get
            {
                return new DelegateCommand
                {
                    CanExecuteFunc = () => Application.Current.MainWindow == null,
                    CommandAction = (_) =>
                    {
                        Application.Current.MainWindow = new MainWindow();
                        Application.Current.MainWindow.Show();
                    }
                };
            }
        }

        /// <summary>
        /// Hides the main window. This command is only enabled if a window is open.
        /// </summary>
        public ICommand HideWindowCommand
        {
            get
            {
                return new DelegateCommand
                {
                    CommandAction = (_) => Application.Current.MainWindow.Close(),
                    CanExecuteFunc = () => Application.Current.MainWindow != null
                };
            }
        }


        /// <summary>
        /// Shuts down the application.
        /// </summary>
        public ICommand ExitApplicationCommand
        {
            get
            {
                return new DelegateCommand { CommandAction = (_) => Application.Current.Shutdown() };
            }
        }

        /// <summary>
        /// Requests the SincHidController to refresh the layer state.
        /// </summary>
        public ICommand RequestRefreshCommand
        {
            get
            {
                return new DelegateCommand
                {
                    CommandAction = (_) => controller?.UpdateLayerStatusPolling(),
                    CanExecuteFunc = () => controller != null
                };
            }
        }

        /// <summary>
        /// Requests the SincHidController to set the correct layer.
        /// </summary>
        public ICommand RequestLayerCommand
        {
            get
            {
                return new DelegateCommand
                {
                    CommandAction = (layer) => controller?.RequestLayerPolling((SincLayerState)layer),
                    CanExecuteFunc = () => controller != null
                };
            }
        }

        /// <summary>
        /// Requests the SincHidController to disable the macro key.
        /// Pass <code>true</code> to disable the macro key, <code>false</code> to enable.
        /// </summary>
        public ICommand SetMacroKeyCommand
        {
            get
            {
                return new DelegateCommand
                {
                    CommandAction = (disabled) =>
                    { 
                        controller?.SetMacroKeyPolling((bool)disabled ? SincMacroKeyState.Disabled : SincMacroKeyState.Enabled);
                    },
                    CanExecuteFunc = () => controller != null
                };
            }
        }
    }
}
