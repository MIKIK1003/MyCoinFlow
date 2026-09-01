using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using MyCoinFlow.WinUI.Models;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace MyCoinFlow.WinUI.Data;

public sealed class TransactionToolsRepository
{
    private readonly AttachmentService _attachmentService = new();
    private static SqlConnection CreateConnection() => new(ConnectionStrings.Current);

    public async Task<IReadOnlyList<AttachmentRecord>> GetAttachmentsAsync(int transactionId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        const string sql = @"SELECT Id, FileName, ISNULL(OriginalName,''), FolderRel, ISNULL(OcrStatus,'Ausstehend'), SizeBytes
FROM dbo.Attachment WHERE TransaktionId=@id ORDER BY Id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", transactionId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<AttachmentRecord>();
        while (await reader.ReadAsync()) rows.Add(new AttachmentRecord
        {
            Id = reader.GetInt32(0), FileName = reader.GetString(1), OriginalName = reader.GetString(2),
            FolderRel = reader.GetString(3), OcrStatus = reader.GetString(4), SizeBytes = reader.IsDBNull(5) ? null : reader.GetInt64(5)
        });
        return rows;
    }

    public async Task<string> AttachAsync(int transactionId, string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Die ausgewählte Datei wurde nicht gefunden.", sourcePath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
            throw new InvalidOperationException("Es sind PDF-, JPG- und PNG-Dateien erlaubt.");
        var (root, maxMb) = await GetAttachmentSettingsAsync();
        var file = new FileInfo(sourcePath);
        if (file.Length > maxMb * 1024L * 1024L) throw new InvalidOperationException($"Die Datei überschreitet das Limit von {maxMb} MB.");
        var now = DateTime.Now;
        var relative = Path.Combine(now.ToString("yyyy"), now.ToString("MM"));
        var folder = Path.Combine(root, relative);
        Directory.CreateDirectory(folder);
        var tempName = $"DOK-TMP-{Guid.NewGuid():N}{extension}";
        var tempPath = Path.Combine(folder, tempName);
        File.Copy(sourcePath, tempPath, false);
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        const string sql = @"INSERT INTO dbo.Attachment
(TransaktionId, FileName, OriginalName, FolderRel, SizeBytes, OcrStatus, EntityType, EntityId, DokumentDatum)
VALUES (@t,@f,@o,@folder,@size,NULL,N'Transaktion',@t,@date); SELECT CAST(SCOPE_IDENTITY() AS INT);";
        int id;
        try
        {
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@t", transactionId);
            command.Parameters.AddWithValue("@f", tempName);
            command.Parameters.AddWithValue("@o", Path.GetFileName(sourcePath));
            command.Parameters.AddWithValue("@folder", relative.Replace('/', '\\'));
            command.Parameters.AddWithValue("@size", file.Length);
            command.Parameters.AddWithValue("@date", now.Date);
            id = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
        catch { File.Delete(tempPath); throw; }
        var finalName = $"DOK-{id:D6}{extension}";
        var finalPath = Path.Combine(folder, finalName);
        File.Move(tempPath, finalPath);
        await using (var update = new SqlCommand("UPDATE dbo.Attachment SET FileName=@name WHERE Id=@id", connection))
        {
            update.Parameters.AddWithValue("@name", finalName); update.Parameters.AddWithValue("@id", id); await update.ExecuteNonQueryAsync();
        }
        _attachmentService.LinkToTransaktion(id, transactionId);
        return finalPath;
    }

    public async Task OpenAttachmentAsync(AttachmentRecord row)
    {
        var (root, _) = await GetAttachmentSettingsAsync();
        var path = Path.Combine(root, row.FolderRel, row.FileName);
        if (!File.Exists(path)) throw new FileNotFoundException("Die Dokumentdatei ist am gespeicherten Ort nicht mehr vorhanden.", path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public async Task<IReadOnlyList<UnlinkedDocumentRecord>> GetUnlinkedDocumentsAsync(string? searchText)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        const string sql = @"SELECT DISTINCT a.Id,a.FileName,ISNULL(a.OriginalName,''),ISNULL(a.Titel,''),ISNULL(a.Beschreibung,''),ISNULL(a.Kategorie,''),a.DokumentDatum,a.ErkannterBetrag,a.SizeBytes
FROM dbo.Attachment a LEFT JOIN dbo.AttachmentText txt ON txt.AttachmentId=a.Id
WHERE a.TransaktionId IS NULL AND a.EntityType IS NULL
AND (@query IS NULL OR a.FileName LIKE '%'+@query+'%' OR a.OriginalName LIKE '%'+@query+'%' OR a.Titel LIKE '%'+@query+'%' OR a.Beschreibung LIKE '%'+@query+'%' OR a.Kategorie LIKE '%'+@query+'%' OR txt.[Text] LIKE '%'+@query+'%')
ORDER BY a.DokumentDatum DESC,a.Id DESC;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@query", string.IsNullOrWhiteSpace(searchText) ? DBNull.Value : searchText.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<UnlinkedDocumentRecord>();
        while (await reader.ReadAsync()) rows.Add(new UnlinkedDocumentRecord
        {
            Id=reader.GetInt32(0),FileName=reader.GetString(1),OriginalName=reader.GetString(2),Title=reader.GetString(3),Description=reader.GetString(4),Category=reader.GetString(5),
            DocumentDate=reader.IsDBNull(6)?null:reader.GetDateTime(6),RecognizedAmount=reader.IsDBNull(7)?null:reader.GetDecimal(7),SizeBytes=reader.IsDBNull(8)?null:reader.GetInt64(8)
        });
        return rows;
    }

    public async Task LinkExistingDocumentAsync(int attachmentId, int transactionId)
    {
        await Task.Run(() => _attachmentService.LinkToTransaktion(attachmentId, transactionId, requireUnlinked: true));
    }

    public async Task UnlinkAttachmentAsync(int id)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync();
        await using var command = new SqlCommand("UPDATE dbo.Attachment SET TransaktionId=NULL, EntityType=NULL, EntityId=NULL WHERE Id=@id", connection);
        command.Parameters.AddWithValue("@id", id); await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAttachmentAsync(AttachmentRecord row)
    {
        await using (var retentionConnection = CreateConnection())
        {
            await retentionConnection.OpenAsync();
            await using var retentionCommand = new SqlCommand("SELECT AufbewahrenBis FROM dbo.Attachment WHERE Id=@id", retentionConnection);
            retentionCommand.Parameters.AddWithValue("@id", row.Id);
            var retention = await retentionCommand.ExecuteScalarAsync();
            if (retention is DateTime retainUntil && retainUntil.Date > DateTime.Today)
                throw new InvalidOperationException($"Das Dokument ist bis {retainUntil:dd.MM.yyyy} aufbewahrungsgesperrt.");
        }
        var (root, _) = await GetAttachmentSettingsAsync();
        var source = Path.Combine(root, row.FolderRel, row.FileName);
        var archiveFolder = Path.Combine(root, "Archiv", row.FolderRel);
        Directory.CreateDirectory(archiveFolder);
        var archive = Path.Combine(archiveFolder, $"{Path.GetFileNameWithoutExtension(row.FileName)}-{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(row.FileName)}");
        if (File.Exists(source)) File.Move(source, archive);
        await using var connection = CreateConnection(); await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        try
        {
            foreach (var sql in new[] { "DELETE FROM dbo.AttachmentText WHERE AttachmentId=@id", "IF OBJECT_ID('dbo.AttachmentVersion','U') IS NOT NULL DELETE FROM dbo.AttachmentVersion WHERE AttachmentId=@id", "DELETE FROM dbo.Attachment WHERE Id=@id" })
            {
                await using var command = new SqlCommand(sql, connection, (SqlTransaction)tx); command.Parameters.AddWithValue("@id", row.Id); await command.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); if (File.Exists(archive) && !File.Exists(source)) { Directory.CreateDirectory(Path.GetDirectoryName(source)!); File.Move(archive, source); } throw; }
    }

    public async Task<IReadOnlyList<DuplicateRecord>> FindDuplicatesAsync(bool identicalNote)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync();
        var noteJoin = identicalNote ? " AND ISNULL(x.Notiz,'')=ISNULL(t.Notiz,'')" : "";
        var sql = $@"SELECT t.Id,t.Datum,t.Betrag,ISNULL(a.Name,''),ISNULL(g.Name,''),ISNULL(t.Notiz,''),
(SELECT COUNT(1) FROM dbo.Attachment z WHERE z.TransaktionId=t.Id)
FROM dbo.Transaktion t LEFT JOIN dbo.Adresse a ON a.Id=t.AdresseId LEFT JOIN dbo.Geldinstitut g ON g.Id=t.GeldinstitutId
WHERE EXISTS (SELECT 1 FROM dbo.Transaktion x WHERE x.Id<>t.Id AND CONVERT(date,x.Datum)=CONVERT(date,t.Datum) AND x.Betrag=t.Betrag{noteJoin})
ORDER BY t.Datum DESC,t.Betrag,t.Id;";
        await using var command = new SqlCommand(sql, connection); await using var reader = await command.ExecuteReaderAsync();
        var raw = new List<(int Id, DateTime Date, decimal Amount, string Address, string Institution, string Note, int Attachments)>();
        while (await reader.ReadAsync()) raw.Add((reader.GetInt32(0),reader.GetDateTime(1),reader.GetDecimal(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetInt32(6)));
        var groups = identicalNote ? raw.GroupBy(x => (x.Date.Date,x.Amount,x.Note.Trim())) : raw.GroupBy(x => (x.Date.Date,x.Amount,string.Empty));
        var result = new List<DuplicateRecord>(); var number = 0;
        foreach (var group in groups) { number++; result.AddRange(group.OrderBy(x=>x.Id).Select(x=>new DuplicateRecord { Group=number,Id=x.Id,Date=x.Date,Amount=x.Amount,Address=x.Address,Institution=x.Institution,Note=x.Note,AttachmentCount=x.Attachments })); }
        return result;
    }

    public async Task<IReadOnlyList<ReportRow>> GetReportAsync(DateTime from, DateTime to)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync();
        const string sql = @"WITH Accounts AS
(
 SELECT k.Id,k.Kontonummer,k.Untergruppe,k.Detail,
 COALESCE(nrule.Richtung,CASE WHEN (k.Kontonummer BETWEEN 3000 AND 3999) OR (k.Kontonummer BETWEEN 7000 AND 7999)
        OR UPPER(CONCAT(ISNULL(k.Art,''),' ',ISNULL(k.Gruppe,''),' ',ISNULL(k.Untergruppe,''),' ',ISNULL(k.Detail,''))) LIKE '%EINNAHM%'
        OR UPPER(CONCAT(ISNULL(k.Art,''),' ',ISNULL(k.Gruppe,''),' ',ISNULL(k.Untergruppe,''),' ',ISNULL(k.Detail,''))) LIKE '%ERTRAG%'
      THEN N'Einnahme' ELSE N'Ausgabe' END) AS Direction
 FROM dbo.Kontenplan k
 OUTER APPLY (SELECT TOP (1) r.Richtung,r.Bezeichnung FROM dbo.NumberRangeRules r WHERE k.Kontonummer BETWEEN r.RangeStart AND r.RangeEnd ORDER BY (r.RangeEnd-r.RangeStart),r.RangeStart) nrule
 WHERE COALESCE(nrule.Richtung,N'')<>N'Neutral'
   AND LOWER(COALESCE(nrule.Bezeichnung,N'')) NOT LIKE N'%invest%'
   AND LOWER(COALESCE(nrule.Bezeichnung,N'')) NOT LIKE N'%amort%'
   AND LOWER(COALESCE(nrule.Bezeichnung,N'')) NOT LIKE N'%durchlauf%'
)
SELECT a.Id,a.Kontonummer,CONCAT(ISNULL(a.Untergruppe,''),CASE WHEN ISNULL(a.Detail,'')='' THEN '' ELSE ' · '+a.Detail END),a.Direction,
ISNULL((SELECT TOP (1) bd.Budgetwert FROM dbo.BudgetDetail bd INNER JOIN dbo.Budgetzeitraum bz ON bz.Id=bd.ZeitraumId
        WHERE bd.KontoId=a.Id AND @from>=bz.Startdatum AND @to<=bz.Enddatum ORDER BY bz.IstAktiv DESC,bz.Id DESC),0),
ISNULL(totals.Actual,0)
FROM Accounts a
OUTER APPLY
(
 SELECT SUM(s.SignedAmount) AS Actual
 FROM
 (
  SELECT CASE WHEN a.Direction=N'Einnahme' THEN
    CASE WHEN t.VonKontoId=a.Id AND t.NachKontoId IS NULL THEN t.Betrag WHEN t.VonKontoId IS NULL AND t.NachKontoId=a.Id THEN t.Betrag WHEN t.VonKontoId=a.Id THEN -t.Betrag ELSE t.Betrag END
   ELSE CASE WHEN t.VonKontoId=a.Id AND t.NachKontoId IS NULL THEN -t.Betrag WHEN t.VonKontoId IS NULL AND t.NachKontoId=a.Id THEN t.Betrag WHEN t.VonKontoId=a.Id THEN t.Betrag ELSE -t.Betrag END END AS SignedAmount
  FROM dbo.Transaktion t
  WHERE (t.VonKontoId=a.Id OR t.NachKontoId=a.Id) AND COALESCE(t.BudgetDatum,t.Datum)>=@from AND COALESCE(t.BudgetDatum,t.Datum)<=@to
 ) s
) totals
ORDER BY a.Kontonummer;";
        await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@from",from.Date);command.Parameters.AddWithValue("@to",to.Date);
        await using var reader=await command.ExecuteReaderAsync(); var rows=new List<ReportRow>();
        var days=Math.Max(1,(to.Date-from.Date).Days+1); var yearDays=DateTime.IsLeapYear(from.Year)?366:365;
        while(await reader.ReadAsync()) { var actual=reader.GetDecimal(5); rows.Add(new ReportRow { AccountId=reader.GetInt32(0),Number=reader.GetInt32(1),Account=reader.GetString(2),Direction=reader.GetString(3),BudgetYear=reader.GetDecimal(4),Actual=actual,Projection=actual*yearDays/days }); }
        return rows.Where(x=>x.BudgetYear!=0||x.Actual!=0).ToList();
    }

    public async Task<int> BookImportAsync(IEnumerable<ImportPreviewRow> rows, int? fallbackAccountId, int? fallbackInstitutionId, string source)
    {
        await using var connection=CreateConnection(); await connection.OpenAsync(); await using var tx=await connection.BeginTransactionAsync(); var count=0;
        try { foreach(var row in rows.Where(x=>x.Selected)) { var accountId=row.AccountId??fallbackAccountId??throw new InvalidOperationException($"Für '{row.Description}' fehlt das Gegenkonto.");var institutionId=row.InstitutionId??fallbackInstitutionId??throw new InvalidOperationException($"Für '{row.Description}' fehlt das Geldinstitut.");var hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{source}|{row.SourceId}|{row.Date:O}|{row.Amount}|{row.Description}")));
            await using var check=new SqlCommand("SELECT TOP (1) Id FROM dbo.Transaktion WHERE ImportHash=@hash",connection,(SqlTransaction)tx);check.Parameters.AddWithValue("@hash",hash);var existing=await check.ExecuteScalarAsync();if(existing is not null&&existing!=DBNull.Value){if(row.StagingId.HasValue)await ArchiveBankStagingAsync(connection,(SqlTransaction)tx,row.StagingId.Value,Convert.ToInt32(existing),"duplicate");continue;}
            const string sql=@"INSERT INTO dbo.Transaktion (Datum,BudgetDatum,VonKontoId,NachKontoId,Betrag,Notiz,AdresseId,GeldinstitutId,ImportQuelle,ImportHash)
OUTPUT INSERTED.Id VALUES(@date,@date,@from,@to,@amount,@note,@address,@bank,@source,@hash);";
            await using var command=new SqlCommand(sql,connection,(SqlTransaction)tx);command.Parameters.AddWithValue("@date",row.Date.Date);command.Parameters.AddWithValue("@from",row.IsIncome?accountId:DBNull.Value);command.Parameters.AddWithValue("@to",row.IsIncome?DBNull.Value:accountId);command.Parameters.AddWithValue("@amount",Math.Abs(row.Amount));command.Parameters.AddWithValue("@note",row.Description);command.Parameters.AddWithValue("@address",(object?)row.AddressId??DBNull.Value);command.Parameters.AddWithValue("@bank",institutionId);command.Parameters.AddWithValue("@source",source);command.Parameters.AddWithValue("@hash",hash);var transactionId=Convert.ToInt32(await command.ExecuteScalarAsync());if(row.StagingId.HasValue)await ArchiveBankStagingAsync(connection,(SqlTransaction)tx,row.StagingId.Value,transactionId,"booked");count++; }
            await tx.CommitAsync(); return count; } catch { await tx.RollbackAsync(); throw; }
    }

    public async Task<(int Inserted, int Skipped)> SaveBankStagingAsync(IEnumerable<ImportPreviewRow> rows, string? filePath)
    {
        var list=rows.ToList();if(list.Count==0)return(0,0);
        await using var connection=CreateConnection();await connection.OpenAsync();
        byte[]? fileHash=null;if(!string.IsNullOrWhiteSpace(filePath)&&File.Exists(filePath)){await using var hashStream=File.OpenRead(filePath);fileHash=await SHA256.HashDataAsync(hashStream);}
        const string batchSql=@"INSERT INTO dbo.BankImportBatch (SourceFormat,FileName,FileHash,AccountIban,Currency) OUTPUT INSERTED.Id VALUES(N'CAMT',@name,@hash,NULL,N'CHF');";
        int batchId;await using(var batch=new SqlCommand(batchSql,connection)){batch.Parameters.AddWithValue("@name",string.IsNullOrWhiteSpace(filePath)?"(WinUI-Import)":Path.GetFileName(filePath));batch.Parameters.Add(new SqlParameter("@hash",SqlDbType.VarBinary,32){Value=(object?)fileHash??DBNull.Value});batchId=Convert.ToInt32(await batch.ExecuteScalarAsync());}
        var inserted=0;var skipped=0;
        foreach(var row in list)
        {
            var signedAmount = (row.IsIncome ? 1 : -1) * Math.Abs(row.Amount);
            var uniq = $"{row.Date:yyyyMMdd}|{signedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}|{row.SourceId}";
            await using(var check=new SqlCommand("SELECT COUNT(1) FROM dbo.BankImportItem WHERE UniqKey=@key",connection)){check.Parameters.AddWithValue("@key",uniq);if(Convert.ToInt32(await check.ExecuteScalarAsync())>0){skipped++;continue;}}
            const string insertSql=@"INSERT INTO dbo.BankImportItem (BatchId,AccountIban,Currency,BookingDate,ValueDate,Amount,Direction,ServiceRef,[Text],CounterpartyName,CounterpartyIban,Uetr,PurposeCode,VorschlagAdresseId,VorschlagNachKontoId,VorschlagVonKontoId,VorschlagGeldinstitutId,UniqKey)
VALUES(@batch,N'',N'CHF',@date,@date,@amount,@direction,@ref,@text,@counterparty,NULL,NULL,NULL,@address,@to,@from,@institution,@key);SELECT CAST(SCOPE_IDENTITY() AS INT);";
            await using var insert=new SqlCommand(insertSql,connection);insert.Parameters.AddWithValue("@batch",batchId);insert.Parameters.AddWithValue("@date",row.Date.Date);insert.Parameters.AddWithValue("@amount",Math.Abs(row.Amount));insert.Parameters.AddWithValue("@direction",row.IsIncome?"CRDT":"DBIT");insert.Parameters.AddWithValue("@ref",(object?)row.SourceId??DBNull.Value);insert.Parameters.AddWithValue("@text",row.Description);insert.Parameters.AddWithValue("@counterparty",(object?)row.Counterparty??DBNull.Value);insert.Parameters.AddWithValue("@address",(object?)row.AddressId??DBNull.Value);insert.Parameters.AddWithValue("@to",!row.IsIncome?(object?)row.AccountId??DBNull.Value:DBNull.Value);insert.Parameters.AddWithValue("@from",row.IsIncome?(object?)row.AccountId??DBNull.Value:DBNull.Value);insert.Parameters.AddWithValue("@institution",(object?)row.InstitutionId??DBNull.Value);insert.Parameters.AddWithValue("@key",uniq);row.StagingId=Convert.ToInt32(await insert.ExecuteScalarAsync());inserted++;
        }
        return(inserted,skipped);
    }

    public async Task<IReadOnlyList<ImportPreviewRow>> LoadBankStagingAsync()
    {
        await using var connection=CreateConnection();await connection.OpenAsync();
        const string sql=@"SELECT i.Id,i.BookingDate,i.Amount,i.Direction,ISNULL(i.[Text],''),ISNULL(i.CounterpartyName,''),ISNULL(i.ServiceRef,''),i.VorschlagAdresseId,i.VorschlagNachKontoId,i.VorschlagVonKontoId,i.VorschlagGeldinstitutId,ISNULL(a.Name,''),ISNULL(k.Detail,''),ISNULL(g.Name,'')
FROM dbo.BankImportItem i LEFT JOIN dbo.Adresse a ON a.Id=i.VorschlagAdresseId LEFT JOIN dbo.Kontenplan k ON k.Id=COALESCE(i.VorschlagNachKontoId,i.VorschlagVonKontoId) LEFT JOIN dbo.Geldinstitut g ON g.Id=i.VorschlagGeldinstitutId WHERE i.[Status]=0 ORDER BY i.BookingDate,i.Id;";
        await using var command=new SqlCommand(sql,connection);await using var reader=await command.ExecuteReaderAsync();var result=new List<ImportPreviewRow>();
        while(await reader.ReadAsync()){var income=string.Equals(reader.GetString(3),"CRDT",StringComparison.OrdinalIgnoreCase);result.Add(new ImportPreviewRow{StagingId=reader.GetInt32(0),Date=reader.GetDateTime(1),Amount=reader.GetDecimal(2),IsIncome=income,Description=reader.GetString(4),Counterparty=reader.GetString(5),SourceId=reader.GetString(6),AddressId=reader.IsDBNull(7)?null:reader.GetInt32(7),AccountId=!reader.IsDBNull(8)?reader.GetInt32(8):reader.IsDBNull(9)?null:reader.GetInt32(9),InstitutionId=reader.IsDBNull(10)?null:reader.GetInt32(10),AddressName=reader.GetString(11),AccountName=reader.GetString(12),InstitutionName=reader.GetString(13)});}
        return result;
    }

    public async Task UpdateBankStagingAssignmentAsync(ImportPreviewRow row)
    {
        if(!row.StagingId.HasValue)return;await using var connection=CreateConnection();await connection.OpenAsync();
        const string sql=@"UPDATE dbo.BankImportItem SET Direction=@direction,VorschlagAdresseId=@address,VorschlagNachKontoId=@to,VorschlagVonKontoId=@from,VorschlagGeldinstitutId=@institution WHERE Id=@id;";
        await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@id",row.StagingId.Value);command.Parameters.AddWithValue("@direction",row.IsIncome?"CRDT":"DBIT");command.Parameters.AddWithValue("@address",(object?)row.AddressId??DBNull.Value);command.Parameters.AddWithValue("@to",!row.IsIncome?(object?)row.AccountId??DBNull.Value:DBNull.Value);command.Parameters.AddWithValue("@from",row.IsIncome?(object?)row.AccountId??DBNull.Value:DBNull.Value);command.Parameters.AddWithValue("@institution",(object?)row.InstitutionId??DBNull.Value);await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteBankStagingAsync(int id){await using var connection=CreateConnection();await connection.OpenAsync();await using var command=new SqlCommand("DELETE FROM dbo.BankImportItem WHERE Id=@id",connection);command.Parameters.AddWithValue("@id",id);await command.ExecuteNonQueryAsync();}

    private static async Task ArchiveBankStagingAsync(SqlConnection connection,SqlTransaction transaction,int stagingId,int transactionId,string reason)
    {
        const string sql=@"INSERT INTO dbo.BankImportItemArchive (SourceItemId,BatchId,AccountIban,Currency,BookingDate,ValueDate,Amount,Direction,ServiceRef,[Text],CounterpartyName,CounterpartyIban,Uetr,PurposeCode,VorschlagAdresseId,VorschlagNachKontoId,VorschlagVonKontoId,VorschlagGeldinstitutId,BookedTransaktionId,ArchiveReason)
SELECT Id,BatchId,AccountIban,Currency,BookingDate,ValueDate,Amount,Direction,ServiceRef,[Text],CounterpartyName,CounterpartyIban,Uetr,PurposeCode,VorschlagAdresseId,VorschlagNachKontoId,VorschlagVonKontoId,VorschlagGeldinstitutId,@transaction,@reason FROM dbo.BankImportItem WHERE Id=@id;DELETE FROM dbo.BankImportItem WHERE Id=@id;";
        await using var command=new SqlCommand(sql,connection,transaction);command.Parameters.AddWithValue("@id",stagingId);command.Parameters.AddWithValue("@transaction",transactionId);command.Parameters.AddWithValue("@reason",reason);await command.ExecuteNonQueryAsync();
    }

    private static async Task<(string Root, int MaxMb)> GetAttachmentSettingsAsync()
    {
        await using var connection=CreateConnection();await connection.OpenAsync();
        const string sql=@"SELECT [Key],[Value] FROM dbo.AppSetting WHERE [Key] IN ('AttachmentRoot','AttachmentMaxMB');";
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);await using var command=new SqlCommand(sql,connection);await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())values[reader.GetString(0)]=reader.GetString(1);
        var root=values.GetValueOrDefault("AttachmentRoot");if(string.IsNullOrWhiteSpace(root))root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"MyCoinFlow","Attachments");
        var max=int.TryParse(values.GetValueOrDefault("AttachmentMaxMB"),out var parsed)?Math.Clamp(parsed,1,1024):20;return(root,max);
    }
}
