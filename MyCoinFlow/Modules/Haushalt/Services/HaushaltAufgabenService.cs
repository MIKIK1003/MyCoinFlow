using MyCoinFlow.Models;
using System;
using System.Collections.Generic;

namespace MyCoinFlow.Services
{
    public class HaushaltAufgabenService
    {
        private readonly DatabaseService _db;

        public HaushaltAufgabenService()
        {
            _db = new DatabaseService();
        }

        public HaushaltAufgabenService(DatabaseService db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public int AufgabenFuerAlleObjekteAktualisieren(IEnumerable<HaushaltObjekt> objekte)
        {
            if (objekte == null)
                throw new ArgumentNullException(nameof(objekte));

            var erstellt = 0;

            foreach (var objekt in objekte)
            {
                var neueAufgabeId = AufgabeFuerObjektErzeugenWennNoetig(objekt);

                if (neueAufgabeId.HasValue)
                    erstellt++;
            }

            return erstellt;
        }

        public int? AufgabeFuerObjektErzeugenWennNoetig(HaushaltObjekt objekt)
        {
            if (objekt == null)
                throw new ArgumentNullException(nameof(objekt));

            if (objekt.Id <= 0)
                return null;

            if (!objekt.ZeitintervallTage.HasValue || objekt.ZeitintervallTage.Value <= 0)
                return null;

            if (objekt.LetzteAusfuehrungAm == null)
                return null;

            var bestehend = _db.HaushaltAktiveAufgabeGetByObjekt(objekt.Id);
            if (bestehend != null)
                return bestehend.Id;

            var faelligAm = objekt.LetzteAusfuehrungAm.Value.Date.AddDays(objekt.ZeitintervallTage.Value);
            var aktivAb = faelligAm.AddDays(-objekt.VorlaufTage);

            if (DateTime.Today < aktivAb)
                return null;

            var titel = BaueAufgabentitel(objekt);

            var aufgabe = new HaushaltAufgabe
            {
                ObjektId = objekt.Id,
                Titel = titel,
                Status = "Offen",
                AktivAb = aktivAb,
                FaelligAm = faelligAm,
                IstAktiv = true
            };

            return _db.HaushaltAufgabeInsert(aufgabe);
        }

        public string BaueAufgabentitel(HaushaltObjekt objekt)
        {
            if (objekt == null)
                throw new ArgumentNullException(nameof(objekt));

            var objektText = objekt.Bezeichnung?.Trim() ?? "";
            var taetigkeitText = objekt.ArbeitsanweisungBezeichnung?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(taetigkeitText))
                return objektText;

            return $"{objektText} {taetigkeitText}".Trim();
        }
    }
}