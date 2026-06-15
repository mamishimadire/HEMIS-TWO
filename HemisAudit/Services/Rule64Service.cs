using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule64Service : IRule64Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;

        private readonly IConfiguration _configuration;

        public Rule64Service(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private static string BuildConnectionString(string server, string database, string driver)
        {
            if (server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                var pipe = ResolveLocalDbPipe(server);
                if (pipe != null)
                    return $"Server={pipe};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=30;";
            }

            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        }

        private static string? ResolveLocalDbPipe(string server)
        {
            try
            {
                var instance = server.Contains('\\') ? server.Split('\\').Last().Trim() : "MSSQLLocalDB";
                using (var start = Process.Start(new ProcessStartInfo("sqllocaldb", $"start \"{instance}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!)
                {
                    start.WaitForExit(8000);
                }

                using var info = Process.Start(new ProcessStartInfo("sqllocaldb", $"info \"{instance}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                })!;
                var output = info.StandardOutput.ReadToEnd();
                info.WaitForExit(3000);

                var match = System.Text.RegularExpressions.Regex.Match(
                    output,
                    @"Instance pipe name:\s*(np:[^\r\n]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                IntegratedSecurity = true,
                TrustServerCertificate = trust,
                Encrypt = false,
                ConnectTimeout = 180
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                await using var connection = new SqlConnection(BuildConnectionString(server, "master", driver));
                await connection.OpenAsync();

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name;";

                await using var reader = await command.ExecuteReaderAsync();
                var items = new List<string>();
                while (await reader.ReadAsync())
                    items.Add(reader.GetString(0));

                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex)
            {
                return new DatabaseListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule64TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                await using var connection = new SqlConnection(BuildConnectionString(server, database, driver));
                await connection.OpenAsync();

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";

                await using var reader = await command.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                return new Rule64TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoCregTable = FindFirst(tables, ["dbo_CREG", "CREG"], ["creg"]),
                    AutoProdTable = FindFirst(tables, ["dbo_STUD_PRODUCTION", "STUD_PRODUCTION", "PRODUCTION"], ["production", "prod"])
                };
            }
            catch (Exception ex)
            {
                return new Rule64TableDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule64ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                tableName = (tableName ?? "").Trim();
                ValidateObjectName(tableName);

                await using var connection = new SqlConnection(BuildConnectionString(server, database, driver));
                await connection.OpenAsync();

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION;";
                command.Parameters.AddWithValue("@TableName", tableName);

                await using var reader = await command.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                        columns.Add(reader.GetString(0));
                }

                return new Rule64ColumnDiscoveryResult { Success = true, Columns = columns };
            }
            catch (Exception ex)
            {
                return new Rule64ColumnDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule64VerifyResult> VerifyTablesAsync(Rule64VerifyRequest request)
        {
            try
            {
                NormalizeRequest(request.StudTable, request.CregTable, request.ProdTable, request.ColumnMapping, out var studTable, out var cregTable, out var prodTable, out _);

                await using var connection = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
                await connection.OpenAsync();

                return new Rule64VerifyResult
                {
                    Success = true,
                    StudCount = await ExecuteCountAsync(connection, $"SELECT COUNT_BIG(*) FROM [{Sanitise(studTable)}];"),
                    CregCount = await ExecuteCountAsync(connection, $"SELECT COUNT_BIG(*) FROM [{Sanitise(cregTable)}];"),
                    ProdCount = await ExecuteCountAsync(connection, $"SELECT COUNT_BIG(*) FROM [{Sanitise(prodTable)}];")
                };
            }
            catch (Exception ex)
            {
                return new Rule64VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule64ValidationSummary> RunValidationAsync(Rule64ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                NormalizeRequest(request.StudTable, request.CregTable, request.ProdTable, request.ColumnMapping, out var studTable, out var cregTable, out var prodTable, out var mapping);
                request.StudTable = studTable;
                request.CregTable = cregTable;
                request.ProdTable = prodTable;
                request.ColumnMapping = mapping;

                var summary = await AnalyseAsync(request, mapping);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error = $"Analysis completed, but the run could not be saved: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule64ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        private async Task<Rule64ValidationSummary> AnalyseAsync(Rule64ValidationRequest request, Rule64ColumnMapping mapping)
        {
            var sql = BuildValidationSql(request.StudTable, request.CregTable, request.ProdTable, mapping);

            await using var connection = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
            await connection.OpenAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();
            var passRows = new List<Rule64ReviewRow>();
            var failRows = new List<Rule64ReviewRow>();

            while (await reader.ReadAsync())
            {
                var row = new Rule64ReviewRow
                {
                    SourceTable = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                    StudentNo = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    CregStudentNo = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                    ProdStudentNo = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                    StudCompareValue = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim(),
                    CregCompareValue = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
                    ErrorCode = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim(),
                    ValidationResult = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim().ToUpperInvariant(),
                    ValidationExplanation = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim()
                };
                row.ExceptionCategory = ResolveExceptionCategory(row);

                if (row.ValidationResult == "PASS")
                    passRows.Add(row);
                else
                    failRows.Add(row);
            }

            failRows = DeduplicateRows(failRows);
            passRows = DeduplicateRows(passRows);
            AssignRowNumbers(failRows);
            AssignRowNumbers(passRows);

            var totalCount = passRows.Count + failRows.Count;
            var failCount = failRows.Count;
            var passCount = passRows.Count;
            var exceptionRate = totalCount == 0 ? 0m : Math.Round((decimal)failCount / totalCount * 100m, 2);

            var summary = new Rule64ValidationSummary
            {
                Success = true,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                CregTable = request.CregTable,
                ProdTable = request.ProdTable,
                ColumnMapping = mapping,
                ClientId = request.ClientId,
                TotalCount = totalCount,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionDetailCount = failCount,
                ExceptionRate = exceptionRate,
                Status = failCount == 0 ? "PASS" : "FAIL",
                PassRows = passRows,
                FailRows = failRows,
                ExceptionCategories = BuildExceptionCategories(passRows, failRows)
            };

            EnsureDerivedSummaryData(summary);
            return summary;
        }

        private static string BuildValidationSql(string studTable, string cregTable, string prodTable, Rule64ColumnMapping mapping)
        {
            // Each CTE is materialised into a temp table with a clustered index so every source
            // table is scanned exactly once and all joins use index seeks instead of hash joins
            // on full re-scans.  CregReference was the main bottleneck: the old CTE was evaluated
            // twice (once for STRING_AGG, once for the value-match join).
            return $@"
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#R64CregRef')     IS NOT NULL DROP TABLE #R64CregRef;
IF OBJECT_ID('tempdb..#R64CregSummary') IS NOT NULL DROP TABLE #R64CregSummary;
IF OBJECT_ID('tempdb..#R64ProdRef')     IS NOT NULL DROP TABLE #R64ProdRef;
IF OBJECT_ID('tempdb..#R64StudPop')     IS NOT NULL DROP TABLE #R64StudPop;

-- CREG: distinct (student, compare-value) pairs
SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{mapping.CregStudentNoCol}])))) AS CregStudentNo,
    UPPER(LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(255), [{mapping.CregCompareValueCol}]), '')))) AS CregCompareValue
INTO #R64CregRef
FROM [{Sanitise(cregTable)}] WITH (NOLOCK)
WHERE [{mapping.CregStudentNoCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), [{mapping.CregStudentNoCol}]))) <> '';

