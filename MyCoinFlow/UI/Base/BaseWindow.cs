using System.Windows;
using System.Windows.Input;
using MyCoinFlow.Services;

namespace MyCoinFlow.UI.Base
{
    // Basisklasse für alle Fenster
    public class BaseWindow : Window
    {
        public BaseWindow()
        {
            // Lifecycle Events
            Loaded += BaseWindow_Loaded;
            Closing += BaseWindow_Closing;

            // Tastatur-Handling
            PreviewKeyDown += BaseWindow_PreviewKeyDown;
        }

        private void BaseWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fensterposition wiederherstellen
                WindowStateService.Restore(this);
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
                // ESC → Fenster schliessen
                if (e.Key == Key.Escape)
                {
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