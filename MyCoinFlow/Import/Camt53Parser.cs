using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace MyCoinFlow.Import
{
    public static class Camt53Parser
    {
        // Bekannte camt.053 Namespaces – wird dynamisch erkannt; Liste dient nur als Fallback.
        private static readonly XNamespace[] KnownNs =
        {
            "urn:iso:std:iso:20022:tech:xsd:camt.053.001.08",
            "urn:iso:std:iso:20022:tech:xsd:camt.053.001.07",
            "urn:iso:std:iso:20022:tech:xsd:camt.053.001.06",
            "urn:iso:std:iso:20022:tech:xsd:camt.053.001.05",
            "urn:iso:std:iso:20022:tech:xsd:camt.053.001.04"
        };

        public static List<BankImportItem> ParseFromFile(string path)
        {
            var xdoc = XDocument.Load(path);
            return Parse(xdoc);
        }

        public static List<BankImportItem> Parse(XDocument xdoc)
        {
            var items = new List<BankImportItem>();

            // 1) Namespace ermitteln
            var ns = xdoc.Root?.Name.Namespace;
            if (ns == null || ns == XNamespace.None)
                ns = KnownNs[0];

            // 2) Stmt-Knoten suchen – zunächst namespaced, bei 0 Treffern namespace-agnostisch
            var stmts = xdoc.Descendants(ns + "Stmt").ToList();
            if (stmts.Count == 0)
            {
                // Fallback: suche Statements per LocalName (andere Bank/Namespace)
                stmts = xdoc.Descendants()
                            .Where(e => e.Name.LocalName == "Stmt")
                            .ToList();

                // wenn wir so fündig werden, Namespace dieses Knotens übernehmen
                if (stmts.Count > 0)
                    ns = stmts[0].Name.Namespace;
            }

            if (stmts.Count == 0)
                return items; // defensiv: keine Statements → keine Items

            foreach (var stmt in stmts)
            {
                // Konto-Identifikation (IBAN bevorzugt, sonst Othr/Id)
                string accountId =
                    TrimOrNull(stmt.Element(ns + "Acct")?
                                    .Element(ns + "Id")?
                                    .Element(ns + "IBAN")?.Value)
                    ?? TrimOrNull(stmt.Element(ns + "Acct")?
                                       .Element(ns + "Id")?
                                       .Element(ns + "Othr")?
                                       .Element(ns + "Id")?.Value)
                    ?? "";

                // Währung: zuerst Konto-Ccy, dann Balance-Amt@Ccy, sonst CHF
                string? acctCcy = stmt.Element(ns + "Acct")?.Element(ns + "Ccy")?.Value;
                string? balCcy = stmt.Elements(ns + "Bal")
                                     .Select(b => b.Element(ns + "Amt")?.Attribute("Ccy")?.Value)
                                     .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                string defaultCcy = !string.IsNullOrWhiteSpace(acctCcy) ? acctCcy!
                                   : !string.IsNullOrWhiteSpace(balCcy) ? balCcy!
                                   : "CHF";

                // Nur Einträge dieses Statements verarbeiten
                foreach (var ntry in stmt.Elements(ns + "Ntry"))
                {
                    // Richtung (Debit/Credit)
                    var ind = (ntry.Element(ns + "CdtDbtInd")?.Value ?? "CRDT")
                              .Trim().ToUpperInvariant();
                    var direction = ind == "DBIT" ? KreditDebit.Debit : KreditDebit.Credit;

                    // Betrag & Währung (Amt direkt unter Ntry)
                    var amtEl = ntry.Element(ns + "Amt");
                    var amtStr = amtEl?.Value ?? "0";
                    var ccy = amtEl?.Attribute("Ccy")?.Value ?? defaultCcy;
                    var amount = Math.Abs(ParseDecimal(amtStr));

                    // Buchungs-/Valutadatum
                    var bookDate = ReadDate(ntry.Element(ns + "BookgDt"), ns) ?? DateTime.Today;
                    var valDate = ReadDate(ntry.Element(ns + "ValDt"), ns);

                    // Service-/Ntry-Referenzen (inkl. Fallback auf TxDtls/Refs/AcctSvcrRef)
                    string serviceRef =
                        ntry.Element(ns + "AcctSvcrRef")?.Value
                        ?? ntry.Element(ns + "NtryRef")?.Value
                        ?? ntry.Descendants(ns + "TxDtls")
                               .Select(tx => tx.Element(ns + "Refs")?.Element(ns + "AcctSvcrRef")?.Value)
                               .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                        ?? ntry.Descendants(ns + "Refs")
                               .Select(r => r.Element(ns + "AcctSvcrRef")?.Value)
                               .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                        ?? "";

                    // UETR (soweit vorhanden)
                    string uetr =
                        ntry.Descendants(ns + "UETR")
                            .Select(u => u.Value)
                            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                        ?? "";

                    // Freitext aus AddtlNtryInf + unstrukturierte RmtInf/Ustrd
                    string text = ntry.Element(ns + "AddtlNtryInf")?.Value ?? "";
                    var txFirst = ntry.Descendants(ns + "TxDtls").FirstOrDefault();

                    var ustrd = txFirst?.Element(ns + "RmtInf")
                                       ?.Elements(ns + "Ustrd")
                                       .Select(e => e.Value)
                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                       .Select(s => s.Trim())
                                       .ToList()
                               ?? new List<string>();

                    if (ustrd.Count > 0)
                    {
                        var joined = string.Join(" | ", ustrd);
                        text = string.IsNullOrWhiteSpace(text) ? joined : $"{text} | {joined}";
                    }

                    // Gegenpartei (CRDT = Dbtr, DBIT = Cdtr); Namen/IBAN robust via Descendants
                    string? cpName = null;
                    string? cpIban = null;
                    if (txFirst != null)
                    {
                        var rltd = txFirst.Element(ns + "RltdPties");
                        if (direction == KreditDebit.Credit)
                        {
                            var party = rltd?.Element(ns + "Dbtr") ?? rltd?.Element(ns + "UltmtDbtr");
                            cpName = FindName(party, ns);
                            var acct = rltd?.Element(ns + "DbtrAcct") ?? rltd?.Element(ns + "UltmtDbtrAcct");
                            cpIban = FindAccountId(acct, ns);
                        }
                        else
                        {
                            var party = rltd?.Element(ns + "Cdtr") ?? rltd?.Element(ns + "UltmtCdtr");
                            cpName = FindName(party, ns);
                            var acct = rltd?.Element(ns + "CdtrAcct") ?? rltd?.Element(ns + "UltmtCdtrAcct");
                            cpIban = FindAccountId(acct, ns);
                        }

                        // weiterer Fallback: AddtlTxInf als Name, falls Bank den Namen dort ablegt
                        if (string.IsNullOrWhiteSpace(cpName))
                        {
                            var addtl = txFirst.Element(ns + "AddtlTxInf")?.Value;
                            if (!string.IsNullOrWhiteSpace(addtl))
                                cpName = addtl;
                        }
                    }

                    // Purpose aus Purp oder BkTxCd zusammensetzen
                    var purposeCode = GetPurposeCode(ntry, ns);

                    var it = new BankImportItem
                    {
                        AccountIban = accountId,
                        Currency = ccy,
                        BookingDate = bookDate,
                        ValueDate = valDate,
                        Amount = amount,
                        Direction = direction,
                        ServiceRef = serviceRef,
                        Text = text,
                        CounterpartyName = TrimOrNull(cpName),
                        CounterpartyIban = TrimOrNull(cpIban),
                        Uetr = TrimOrNull(uetr),
                        PurposeCode = TrimOrNull(purposeCode)
                    };

                    items.Add(it);
                }
            }

            return items;
        }

        private static string? FindName(XElement? party, XNamespace ns)
        {
            if (party == null) return null;
            var nm = party.Element(ns + "Nm")?.Value
                     ?? party.Descendants(ns + "Nm").FirstOrDefault()?.Value;
            return TrimOrNull(nm);
        }

        private static string? FindAccountId(XElement? acct, XNamespace ns)
        {
            if (acct == null) return null;

            // IBAN bevorzugt
            var iban = acct.Descendants(ns + "IBAN").FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(iban)) return iban.Trim();

            // Fallback: Othr/Id (z. B. Postkonto, interne Id)
            var othr = acct.Descendants(ns + "Othr").FirstOrDefault();
            var id = othr?.Element(ns + "Id")?.Value;
            return TrimOrNull(id);
        }

        private static DateTime? ReadDate(XElement? dateNode, XNamespace ns)
        {
            if (dateNode == null) return null;

            var dt = dateNode.Element(ns + "Dt")?.Value;
            if (!string.IsNullOrWhiteSpace(dt) && DateTime.TryParse(dt, out var d)) return d.Date;

            var dtm = dateNode.Element(ns + "DtTm")?.Value;
            if (!string.IsNullOrWhiteSpace(dtm) && DateTime.TryParse(dtm, out var t)) return t;

            return null;
        }

        private static string? GetPurposeCode(XElement ntry, XNamespace ns)
        {
            // Purp (Cd/Prtry)
            var purp = ntry.Descendants(ns + "Purp")
                           .Select(p => p.Element(ns + "Cd")?.Value ?? p.Element(ns + "Prtry")?.Value)
                           .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (!string.IsNullOrWhiteSpace(purp)) return purp!.Trim();

            // BkTxCd (Domn/Cd, Fmly/Cd, Fmly/SubFmlyCd) + Prtry/Cd
            var bk = ntry.Descendants(ns + "BkTxCd").FirstOrDefault();
            if (bk != null)
            {
                var dom = bk.Element(ns + "Domn");
                var cd = dom?.Element(ns + "Cd")?.Value;
                var fam = dom?.Element(ns + "Fmly");
                var fcd = fam?.Element(ns + "Cd")?.Value;
                var sub = fam?.Element(ns + "SubFmlyCd")?.Value;
                var prtry = bk.Element(ns + "Prtry")?.Element(ns + "Cd")?.Value;

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(cd)) parts.Add(cd!.Trim());
                if (!string.IsNullOrWhiteSpace(fcd)) parts.Add(fcd!.Trim());
                if (!string.IsNullOrWhiteSpace(sub)) parts.Add(sub!.Trim());
                if (!string.IsNullOrWhiteSpace(prtry)) parts.Add(prtry!.Trim());

                if (parts.Count > 0) return string.Join(".", parts);
            }

            return null;
        }

        private static decimal ParseDecimal(string s)
        {
            if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var d))
                return d;
            if (decimal.TryParse(s, out d)) return d;
            return 0m;
        }

        private static string? TrimOrNull(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
