using System;
using System.Windows.Input;

namespace MyCoinFlow.Helpers
{
    /// <summary>
    /// Einfacher RelayCommand mit CanExecute-Unterstützung und RaiseCanExecuteChanged().
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        // WPF hooked RequerySuggested -> Commands aktualisieren sich z.B. bei Fokus-/Eingabeänderungen
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Manuell neu bewerten lassen (z.B. nach Property-Änderung im ViewModel aufrufen).
        /// </summary>
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}

