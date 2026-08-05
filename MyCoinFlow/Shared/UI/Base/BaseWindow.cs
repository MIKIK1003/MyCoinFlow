using System.Linq;
using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Services;

namespace MyCoinFlow.UI.Base
{
    // Basisklasse für alle Fenster
    public class BaseWindow : Window
    {
        // ✅ NEU: Steuerung, ob ESC das Fenster schliessen darf
        protected virtual bool AllowEscapeClose => true;

        public BaseWindow()
        {
            // Lifecycle Events
            Loaded += BaseWindow_Loaded;
            Closing += BaseWindow_Closing;

            // Tastatur-Handling
            PreviewKeyDown += BaseWindow_PreviewKeyDown;

            RepariereMainWindowFallsNoetig();
        }

        /// <summary>
        /// Sicherheitsnetz: Solange Application.MainWindow leer ist, macht WPF das nächste
        /// erzeugte Fenster automatisch zum MainWindow. Für einen Dialog, der gleich danach
        /// "Owner = Application.Current.MainWindow" setzt, hiesse das: Owner = er selbst –
        /// WPF quittiert das mit einer ArgumentException. Hier wird MainWindow deshalb auf
        /// ein bereits sichtbares Fenster zurückgebogen, bevor der Owner gesetzt wird.
        /// </summary>
        private void RepariereMainWindowFallsNoetig()
        {
            try
            {
                var app = Application.Current;
                if (app == null || !ReferenceEquals(app.MainWindow, this))
                    return;

                var echtesFenster = app.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => !ReferenceEquals(w, this) && w.IsVisible);

                // Kein anderes sichtbares Fenster: Dann ist dieses Fenster tatsächlich
                // das Hauptfenster – nichts korrigieren.
                if (echtesFenster != null)
                    app.MainWindow = echtesFenster;
            }
            catch
            {
                // Fensteraufbau darf daran nie scheitern.
            }
        }

        private void BaseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fensterposition wiederherstellen
                WindowStateService.Restore(this);

                // 🔴 NEU: Fokus sauber setzen (nachdem Layout fertig ist)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Falls bereits ein Fokus gesetzt ist → nichts tun
                        var focused = FocusManager.GetFocusedElement(this);
                        if (focused != null && focused is IInputElement)
                            return;

                        // Erstes fokussierbares Element finden
                        MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                    }
                    catch
                    {
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch
            {
                // bewusst keine Exception werfen
            }
        }

        private void BaseWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Fensterposition speichern
                WindowStateService.Save(this);
            }
            catch
            {
                // bewusst keine Exception werfen
            }
        }

        private void BaseWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // ESC → Fenster schliessen (nur wenn erlaubt)
                if (e.Key == Key.Escape)
                {
                    if (AllowEscapeClose)
                        Close();

                    e.Handled = true;
                    return;
                }

                // ENTER → Default Button auslösen
                if (e.Key == Key.Enter)
                {
                    var focusedElement = FocusManager.GetFocusedElement(this);

                    // Wenn bereits Button → nichts machen
                    if (focusedElement is System.Windows.Controls.Button)
                        return;

                    var e2 = new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(this),
                        0,
                        Key.Enter)
                    {
                        RoutedEvent = Keyboard.KeyDownEvent
                    };

                    InputManager.Current.ProcessInput(e2);
                    e.Handled = true;
                }
            }
            catch
            {
                // keine UI-Blockade
            }
        }
    }
}