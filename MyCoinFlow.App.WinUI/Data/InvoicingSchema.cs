using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public static class InvoicingSchema
{
    public const int CurrentVersion = 7;
    private const int ExpectedTableCount = 21;
    private const int ExpectedForeignKeyCount = 27;
    private const int ExpectedUniqueIndexCount = 15;
    private const int ExpectedTriggerCount = 6;

    public static async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        await Task.Run(() => new DatabaseService().EnsureStweSchema(), cancellationToken);
        await using var connection = await OpenTenantConnectionAsync(cancellationToken);
        await using var command = new SqlCommand(InstallationSql, connection)
        {
            CommandTimeout = 45
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
        await VerifyAsync(connection, cancellationToken);
    }

    public static async Task<int> VerifyAsync(CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        await using var connection = await OpenTenantConnectionAsync(cancellationToken);
        return await VerifyAsync(connection, cancellationToken);
    }

    internal static async Task<SqlConnection> OpenTenantConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
        var expectedDatabase = ConnectionStrings.ActiveDatabaseName;
        var connection = new SqlConnection(ConnectionStrings.Current);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT CONVERT(nvarchar(128), DB_NAME());", connection);
            var actualDatabase = Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(actualDatabase, expectedDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Mandantengrenze verletzt: Erwartet wurde '{expectedDatabase}', geöffnet wurde '{actualDatabase}'.");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static void RequireAuthenticated()
    {
        if (!CurrentUserContext.IsAuthenticated)
            throw new UnauthorizedAccessException("Fakturieren erfordert eine angemeldete MyCoinFlow-Sitzung.");
    }

    internal static void RequireAdministrator()
    {
        RequireAuthenticated();
        if (!CurrentUserContext.IsAdmin)
            throw new UnauthorizedAccessException(
                "Finanzstammdaten dürfen nur von Administratorinnen und Administratoren geändert werden.");
    }

    private static async Task<int> VerifyAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
SELECT
    (SELECT TOP (1) [Version] FROM dbo.FakturierungSchemaVersion WHERE Id = 1) AS SchemaVersion,
    (SELECT COUNT(*) FROM sys.tables
     WHERE object_id IN (
        OBJECT_ID(N'dbo.FakturierungSchemaVersion'),
        OBJECT_ID(N'dbo.FakturierungWaehrung'),
        OBJECT_ID(N'dbo.FakturierungEinstellung'),
        OBJECT_ID(N'dbo.FakturierungNummernkreis'),
        OBJECT_ID(N'dbo.FakturierungWechselkurs'),
        OBJECT_ID(N'dbo.FakturierungMwstSatz'),
        OBJECT_ID(N'dbo.FakturierungZahlungskonto'),
        OBJECT_ID(N'dbo.FakturierungErtragskonto'),
        OBJECT_ID(N'dbo.FakturierungArtikel'),
        OBJECT_ID(N'dbo.FakturierungTextbaustein'),
        OBJECT_ID(N'dbo.FakturierungPositionsentwurf'),
        OBJECT_ID(N'dbo.FakturierungDokument'),
        OBJECT_ID(N'dbo.FakturierungDokumentPosition'),
        OBJECT_ID(N'dbo.FakturierungRechnung'),
        OBJECT_ID(N'dbo.FakturierungAbzahlungsrate'),
        OBJECT_ID(N'dbo.FakturierungOffenerPosten'),
        OBJECT_ID(N'dbo.FakturierungRevisionsereignis'),
        OBJECT_ID(N'dbo.FakturierungDokumentAusgabe'),
        OBJECT_ID(N'dbo.FakturierungEigentuemerProfil'),
        OBJECT_ID(N'dbo.FakturierungEinheitNutzung'),
        OBJECT_ID(N'dbo.FakturierungMietverhaeltnis'))) AS TableCount,
    (SELECT COUNT(*) FROM sys.foreign_keys
     WHERE name IN (
        N'FK_FaktEinstellung_Basiswaehrung',
        N'FK_FaktEinstellung_Kursgewinnkonto',
        N'FK_FaktEinstellung_Kursverlustkonto',
        N'FK_FaktWechselkurs_Waehrung',
        N'FK_FaktZahlungskonto_Waehrung',
        N'FK_FaktZahlungskonto_Geldinstitut',
        N'FK_FaktErtragskonto_Konto',
        N'FK_FaktArtikel_Mwst',
        N'FK_FaktArtikel_Ertragskonto',
        N'FK_FaktPosition_Artikel',
        N'FK_FaktPosition_Mwst',
        N'FK_FaktPosition_Ertragskonto',
        N'FK_FaktDokument_Vorgaenger',
        N'FK_FaktDokumentPosition_Dokument',
        N'FK_FaktRechnung_Dokument',
        N'FK_FaktRechnung_Referenz',
        N'FK_FaktRate_Rechnung',
        N'FK_FaktOffenerPosten_Rechnung',
        N'FK_FaktRevision_Dokument',
        N'FK_FaktRevision_Referenz',
        N'FK_FaktAusgabe_Dokument',
        N'FK_FaktAusgabe_Zahlungskonto',
        N'FK_FaktEigentuemerProfil_Eigentuemer',
        N'FK_FaktEigentuemerProfil_Rechnungsadresse',
        N'FK_FaktEinheitNutzung_Einheit',
        N'FK_FaktMietverhaeltnis_Einheit',
        N'FK_FaktMietverhaeltnis_Mieteradresse')) AS ForeignKeyCount,
    (SELECT COUNT(*) FROM sys.indexes
     WHERE name IN (
        N'UX_FaktWechselkurs_Waehrung_GueltigAb',
        N'UX_FaktMwst_Code_GueltigAb',
        N'UX_FaktZahlungskonto_Iban',
        N'UX_FaktArtikel_Artikelnummer',
        N'UX_FaktTextbaustein_Name',
        N'UX_FaktPosition_Kontext_Reihenfolge',
        N'UX_FaktDokument_Typ_Nummer',
        N'UX_FaktDokument_Fluss_AP05Typ',
        N'UX_FaktDokument_Fluss_Reihenfolge',
        N'UX_FaktDokument_Vorgaenger',
        N'UX_FaktDokumentPosition_Dokument_Reihenfolge',
        N'UX_FaktRechnung_Zahlungsreferenz',
        N'UX_FaktRate_Rechnung_Reihenfolge',
        N'UX_FaktRevision_Dokument_Reihenfolge',
        N'UX_FaktAusgabe_Zahlungsreferenz')) AS UniqueIndexCount,
    (SELECT COUNT(*) FROM sys.triggers
     WHERE name IN (
        N'TR_FaktEinheitNutzung_Zeitraum',
        N'TR_FaktMietverhaeltnis_Zeitraum',
        N'TR_FaktDokument_Unveraenderlich',
        N'TR_FaktRechnung_Unveraenderlich',
        N'TR_FaktRevision_AppendOnly',
        N'TR_FaktAusgabe_Unveraenderlich')) AS TriggerCount;
""";

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Das Fakturierungsschema konnte nicht geprüft werden.");

        var version = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        var tableCount = reader.GetInt32(1);
        var foreignKeyCount = reader.GetInt32(2);
        var uniqueIndexCount = reader.GetInt32(3);
        var triggerCount = reader.GetInt32(4);
        if (version != CurrentVersion ||
            tableCount != ExpectedTableCount ||
            foreignKeyCount != ExpectedForeignKeyCount ||
            uniqueIndexCount != ExpectedUniqueIndexCount ||
            triggerCount != ExpectedTriggerCount)
        {
            throw new InvalidOperationException(
                $"Fakturierungsschema unvollständig: Version {version}/{CurrentVersion}, " +
                $"Tabellen {tableCount}/{ExpectedTableCount}, Fremdschlüssel " +
                $"{foreignKeyCount}/{ExpectedForeignKeyCount}, eindeutige Indizes " +
                $"{uniqueIndexCount}/{ExpectedUniqueIndexCount}, Trigger " +
                $"{triggerCount}/{ExpectedTriggerCount}.");
        }

        return version;
    }

    private const string InstallationSql = """
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Kontenplan', N'U') IS NULL
    THROW 51000, N'Der vorhandene MyCoinFlow-Kontenplan fehlt in der aktiven Mandantendatenbank.', 1;
IF OBJECT_ID(N'dbo.Geldinstitut', N'U') IS NULL
    THROW 51002, N'Der vorhandene MyCoinFlow-Geldinstitut-Stamm fehlt in der aktiven Mandantendatenbank.', 1;
IF OBJECT_ID(N'dbo.Adresse', N'U') IS NULL
    THROW 51003, N'Der vorhandene MyCoinFlow-Adressstamm fehlt in der aktiven Mandantendatenbank.', 1;
IF OBJECT_ID(N'dbo.StweLiegenschaft', N'U') IS NULL
   OR OBJECT_ID(N'dbo.StweEinheit', N'U') IS NULL
   OR OBJECT_ID(N'dbo.StweEigentuemer', N'U') IS NULL
   OR OBJECT_ID(N'dbo.StweEinheitEigentum', N'U') IS NULL
    THROW 51004, N'Der vorhandene MyCoinFlow-Liegenschaftenstamm ist unvollständig.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock
        @Resource = N'MyCoinFlow.Fakturierung.Schema',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 15000;
    IF @LockResult < 0
        THROW 51001, N'Das Fakturierungsschema konnte nicht exklusiv initialisiert werden.', 1;

    IF OBJECT_ID(N'dbo.FakturierungSchemaVersion', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungSchemaVersion
        (
            Id tinyint NOT NULL CONSTRAINT PK_FakturierungSchemaVersion PRIMARY KEY,
            [Version] int NOT NULL,
            AppliedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungSchemaVersion_AppliedAt DEFAULT(SYSDATETIME()),
            CONSTRAINT CK_FakturierungSchemaVersion_Singleton CHECK (Id = 1)
        );
    END;

    IF OBJECT_ID(N'dbo.FakturierungWaehrung', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungWaehrung
        (
            Code char(3) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT PK_FakturierungWaehrung PRIMARY KEY,
            DisplayName nvarchar(80) NOT NULL,
            IsActive bit NOT NULL CONSTRAINT DF_FakturierungWaehrung_IsActive DEFAULT(0),
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungWaehrung_UpdatedAt DEFAULT(SYSDATETIME()),
            CONSTRAINT CK_FakturierungWaehrung_Code
                CHECK (Code = UPPER(Code) AND Code NOT LIKE '%[^A-Z]%')
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungWaehrung WHERE Code = 'CHF')
        INSERT dbo.FakturierungWaehrung (Code, DisplayName, IsActive)
        VALUES ('CHF', N'Schweizer Franken', 1);

    IF OBJECT_ID(N'dbo.FakturierungEinstellung', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungEinstellung
        (
            Id tinyint NOT NULL CONSTRAINT PK_FakturierungEinstellung PRIMARY KEY,
            IssuerName nvarchar(200) NOT NULL CONSTRAINT DF_FaktEinstellung_IssuerName DEFAULT(N''),
            IssuerStreet nvarchar(200) NOT NULL CONSTRAINT DF_FaktEinstellung_IssuerStreet DEFAULT(N''),
            IssuerPostalCode nvarchar(24) NOT NULL CONSTRAINT DF_FaktEinstellung_IssuerPostalCode DEFAULT(N''),
            IssuerCity nvarchar(120) NOT NULL CONSTRAINT DF_FaktEinstellung_IssuerCity DEFAULT(N''),
            IssuerCountryCode char(2) NOT NULL CONSTRAINT DF_FaktEinstellung_Country DEFAULT('CH'),
            VatNumber nvarchar(40) NOT NULL CONSTRAINT DF_FaktEinstellung_VatNumber DEFAULT(N''),
            InvoiceEmail nvarchar(256) NOT NULL CONSTRAINT DF_FaktEinstellung_InvoiceEmail DEFAULT(N''),
            InvoicePhone nvarchar(80) NOT NULL CONSTRAINT DF_FaktEinstellung_InvoicePhone DEFAULT(N''),
            DefaultPaymentDays smallint NOT NULL CONSTRAINT DF_FaktEinstellung_PaymentDays DEFAULT(30),
            BaseCurrency char(3) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT DF_FaktEinstellung_BaseCurrency DEFAULT('CHF'),
            ExchangeGainAccountId int NULL,
            ExchangeLossAccountId int NULL,
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FaktEinstellung_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL CONSTRAINT DF_FaktEinstellung_UpdatedBy DEFAULT(N''),
            CONSTRAINT CK_FaktEinstellung_Singleton CHECK (Id = 1),
            CONSTRAINT CK_FaktEinstellung_PaymentDays CHECK (DefaultPaymentDays BETWEEN 0 AND 365),
            CONSTRAINT CK_FaktEinstellung_CountryCode
                CHECK (IssuerCountryCode = UPPER(IssuerCountryCode) AND LEN(IssuerCountryCode) = 2),
            CONSTRAINT FK_FaktEinstellung_Basiswaehrung
                FOREIGN KEY (BaseCurrency) REFERENCES dbo.FakturierungWaehrung(Code),
            CONSTRAINT FK_FaktEinstellung_Kursgewinnkonto
                FOREIGN KEY (ExchangeGainAccountId) REFERENCES dbo.Kontenplan(Id),
            CONSTRAINT FK_FaktEinstellung_Kursverlustkonto
                FOREIGN KEY (ExchangeLossAccountId) REFERENCES dbo.Kontenplan(Id)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungEinstellung WHERE Id = 1)
        INSERT dbo.FakturierungEinstellung (Id) VALUES (1);

    IF OBJECT_ID(N'dbo.FakturierungNummernkreis', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungNummernkreis
        (
            DocumentType varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT PK_FakturierungNummernkreis PRIMARY KEY,
            DisplayName nvarchar(80) NOT NULL,
            Prefix nvarchar(12) NOT NULL,
            NextNumber bigint NOT NULL,
            Digits tinyint NOT NULL,
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungNummernkreis_UpdatedAt DEFAULT(SYSDATETIME()),
            CONSTRAINT CK_FakturierungNummernkreis_NextNumber CHECK (NextNumber >= 1),
            CONSTRAINT CK_FakturierungNummernkreis_Digits CHECK (Digits BETWEEN 3 AND 12)
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungNummernkreis WHERE DocumentType = 'OFFER')
        INSERT dbo.FakturierungNummernkreis VALUES ('OFFER', N'Offerte', N'OFF', 1, 5, SYSDATETIME());
    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungNummernkreis WHERE DocumentType = 'ORDER')
        INSERT dbo.FakturierungNummernkreis VALUES ('ORDER', N'Auftragsbestätigung', N'AUF', 1, 5, SYSDATETIME());
    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungNummernkreis WHERE DocumentType = 'DELIVERY')
        INSERT dbo.FakturierungNummernkreis VALUES ('DELIVERY', N'Lieferung', N'LIE', 1, 5, SYSDATETIME());
    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungNummernkreis WHERE DocumentType = 'INVOICE')
        INSERT dbo.FakturierungNummernkreis VALUES ('INVOICE', N'Rechnung', N'RE', 1, 5, SYSDATETIME());
    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungNummernkreis WHERE DocumentType = 'CORRECTION')
        INSERT dbo.FakturierungNummernkreis VALUES ('CORRECTION', N'Korrektur- / Stornobeleg', N'KOR', 1, 5, SYSDATETIME());

    IF OBJECT_ID(N'dbo.FakturierungWechselkurs', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungWechselkurs
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungWechselkurs PRIMARY KEY,
            DocumentCurrency char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            RateToBase decimal(19,8) NOT NULL,
            ValidFrom date NOT NULL,
            ValidTo date NULL,
            Source nvarchar(120) NOT NULL,
            IsActive bit NOT NULL CONSTRAINT DF_FakturierungWechselkurs_IsActive DEFAULT(1),
            CreatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungWechselkurs_CreatedAt DEFAULT(SYSDATETIME()),
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungWechselkurs_UpdatedAt DEFAULT(SYSDATETIME()),
            CONSTRAINT CK_FakturierungWechselkurs_Rate CHECK (RateToBase > 0),
            CONSTRAINT CK_FakturierungWechselkurs_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
            CONSTRAINT FK_FaktWechselkurs_Waehrung
                FOREIGN KEY (DocumentCurrency) REFERENCES dbo.FakturierungWaehrung(Code)
        );
        CREATE UNIQUE INDEX UX_FaktWechselkurs_Waehrung_GueltigAb
            ON dbo.FakturierungWechselkurs(DocumentCurrency, ValidFrom);
    END;

    IF OBJECT_ID(N'dbo.FakturierungMwstSatz', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungMwstSatz
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungMwstSatz PRIMARY KEY,
            Code nvarchar(24) NOT NULL,
            DisplayName nvarchar(100) NOT NULL,
            RatePercent decimal(7,4) NOT NULL,
            ValidFrom date NOT NULL,
            ValidTo date NULL,
            IsDefault bit NOT NULL CONSTRAINT DF_FakturierungMwstSatz_IsDefault DEFAULT(0),
            IsActive bit NOT NULL CONSTRAINT DF_FakturierungMwstSatz_IsActive DEFAULT(1),
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungMwstSatz_UpdatedAt DEFAULT(SYSDATETIME()),
            CONSTRAINT CK_FakturierungMwstSatz_Rate CHECK (RatePercent BETWEEN 0 AND 100),
            CONSTRAINT CK_FakturierungMwstSatz_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
        );
        CREATE UNIQUE INDEX UX_FaktMwst_Code_GueltigAb
            ON dbo.FakturierungMwstSatz(Code, ValidFrom);
    END;

    IF OBJECT_ID(N'dbo.FakturierungZahlungskonto', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungZahlungskonto
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungZahlungskonto PRIMARY KEY,
            DisplayName nvarchar(120) NOT NULL,
            Iban varchar(34) NOT NULL,
            CurrencyCode char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            IsQrIban bit NOT NULL CONSTRAINT DF_FakturierungZahlungskonto_IsQrIban DEFAULT(0),
            GeldinstitutId int NULL,
            IsActive bit NOT NULL CONSTRAINT DF_FakturierungZahlungskonto_IsActive DEFAULT(1),
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungZahlungskonto_UpdatedAt DEFAULT(SYSDATETIME()),
            CONSTRAINT CK_FakturierungZahlungskonto_Iban CHECK (LEN(Iban) BETWEEN 15 AND 34 AND Iban NOT LIKE '% %'),
            CONSTRAINT CK_FakturierungZahlungskonto_QrCurrency
                CHECK (IsQrIban = 0 OR CurrencyCode IN ('CHF', 'EUR')),
            CONSTRAINT CK_FakturierungZahlungskonto_Geldinstitut
                CHECK (IsActive = 0 OR GeldinstitutId IS NOT NULL),
            CONSTRAINT FK_FaktZahlungskonto_Waehrung
                FOREIGN KEY (CurrencyCode) REFERENCES dbo.FakturierungWaehrung(Code),
            CONSTRAINT FK_FaktZahlungskonto_Geldinstitut
                FOREIGN KEY (GeldinstitutId) REFERENCES dbo.Geldinstitut(Id)
        );
        CREATE UNIQUE INDEX UX_FaktZahlungskonto_Iban
            ON dbo.FakturierungZahlungskonto(Iban, CurrencyCode);
    END;

    IF COL_LENGTH(N'dbo.FakturierungZahlungskonto', N'GeldinstitutId') IS NULL
    BEGIN
        ALTER TABLE dbo.FakturierungZahlungskonto ADD GeldinstitutId int NULL;
        EXEC sys.sp_executesql N'
        ;WITH EindeutigesGeldinstitut AS
        (
            SELECT MIN(Id) AS Id,
                   UPPER(REPLACE(REPLACE(LTRIM(RTRIM(IBAN)), N'' '', N''''), N''-'', N'''')) AS NormalizedIban
            FROM dbo.Geldinstitut
            WHERE NULLIF(LTRIM(RTRIM(IBAN)), N'''') IS NOT NULL
            GROUP BY UPPER(REPLACE(REPLACE(LTRIM(RTRIM(IBAN)), N'' '', N''''), N''-'', N''''))
            HAVING COUNT(*) = 1
        )
        UPDATE zahlungskonto
        SET GeldinstitutId = geldinstitut.Id
        FROM dbo.FakturierungZahlungskonto zahlungskonto
        JOIN EindeutigesGeldinstitut geldinstitut
          ON geldinstitut.NormalizedIban =
             UPPER(REPLACE(REPLACE(LTRIM(RTRIM(zahlungskonto.Iban)), N'' '', N''''), N''-'', N''''));
        ';
    END;

    EXEC sys.sp_executesql N'
        UPDATE dbo.FakturierungZahlungskonto
        SET IsActive = 0, UpdatedAt = SYSDATETIME()
        WHERE GeldinstitutId IS NULL AND IsActive = 1;
    ';

    IF COL_LENGTH(N'dbo.FakturierungZahlungskonto', N'AccountId') IS NOT NULL
        ALTER TABLE dbo.FakturierungZahlungskonto ALTER COLUMN AccountId int NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungZahlungskonto')
          AND name = N'FK_FaktZahlungskonto_Geldinstitut')
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.FakturierungZahlungskonto
            ADD CONSTRAINT FK_FaktZahlungskonto_Geldinstitut
                FOREIGN KEY (GeldinstitutId) REFERENCES dbo.Geldinstitut(Id);
        ';

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungZahlungskonto')
          AND name = N'CK_FakturierungZahlungskonto_Geldinstitut')
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.FakturierungZahlungskonto
            ADD CONSTRAINT CK_FakturierungZahlungskonto_Geldinstitut
                CHECK (IsActive = 0 OR GeldinstitutId IS NOT NULL);
        ';

    IF OBJECT_ID(N'dbo.FakturierungErtragskonto', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungErtragskonto
        (
            AccountId int NOT NULL CONSTRAINT PK_FakturierungErtragskonto PRIMARY KEY,
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungErtragskonto_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL,
            CONSTRAINT FK_FaktErtragskonto_Konto
                FOREIGN KEY (AccountId) REFERENCES dbo.Kontenplan(Id)
        );
    END;

    IF OBJECT_ID(N'dbo.FakturierungArtikel', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungArtikel
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungArtikel PRIMARY KEY,
            ArticleNumber nvarchar(64) COLLATE Latin1_General_100_CI_AS NOT NULL,
            Designation nvarchar(200) NOT NULL,
            [Description] nvarchar(2000) NOT NULL
                CONSTRAINT DF_FakturierungArtikel_Description DEFAULT(N''),
            Unit nvarchar(40) NOT NULL,
            Category nvarchar(100) NOT NULL,
            IsActive bit NOT NULL CONSTRAINT DF_FakturierungArtikel_IsActive DEFAULT(1),
            SalePrice decimal(19,4) NOT NULL,
            VatRateId int NOT NULL,
            RevenueAccountId int NOT NULL,
            AncillaryClassification varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT DF_FakturierungArtikel_AncillaryClass DEFAULT('STANDARD'),
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungArtikel_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL,
            CONSTRAINT CK_FakturierungArtikel_Number
                CHECK (
                    LEN(ArticleNumber) BETWEEN 1 AND 64
                    AND ArticleNumber COLLATE Latin1_General_100_BIN2 =
                        UPPER(LTRIM(RTRIM(ArticleNumber))) COLLATE Latin1_General_100_BIN2
                    AND ArticleNumber NOT LIKE N'%  %'),
            CONSTRAINT CK_FakturierungArtikel_Price CHECK (SalePrice >= 0),
            CONSTRAINT CK_FakturierungArtikel_AncillaryClass CHECK (
                AncillaryClassification IN (
                    'STANDARD',
                    'TENANT_OPERATING_COST',
                    'REPAIR',
                    'RENEWAL',
                    'NON_TRANSFERABLE')),
            CONSTRAINT FK_FaktArtikel_Mwst
                FOREIGN KEY (VatRateId) REFERENCES dbo.FakturierungMwstSatz(Id),
            CONSTRAINT FK_FaktArtikel_Ertragskonto
                FOREIGN KEY (RevenueAccountId) REFERENCES dbo.FakturierungErtragskonto(AccountId)
        );
        CREATE UNIQUE INDEX UX_FaktArtikel_Artikelnummer
            ON dbo.FakturierungArtikel(ArticleNumber);
        CREATE INDEX IX_FaktArtikel_Aktiv_Kategorie
            ON dbo.FakturierungArtikel(IsActive, Category, Designation);
    END;

    IF OBJECT_ID(N'dbo.FakturierungTextbaustein', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungTextbaustein
        (
            Id int IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_FakturierungTextbaustein PRIMARY KEY,
            [Name] nvarchar(160) COLLATE Latin1_General_100_CI_AS NOT NULL,
            PlainText nvarchar(max) NOT NULL,
            FormattedText nvarchar(max) NULL,
            IsActive bit NOT NULL
                CONSTRAINT DF_FakturierungTextbaustein_IsActive DEFAULT(1),
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungTextbaustein_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL,
            CONSTRAINT CK_FakturierungTextbaustein_Name
                CHECK (LEN(LTRIM(RTRIM([Name]))) BETWEEN 1 AND 160),
            CONSTRAINT CK_FakturierungTextbaustein_PlainText
                CHECK (LEN(PlainText) > 0 AND DATALENGTH(PlainText) <= 200000),
            CONSTRAINT CK_FakturierungTextbaustein_FormattedText
                CHECK (
                    FormattedText IS NULL
                    OR (
                        DATALENGTH(FormattedText) <= 500000
                        AND LTRIM(FormattedText) LIKE N'{\rtf%'
                    ))
        );
        CREATE UNIQUE INDEX UX_FaktTextbaustein_Name
            ON dbo.FakturierungTextbaustein([Name]);
        CREATE INDEX IX_FaktTextbaustein_Aktiv_Name
            ON dbo.FakturierungTextbaustein(IsActive, [Name]);
    END;

    IF OBJECT_ID(N'dbo.FakturierungPositionsentwurf', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungPositionsentwurf
        (
            Id int IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_FakturierungPositionsentwurf PRIMARY KEY,
            ContextSource varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ContextSourceId int NOT NULL,
            SequenceNumber int NOT NULL,
            PositionType varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ArticleId int NULL,
            Designation nvarchar(200) NOT NULL,
            Category nvarchar(100) NOT NULL,
            Unit nvarchar(40) NOT NULL,
            Quantity decimal(19,4) NOT NULL,
            UnitPrice decimal(19,4) NOT NULL,
            VatRateId int NULL,
            VatCodeSnapshot nvarchar(32) NOT NULL,
            VatRatePercentSnapshot decimal(9,4) NULL,
            RevenueAccountId int NULL,
            RevenueAccountSnapshot nvarchar(200) NOT NULL,
            AncillaryClassificationSnapshot varchar(32)
                COLLATE Latin1_General_100_BIN2 NOT NULL,
            MainTextPlain nvarchar(max) NOT NULL,
            MainTextFormatted nvarchar(max) NULL,
            AdditionalTextPlain nvarchar(max) NOT NULL,
            AdditionalTextFormatted nvarchar(max) NULL,
            IsFooter bit NOT NULL
                CONSTRAINT DF_FakturierungPositionsentwurf_IsFooter DEFAULT(0),
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungPositionsentwurf_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL,
            CONSTRAINT CK_FakturierungPosition_Context
                CHECK (
                    ContextSource IN ('ARTICLE', 'PROPERTY')
                    AND ContextSourceId > 0),
            CONSTRAINT CK_FakturierungPosition_Sequence
                CHECK (SequenceNumber > 0 AND SequenceNumber % 10 = 0),
            CONSTRAINT CK_FakturierungPosition_Type
                CHECK (PositionType IN ('ARTICLE', 'TEXT')),
            CONSTRAINT CK_FakturierungPosition_TextLengths
                CHECK (
                    DATALENGTH(MainTextPlain) <= 200000
                    AND DATALENGTH(AdditionalTextPlain) <= 200000
                    AND (
                        MainTextFormatted IS NULL
                        OR DATALENGTH(MainTextFormatted) <= 500000)
                    AND (
                        AdditionalTextFormatted IS NULL
                        OR DATALENGTH(AdditionalTextFormatted) <= 500000)),
            CONSTRAINT CK_FakturierungPosition_Rtf
                CHECK (
                    (
                        MainTextFormatted IS NULL
                        OR LTRIM(MainTextFormatted) LIKE N'{\rtf%')
                    AND (
                        AdditionalTextFormatted IS NULL
                        OR LTRIM(AdditionalTextFormatted) LIKE N'{\rtf%')),
            CONSTRAINT CK_FakturierungPosition_ArticleValues
                CHECK (
                    (
                        PositionType = 'ARTICLE'
                        AND IsFooter = 0
                        AND LEN(LTRIM(RTRIM(Designation))) > 0
                        AND LEN(LTRIM(RTRIM(Category))) > 0
                        AND LEN(LTRIM(RTRIM(Unit))) > 0
                        AND Quantity > 0
                        AND UnitPrice >= 0
                        AND VatRateId IS NOT NULL
                        AND VatRatePercentSnapshot IS NOT NULL
                        AND RevenueAccountId IS NOT NULL
                    )
                    OR (
                        PositionType = 'TEXT'
                        AND ArticleId IS NULL
                        AND LEN(MainTextPlain) > 0
                        AND Category = N''
                        AND Unit = N''
                        AND Quantity = 0
                        AND UnitPrice = 0
                        AND VatRateId IS NULL
                        AND VatCodeSnapshot = N''
                        AND VatRatePercentSnapshot IS NULL
                        AND RevenueAccountId IS NULL
                        AND RevenueAccountSnapshot = N''
                        AND AdditionalTextPlain = N''
                        AND AdditionalTextFormatted IS NULL
                    )),
            CONSTRAINT CK_FakturierungPosition_AncillaryClass CHECK (
                AncillaryClassificationSnapshot IN (
                    'STANDARD',
                    'TENANT_OPERATING_COST',
                    'REPAIR',
                    'RENEWAL',
                    'NON_TRANSFERABLE')),
            CONSTRAINT FK_FaktPosition_Artikel
                FOREIGN KEY (ArticleId) REFERENCES dbo.FakturierungArtikel(Id),
            CONSTRAINT FK_FaktPosition_Mwst
                FOREIGN KEY (VatRateId) REFERENCES dbo.FakturierungMwstSatz(Id),
            CONSTRAINT FK_FaktPosition_Ertragskonto
                FOREIGN KEY (RevenueAccountId) REFERENCES dbo.FakturierungErtragskonto(AccountId)
        );
        CREATE UNIQUE INDEX UX_FaktPosition_Kontext_Reihenfolge
            ON dbo.FakturierungPositionsentwurf
                (ContextSource, ContextSourceId, SequenceNumber);
        CREATE INDEX IX_FaktPosition_Kontext_Typ
            ON dbo.FakturierungPositionsentwurf
                (ContextSource, ContextSourceId, PositionType, IsFooter);
    END;

    IF OBJECT_ID(N'dbo.FakturierungDokument', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungDokument
        (
            Id int IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_FakturierungDokument PRIMARY KEY,
            FlowId uniqueidentifier NOT NULL,
            FlowSequence int NOT NULL,
            DocumentType varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            DocumentNumber nvarchar(40) NOT NULL,
            DocumentDate date NOT NULL,
            [Status] varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Subject nvarchar(240) NOT NULL,
            ContextSource varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ContextSourceId int NOT NULL,
            ContextTitleSnapshot nvarchar(300) NOT NULL,
            ContextSubtitleSnapshot nvarchar(300) NOT NULL,
            IssuerName nvarchar(200) NOT NULL,
            IssuerStreet nvarchar(200) NOT NULL,
            IssuerPostalCode nvarchar(24) NOT NULL,
            IssuerCity nvarchar(120) NOT NULL,
            IssuerCountryCode char(2) NOT NULL,
            IssuerVatNumber nvarchar(40) NOT NULL,
            IssuerEmail nvarchar(256) NOT NULL,
            IssuerPhone nvarchar(80) NOT NULL,
            RecipientAddressIdSnapshot int NOT NULL,
            RecipientKind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            RecipientName nvarchar(200) NOT NULL,
            RecipientStreet nvarchar(200) NOT NULL,
            RecipientPostalCode nvarchar(24) NOT NULL,
            RecipientCity nvarchar(120) NOT NULL,
            RecipientCountry nvarchar(100) NOT NULL,
            CurrencyCode char(3) NOT NULL,
            ExchangeRateToBase decimal(19,8) NOT NULL,
            ExchangeRateSource nvarchar(120) NOT NULL,
            PreviousDocumentId int NULL,
            CreatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FakturierungDokument_CreatedAt DEFAULT(SYSDATETIME()),
            CreatedBy nvarchar(64) NOT NULL,
            TransitionedAt datetime2(0) NULL,
            TransitionedBy nvarchar(64) NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT CK_FakturierungDokument_Type
                CHECK (DocumentType IN ('OFFER', 'ORDER', 'DELIVERY', 'INVOICE', 'CORRECTION')),
            CONSTRAINT CK_FakturierungDokument_Status
                CHECK ([Status] IN ('DRAFT', 'TRANSFERRED', 'DEFINITIVE')),
            CONSTRAINT CK_FakturierungDokument_Context
                CHECK (
                    ContextSource IN ('ARTICLE', 'PROPERTY')
                    AND ContextSourceId > 0),
            CONSTRAINT CK_FakturierungDokument_Recipient
                CHECK (
                    RecipientAddressIdSnapshot > 0
                    AND RecipientKind IN ('CUSTOMER', 'OWNER', 'TENANT')
                    AND LEN(LTRIM(RTRIM(RecipientName))) > 0),
            CONSTRAINT CK_FakturierungDokument_Header
                CHECK (
                    LEN(LTRIM(RTRIM(DocumentNumber))) > 0
                    AND LEN(LTRIM(RTRIM(Subject))) > 0
                    AND LEN(LTRIM(RTRIM(ContextTitleSnapshot))) > 0
                    AND LEN(LTRIM(RTRIM(IssuerName))) > 0),
            CONSTRAINT CK_FakturierungDokument_Currency
                CHECK (
                    CurrencyCode = UPPER(CurrencyCode)
                    AND LEN(CurrencyCode) = 3
                    AND ExchangeRateToBase > 0
                    AND LEN(LTRIM(RTRIM(ExchangeRateSource))) > 0),
            CONSTRAINT CK_FakturierungDokument_FlowSequence
                CHECK (FlowSequence > 0 AND FlowSequence % 10 = 0),
            CONSTRAINT CK_FakturierungDokument_Transition
                CHECK (
                    ([Status] = 'DRAFT' AND TransitionedAt IS NULL AND TransitionedBy IS NULL)
                    OR
                    ([Status] IN ('TRANSFERRED', 'DEFINITIVE') AND TransitionedAt IS NOT NULL
                     AND LEN(LTRIM(RTRIM(TransitionedBy))) > 0)),
            CONSTRAINT FK_FaktDokument_Vorgaenger
                FOREIGN KEY (PreviousDocumentId) REFERENCES dbo.FakturierungDokument(Id)
        );
        CREATE UNIQUE INDEX UX_FaktDokument_Typ_Nummer
            ON dbo.FakturierungDokument(DocumentType, DocumentNumber);
        CREATE UNIQUE INDEX UX_FaktDokument_Fluss_AP05Typ
            ON dbo.FakturierungDokument(FlowId, DocumentType)
            WHERE DocumentType IN ('OFFER', 'ORDER', 'DELIVERY');
        CREATE UNIQUE INDEX UX_FaktDokument_Fluss_Reihenfolge
            ON dbo.FakturierungDokument(FlowId, FlowSequence);
        CREATE UNIQUE INDEX UX_FaktDokument_Vorgaenger
            ON dbo.FakturierungDokument(PreviousDocumentId)
            WHERE PreviousDocumentId IS NOT NULL;
        CREATE INDEX IX_FaktDokument_Status_Datum
            ON dbo.FakturierungDokument([Status], DocumentDate DESC, Id DESC);
    END;

    IF COL_LENGTH(N'dbo.FakturierungDokument', N'FlowSequence') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.FakturierungDokument ADD FlowSequence int NULL;
        ';
        EXEC sys.sp_executesql N'
        ;WITH GeordneteDokumente AS
        (
            SELECT Id,
                   ROW_NUMBER() OVER
                   (
                       PARTITION BY FlowId
                       ORDER BY CASE DocumentType
                           WHEN ''OFFER'' THEN 10
                           WHEN ''ORDER'' THEN 20
                           WHEN ''DELIVERY'' THEN 30
                           WHEN ''INVOICE'' THEN 40
                           ELSE 50
                       END, Id
                   ) * 10 AS NeueReihenfolge
            FROM dbo.FakturierungDokument
        )
        UPDATE dokument
        SET FlowSequence = geordnet.NeueReihenfolge
        FROM dbo.FakturierungDokument dokument
        JOIN GeordneteDokumente geordnet ON geordnet.Id = dokument.Id;
        ';
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.FakturierungDokument ALTER COLUMN FlowSequence int NOT NULL;
        ';
    END;

    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.FakturierungDokument')
          AND name = N'UX_FaktDokument_Fluss_Typ')
        DROP INDEX UX_FaktDokument_Fluss_Typ ON dbo.FakturierungDokument;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.FakturierungDokument')
          AND name = N'UX_FaktDokument_Fluss_AP05Typ')
        CREATE UNIQUE INDEX UX_FaktDokument_Fluss_AP05Typ
            ON dbo.FakturierungDokument(FlowId, DocumentType)
            WHERE DocumentType IN ('OFFER', 'ORDER', 'DELIVERY');

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.FakturierungDokument')
          AND name = N'UX_FaktDokument_Fluss_Reihenfolge')
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX UX_FaktDokument_Fluss_Reihenfolge
                ON dbo.FakturierungDokument(FlowId, FlowSequence);
        ';

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungDokument')
          AND name = N'CK_FakturierungDokument_FlowSequence')
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.FakturierungDokument ADD
                CONSTRAINT CK_FakturierungDokument_FlowSequence
                    CHECK (FlowSequence > 0 AND FlowSequence % 10 = 0);
        ';

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungDokument')
          AND name = N'CK_FakturierungDokument_Type'
          AND definition LIKE N'%CORRECTION%')
    BEGIN
        IF EXISTS (
            SELECT 1 FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungDokument')
              AND name = N'CK_FakturierungDokument_Type')
            ALTER TABLE dbo.FakturierungDokument DROP CONSTRAINT CK_FakturierungDokument_Type;
        ALTER TABLE dbo.FakturierungDokument ADD
            CONSTRAINT CK_FakturierungDokument_Type
                CHECK (DocumentType IN ('OFFER', 'ORDER', 'DELIVERY', 'INVOICE', 'CORRECTION'));
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungDokument')
          AND name = N'CK_FakturierungDokument_Status'
          AND definition LIKE N'%DEFINITIVE%')
    BEGIN
        IF EXISTS (
            SELECT 1 FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungDokument')
              AND name = N'CK_FakturierungDokument_Status')
            ALTER TABLE dbo.FakturierungDokument DROP CONSTRAINT CK_FakturierungDokument_Status;
        ALTER TABLE dbo.FakturierungDokument ADD
            CONSTRAINT CK_FakturierungDokument_Status
                CHECK ([Status] IN ('DRAFT', 'TRANSFERRED', 'DEFINITIVE'));
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungDokument')
          AND name = N'CK_FakturierungDokument_Transition'
          AND definition LIKE N'%DEFINITIVE%')
    BEGIN
        IF EXISTS (
            SELECT 1 FROM sys.check_constraints
            WHERE parent_object_id = OBJECT_ID(N'dbo.FakturierungDokument')
              AND name = N'CK_FakturierungDokument_Transition')
            ALTER TABLE dbo.FakturierungDokument DROP CONSTRAINT CK_FakturierungDokument_Transition;
        ALTER TABLE dbo.FakturierungDokument ADD
            CONSTRAINT CK_FakturierungDokument_Transition
                CHECK (
                    ([Status] = 'DRAFT' AND TransitionedAt IS NULL AND TransitionedBy IS NULL)
                    OR
                    ([Status] IN ('TRANSFERRED', 'DEFINITIVE')
                     AND TransitionedAt IS NOT NULL
                     AND LEN(LTRIM(RTRIM(TransitionedBy))) > 0));
    END;

    IF OBJECT_ID(N'dbo.FakturierungDokumentPosition', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungDokumentPosition
        (
            Id int IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_FakturierungDokumentPosition PRIMARY KEY,
            DocumentId int NOT NULL,
            SequenceNumber int NOT NULL,
            PositionType varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SourcePositionId int NULL,
            ArticleIdSnapshot int NULL,
            Designation nvarchar(200) NOT NULL,
            Category nvarchar(100) NOT NULL,
            Unit nvarchar(40) NOT NULL,
            Quantity decimal(19,4) NOT NULL,
            UnitPrice decimal(19,4) NOT NULL,
            VatCodeSnapshot nvarchar(32) NOT NULL,
            VatRatePercentSnapshot decimal(9,4) NULL,
            RevenueAccountSnapshot nvarchar(200) NOT NULL,
            AncillaryClassificationSnapshot varchar(32)
                COLLATE Latin1_General_100_BIN2 NOT NULL,
            MainTextPlain nvarchar(max) NOT NULL,
            MainTextFormatted nvarchar(max) NULL,
            AdditionalTextPlain nvarchar(max) NOT NULL,
            AdditionalTextFormatted nvarchar(max) NULL,
            IsFooter bit NOT NULL,
            CONSTRAINT CK_FakturierungDokumentPosition_Sequence
                CHECK (SequenceNumber > 0 AND SequenceNumber % 10 = 0),
            CONSTRAINT CK_FakturierungDokumentPosition_Type
                CHECK (PositionType IN ('ARTICLE', 'TEXT')),
            CONSTRAINT CK_FakturierungDokumentPosition_TextLengths
                CHECK (
                    DATALENGTH(MainTextPlain) <= 200000
                    AND DATALENGTH(AdditionalTextPlain) <= 200000
                    AND (MainTextFormatted IS NULL OR DATALENGTH(MainTextFormatted) <= 500000)
                    AND (AdditionalTextFormatted IS NULL OR DATALENGTH(AdditionalTextFormatted) <= 500000)),
            CONSTRAINT CK_FakturierungDokumentPosition_Rtf
                CHECK (
                    (MainTextFormatted IS NULL OR LTRIM(MainTextFormatted) LIKE N'{\rtf%')
                    AND
                    (AdditionalTextFormatted IS NULL OR LTRIM(AdditionalTextFormatted) LIKE N'{\rtf%')),
            CONSTRAINT CK_FakturierungDokumentPosition_Values
                CHECK (
                    (
                        PositionType = 'ARTICLE'
                        AND IsFooter = 0
                        AND LEN(LTRIM(RTRIM(Designation))) > 0
                        AND LEN(LTRIM(RTRIM(Category))) > 0
                        AND LEN(LTRIM(RTRIM(Unit))) > 0
                        AND Quantity > 0
                        AND UnitPrice >= 0
                        AND VatRatePercentSnapshot IS NOT NULL
                    )
                    OR
                    (
                        PositionType = 'TEXT'
                        AND ArticleIdSnapshot IS NULL
                        AND LEN(MainTextPlain) > 0
                        AND Category = N''
                        AND Unit = N''
                        AND Quantity = 0
                        AND UnitPrice = 0
                        AND VatCodeSnapshot = N''
                        AND VatRatePercentSnapshot IS NULL
                        AND RevenueAccountSnapshot = N''
                        AND AdditionalTextPlain = N''
                        AND AdditionalTextFormatted IS NULL
                    )),
            CONSTRAINT CK_FakturierungDokumentPosition_AncillaryClass
                CHECK (
                    AncillaryClassificationSnapshot IN (
                        'STANDARD',
                        'TENANT_OPERATING_COST',
                        'REPAIR',
                        'RENEWAL',
                        'NON_TRANSFERABLE')),
            CONSTRAINT FK_FaktDokumentPosition_Dokument
                FOREIGN KEY (DocumentId) REFERENCES dbo.FakturierungDokument(Id)
        );
        CREATE UNIQUE INDEX UX_FaktDokumentPosition_Dokument_Reihenfolge
            ON dbo.FakturierungDokumentPosition(DocumentId, SequenceNumber);
    END;


    IF OBJECT_ID(N'dbo.FakturierungRechnung', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungRechnung
        (
            DocumentId int NOT NULL CONSTRAINT PK_FakturierungRechnung PRIMARY KEY,
            InvoiceKind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ReferenceInvoiceDocumentId int NULL,
            AdjustmentReason nvarchar(500) NULL,
            FullGrossBasis decimal(19,2) NOT NULL,
            PreviouslyInvoicedGross decimal(19,2) NOT NULL,
            NetAmount decimal(19,2) NOT NULL,
            VatAmount decimal(19,2) NOT NULL,
            DiscountPercent decimal(7,4) NOT NULL,
            DiscountAmount decimal(19,2) NOT NULL,
            RoundingAdjustment decimal(19,2) NOT NULL,
            GrossAmount decimal(19,2) NOT NULL,
            BaseCurrencyCode char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            BaseGrossAmount decimal(19,2) NOT NULL,
            PaymentDays smallint NULL,
            DueDate date NULL,
            SkontoPercent decimal(7,4) NULL,
            SkontoDays smallint NULL,
            SkontoDueDate date NULL,
            SkontoAmount decimal(19,2) NULL,
            PaymentReference nvarchar(80) NULL,
            FinalizedAt datetime2(0) NOT NULL,
            FinalizedBy nvarchar(64) NOT NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT CK_FakturierungRechnung_Kind
                CHECK (InvoiceKind IN ('FULL', 'PARTIAL', 'FINAL', 'CORRECTION', 'CANCELLATION')),
            CONSTRAINT CK_FakturierungRechnung_Basis
                CHECK (
                    FullGrossBasis > 0
                    AND PreviouslyInvoicedGross >= 0
                    AND PreviouslyInvoicedGross < FullGrossBasis),
            CONSTRAINT CK_FakturierungRechnung_Discount
                CHECK (
                    DiscountPercent BETWEEN 0 AND 100
                    AND DiscountAmount >= 0),
            CONSTRAINT CK_FakturierungRechnung_Components
                CHECK (GrossAmount = NetAmount + VatAmount + RoundingAdjustment),
            CONSTRAINT CK_FakturierungRechnung_BaseCurrency
                CHECK (
                    BaseCurrencyCode = UPPER(BaseCurrencyCode)
                    AND BaseCurrencyCode NOT LIKE '%[^A-Z]%'
                    AND LEN(BaseCurrencyCode) = 3),
            CONSTRAINT CK_FakturierungRechnung_Positive
                CHECK (
                    InvoiceKind NOT IN ('FULL', 'PARTIAL', 'FINAL')
                    OR (
                        ReferenceInvoiceDocumentId IS NULL
                        AND AdjustmentReason IS NULL
                        AND NetAmount >= 0
                        AND VatAmount >= 0
                        AND GrossAmount > 0
                        AND BaseGrossAmount > 0
                        AND PaymentDays BETWEEN 0 AND 365
                        AND DueDate IS NOT NULL
                        AND LEN(LTRIM(RTRIM(COALESCE(PaymentReference, N'')))) > 0
                    )),
            CONSTRAINT CK_FakturierungRechnung_Adjustment
                CHECK (
                    InvoiceKind NOT IN ('CORRECTION', 'CANCELLATION')
                    OR (
                        ReferenceInvoiceDocumentId IS NOT NULL
                        AND LEN(LTRIM(RTRIM(COALESCE(AdjustmentReason, N'')))) > 0
                        AND NetAmount < 0
                        AND VatAmount = 0
                        AND DiscountPercent = 0
                        AND DiscountAmount = 0
                        AND RoundingAdjustment = 0
                        AND GrossAmount < 0
                        AND BaseGrossAmount < 0
                        AND PaymentDays IS NULL
                        AND DueDate IS NULL
                        AND SkontoPercent IS NULL
                        AND SkontoDays IS NULL
                        AND SkontoDueDate IS NULL
                        AND SkontoAmount IS NULL
                        AND PaymentReference IS NULL
                    )),
            CONSTRAINT CK_FakturierungRechnung_Skonto
                CHECK (
                    (
                        SkontoPercent IS NULL
                        AND SkontoDays IS NULL
                        AND SkontoDueDate IS NULL
                        AND SkontoAmount IS NULL
                    )
                    OR (
                        InvoiceKind IN ('FULL', 'PARTIAL', 'FINAL')
                        AND SkontoPercent > 0
                        AND SkontoPercent <= 100
                        AND SkontoDays BETWEEN 0 AND PaymentDays
                        AND SkontoDueDate IS NOT NULL
                        AND SkontoDueDate <= DueDate
                        AND SkontoAmount >= 0
                    )),
            CONSTRAINT FK_FaktRechnung_Dokument
                FOREIGN KEY (DocumentId) REFERENCES dbo.FakturierungDokument(Id),
            CONSTRAINT FK_FaktRechnung_Referenz
                FOREIGN KEY (ReferenceInvoiceDocumentId) REFERENCES dbo.FakturierungDokument(Id)
        );
        CREATE UNIQUE INDEX UX_FaktRechnung_Zahlungsreferenz
            ON dbo.FakturierungRechnung(PaymentReference)
            WHERE PaymentReference IS NOT NULL;
    END;

    IF OBJECT_ID(N'dbo.FakturierungAbzahlungsrate', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungAbzahlungsrate
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungAbzahlungsrate PRIMARY KEY,
            InvoiceDocumentId int NOT NULL,
            SequenceNumber int NOT NULL,
            DueDate date NOT NULL,
            Amount decimal(19,2) NOT NULL,
            Label nvarchar(160) NOT NULL,
            PaidAmount decimal(19,2) NOT NULL
                CONSTRAINT DF_FaktRate_PaidAmount DEFAULT(0),
            [Status] varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL
                CONSTRAINT DF_FaktRate_Status DEFAULT('OPEN'),
            CONSTRAINT CK_FakturierungRate_Sequence
                CHECK (SequenceNumber > 0 AND SequenceNumber % 10 = 0),
            CONSTRAINT CK_FakturierungRate_Amount
                CHECK (Amount > 0 AND PaidAmount BETWEEN 0 AND Amount),
            CONSTRAINT CK_FakturierungRate_Label
                CHECK (LEN(LTRIM(RTRIM(Label))) > 0),
            CONSTRAINT CK_FakturierungRate_Status
                CHECK ([Status] IN ('OPEN', 'PARTIALLY_PAID', 'PAID')),
            CONSTRAINT FK_FaktRate_Rechnung
                FOREIGN KEY (InvoiceDocumentId) REFERENCES dbo.FakturierungRechnung(DocumentId)
        );
        CREATE UNIQUE INDEX UX_FaktRate_Rechnung_Reihenfolge
            ON dbo.FakturierungAbzahlungsrate(InvoiceDocumentId, SequenceNumber);
    END;

    IF OBJECT_ID(N'dbo.FakturierungOffenerPosten', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungOffenerPosten
        (
            DocumentId int NOT NULL CONSTRAINT PK_FakturierungOffenerPosten PRIMARY KEY,
            CurrencyCode char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            BaseCurrencyCode char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            OriginalAmount decimal(19,2) NOT NULL,
            CorrectionAmount decimal(19,2) NOT NULL
                CONSTRAINT DF_FaktOffenerPosten_Correction DEFAULT(0),
            PaidAmount decimal(19,2) NOT NULL
                CONSTRAINT DF_FaktOffenerPosten_Paid DEFAULT(0),
            OpenAmount decimal(19,2) NOT NULL,
            BaseOriginalAmount decimal(19,2) NOT NULL,
            BaseCorrectionAmount decimal(19,2) NOT NULL
                CONSTRAINT DF_FaktOffenerPosten_BaseCorrection DEFAULT(0),
            BasePaidAmount decimal(19,2) NOT NULL
                CONSTRAINT DF_FaktOffenerPosten_BasePaid DEFAULT(0),
            BaseOpenAmount decimal(19,2) NOT NULL,
            DueDate date NOT NULL,
            [Status] varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FaktOffenerPosten_UpdatedAt DEFAULT(SYSDATETIME()),
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT CK_FakturierungOffenerPosten_Currency
                CHECK (
                    CurrencyCode = UPPER(CurrencyCode)
                    AND BaseCurrencyCode = UPPER(BaseCurrencyCode)
                    AND LEN(CurrencyCode) = 3
                    AND LEN(BaseCurrencyCode) = 3),
            CONSTRAINT CK_FakturierungOffenerPosten_Amounts
                CHECK (
                    OriginalAmount > 0
                    AND CorrectionAmount BETWEEN -OriginalAmount AND 0
                    AND PaidAmount = 0
                    AND OpenAmount = OriginalAmount + CorrectionAmount
                    AND BaseOriginalAmount > 0
                    AND BaseCorrectionAmount BETWEEN -BaseOriginalAmount AND 0
                    AND BasePaidAmount = 0
                    AND BaseOpenAmount = BaseOriginalAmount + BaseCorrectionAmount),
            CONSTRAINT CK_FakturierungOffenerPosten_Status
                CHECK (
                    ([Status] = 'OPEN' AND CorrectionAmount = 0 AND OpenAmount = OriginalAmount)
                    OR
                    ([Status] = 'CORRECTED' AND CorrectionAmount < 0 AND OpenAmount > 0)
                    OR
                    ([Status] = 'CANCELLED' AND CorrectionAmount = -OriginalAmount AND OpenAmount = 0)),
            CONSTRAINT FK_FaktOffenerPosten_Rechnung
                FOREIGN KEY (DocumentId) REFERENCES dbo.FakturierungRechnung(DocumentId)
        );
    END;

    IF OBJECT_ID(N'dbo.FakturierungRevisionsereignis', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungRevisionsereignis
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungRevisionsereignis PRIMARY KEY,
            DocumentId int NOT NULL,
            SequenceNumber int NOT NULL,
            EventType varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ReferenceDocumentId int NULL,
            Amount decimal(19,2) NOT NULL,
            CurrencyCode char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Narrative nvarchar(1000) NOT NULL,
            EventAt datetime2(0) NOT NULL
                CONSTRAINT DF_FaktRevision_EventAt DEFAULT(SYSDATETIME()),
            EventBy nvarchar(64) NOT NULL,
            CONSTRAINT CK_FakturierungRevision_Sequence
                CHECK (SequenceNumber > 0 AND SequenceNumber % 10 = 0),
            CONSTRAINT CK_FakturierungRevision_Type
                CHECK (EventType IN (
                    'INVOICE_FINALIZED',
                    'NEXT_PARTIAL_CREATED',
                    'NEXT_FINAL_CREATED',
                    'CORRECTION_CREATED',
                    'CANCELLATION_CREATED')),
            CONSTRAINT CK_FakturierungRevision_Content
                CHECK (
                    Amount <> 0
                    AND CurrencyCode = UPPER(CurrencyCode)
                    AND LEN(CurrencyCode) = 3
                    AND LEN(LTRIM(RTRIM(Narrative))) > 0
                    AND (
                        EventType NOT IN ('CORRECTION_CREATED', 'CANCELLATION_CREATED')
                        OR ReferenceDocumentId IS NOT NULL
                    )),
            CONSTRAINT FK_FaktRevision_Dokument
                FOREIGN KEY (DocumentId) REFERENCES dbo.FakturierungDokument(Id),
            CONSTRAINT FK_FaktRevision_Referenz
                FOREIGN KEY (ReferenceDocumentId) REFERENCES dbo.FakturierungDokument(Id)
        );
        CREATE UNIQUE INDEX UX_FaktRevision_Dokument_Reihenfolge
            ON dbo.FakturierungRevisionsereignis(DocumentId, SequenceNumber);
    END;

    IF OBJECT_ID(N'dbo.FakturierungDokumentAusgabe', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungDokumentAusgabe
        (
            DocumentId int NOT NULL CONSTRAINT PK_FakturierungDokumentAusgabe PRIMARY KEY,
            PaymentAccountId int NOT NULL,
            TemplateVersion smallint NOT NULL
                CONSTRAINT DF_FaktAusgabe_TemplateVersion DEFAULT(1),
            OutputKind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            PaymentAccountName nvarchar(120) NOT NULL,
            Iban varchar(34) NOT NULL,
            Bic nvarchar(16) NULL,
            AccountNumber nvarchar(80) NULL,
            CurrencyCode char(3) COLLATE Latin1_General_100_BIN2 NOT NULL,
            IsQrIban bit NOT NULL,
            ReferenceType varchar(4) COLLATE Latin1_General_100_BIN2 NOT NULL,
            PaymentReference nvarchar(80) NOT NULL,
            QrPayload nvarchar(997) NULL,
            CreatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FaktAusgabe_CreatedAt DEFAULT(SYSDATETIME()),
            CreatedBy nvarchar(64) NOT NULL,
            [RowVersion] rowversion NOT NULL,
            CONSTRAINT CK_FakturierungAusgabe_Template
                CHECK (TemplateVersion = 1),
            CONSTRAINT CK_FakturierungAusgabe_OutputKind
                CHECK (OutputKind IN ('SWISS_QR', 'ALTERNATIVE')),
            CONSTRAINT CK_FakturierungAusgabe_Iban
                CHECK (LEN(Iban) BETWEEN 15 AND 34 AND Iban NOT LIKE '% %'),
            CONSTRAINT CK_FakturierungAusgabe_Currency
                CHECK (
                    CurrencyCode = UPPER(CurrencyCode)
                    AND CurrencyCode NOT LIKE '%[^A-Z]%'
                    AND LEN(CurrencyCode) = 3),
            CONSTRAINT CK_FakturierungAusgabe_Reference
                CHECK (
                    (
                        OutputKind = 'SWISS_QR'
                        AND CurrencyCode IN ('CHF', 'EUR')
                        AND LEN(Iban) = 21
                        AND LEFT(Iban, 2) IN ('CH', 'LI')
                        AND LEN(LTRIM(RTRIM(COALESCE(QrPayload, N'')))) > 0
                        AND (
                            (
                                IsQrIban = 1
                                AND ReferenceType = 'QRR'
                                AND LEN(PaymentReference) = 27
                                AND PaymentReference NOT LIKE '%[^0-9]%'
                            )
                            OR
                            (
                                IsQrIban = 0
                                AND ReferenceType = 'SCOR'
                                AND LEN(PaymentReference) BETWEEN 5 AND 25
                            )
                        )
                    )
                    OR
                    (
                        OutputKind = 'ALTERNATIVE'
                        AND IsQrIban = 0
                        AND ReferenceType = 'SCOR'
                        AND LEN(PaymentReference) BETWEEN 5 AND 25
                        AND LEN(LTRIM(RTRIM(COALESCE(Bic, N'')))) > 0
                        AND QrPayload IS NULL
                    )),
            CONSTRAINT FK_FaktAusgabe_Dokument
                FOREIGN KEY (DocumentId) REFERENCES dbo.FakturierungRechnung(DocumentId),
            CONSTRAINT FK_FaktAusgabe_Zahlungskonto
                FOREIGN KEY (PaymentAccountId) REFERENCES dbo.FakturierungZahlungskonto(Id)
        );
        CREATE UNIQUE INDEX UX_FaktAusgabe_Zahlungsreferenz
            ON dbo.FakturierungDokumentAusgabe(PaymentReference);
    END;

    IF OBJECT_ID(N'dbo.FakturierungEigentuemerProfil', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungEigentuemerProfil
        (
            OwnerId int NOT NULL CONSTRAINT PK_FakturierungEigentuemerProfil PRIMARY KEY,
            BillingAddressId int NOT NULL,
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FaktEigentuemerProfil_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL,
            CONSTRAINT FK_FaktEigentuemerProfil_Eigentuemer
                FOREIGN KEY (OwnerId) REFERENCES dbo.StweEigentuemer(Id),
            CONSTRAINT FK_FaktEigentuemerProfil_Rechnungsadresse
                FOREIGN KEY (BillingAddressId) REFERENCES dbo.Adresse(Id)
        );
    END;

    IF OBJECT_ID(N'dbo.FakturierungEinheitNutzung', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungEinheitNutzung
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungEinheitNutzung PRIMARY KEY,
            UnitId int NOT NULL,
            UsageType varchar(24) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ValidFrom date NOT NULL,
            ValidTo date NULL,
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FaktEinheitNutzung_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL,
            CONSTRAINT CK_FakturierungEinheitNutzung_Type
                CHECK (UsageType IN ('OWNER_OCCUPIED', 'RENTED', 'VACANT')),
            CONSTRAINT CK_FakturierungEinheitNutzung_Dates
                CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
            CONSTRAINT FK_FaktEinheitNutzung_Einheit
                FOREIGN KEY (UnitId) REFERENCES dbo.StweEinheit(Id)
        );
        CREATE INDEX IX_FaktEinheitNutzung_Einheit_Zeitraum
            ON dbo.FakturierungEinheitNutzung(UnitId, ValidFrom, ValidTo);
    END;

    IF OBJECT_ID(N'dbo.FakturierungMietverhaeltnis', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.FakturierungMietverhaeltnis
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FakturierungMietverhaeltnis PRIMARY KEY,
            UnitId int NOT NULL,
            TenantAddressId int NOT NULL,
            ValidFrom date NOT NULL,
            ValidTo date NULL,
            AncillaryMode varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ContractReference nvarchar(160) NOT NULL,
            DirectBillingAllowed bit NOT NULL
                CONSTRAINT DF_FaktMietverhaeltnis_DirectBilling DEFAULT(0),
            DirectBillingApprovalReference nvarchar(240) NULL,
            UpdatedAt datetime2(0) NOT NULL
                CONSTRAINT DF_FaktMietverhaeltnis_UpdatedAt DEFAULT(SYSDATETIME()),
            UpdatedBy nvarchar(64) NOT NULL,
            CONSTRAINT CK_FakturierungMietverhaeltnis_Dates
                CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom),
            CONSTRAINT CK_FakturierungMietverhaeltnis_Mode
                CHECK (AncillaryMode IN ('INCLUDED', 'ADVANCE', 'FLAT_RATE')),
            CONSTRAINT CK_FakturierungMietverhaeltnis_Contract
                CHECK (LEN(LTRIM(RTRIM(ContractReference))) > 0),
            CONSTRAINT CK_FakturierungMietverhaeltnis_DirectBilling
                CHECK (
                    DirectBillingAllowed = 0
                    OR (
                        AncillaryMode IN ('ADVANCE', 'FLAT_RATE')
                        AND LEN(LTRIM(RTRIM(COALESCE(DirectBillingApprovalReference, N'')))) > 0
                    )),
            CONSTRAINT FK_FaktMietverhaeltnis_Einheit
                FOREIGN KEY (UnitId) REFERENCES dbo.StweEinheit(Id),
            CONSTRAINT FK_FaktMietverhaeltnis_Mieteradresse
                FOREIGN KEY (TenantAddressId) REFERENCES dbo.Adresse(Id)
        );
        CREATE INDEX IX_FaktMietverhaeltnis_Einheit_Zeitraum
            ON dbo.FakturierungMietverhaeltnis(UnitId, ValidFrom, ValidTo);
    END;

    EXEC sys.sp_executesql N'
    CREATE OR ALTER TRIGGER dbo.TR_FaktEinheitNutzung_Zeitraum
    ON dbo.FakturierungEinheitNutzung
    AFTER INSERT, UPDATE
    AS
    BEGIN
        SET NOCOUNT ON;

        IF EXISTS
        (
            SELECT 1
            FROM inserted i
            JOIN dbo.FakturierungEinheitNutzung existing WITH (UPDLOCK, HOLDLOCK)
              ON existing.UnitId = i.UnitId
             AND existing.Id <> i.Id
             AND i.ValidFrom <= COALESCE(existing.ValidTo, CONVERT(date, ''99991231''))
             AND existing.ValidFrom <= COALESCE(i.ValidTo, CONVERT(date, ''99991231''))
        )
            THROW 51010, N''Nutzungszeiträume derselben Einheit dürfen sich nicht überschneiden.'', 1;

    END;';

    EXEC sys.sp_executesql N'
    CREATE OR ALTER TRIGGER dbo.TR_FaktMietverhaeltnis_Zeitraum
    ON dbo.FakturierungMietverhaeltnis
    AFTER INSERT, UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;

        IF EXISTS
        (
            SELECT 1
            FROM inserted i
            JOIN dbo.FakturierungMietverhaeltnis existing WITH (UPDLOCK, HOLDLOCK)
              ON existing.UnitId = i.UnitId
             AND existing.Id <> i.Id
             AND i.ValidFrom <= COALESCE(existing.ValidTo, CONVERT(date, ''99991231''))
             AND existing.ValidFrom <= COALESCE(i.ValidTo, CONVERT(date, ''99991231''))
        )
            THROW 51012, N''Mietverhältnisse derselben Einheit dürfen sich nicht überschneiden.'', 1;

    END;';

    EXEC sys.sp_executesql N'
    CREATE OR ALTER TRIGGER dbo.TR_FaktDokument_Unveraenderlich
    ON dbo.FakturierungDokument
    AFTER UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;

        IF EXISTS (SELECT 1 FROM deleted WHERE [Status] = ''DEFINITIVE'')
            THROW 51020, N''Definitive Fakturierungsdokumente sind unveränderlich und dürfen nicht gelöscht werden.'', 1;
    END;';

    EXEC sys.sp_executesql N'
    CREATE OR ALTER TRIGGER dbo.TR_FaktRechnung_Unveraenderlich
    ON dbo.FakturierungRechnung
    AFTER UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;

        IF EXISTS (SELECT 1 FROM deleted)
            THROW 51022, N''Definitive Finanzsnapshots sind unveränderlich und dürfen nicht gelöscht werden.'', 1;
    END;';

    EXEC sys.sp_executesql N'
    CREATE OR ALTER TRIGGER dbo.TR_FaktRevision_AppendOnly
    ON dbo.FakturierungRevisionsereignis
    AFTER UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;

        IF EXISTS (SELECT 1 FROM deleted)
            THROW 51021, N''Revisionsereignisse sind unveränderlich und dürfen nicht gelöscht werden.'', 1;
    END;';

    EXEC sys.sp_executesql N'
    CREATE OR ALTER TRIGGER dbo.TR_FaktAusgabe_Unveraenderlich
    ON dbo.FakturierungDokumentAusgabe
    AFTER INSERT, UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;

        IF EXISTS (SELECT 1 FROM deleted)
            THROW 51023, N''Ausgabesnapshots sind unveränderlich und dürfen nicht gelöscht werden.'', 1;

        IF EXISTS
        (
            SELECT 1
            FROM inserted i
            JOIN dbo.FakturierungDokument d ON d.Id = i.DocumentId
            JOIN dbo.FakturierungRechnung r ON r.DocumentId = i.DocumentId
            JOIN dbo.FakturierungZahlungskonto p ON p.Id = i.PaymentAccountId
            LEFT JOIN dbo.Geldinstitut g ON g.Id = p.GeldinstitutId
            WHERE d.[Status] COLLATE Latin1_General_100_BIN2 <> ''DEFINITIVE''
               OR r.InvoiceKind COLLATE Latin1_General_100_BIN2 NOT IN (''FULL'', ''PARTIAL'', ''FINAL'')
               OR r.GrossAmount <= 0
               OR d.CurrencyCode COLLATE Latin1_General_100_BIN2 <> i.CurrencyCode COLLATE Latin1_General_100_BIN2
               OR p.CurrencyCode COLLATE Latin1_General_100_BIN2 <> i.CurrencyCode COLLATE Latin1_General_100_BIN2
               OR p.IsActive <> 1
               OR p.DisplayName COLLATE Latin1_General_100_BIN2 <> i.PaymentAccountName COLLATE Latin1_General_100_BIN2
               OR p.Iban COLLATE Latin1_General_100_BIN2 <> i.Iban COLLATE Latin1_General_100_BIN2
               OR COALESCE(g.BIC, N'''') COLLATE Latin1_General_100_BIN2 <> COALESCE(i.Bic, N'''') COLLATE Latin1_General_100_BIN2
               OR COALESCE(g.KontoNummer, N'''') COLLATE Latin1_General_100_BIN2 <> COALESCE(i.AccountNumber, N'''') COLLATE Latin1_General_100_BIN2
        )
            THROW 51024, N''Ausgabesnapshots dürfen nur aus einer definitiven positiven Rechnung und dem unveränderten aktiven Zahlungskonto entstehen.'', 1;
    END;';

    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungSchemaVersion WHERE Id = 1)
        INSERT dbo.FakturierungSchemaVersion (Id, [Version]) VALUES (1, 7);
    ELSE
        UPDATE dbo.FakturierungSchemaVersion
        SET [Version] = 7, AppliedAt = SYSDATETIME()
        WHERE Id = 1 AND [Version] < 7;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";
}
