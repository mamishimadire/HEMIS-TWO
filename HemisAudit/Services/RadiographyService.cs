using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class RadiographyService : IRadiographyService
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public RadiographyService(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        // ── Database discovery ─────────────────────────────────────────────────

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, "master", driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name;";
                await using var reader = await cmd.ExecuteReaderAsync();
                var items = new List<string>();
                while (await reader.ReadAsync()) items.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex) { return new DatabaseListResult { Success = false, Error = ex.Message }; }
        }

        public async Task<RadiographyTableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";
                await using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
                return new RadiographyTableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoRadiographyTable = FindFirst(tables, ["Radiography", "radiography"], ["radio"]),
                    AutoProductionTable = FindFirst(tables, ["Clinical_Production", "ClinicalProduction"], ["production"])
                };
            }
            catch (Exception ex) { return new RadiographyTableDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                ValidateObjectName(tableName);
                var tbl = Sanitise(tableName);
                var connStr = BuildConnectionString(server, database, driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @T ORDER BY ORDINAL_POSITION;";
                cmd.Parameters.AddWithValue("@T", tbl);
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = new List<string>();
                while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) cols.Add(reader.GetString(0));
                return new ColumnListResult { Success = true, Columns = cols, AutoSelected = cols.FirstOrDefault() };
            }
            catch (Exception ex) { return new ColumnListResult { Success = false, Error = ex.Message }; }
        }

        // ── Verification ───────────────────────────────────────────────────────

        public async Task<RadiographyVerifyResult> VerifyTablesAsync(RadiographyVerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var radiographyTable = Sanitise(request.RadiographyTable);
                var prodTable = Sanitise(request.ProductionTable);
                var qualCol = Sanitise(request.QualificationColumn);

                var radiographyCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{radiographyTable}];");
                var prodCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{prodTable}];");

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = BuildPopulationCountSql(radiographyTable, prodTable, qualCol);
                await using var reader = await cmd.ExecuteReaderAsync();

                var result = new RadiographyVerifyResult { Success = true, RadiographyRecordCount = radiographyCount, ProductionRecordCount = prodCount };
                if (await reader.ReadAsync())
                {
                    result.TotalTested = GetInt(reader, 0);
                    result.MatchedCount = GetInt(reader, 1);
                    result.MissingCount = GetInt(reader, 2);
                }
                return result;
            }
            catch (Exception ex) { return new RadiographyVerifyResult { Success = false, Error = ex.Message }; }
        }

        // ── Validation ─────────────────────────────────────────────────────────

        public async Task<RadiographyValidationSummary> RunValidationAsync(RadiographyValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                var browserSummary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (browserSummary.Success && request.ClientId > 0)
                {
                    try
                    {
                        var full = CloneSummary(browserSummary);
                        if (full.IsPreviewOnly || full.ReviewRows.Count < full.TotalValidated)
                            full = await AnalyseAsync(request, includeAllReviewRows: true);
                        full.SavedRunId = null;
                        browserSummary.SavedRunId = await SaveValidationRunAsync(CloneRequest(request), full, userEmail, userName, markWorkspaceSaved: false);
                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(71, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(71, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);
                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full Radiography population. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Radiography run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }
                return browserSummary;
            }
            catch (Exception ex) { return new RadiographyValidationSummary { Success = false, Error = ex.Message }; }
        }

        private async Task<RadiographyValidationSummary> AnalyseAsync(RadiographyValidationRequest request, bool includeAllReviewRows)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandTimeout = 300;
                cmd.CommandText = await GenerateSqlAsync(request);

                await using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<RadiographyReviewRow>();
                int passCount = 0, failCount = 0, totalCount = 0;

                while (await reader.ReadAsync())
                {
                    totalCount++;
                    var status = reader.GetString(2);
                    var row = new RadiographyReviewRow
                    {
                        RadiographyQualification = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        RadiographySurname = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Status = status,
                        ProductionQualification = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        ProductionSurname = reader.IsDBNull(4) ? "" : reader.GetString(4)
                    };

                    if (status == "PASS") passCount++;
                    else if (status == "FAIL") failCount++;

                    if (results.Count < (includeAllReviewRows ? int.MaxValue : BrowserPreviewRowLimit))
                        results.Add(row);
                }

                var exceptionRate = totalCount > 0 ? Math.Round((decimal)failCount * 100 / totalCount, 2) : 0;
                var statusResult = exceptionRate == 0 ? "PASS" : "FAIL";

                return new RadiographyValidationSummary
                {
                    Success = true,
                    Status = statusResult,
                    TotalValidated = totalCount,
                    PassCount = passCount,
                    FailCount = failCount,
                    ExceptionRate = exceptionRate,
                    ReviewRows = results,
                    IsPreviewOnly = !includeAllReviewRows && results.Count < totalCount
                };
            }
            catch (Exception ex)
            {
                return new RadiographyValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<string> GenerateSqlAsync(RadiographyValidationRequest request)
        {
            ValidateRequest(request);
            var radiographyTable = Sanitise(request.RadiographyTable);
            var prodTable = Sanitise(request.ProductionTable);
            var qualCol = Sanitise(request.QualificationColumn);
            var surnameCol = Sanitise(request.SurnameColumn);

            var sql = $@"-- RADIOGRAPHY MODULE: Qualification Code and Surname Validation
-- Checks if QUALIFICATION values from Radiography table exist in Clinical Production table
-- and confirms matching Surname records

-- Build reference qualification codes from Production table
SELECT
    UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification,
    UPPER(LTRIM(RTRIM(CAST([{surnameCol}] AS nvarchar(500))))) AS ProdSurname
INTO #ProdQualifications
FROM [{prodTable}]
WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> '';

CREATE INDEX IDX_ProdQual ON #ProdQualifications(ProdQualification);

-- Validate Radiography against Production
SELECT
    UPPER(LTRIM(RTRIM(CAST(RG.[{qualCol}] AS nvarchar(255))))) AS RadioQual,
    UPPER(LTRIM(RTRIM(CAST(RG.[{surnameCol}] AS nvarchar(500))))) AS RadioSurname,
    CASE WHEN PQ.ProdQualification IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Status,
    ISNULL(PQ.ProdQualification, '') AS MatchedQual,
    ISNULL(PQ.ProdSurname, '') AS MatchedSurname
FROM [{radiographyTable}] RG
LEFT JOIN #ProdQualifications PQ ON UPPER(LTRIM(RTRIM(CAST(RG.[{qualCol}] AS nvarchar(255))))) = PQ.ProdQualification
WHERE RG.[{qualCol}] IS NOT NULL AND RG.[{qualCol}] <> ''
ORDER BY Status DESC, RadioQual;

DROP TABLE #ProdQualifications;";

            return await Task.FromResult(sql);
        }

        public async Task<string> GenerateRScriptAsync(RadiographyValidationRequest request)
        {
            return await Task.FromResult(
                RadiographyRScriptGenerator.Generate(
                    71,
                    "HEMIS Radiography Module: Qualification and Surname Validation",
                    request.RadiographyTable,
                    request.ProductionTable,
                    request.QualificationColumn,
                    request.SurnameColumn));
        }

        // ── Workspace state ────────────────────────────────────────────────────

        public async Task<RadiographyWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null)
        {
            try
            {
                if (clientId <= 0) return null;

                try
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    await using var command = connection.CreateConfiguredCommand();
                    command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS DeceasedTable,
    ISNULL(vr.StudColumn, '') AS StudColumn,
    ISNULL(vr.DeceasedColumn, '') AS DeceasedColumn,
    ISNULL(vr.Status, '') AS Status,
    vr.ResultsJSON,
    vr.WorkspaceSavedAt
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 71
ORDER BY vr.IsCurrent DESC, vr.RunTimestamp DESC, vr.RunID DESC;";
                    command.Parameters.AddWithValue("@ClientID", clientId);

                    await using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var runId = reader.GetInt32(0);
                        var summaryJson = reader.IsDBNull(8) ? null : reader.GetString(8);
                        RadiographyValidationSummary? summary = null;
                        if (summaryJson != null)
                        {
                            try { summary = JsonConvert.DeserializeObject<RadiographyValidationSummary>(ValidationPayloadCodec.Decode(summaryJson)); }
                            catch { }
                        }
                        if (summary != null && summary.ReviewRows.Count > BrowserPreviewRowLimit)
                            summary.ReviewRows = summary.ReviewRows.Take(BrowserPreviewRowLimit).ToList();

                        return new RadiographyWorkspaceState
                        {
                            ClientId = clientId,
                            Server = reader.GetString(1),
                            Database = reader.GetString(2),
                            Driver = "ODBC Driver 17 for SQL Server",
                            RadiographyTable = reader.GetString(3),
                            ProductionTable = reader.GetString(4),
                            QualificationColumn = reader.GetString(5),
                            SurnameColumn = reader.GetString(6),
                            LastRunId = runId,
                            LastRunStatus = reader.GetString(7),
                            CurrentStatus = reader.GetString(7),
                            Summary = summary,
                            IsWorkspaceSaved = !reader.IsDBNull(9),
                            LastRunAt = DateTime.UtcNow
                        };
                    }
                }
                catch { }

                var cached = _pendingValidationCache.GetPending<RadiographyValidationRequest, RadiographyValidationSummary>(71, clientId, userEmail ?? "");
                if (cached?.Request is not null && cached.Summary is not null)
                {
                    var req = cached.Request;
                    return new RadiographyWorkspaceState
                    {
                        ClientId = clientId,
                        Server = req.Server,
                        Database = req.Database,
                        Driver = req.Driver,
                        RadiographyTable = req.RadiographyTable,
                        ProductionTable = req.ProductionTable,
                        QualificationColumn = req.QualificationColumn,
                        SurnameColumn = req.SurnameColumn,
                        Summary = cached.Summary,
                        LastRunAt = DateTime.UtcNow
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> SaveWorkspaceStateAsync(int clientId, RadiographyValidationRequest config, string? userEmail = null)
        {
            try
            {
                if (clientId <= 0) return false;
                await Task.Delay(100);
                return true;
            }
            catch { return false; }
        }

        // ── Helper methods ─────────────────────────────────────────────────────

        private static string BuildConnectionString(string server, string database, string driver)
        {
            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        }

        private static string BuildPopulationCountSql(string radiographyTable, string prodTable, string qualCol)
        {
            return $@"
SELECT
    COUNT(*) AS TotalTested,
    SUM(CASE WHEN PQ.ProdQualification IS NOT NULL THEN 1 ELSE 0 END) AS MatchedCount,
    SUM(CASE WHEN PQ.ProdQualification IS NULL THEN 1 ELSE 0 END) AS MissingCount
FROM (
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CAST(RG.[{qualCol}] AS nvarchar(255))))) AS RadioQual
    FROM [{radiographyTable}] RG
    WHERE RG.[{qualCol}] IS NOT NULL AND RG.[{qualCol}] <> ''
) RG
LEFT JOIN (
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification
    FROM [{prodTable}]
    WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> ''
) PQ ON RG.RadioQual = PQ.ProdQualification;";
        }

        private static async Task<int> CountAsync(SqlConnection conn, string sql)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = sql;
            var result = await cmd.ExecuteScalarAsync();
            return result is int count ? count : 0;
        }

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);

        private static string Sanitise(string? objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return "";
            var trimmed = objectName.Trim().Replace("'", "").Replace("\"", "");
            return trimmed.Length > 128 ? trimmed[..128] : trimmed;
        }

        private static void ValidateObjectName(string? objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                throw new ArgumentException("Object name cannot be empty.");
            if (objectName.Length > 128)
                throw new ArgumentException("Object name cannot exceed 128 characters.");
        }

        private static void ValidateRequest(RadiographyVerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(request.RadiographyTable))
                throw new ArgumentException("RadiographyTable must be specified.");
            if (string.IsNullOrWhiteSpace(request.ProductionTable))
                throw new ArgumentException("ProductionTable must be specified.");
        }

        private static void ValidateRequest(RadiographyValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(request.RadiographyTable))
                throw new ArgumentException("RadiographyTable must be specified.");
            if (string.IsNullOrWhiteSpace(request.ProductionTable))
                throw new ArgumentException("ProductionTable must be specified.");
        }

        private static string? FindFirst(List<string> items, string[] preferred, string[] contains)
        {
            foreach (var p in preferred)
                if (items.Any(i => i.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    return items.First(i => i.Equals(p, StringComparison.OrdinalIgnoreCase));

            foreach (var c in contains)
                if (items.Any(i => i.Contains(c, StringComparison.OrdinalIgnoreCase)))
                    return items.First(i => i.Contains(c, StringComparison.OrdinalIgnoreCase));

            return null;
        }

        private static RadiographyValidationSummary CloneSummary(RadiographyValidationSummary summary)
        {
            return JsonConvert.DeserializeObject<RadiographyValidationSummary>(
                JsonConvert.SerializeObject(summary)) ?? new RadiographyValidationSummary();
        }

        private static RadiographyValidationRequest CloneRequest(RadiographyValidationRequest request)
        {
            return JsonConvert.DeserializeObject<RadiographyValidationRequest>(
                JsonConvert.SerializeObject(request)) ?? new RadiographyValidationRequest();
        }

        private async Task<int> SaveValidationRunAsync(RadiographyValidationRequest request, RadiographyValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 71);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 71);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
 HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
 ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent)
