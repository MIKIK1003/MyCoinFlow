using System.ComponentModel;
using System.Runtime.CompilerServices;
using MyCoinFlow.Helpers;

namespace MyCoinFlow.ViewModels
{
    /// <summary>
    /// Basisklasse für alle ViewModels, die die Benachrichtigung über Eigenschaftsänderungen unterstützt.
    /// </summary>
    public class BaseViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Dieses Event wird ausgelöst, wenn sich eine Eigenschaft im ViewModel ändert.
        /// Die Oberfläche kann darauf reagieren und den neuen Wert anzeigen.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Hilfsmethode, um das PropertyChanged-Event auszulösen.
        /// Der CallerMemberName-Mechanismus sorgt dafür, dass der Name der aufrufenden Eigenschaft automatisch übergeben wird.
        /// </summary>
        /// <param name="propertyName">Name der Eigenschaft (optional, wird normalerweise automatisch ermittelt)</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