CREATE CLUSTERED INDEX IX_R64CregRef ON #R64CregRef(CregStudentNo, CregCompareValue);

-- CREG: one row per student with all compare-values concatenated
SELECT
    CregStudentNo,
    STRING_AGG(CASE WHEN CregCompareValue = '' THEN '[BLANK]' ELSE CregCompareValue END, ', ')
        WITHIN GROUP (ORDER BY CregCompareValue) AS CregCompareValue
INTO #R64CregSummary
FROM #R64CregRef
GROUP BY CregStudentNo;

CREATE CLUSTERED INDEX IX_R64CregSummary ON #R64CregSummary(CregStudentNo);

-- PRODUCTION: distinct student numbers
SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{mapping.ProdStudentNoCol}])))) AS ProdStudentNo
INTO #R64ProdRef
FROM [{Sanitise(prodTable)}] WITH (NOLOCK)
WHERE [{mapping.ProdStudentNoCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), [{mapping.ProdStudentNoCol}]))) <> '';

CREATE CLUSTERED INDEX IX_R64ProdRef ON #R64ProdRef(ProdStudentNo);

-- STUD: distinct (student, compare-value) pairs
SELECT DISTINCT
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{mapping.StudStudentNoCol}])))) AS StudentNo,
    UPPER(LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(255), [{mapping.StudCompareValueCol}]), '')))) AS StudCompareValue
