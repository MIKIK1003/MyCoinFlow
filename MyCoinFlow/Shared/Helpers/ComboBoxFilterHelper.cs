using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace MyCoinFlow.Helpers
{
    /// <summary>
    /// Contains-Filter für editierbare ComboBoxen (Suche beim Tippen).
    ///
    /// Hintergrund: Wird beim Tippen die ItemsSource neu gesetzt, setzt WPF den
    /// Text der editierbaren ComboBox zurück und markiert den gesamten Inhalt.
    /// Das nächste getippte Zeichen überschreibt dann die bisherige Eingabe
    /// ("N" tippen → "N" ist markiert → "a" ersetzt das "N"). Dieser Helper
    /// stellt Text und Cursorposition nach dem Filtern wieder her.
    /// </summary>
    public static class ComboBoxFilterHelper
    {
        public static void FiltereMitTextErhalt<T>(
            ComboBox cb,
            List<T> alleEintraege,
            Func<T, string> anzeigeText)
        {
            var text = cb.Text ?? "";

            var editor = cb.Template?.FindName("PART_EditableTextBox", cb) as TextBox;
            int caret = editor?.CaretIndex ?? text.Length;

            List<T> neueListe;
            if (string.IsNullOrWhiteSpace(text))
            {
                neueListe = alleEintraege;
            }
            else
            {
                var gefiltert = alleEintraege
                    .Where(k => (anzeigeText(k) ?? "")
                        .IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                neueListe = gefiltert.Count > 0 ? gefiltert : alleEintraege;
            }

            cb.ItemsSource = neueListe;
            cb.IsDropDownOpen = true;

            // Text und Cursor wiederherstellen (siehe Klassen-Kommentar)
            cb.Text = text;
            if (editor != null)
            {
                editor.CaretIndex = Math.Min(caret, text.Length);
                editor.SelectionLength = 0;
            }
        }
    }
}
