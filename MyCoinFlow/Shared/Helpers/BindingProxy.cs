using System.Windows;

namespace MyCoinFlow.Helpers
{
    /// <summary>
    /// Binding-Proxy für Bindings aus nicht visuellem Kontext (z. B. DataGridColumn).
    /// </summary>
    public sealed class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public object? Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }
}
