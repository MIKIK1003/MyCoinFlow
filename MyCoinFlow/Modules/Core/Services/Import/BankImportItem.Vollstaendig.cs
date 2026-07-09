// Datei: Import/BankImportItem.Vollstaendig.cs
namespace MyCoinFlow.Import
{
    // Ergänzt die bestehende Klasse um eine berechnete Eigenschaft
    public sealed partial class BankImportItem
    {
        /// <summary>
        /// Vollständig, wenn:
        /// - Debit (Bank -> Konto): Adresse + NachKonto vorhanden UND (Bank vorhanden/ableitbar).
        ///   AUSNAHME: Umbuchung (Bank <-> Bank): Adresse reicht; NachKonto ist NICHT erforderlich.
        /// - Credit (Adresse -> Bank): Adresse vorhanden UND (Bank vorhanden/ableitbar).
        /// </summary>
        public bool IstVollstaendig
        {
            get
            {
                bool hatOderKannBank =
                    !string.IsNullOrWhiteSpace(AccountIban) || VorschlagGeldinstitutId.HasValue;

                if (Direction == KreditDebit.Debit)      // Bank -> Konto
                {
                    if (IstUmbuchung)
                        return hatOderKannBank && VorschlagAdresseId.HasValue; // Umbuchung: ohne NachKonto OK
                    return hatOderKannBank && VorschlagAdresseId.HasValue && VorschlagNachKontoId.HasValue;
                }

                // Credit (Adresse -> Bank)
                return hatOderKannBank && VorschlagAdresseId.HasValue;
            }
        }
    }
}
