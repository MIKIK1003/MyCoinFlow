using System.Collections.ObjectModel;

namespace MyCoinFlow.Models
{
    public sealed record DmsDocumentChanges(
        string? Title,
        string? Category,
        DmsBelegart? DocumentType,
        string? Description,
        string? Keywords,
        string? Note,
        DmsBearbeitungsstatus WorkflowStatus,
        string? Responsible,
        DateTime? DocumentDate,
        decimal? RecognizedAmount,
        int? AddressId,
        bool IsWarrantyCertificate,
        DateTime? WarrantyExpiresAt,
        DateTime? DueAt,
        DateTime? RetainUntil);

    public sealed class DmsDocumentGroup
    {
        public DmsDocumentGroup(string key, string title, IEnumerable<DmsDocument> documents)
        {
            Key = key;
            Title = title;
            Documents = new ObservableCollection<DmsDocument>(documents);
        }

        public string Key { get; }
        public string Title { get; }
        public ObservableCollection<DmsDocument> Documents { get; }
        public int Count => Documents.Count;
        public int OpenCount => Documents.Count(d => d.Bearbeitungsstatus != DmsBearbeitungsstatus.Erledigt);
        public string Summary => OpenCount == 0
            ? $"{Count} Dokumente · vollständig bearbeitet"
            : $"{Count} Dokumente · {OpenCount} offen";
    }

    public sealed class DmsVersionEntry
    {
        public int Id { get; set; }
        public int AttachmentId { get; set; }
        public int VersionNumber { get; set; }
        public string FileName { get; set; } = "";
        public string FolderRel { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedBy { get; set; }
        public string? Comment { get; set; }
        public bool IsCurrent { get; set; }
        public string VersionText => IsCurrent ? $"v{VersionNumber} · aktuell" : $"v{VersionNumber}";
        public string CreatedText => $"{CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm} · {CreatedBy ?? "System"}";
        public string CommentDisplay => string.IsNullOrWhiteSpace(Comment) ? "Keine Versionsnotiz" : Comment;
    }

    public sealed class DmsActivityEntry
    {
        public int Id { get; set; }
        public int? AttachmentId { get; set; }
        public string ActivityType { get; set; } = "";
        public DateTime OccurredAtUtc { get; set; }
        public string? Username { get; set; }
        public string? Description { get; set; }
        public string Title => string.IsNullOrWhiteSpace(Description) ? ActivityType : Description;
        public string Subtitle => $"{OccurredAtUtc.ToLocalTime():dd.MM.yyyy HH:mm} · {Username ?? "System"}";
    }

    public sealed record DmsFileInfo(
        int Id,
        string FileName,
        string OriginalName,
        string FolderRel,
        long SizeBytes,
        DateTime ImportedAtUtc,
        int CurrentVersion,
        string? ContentHash,
        DateTime LastChangedAtUtc,
        DateTime? RetainUntil,
        string DisplayTitle);
}