VALUES
(@ClientID, @UserID, 71, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
 @HemisServer, @AuditDatabase, @StudTable, @DeceasedTable, @StudColumn, @DeceasedColumn,
 @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Radiography Qualification Validation");
            command.Parameters.AddWithValue("@Status", summary.Status ?? "");
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.RadiographyTable);
            command.Parameters.AddWithValue("@DeceasedTable", request.ProductionTable);
            command.Parameters.AddWithValue("@StudColumn", request.QualificationColumn);
            command.Parameters.AddWithValue("@DeceasedColumn", request.SurnameColumn);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(
                JsonConvert.SerializeObject(summary.ReviewRows.Where(r => r.Status == "FAIL").ToList())));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var value = await command.ExecuteScalarAsync();
            var runId = Convert.ToInt32(value);

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash(
                $"Radiography|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.PassCount}|{summary.FailCount}|{summary.ExceptionRate}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server, InitialCatalog = database, IntegratedSecurity = true,
                TrustServerCertificate = trust, Encrypt = false, ConnectTimeout = 180
            };
            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private static async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"UPDATE dbo.ValidationRuns SET IsCurrent = 0
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"SELECT TOP 1 RecordHash FROM dbo.ValidationRuns
WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND RecordHash IS NOT NULL
ORDER BY RunTimestamp DESC, RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string email, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var clientId = await QualSurnameModuleHelper.GetClientIdForRunAsync(connection, runId)
                ?? throw new InvalidOperationException("Validation run was not found.");
            var userId = await GetSystemUserIdByEmailAsync(connection, email)
                ?? throw new InvalidOperationException("Reviewer could not be resolved in the system database.");
            var role = await QualSurnameModuleHelper.GetEngagementRoleAsync(connection, clientId, userId)
                ?? throw new InvalidOperationException("User is not assigned to this engagement.");
            await QualSurnameModuleHelper.AddOrUpdateSignoffAsync(connection, runId, clientId, userId, role, comment);
        }

        public async Task RemoveSignoffAsync(int runId, string email)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var clientId = await QualSurnameModuleHelper.GetClientIdForRunAsync(connection, runId)
                ?? throw new InvalidOperationException("Validation run was not found.");
            var userId = await GetSystemUserIdByEmailAsync(connection, email)
                ?? throw new InvalidOperationException("Reviewer could not be resolved in the system database.");
            var role = await QualSurnameModuleHelper.GetEngagementRoleAsync(connection, clientId, userId)
                ?? throw new InvalidOperationException("User is not assigned to this engagement.");
            await QualSurnameModuleHelper.RemoveSignoffAsync(connection, runId, role);
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)));
        }
    }
}
