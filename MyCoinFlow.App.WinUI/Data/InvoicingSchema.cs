using Microsoft.Data.SqlClient;
using MyCoinFlow.Services;
using System.Data;

namespace MyCoinFlow.WinUI.Data;

public static class InvoicingSchema
{
    public const int CurrentVersion = 2;
    private const int ExpectedTableCount = 8;
    private const int ExpectedForeignKeyCount = 7;
    private const int ExpectedUniqueIndexCount = 3;

    public static async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        RequireAuthenticated();
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
        OBJECT_ID(N'dbo.FakturierungErtragskonto'))) AS TableCount,
    (SELECT COUNT(*) FROM sys.foreign_keys
     WHERE name IN (
        N'FK_FaktEinstellung_Basiswaehrung',
        N'FK_FaktEinstellung_Kursgewinnkonto',
        N'FK_FaktEinstellung_Kursverlustkonto',
        N'FK_FaktWechselkurs_Waehrung',
        N'FK_FaktZahlungskonto_Waehrung',
        N'FK_FaktZahlungskonto_Geldinstitut',
        N'FK_FaktErtragskonto_Konto')) AS ForeignKeyCount,
    (SELECT COUNT(*) FROM sys.indexes
     WHERE name IN (
        N'UX_FaktWechselkurs_Waehrung_GueltigAb',
        N'UX_FaktMwst_Code_GueltigAb',
        N'UX_FaktZahlungskonto_Iban')) AS UniqueIndexCount;
""";

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Das Fakturierungsschema konnte nicht geprüft werden.");

        var version = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        var tableCount = reader.GetInt32(1);
        var foreignKeyCount = reader.GetInt32(2);
        var uniqueIndexCount = reader.GetInt32(3);
        if (version != CurrentVersion ||
            tableCount != ExpectedTableCount ||
            foreignKeyCount != ExpectedForeignKeyCount ||
            uniqueIndexCount != ExpectedUniqueIndexCount)
        {
            throw new InvalidOperationException(
                $"Fakturierungsschema unvollständig: Version {version}/{CurrentVersion}, " +
                $"Tabellen {tableCount}/{ExpectedTableCount}, Fremdschlüssel " +
                $"{foreignKeyCount}/{ExpectedForeignKeyCount}, eindeutige Indizes " +
                $"{uniqueIndexCount}/{ExpectedUniqueIndexCount}.");
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

    IF NOT EXISTS (SELECT 1 FROM dbo.FakturierungSchemaVersion WHERE Id = 1)
        INSERT dbo.FakturierungSchemaVersion (Id, [Version]) VALUES (1, 2);
    ELSE
        UPDATE dbo.FakturierungSchemaVersion
        SET [Version] = 2, AppliedAt = SYSDATETIME()
        WHERE Id = 1 AND [Version] < 2;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";
}