INTO #R64StudPop
FROM [{Sanitise(studTable)}] WITH (NOLOCK)
WHERE [{mapping.StudStudentNoCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), [{mapping.StudStudentNoCol}]))) <> '';

CREATE CLUSTERED INDEX IX_R64StudPop ON #R64StudPop(StudentNo, StudCompareValue);

-- Final validation join — all lookups are now index seeks on temp tables
SELECT
    'STUD' AS SourceTable,
    ISNULL(P.StudentNo, '') AS StudentNo,
    ISNULL(CS.CregStudentNo, '') AS CregStudentNo,
    ISNULL(PR.ProdStudentNo, '') AS ProdStudentNo,
    ISNULL(P.StudCompareValue, '') AS StudCompareValue,
    CASE
        WHEN CM.CregStudentNo IS NOT NULL THEN ISNULL(CM.CregCompareValue, '')
        ELSE ISNULL(CS.CregCompareValue, '')
    END AS CregCompareValue,
    CASE
        WHEN CS.CregStudentNo IS NULL THEN 'NOTE'
        WHEN CM.CregStudentNo IS NULL THEN 'MISMATCH'
        ELSE ''
    END AS ErrorCode,
    CASE
        WHEN CS.CregStudentNo IS NULL THEN 'FAIL'
        WHEN CM.CregStudentNo IS NULL THEN 'FAIL'
        ELSE 'PASS'
    END AS ValidationResult,
    CASE
        WHEN CS.CregStudentNo IS NULL AND PR.ProdStudentNo IS NULL
            THEN 'FAIL: STUD.{mapping.StudStudentNoCol} student number ''' + P.StudentNo + ''' was not found in CREG.{mapping.CregStudentNoCol}. Note: this student should not appear in production. Confirmation: it was not found in STUD PRODUCTION.{mapping.ProdStudentNoCol}.'
        WHEN CS.CregStudentNo IS NULL
            THEN 'FAIL: STUD.{mapping.StudStudentNoCol} student number ''' + P.StudentNo + ''' was not found in CREG.{mapping.CregStudentNoCol}. Note: this student should not appear in production. Confirmation: it exists in STUD PRODUCTION.{mapping.ProdStudentNoCol} as ''' + PR.ProdStudentNo + '''.'
        WHEN CM.CregStudentNo IS NULL
            THEN 'FAIL: STUD.{mapping.StudStudentNoCol} student number ''' + P.StudentNo + ''' exists in CREG.{mapping.CregStudentNoCol}, but STUD.{mapping.StudCompareValueCol} value ''' + CASE WHEN ISNULL(P.StudCompareValue, '') = '' THEN '[BLANK]' ELSE P.StudCompareValue END + ''' does not match CREG.{mapping.CregCompareValueCol} value(s) ''' + CASE WHEN ISNULL(CS.CregCompareValue, '') = '' THEN '[BLANK]' ELSE CS.CregCompareValue END + '''.'
        ELSE 'PASS: STUD.{mapping.StudStudentNoCol} student number ''' + P.StudentNo + ''' exists in CREG.{mapping.CregStudentNoCol} and STUD.{mapping.StudCompareValueCol} value ''' + CASE WHEN ISNULL(P.StudCompareValue, '') = '' THEN '[BLANK]' ELSE P.StudCompareValue END + ''' matches CREG.{mapping.CregCompareValueCol} value ''' + CASE WHEN ISNULL(CM.CregCompareValue, '') = '' THEN '[BLANK]' ELSE CM.CregCompareValue END + '''.'
    END AS ValidationExplanation
FROM #R64StudPop P
LEFT JOIN #R64CregSummary CS ON CS.CregStudentNo = P.StudentNo
LEFT JOIN #R64CregRef      CM ON CM.CregStudentNo = P.StudentNo AND CM.CregCompareValue = P.StudCompareValue
LEFT JOIN #R64ProdRef      PR ON PR.ProdStudentNo = P.StudentNo
ORDER BY
    CASE WHEN CS.CregStudentNo IS NULL OR CM.CregStudentNo IS NULL THEN 0 ELSE 1 END,
    P.StudentNo;";
        }

        public string GenerateSql(Rule64ValidationRequest request)
        {
            NormalizeRequest(request.StudTable, request.CregTable, request.ProdTable, request.ColumnMapping, out var studTable, out var cregTable, out var prodTable, out var mapping);

            return $@"-- ============================================================
-- HEMIS RULE 64 - STUD to CREG Student Number Validation
-- Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Database  : {request.Database}
-- Tables    : [{Sanitise(studTable)}] STUD | [{Sanitise(cregTable)}] CREG | [{Sanitise(prodTable)}] STUD PRODUCTION
-- Join      : STUD.{mapping.StudStudentNoCol} -> CREG.{mapping.CregStudentNoCol}
-- Compare   : STUD.{mapping.StudCompareValueCol} -> CREG.{mapping.CregCompareValueCol}
-- Confirm   : STUD.{mapping.StudStudentNoCol} -> STUD PRODUCTION.{mapping.ProdStudentNoCol}
-- Rule      : PASS when the STUD student number exists in CREG and the compare values match
--           : FAIL when the student number is missing from CREG
--           : FAIL when the student number exists in CREG but the compare values differ
-- Fail Note : Student should not appear in production; confirmation uses STUD PRODUCTION
-- ============================================================
{BuildValidationSql(studTable, cregTable, prodTable, mapping)}";
        }

        private async Task<int> SaveValidationRunAsync(Rule64ValidationRequest request, Rule64ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 64);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 64);
            var persisted = CloneSummaryForPersistence(summary);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, BridgeTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent
)
VALUES
(
    @ClientID, @UserID, 64, 'STUD to CREG Student Number Validation', @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @CregTable, @ProdTable, @StudStudentNoCol, @CregStudentNoCol,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";

            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalCount);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@CregTable", request.CregTable);
            command.Parameters.AddWithValue("@ProdTable", request.ProdTable);
            command.Parameters.AddWithValue("@StudStudentNoCol", request.ColumnMapping.StudStudentNoCol);
            command.Parameters.AddWithValue("@CregStudentNoCol", request.ColumnMapping.CregStudentNoCol);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted.FailRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule64|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 64;";
            command.Parameters.AddWithValue("@RunID", runId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        public async Task<Rule64WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS CregTable,
    ISNULL(vr.BridgeTable, '') AS ProdTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 64 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(10) ? null : reader.GetString(10));
            if (summary != null)
                ApplyBrowserPreview(summary);

            var workspace = new Rule64WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "dbo_STUD" : reader.GetString(4),
                CregTable = reader.IsDBNull(5) ? "dbo_CREG" : reader.GetString(5),
                ProdTable = reader.IsDBNull(6) ? "dbo_STUD_PRODUCTION" : reader.GetString(6),
                CurrentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary = summary,
                ColumnMapping = summary?.ColumnMapping ?? new Rule64ColumnMapping()
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

            await reader.CloseAsync();

            workspace.Driver = "ODBC Driver 17 for SQL Server";
            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var mySignoff = signoffs.FirstOrDefault(s =>
                ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = mySignoff != null;
            workspace.CurrentUserSignoffComment = mySignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule64RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 64;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            ApplyBrowserPreview(summary);

            var clientId = reader.GetInt32(1);
            var review = new Rule64RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = clientId,
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary = summary
            };

            await reader.CloseAsync();

            summary.SavedRunId = review.RunId;
            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.Signoffs = signoffs;
            review.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            review.GeneratedSql = GenerateSql(new Rule64ValidationRequest
            {
                ClientId = clientId,
                Database = summary.Database,
                StudTable = summary.StudTable,
                CregTable = summary.CregTable,
                ProdTable = summary.ProdTable,
                ColumnMapping = summary.ColumnMapping
            });

            return review;
        }

        public async Task<Rule64WorkspaceSaveResult> SaveWorkspaceAsync(Rule64ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule64WorkspaceSaveResult { Success = false, Error = "Your account could not be resolved in the system database." };

                if (!request.RunId.HasValue || request.RunId.Value <= 0)
                    return new Rule64WorkspaceSaveResult { Success = false, Error = "No saved run exists. Run Rule 64 first." };

                NormalizeRequest(request.StudTable, request.CregTable, request.ProdTable, request.ColumnMapping, out var studTable, out var cregTable, out var prodTable, out var mapping);
                request.StudTable = studTable;
                request.CregTable = cregTable;
                request.ProdTable = prodTable;
                request.ColumnMapping = mapping;

                var cleared = await ClearSignoffsAsync(connection, request.RunId.Value);
                var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 64);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @StudTable,
    DeceasedTable        = @CregTable,
    BridgeTable          = @ProdTable,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    Status               = 'Needs Review',
    RecordHash           = @RecordHash
WHERE RunID = @RunID AND RuleNumber = 64;";
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@CregTable", request.CregTable);
                command.Parameters.AddWithValue("@ProdTable", request.ProdTable);
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule64|{request.RunId.Value}|{request.ClientId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                await command.ExecuteNonQueryAsync();

                var storedSummary = await GetStoredSummaryAsync(request.RunId.Value);
                if (storedSummary != null)
                {
                    storedSummary.StudTable = request.StudTable;
                    storedSummary.CregTable = request.CregTable;
                    storedSummary.ProdTable = request.ProdTable;
                    storedSummary.ColumnMapping = request.ColumnMapping;
                    await UpdateStoredSummaryAsync(connection, request.RunId.Value, storedSummary);
                }

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule64WorkspaceSaveResult
                {
                    Success = true,
                    Message = cleared > 0 ? $"Workspace saved. {cleared} signoff(s) were cleared." : "Workspace saved.",
                    SignoffsCleared = cleared > 0,
                    ClearedSignoffCount = cleared,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule64WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule64WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                    return new Rule64WorkspaceSaveResult { Success = false, Error = "Saved run not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var cleared = await ClearSignoffsAsync(connection, runId);
                var previousHash = await GetLatestValidationHashAsync(connection, clientId.Value, 64);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    RecordHash           = @RecordHash
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginWorkspaceEdit|Rule64|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", runId);
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule64WorkspaceSaveResult
                {
                    Success = true,
                    Message = cleared > 0 ? $"Workspace unlocked for editing. {cleared} signoff(s) cleared." : "Workspace unlocked for editing.",
                    SignoffsCleared = cleared > 0,
                    ClearedSignoffCount = cleared,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule64WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail)
                ?? throw new InvalidOperationException("Your account could not be resolved in the system database.");
            var role = await GetRunEngagementRoleAsync(connection, runId, reviewerId)
                ?? throw new InvalidOperationException("You are not assigned to this engagement.");
            var clientId = await GetClientIdForRunAsync(connection, runId)
                ?? throw new InvalidOperationException("Validation run not found.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
    UPDATE dbo.ReviewSignoffs SET SignoffRole=@Role, ReviewType='Final', Comment=@Comment, SignedOffAt=GETDATE() WHERE RunID=@RunID AND ReviewerID=@ReviewerID;
ELSE
    INSERT INTO dbo.ReviewSignoffs (ClientID,RunID,ReviewerID,SignoffRole,ReviewType,Comment,SignedOffAt)
    VALUES (@ClientID,@RunID,@ReviewerID,@Role,'Final',@Comment,GETDATE());";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            command.Parameters.AddWithValue("@Role", role);
            await command.ExecuteNonQueryAsync();
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail)
                ?? throw new InvalidOperationException("Your account could not be resolved.");

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);
            await command.ExecuteNonQueryAsync();
        }

        private static void ApplyBrowserPreview(Rule64ValidationSummary? summary)
        {
            if (summary == null) return;

            var failRows = summary.FailRows ?? new List<Rule64ReviewRow>();
            var passRows = summary.PassRows ?? new List<Rule64ReviewRow>();

            summary.IsPreviewOnly = failRows.Count > BrowserPreviewRowLimit || passRows.Count > BrowserPreviewRowLimit;
            summary.FailRows = failRows.Take(BrowserPreviewRowLimit).ToList();
            summary.PassRows = passRows.Take(BrowserPreviewRowLimit).ToList();
            summary.PreviewLimit = summary.IsPreviewOnly ? BrowserPreviewRowLimit : 0;
        }

        private static void EnsureDerivedSummaryData(Rule64ValidationSummary? summary)
        {
            if (summary == null)
                return;

            summary.PassRows = DeduplicateRows(summary.PassRows ?? new List<Rule64ReviewRow>());
            summary.FailRows = DeduplicateRows(summary.FailRows ?? new List<Rule64ReviewRow>());
            summary.ExceptionCategories ??= new List<Rule64ExceptionCategoryViewModel>();
            AssignRowNumbers(summary.PassRows);
            AssignRowNumbers(summary.FailRows);

            var hasDetailRows = summary.PassRows.Count > 0 || summary.FailRows.Count > 0;
            if (hasDetailRows)
            {
                summary.PassCount = summary.PassRows.Count;
                summary.FailCount = summary.FailRows.Count;
                summary.TotalCount = summary.PassCount + summary.FailCount;
            }
            else if (summary.TotalCount <= 0)
            {
                summary.TotalCount = summary.PassCount + summary.FailCount;
            }

            summary.ExceptionDetailCount = hasDetailRows
                ? summary.FailRows.Count
                : Math.Max(summary.ExceptionDetailCount, summary.FailRows.Count);
            summary.ExceptionRate = summary.TotalCount == 0
                ? 0m
                : Math.Round((decimal)summary.FailCount / summary.TotalCount * 100m, 2);

            if (string.IsNullOrWhiteSpace(summary.Status))
                summary.Status = summary.FailCount == 0 ? "PASS" : "FAIL";

            if (summary.PassRows.Count > 0 || summary.FailRows.Count > 0)
                summary.ExceptionCategories = BuildExceptionCategories(summary.PassRows, summary.FailRows);
        }

        private static List<Rule64ExceptionCategoryViewModel> BuildExceptionCategories(
            IReadOnlyCollection<Rule64ReviewRow> passRows,
            IReadOnlyCollection<Rule64ReviewRow> failRows)
        {
            return passRows
                .Concat(failRows)
                .GroupBy(row => ResolveExceptionCategory(row), StringComparer.OrdinalIgnoreCase)
                .Select(group => new Rule64ExceptionCategoryViewModel
                {
                    Category = group.Key,
                    Description = GetExceptionCategoryDescription(group.Key),
                    Count = group.Count()
                })
                .OrderByDescending(category => category.Count)
                .ThenBy(category => category.Category, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<Rule64ReviewRow> DeduplicateRows(IEnumerable<Rule64ReviewRow> rows)
        {
            var deduplicated = new List<Rule64ReviewRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows ?? Enumerable.Empty<Rule64ReviewRow>())
            {
                row.ExceptionCategory = ResolveExceptionCategory(row);
                var key = string.Join("|", new[]
                {
                    row.SourceTable?.Trim() ?? "",
                    row.StudentNo?.Trim() ?? "",
                    row.CregStudentNo?.Trim() ?? "",
                    row.ProdStudentNo?.Trim() ?? "",
                    row.StudCompareValue?.Trim() ?? "",
                    row.CregCompareValue?.Trim() ?? "",
                    row.ValidationResult?.Trim() ?? "",
                    row.ErrorCode?.Trim() ?? ""
                });

                if (!seen.Add(key))
                    continue;

                deduplicated.Add(row);
            }

            return deduplicated;
        }

        private static void AssignRowNumbers(List<Rule64ReviewRow> rows)
        {
            for (var index = 0; index < rows.Count; index++)
                rows[index].RowNumber = index + 1;
        }

        private static string ResolveExceptionCategory(Rule64ReviewRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.ExceptionCategory))
            {
                var existing = row.ExceptionCategory.Trim().ToUpperInvariant();
                if (existing is "PASS_FOUND_IN_CREG"
                    or "PASS_FOUND_IN_CREG__VALUE_MATCH"
                    or "VALUE_MISMATCH__FOUND_IN_CREG"
                    or "NOT_FOUND_IN_CREG__NOT_IN_PRODUCTION"
                    or "NOT_FOUND_IN_CREG__FOUND_IN_PRODUCTION")
                    return existing;
            }

            if (string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase))
                return "PASS_FOUND_IN_CREG__VALUE_MATCH";

            if (!string.IsNullOrWhiteSpace(row.CregStudentNo))
                return "VALUE_MISMATCH__FOUND_IN_CREG";

            return string.IsNullOrWhiteSpace(row.ProdStudentNo)
                ? "NOT_FOUND_IN_CREG__NOT_IN_PRODUCTION"
                : "NOT_FOUND_IN_CREG__FOUND_IN_PRODUCTION";
        }

        private static string GetExceptionCategoryDescription(string category) =>
            category.ToUpperInvariant() switch
            {
                "PASS_FOUND_IN_CREG" => "Student No found in CREG",
                "PASS_FOUND_IN_CREG__VALUE_MATCH" => "Student No found in CREG and the compare values match",
                "VALUE_MISMATCH__FOUND_IN_CREG" => "Student No found in CREG but the STUD and CREG compare values differ",
                "NOT_FOUND_IN_CREG__NOT_IN_PRODUCTION" => "Student No not found in CREG and not found in STUD PRODUCTION",
                "NOT_FOUND_IN_CREG__FOUND_IN_PRODUCTION" => "Student No not found in CREG but found in STUD PRODUCTION",
                _ => category
            };

        public async Task<Rule64ValidationSummary?> GetStoredSummaryAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 ResultsJSON
FROM dbo.ValidationRuns
WHERE RunID = @RunID AND RuleNumber = 64;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(0) ? null : reader.GetString(0));
            if (summary != null)
            {
                summary.SavedRunId = runId;
                EnsureDerivedSummaryData(summary);
            }
            return summary;
        }

        private static Rule64ValidationSummary CloneSummaryForPersistence(Rule64ValidationSummary source) =>
            JsonConvert.DeserializeObject<Rule64ValidationSummary>(JsonConvert.SerializeObject(source)) ?? new Rule64ValidationSummary();

        private static Rule64ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded))
                    return null;
                var summary = JsonConvert.DeserializeObject<Rule64ValidationSummary>(decoded);
                EnsureDerivedSummaryData(summary);
                return summary;
            }
            catch
            {
                return null;
            }
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule64ValidationSummary summary)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            await command.ExecuteNonQueryAsync();
        }

        private static void NormalizeRequest(
            string studTableIn,
            string cregTableIn,
            string prodTableIn,
            Rule64ColumnMapping? mappingIn,
            out string studTable,
            out string cregTable,
            out string prodTable,
            out Rule64ColumnMapping mapping)
        {
            studTable = (studTableIn ?? "dbo_STUD").Trim();
            cregTable = (cregTableIn ?? "dbo_CREG").Trim();
            prodTable = (prodTableIn ?? "dbo_STUD_PRODUCTION").Trim();
            mapping = mappingIn ?? new Rule64ColumnMapping();

            mapping.StudStudentNoCol = ColumnOrDefault(mapping.StudStudentNoCol, "_007");
            mapping.CregStudentNoCol = ColumnOrDefault(mapping.CregStudentNoCol, "_007");
            mapping.StudCompareValueCol = ColumnOrDefault(mapping.StudCompareValueCol, "_001");
            mapping.CregCompareValueCol = ColumnOrDefault(mapping.CregCompareValueCol, "_001");
            mapping.ProdStudentNoCol = ColumnOrDefault(mapping.ProdStudentNoCol, "IAGSTNO");

            ValidateObjectName(studTable);
            ValidateObjectName(cregTable);
            ValidateObjectName(prodTable);
            ValidateObjectName(mapping.StudStudentNoCol);
            ValidateObjectName(mapping.CregStudentNoCol);
            ValidateObjectName(mapping.StudCompareValueCol);
            ValidateObjectName(mapping.CregCompareValueCol);
            ValidateObjectName(mapping.ProdStudentNoCol);
        }

        private static string ColumnOrDefault(string? value, string defaultValue) =>
            string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

        private static string? FindFirst(List<string> items, string[] exactMatches, string[] partials)
        {
            foreach (var exact in exactMatches)
            {
                if (items.Any(item => string.Equals(item, exact, StringComparison.OrdinalIgnoreCase)))
                    return exact;
            }

            foreach (var partial in partials)
            {
                var match = items.FirstOrDefault(item => item.IndexOf(partial, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static void ValidateObjectName(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Object name cannot be blank.");

            if (name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-')))
                throw new InvalidOperationException($"Invalid object name '{name}'.");
        }

        private static string ComputeHash(string input)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        private static async Task<int> ExecuteCountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments WHERE ClientID = @ClientID AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<string?> GetRunEngagementRoleAsync(SqlConnection connection, int runId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 uca.EngagementRole FROM dbo.UserClientAssignments uca
INNER JOIN dbo.ValidationRuns vr ON vr.ClientID = uca.ClientID
WHERE vr.RunID = @RunID AND uca.UserID = @UserID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@UserID", userId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 64;";
            command.Parameters.AddWithValue("@RunID", runId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = 0 WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var result = await command.ExecuteScalarAsync();
            if (string.Equals(Convert.ToString(result), "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This engagement is archived and cannot accept new validation runs.");
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber ORDER BY RunTimestamp DESC, RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT rs.SignoffRole, rs.Comment, rs.SignedOffAt, u.Email,
       LTRIM(RTRIM(ISNULL(u.FirstName,'')+' '+ISNULL(u.LastName,''))) AS ReviewerName,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var items = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
            {
                items.Add(new RunSignoffViewModel
                {
                    SignoffRole = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Comment = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    SignedOffAt = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2),
                    ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ReviewerName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    IsCurrentUser = !reader.IsDBNull(5) && reader.GetBoolean(5)
                });
            }

            return items;
        }

        private async Task<int> ClearSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            return count;
        }

        private async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns WHERE RunID = @RunID
    AND (WorkspaceSavedAt IS NOT NULL OR EXISTS (
        SELECT 1 FROM dbo.ReviewSignoffs rs WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
    }
}
