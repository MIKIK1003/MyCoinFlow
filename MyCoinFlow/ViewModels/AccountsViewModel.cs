using MyCoinFlow.Models;
using MyCoinFlow.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace MyCoinFlow.ViewModels
{
    public class AccountsViewModel : BaseViewModel
    {
        private readonly DatabaseService _db = new();

        // ===== Ansicht =====

        private bool _istTabellenAnsicht = false; // Standard: Baum
        public bool IstTabellenAnsicht
        {
            get => _istTabellenAnsicht;
            set
            {
                if (_istTabellenAnsicht == value) return;
                _istTabellenAnsicht = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IstBaumAnsicht));
            }
        }

        public bool IstBaumAnsicht => !IstTabellenAnsicht;

        // ===== Daten =====

        private ObservableCollection<KontoplanEintrag> _kontenListeFlach = new();
        public ObservableCollection<KontoplanEintrag> KontenListeFlach
        {
            get => _kontenListeFlach;
            private set { _kontenListeFlach = value; OnPropertyChanged(); }
        }

        private ObservableCollection<KontoplanKnoten> _kontoplanKnotenListe = new();
        public ObservableCollection<KontoplanKnoten> KontoplanKnotenListe
        {
            get => _kontoplanKnotenListe;
            private set { _kontoplanKnotenListe = value; OnPropertyChanged(); }
        }

        // ===== Selektion =====

        private object? _ausgewaehlterKnoten;
        public object? AusgewaehlterKnoten
        {
            get => _ausgewaehlterKnoten;
            set
            {
                if (Equals(_ausgewaehlterKnoten, value)) return;
                _ausgewaehlterKnoten = value;
                OnPropertyChanged();
            }
        }

        public AccountsViewModel()
        {
            LadeKontenplan();
        }

        private void LadeKontenplan()
        {
            var daten = _db.LadeKontenplan();

            KontenListeFlach = new ObservableCollection<KontoplanEintrag>(daten);
            KontoplanKnotenListe = new ObservableCollection<KontoplanKnoten>(BuildTree(daten));
        }

        /// <summary>
        /// Baut den Kontenplan-Baum:
        /// Art → Gruppe → Untergruppe → Konto
        /// Budget/Ist/Differenz kommen automatisch aus KontoplanKnoten (über OriginalEintrag/Kinder).
        /// </summary>
        private static KontoplanKnoten[] BuildTree(System.Collections.Generic.List<KontoplanEintrag> daten)
        {
            return daten
                .GroupBy(k => (k.Art ?? "").Trim())
                .OrderBy(g => g.Key)
                .Select(gArt =>
                {
                    var artNode = new KontoplanKnoten(
                        string.IsNullOrWhiteSpace(gArt.Key) ? "(ohne Art)" : gArt.Key
                    );

                    foreach (var gGrp in gArt.GroupBy(k => (k.Gruppe ?? "").Trim()).OrderBy(g => g.Key))
                    {
                        var grpNode = new KontoplanKnoten(
                            string.IsNullOrWhiteSpace(gGrp.Key) ? "(ohne Gruppe)" : gGrp.Key
                        );
                        artNode.Kinder.Add(grpNode);

                        foreach (var gUg in gGrp.GroupBy(k => (k.Untergruppe ?? "").Trim()).OrderBy(g => g.Key))
                        {
                            var ugNode = new KontoplanKnoten(
                                string.IsNullOrWhiteSpace(gUg.Key) ? "(ohne Untergruppe)" : gUg.Key
                            );
                            grpNode.Kinder.Add(ugNode);

                            foreach (var konto in gUg
                                .OrderBy(k => k.Kontonummer)
                                .ThenBy(k => k.Detail))
                            {
                                // HIER der entscheidende Punkt:
                                // Leaf-Knoten bekommt den OriginalEintrag
                                var leaf = new KontoplanKnoten(
                                    bezeichnung: konto.Detail ?? "(ohne Detail)",
                                    originalEintrag: konto
                                );

                                ugNode.Kinder.Add(leaf);
                            }
                        }
                    }

                    return artNode;
                })
                .ToArray();
        }
    }
}
