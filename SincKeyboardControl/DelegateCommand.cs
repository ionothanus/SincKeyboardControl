// Derived from hardcodet/wpf-notifyicon
// https://github.com/hardcodet/wpf-notifyicon/blob/b198c4597acaf657e05ad7225bbdb00908f7b00d/src/NotifyIconWpf.Sample.Windowless/DelegateCommand.cs
// under the CPOL 1.02
// modifications:
// - namespace
// - add object/parameter linkage

using System;
using System.Windows.Input;

namespace SincKeyboardControl
{
    /// <summary>
    /// Simplistic delegate command for the demo.
    /// </summary>
    public class DelegateCommand : ICommand
    {
        public Action<object> CommandAction { get; set; }
        public Func<bool> CanExecuteFunc { get; set; }

        public void Execute(object parameter)
        {
            CommandAction(parameter);
        }

        public bool CanExecute(object parameter)
        {
            return CanExecuteFunc == null || CanExecuteFunc();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
