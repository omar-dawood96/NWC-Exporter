using System;
using System.Windows.Input;

namespace NWCExporter.Commands
{
    public class RelayCommand : ICommand
    {
        // ── Fields ────────────────────────────────────────────────────────────
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        // ── Constructors ──────────────────────────────────────────────────────
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        // ── Public methods ────────────────────────────────────────────────────
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }

    public class RelayCommand<T> : ICommand
    {
        // ── Fields ────────────────────────────────────────────────────────────
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        // ── Constructors ──────────────────────────────────────────────────────
        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        // ── Public methods ────────────────────────────────────────────────────
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter is T t ? t : default) ?? true;
        public void Execute(object? parameter) => _execute(parameter is T t ? t : default);
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}