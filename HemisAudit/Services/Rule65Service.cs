using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule65Service : IRule65Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;

        private readonly IConfiguration _configuration;

        public Rule65Service(IConfiguration configuration)
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

        public async Task<Rule65TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule65TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoCancellationTable = FindFirst(tables,
                        ["canceliation list", "cancellation list", "CANCELLATION_LIST", "CANCELIATION_LIST"],
                        ["canceliation", "cancellation", "cancel"]),
                    AutoClientTable = FindFirst(tables,
                        ["CENSUS_LIST_CLIENT", "dbo_CENSUS_LIST_CLIENT"],
                        ["census_list_client", "current_census"])
                };
            }
            catch (Exception ex)
            {
                return new Rule65TableDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule65ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                ValidateObjectName(tableName);

                await using var connection = new SqlConnection(BuildConnectionString(server, database, driver));
                await connection.OpenAsync();

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION;";
                command.Parameters.AddWithValue("@TableName", tableName.Trim());

                await using var reader = await command.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                        columns.Add(reader.GetString(0));
                }

                return new Rule65ColumnDiscoveryResult
                {
                    Success = true,
                    Columns = columns,
                    AutoStudentNoCol = FindFirst(columns, ["STD_NO"], ["std_no", "student_no", "studentno"]),
                    AutoQualificationCol = FindFirst(columns, ["QUAL"], ["qual", "qualification"]),
                    AutoSubjectCol = FindFirst(columns, ["SUBJ"], ["subj", "subject"]),
                    AutoCancelDateCol = FindFirst(columns, ["CANCEL"], ["cancel", "cancel_date"]),
                    AutoCensusDateCol = FindFirst(columns, ["CENSUS"], ["census", "census_date"]),
                    AutoCurrentCensusCol = FindFirst(columns, ["CURRENT_CENSUS"], ["current_census", "currentcensus"])
                };
            }
            catch (Exception ex)
            {
                return new Rule65ColumnDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule65VerifyResult> VerifyTablesAsync(Rule65VerifyRequest request)
        {
            try
            {
                NormalizeRequest(
                    request.CancellationTable,
                    request.ClientTable,
                    request.ColumnMapping,
                    out var cancellationTable,
                    out var clientTable,
                    out _);

                await using var connection = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
                await connection.OpenAsync();

                return new Rule65VerifyResult
                {
                    Success = true,
                    CancellationCount = await ExecuteCountAsync(connection, $"SELECT COUNT_BIG(*) FROM [{Sanitise(cancellationTable)}];"),
                    ClientCount = await ExecuteCountAsync(connection, $"SELECT COUNT_BIG(*) FROM [{Sanitise(clientTable)}];")
                };
            }
            catch (Exception ex)
            {
                return new Rule65VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule65ValidationSummary> RunValidationAsync(Rule65ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                NormalizeRequest(
                    request.CancellationTable,
                    request.ClientTable,
                    request.ColumnMapping,
                    out var cancellationTable,
                    out var clientTable,
                    out var mapping);

                request.CancellationTable = cancellationTable;
                request.ClientTable = clientTable;
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
                return new Rule65ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        private async Task<Rule65ValidationSummary> AnalyseAsync(Rule65ValidationRequest request, Rule65ColumnMapping mapping)
        {
            var sql = BuildValidationSql(request.CancellationTable, request.ClientTable, mapping);

            await using var connection = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
            await connection.OpenAsync();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();
            var passRows = new List<Rule65ReviewRow>();
            var failRows = new List<Rule65ReviewRow>();

            while (await reader.ReadAsync())
            {
                var row = new Rule65ReviewRow
                {
                    SourceTable = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                    StudentNo = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    Qualification = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                    Subject = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                    CancelDate = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim(),
                    CensusDate = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
                    CurrentCensus = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim(),
                    ExceptionCategory = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim(),
                    ErrorCode = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim(),
                    ValidationResult = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim().ToUpperInvariant(),
                    ValidationExplanation = reader.IsDBNull(10) ? "" : reader.GetString(10).Trim()
                };

                if (row.ValidationResult == "PASS")
                    passRows.Add(row);
                else
                    failRows.Add(row);
            }

            passRows = DeduplicateRows(passRows);
            failRows = DeduplicateRows(failRows);
            AssignRowNumbers(passRows);
            AssignRowNumbers(failRows);

            var totalCount = passRows.Count + failRows.Count;
            var failCount = failRows.Count;
            var passCount = passRows.Count;
            var exceptionRate = totalCount == 0 ? 0m : Math.Round((decimal)failCount / totalCount * 100m, 2);

            var summary = new Rule65ValidationSummary
            {
                Success = true,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                CancellationTable = request.CancellationTable,
                ClientTable = request.ClientTable,
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

        private static string BuildValidationSql(string cancellationTable, string clientTable, Rule65ColumnMapping mapping)
        {
            return $@"
SET NOCOUNT ON;

IF OBJECT_ID('tempdb..#R65CurrentCensus') IS NOT NULL DROP TABLE #R65CurrentCensus;
IF OBJECT_ID('tempdb..#R65Population')    IS NOT NULL DROP TABLE #R65Population;

SELECT DISTINCT
    TRY_CONVERT(date, [{mapping.CurrentCensusCol}]) AS CurrentCensusDate
INTO #R65CurrentCensus
FROM [{Sanitise(clientTable)}] WITH (NOLOCK)
WHERE TRY_CONVERT(date, [{mapping.CurrentCensusCol}]) IS NOT NULL;

CREATE CLUSTERED INDEX IX_R65CurrentCensus ON #R65CurrentCensus(CurrentCensusDate);

SELECT DISTINCT
    LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(255), [{mapping.StudentNoCol}]), ''))) AS StudentNo,
    LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(255), [{mapping.QualificationCol}]), ''))) AS Qualification,
    LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(255), [{mapping.SubjectCol}]), ''))) AS Subject,
    LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(255), [{mapping.CancelDateCol}]), ''))) AS CancelDate,
    LTRIM(RTRIM(ISNULL(CONVERT(nvarchar(255), [{mapping.CensusDateCol}]), ''))) AS CensusDate,
    TRY_CONVERT(date, [{mapping.CancelDateCol}]) AS CancelDateParsed,
    TRY_CONVERT(date, [{mapping.CensusDateCol}]) AS CensusDateParsed
INTO #R65Population
FROM [{Sanitise(cancellationTable)}] WITH (NOLOCK)
WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(255), [{mapping.CancelDateCol}]))), '') IS NOT NULL;

CREATE CLUSTERED INDEX IX_R65Population ON #R65Population(CancelDateParsed, StudentNo, Qualification, Subject);

SELECT
    'CANCELLATION LIST' AS SourceTable,
    ISNULL(P.StudentNo, '') AS StudentNo,
    ISNULL(P.Qualification, '') AS Qualification,
    ISNULL(P.Subject, '') AS Subject,
    ISNULL(P.CancelDate, '') AS CancelDate,
    ISNULL(P.CensusDate, '') AS CensusDate,
    CASE
        WHEN CC.CurrentCensusDate IS NULL THEN ''
        ELSE CONVERT(varchar(10), CC.CurrentCensusDate, 23)
    END AS CurrentCensus,
    CASE
        WHEN P.CancelDateParsed IS NULL THEN 'INVALID_CANCEL_DATE'
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed AND CC.CurrentCensusDate IS NOT NULL THEN 'CANCEL_EQUALS_CENSUS_AND_CURRENT_CENSUS'
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed THEN 'CANCEL_EQUALS_CENSUS'
        WHEN CC.CurrentCensusDate IS NOT NULL THEN 'CURRENT_CENSUS_MATCH'
        ELSE 'PASS_NOT_ON_CENSUS'
    END AS ExceptionCategory,
    CASE
        WHEN P.CancelDateParsed IS NULL THEN 'INVALID_CANCEL_DATE'
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed AND CC.CurrentCensusDate IS NOT NULL THEN 'BOTH'
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed THEN 'ROW_CENSUS'
        ELSE ''
    END AS ErrorCode,
    CASE
        WHEN P.CancelDateParsed IS NULL THEN 'FAIL'
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed THEN 'FAIL'
        ELSE 'PASS'
    END AS ValidationResult,
    CASE
        WHEN P.CancelDateParsed IS NULL
            THEN 'FAIL: CANCEL value ''' + ISNULL(P.CancelDate, '') + ''' could not be converted to a valid date.'
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed AND CC.CurrentCensusDate IS NOT NULL
            THEN 'FAIL: CANCEL date ''' + CONVERT(varchar(10), P.CancelDateParsed, 23) + ''' equals the row CENSUS date and also matches CURRENT_CENSUS ''' + CONVERT(varchar(10), CC.CurrentCensusDate, 23) + '''.'
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed
            THEN 'FAIL: CANCEL date ''' + CONVERT(varchar(10), P.CancelDateParsed, 23) + ''' equals the row CENSUS date ''' + CONVERT(varchar(10), P.CensusDateParsed, 23) + '''.'
        WHEN CC.CurrentCensusDate IS NOT NULL
            THEN 'PASS: CANCEL date ''' + CONVERT(varchar(10), P.CancelDateParsed, 23) + ''' is a recognised CURRENT_CENSUS date but does not equal the row CENSUS date - cancellation is valid.'
        ELSE 'PASS: CANCEL date ''' + CONVERT(varchar(10), P.CancelDateParsed, 23) + ''' does not equal the row CENSUS date and does not appear in CURRENT_CENSUS.'
    END AS ValidationExplanation
FROM #R65Population P
LEFT JOIN #R65CurrentCensus CC
    ON CC.CurrentCensusDate = P.CancelDateParsed
ORDER BY
    CASE
        WHEN P.CancelDateParsed IS NULL THEN 0
        WHEN P.CensusDateParsed IS NOT NULL AND P.CancelDateParsed = P.CensusDateParsed THEN 0
        ELSE 1
    END,
    P.StudentNo,
    P.Qualification,
    P.Subject,
    P.CancelDate;";
        }

        public string GenerateSql(Rule65ValidationRequest request)
        {
            NormalizeRequest(
                request.CancellationTable,
                request.ClientTable,
                request.ColumnMapping,
                out var cancellationTable,
                out var clientTable,
                out var mapping);

            return $@"-- ============================================================
-- HEMIS RULE 65 - Cancellation Census Date Validation
-- Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Database  : {request.Database}
-- Tables    : [{Sanitise(cancellationTable)}] Cancellation List | [{Sanitise(clientTable)}] CENSUS_LIST_CLIENT
-- Compare   : {mapping.CancelDateCol} against row {mapping.CensusDateCol}
-- Compare   : {mapping.CancelDateCol} against client {mapping.CurrentCensusCol}
-- Rule      : FAIL when CANCEL equals CENSUS on the cancellation row
--           : FAIL when CANCEL matches CURRENT_CENSUS in CENSUS_LIST_CLIENT
--           : PASS when neither comparison matches
-- ============================================================
{BuildValidationSql(cancellationTable, clientTable, mapping)}";
        }

        private async Task<int> SaveValidationRunAsync(Rule65ValidationRequest request, Rule65ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 65);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 65);
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
    @ClientID, @UserID, 65, 'Cancellation Census Date Validation', @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @CancellationTable, @ClientTable, '', @CancelDateCol, @CurrentCensusCol,
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
            command.Parameters.AddWithValue("@CancellationTable", request.CancellationTable);
            command.Parameters.AddWithValue("@ClientTable", request.ClientTable);
            command.Parameters.AddWithValue("@CancelDateCol", request.ColumnMapping.CancelDateCol);
            command.Parameters.AddWithValue("@CurrentCensusCol", request.ColumnMapping.CurrentCensusCol);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted.FailRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule65|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 65;";
            command.Parameters.AddWithValue("@RunID", runId);
            var result = await command.ExecuteScalarAsync();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
        }

        public async Task<Rule65WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS CancellationTable,
    ISNULL(vr.DeceasedTable, '') AS ClientTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 65 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(9) ? null : reader.GetString(9));
            if (summary != null)
                ApplyBrowserPreview(summary);

            var workspace = new Rule65WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CancellationTable = reader.IsDBNull(4) ? "canceliation list" : reader.GetString(4),
                ClientTable = reader.IsDBNull(5) ? "CENSUS_LIST_CLIENT" : reader.GetString(5),
                CurrentStatus = reader.IsDBNull(6) ? "" : reader.GetString(6),
                LastEditedByUserName = reader.IsDBNull(7) ? null : reader.GetString(7),
                LastEditedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                Summary = summary,
                ColumnMapping = summary?.ColumnMapping ?? new Rule65ColumnMapping()
            };
            await reader.CloseAsync();

            if (summary != null)
            {
                workspace.CurrentStatus = summary.Status;
                workspace.CancellationTable = summary.CancellationTable;
                workspace.ClientTable = summary.ClientTable;
                workspace.ColumnMapping = summary.ColumnMapping;
            }

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
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId.Value);

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule65RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 65;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            ApplyBrowserPreview(summary);

            var clientId = reader.GetInt32(1);
            var review = new Rule65RunReviewViewModel
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
            review.GeneratedSql = GenerateSql(new Rule65ValidationRequest
            {
                ClientId = clientId,
                Database = summary.Database,
                CancellationTable = summary.CancellationTable,
                ClientTable = summary.ClientTable,
                ColumnMapping = summary.ColumnMapping
            });

            return review;
        }

        public async Task<Rule65WorkspaceSaveResult> SaveWorkspaceAsync(Rule65ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
                if (!reviewerId.HasValue)
                    return new Rule65WorkspaceSaveResult { Success = false, Error = "Your account could not be resolved in the system database." };

                if (!request.RunId.HasValue || request.RunId.Value <= 0)
                    return new Rule65WorkspaceSaveResult { Success = false, Error = "No saved run exists. Run Rule 65 first." };

                NormalizeRequest(
                    request.CancellationTable,
                    request.ClientTable,
                    request.ColumnMapping,
                    out var cancellationTable,
                    out var clientTable,
                    out var mapping);

                request.CancellationTable = cancellationTable;
                request.ClientTable = clientTable;
                request.ColumnMapping = mapping;

                var cleared = await ClearSignoffsAsync(connection, request.RunId.Value);
                var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 65);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    HemisServer          = @HemisServer,
    AuditDatabase        = @AuditDatabase,
    StudTable            = @CancellationTable,
    DeceasedTable        = @ClientTable,
    BridgeTable          = '',
    StudColumn           = @CancelDateCol,
    DeceasedColumn       = @CurrentCensusCol,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    WorkspaceSavedAt     = GETDATE(),
    Status               = 'Needs Review',
    RecordHash           = @RecordHash
WHERE RunID = @RunID AND RuleNumber = 65;";
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@CancellationTable", request.CancellationTable);
                command.Parameters.AddWithValue("@ClientTable", request.ClientTable);
                command.Parameters.AddWithValue("@CancelDateCol", request.ColumnMapping.CancelDateCol);
                command.Parameters.AddWithValue("@CurrentCensusCol", request.ColumnMapping.CurrentCensusCol);
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule65|{request.RunId.Value}|{request.ClientId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                await command.ExecuteNonQueryAsync();

                var storedSummary = await GetStoredSummaryAsync(request.RunId.Value);
                if (storedSummary != null)
                {
                    storedSummary.Database = request.Database;
                    storedSummary.CancellationTable = request.CancellationTable;
                    storedSummary.ClientTable = request.ClientTable;
                    storedSummary.ColumnMapping = request.ColumnMapping;
                    await UpdateStoredSummaryAsync(connection, request.RunId.Value, storedSummary);
                }

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule65WorkspaceSaveResult
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
                return new Rule65WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule65WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                    return new Rule65WorkspaceSaveResult { Success = false, Error = "Saved run not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var cleared = await ClearSignoffsAsync(connection, runId);
                var previousHash = await GetLatestValidationHashAsync(connection, clientId.Value, 65);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns SET
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt         = GETDATE(),
    RecordHash           = @RecordHash
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@LastEditedByUserName", reviewerName ?? reviewerEmail);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginWorkspaceEdit|Rule65|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                command.Parameters.AddWithValue("@RunID", runId);
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule65WorkspaceSaveResult
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
                return new Rule65WorkspaceSaveResult { Success = false, Error = ex.Message };
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

        private static void ApplyBrowserPreview(Rule65ValidationSummary? summary)
        {
            if (summary == null)
                return;

            var failRows = summary.FailRows ?? new List<Rule65ReviewRow>();
            var passRows = summary.PassRows ?? new List<Rule65ReviewRow>();

            summary.IsPreviewOnly = failRows.Count > BrowserPreviewRowLimit || passRows.Count > BrowserPreviewRowLimit;
            summary.FailRows = failRows.Take(BrowserPreviewRowLimit).ToList();
            summary.PassRows = passRows.Take(BrowserPreviewRowLimit).ToList();
            summary.PreviewLimit = summary.IsPreviewOnly ? BrowserPreviewRowLimit : 0;
        }

        private static void EnsureDerivedSummaryData(Rule65ValidationSummary? summary)
        {
            if (summary == null)
                return;

            summary.PassRows = DeduplicateRows(summary.PassRows ?? new List<Rule65ReviewRow>());
            summary.FailRows = DeduplicateRows(summary.FailRows ?? new List<Rule65ReviewRow>());
            summary.ExceptionCategories ??= new List<Rule65ExceptionCategoryViewModel>();
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

        private static List<Rule65ExceptionCategoryViewModel> BuildExceptionCategories(
            IReadOnlyCollection<Rule65ReviewRow> passRows,
            IReadOnlyCollection<Rule65ReviewRow> failRows)
        {
            return passRows
                .Concat(failRows)
                .GroupBy(row => ResolveExceptionCategory(row), StringComparer.OrdinalIgnoreCase)
                .Select(group => new Rule65ExceptionCategoryViewModel
                {
                    Category = group.Key,
                    Description = GetExceptionCategoryDescription(group.Key),
                    Count = group.Count()
                })
                .OrderByDescending(category => category.Count)
                .ThenBy(category => category.Category, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<Rule65ReviewRow> DeduplicateRows(IEnumerable<Rule65ReviewRow> rows)
        {
            var deduplicated = new List<Rule65ReviewRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows ?? Enumerable.Empty<Rule65ReviewRow>())
            {
                row.ExceptionCategory = ResolveExceptionCategory(row);
                var key = string.Join("|", new[]
                {
                    row.SourceTable?.Trim() ?? "",
                    row.StudentNo?.Trim() ?? "",
                    row.Qualification?.Trim() ?? "",
                    row.Subject?.Trim() ?? "",
                    row.CancelDate?.Trim() ?? "",
                    row.CensusDate?.Trim() ?? "",
                    row.CurrentCensus?.Trim() ?? "",
                    row.ExceptionCategory?.Trim() ?? "",
                    row.ValidationResult?.Trim() ?? "",
                    row.ErrorCode?.Trim() ?? ""
                });

                if (!seen.Add(key))
                    continue;

                deduplicated.Add(row);
            }

            return deduplicated;
        }

        private static void AssignRowNumbers(List<Rule65ReviewRow> rows)
        {
            for (var index = 0; index < rows.Count; index++)
                rows[index].RowNumber = index + 1;
        }

        private static string ResolveExceptionCategory(Rule65ReviewRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.ExceptionCategory))
                return row.ExceptionCategory.Trim().ToUpperInvariant();

            if (string.Equals(row.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase))
                return "PASS_NOT_ON_CENSUS";

            return string.IsNullOrWhiteSpace(row.ErrorCode)
                ? "FAIL_OTHER"
                : row.ErrorCode.Trim().ToUpperInvariant();
        }

        private static string GetExceptionCategoryDescription(string category) =>
            category.ToUpperInvariant() switch
            {
                "PASS_NOT_ON_CENSUS" => "Cancel date does not equal the row census date and does not match CURRENT_CENSUS",
                "CANCEL_EQUALS_CENSUS" => "Cancel date equals the row census date",
                "CURRENT_CENSUS_MATCH" => "Cancel date matches CURRENT_CENSUS in CENSUS_LIST_CLIENT",
                "CANCEL_EQUALS_CENSUS_AND_CURRENT_CENSUS" => "Cancel date equals the row census date and matches CURRENT_CENSUS",
                "INVALID_CANCEL_DATE" => "Cancel value could not be converted to a valid date",
                "FAIL_OTHER" => "Other Rule 65 failure",
                _ => category
            };

        public async Task<Rule65ValidationSummary?> GetStoredSummaryAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 ResultsJSON
FROM dbo.ValidationRuns
WHERE RunID = @RunID AND RuleNumber = 65;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(0) ? null : reader.GetString(0));
            if (summary != null)
            {
                summary.SavedRunId = runId;
                EnsureDerivedSummaryData(summary);
            }
            return summary;
        }

        private static Rule65ValidationSummary CloneSummaryForPersistence(Rule65ValidationSummary source) =>
            JsonConvert.DeserializeObject<Rule65ValidationSummary>(JsonConvert.SerializeObject(source)) ?? new Rule65ValidationSummary();

        private static Rule65ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json) ?? json;
                var summary = JsonConvert.DeserializeObject<Rule65ValidationSummary>(decoded);
                if (summary != null)
                    EnsureDerivedSummaryData(summary);
                return summary;
            }
            catch
            {
                return null;
            }
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule65ValidationSummary summary)
        {
            var persisted = CloneSummaryForPersistence(summary);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persisted)));
            await command.ExecuteNonQueryAsync();
        }

        private static void NormalizeRequest(
            string cancellationTableIn,
            string clientTableIn,
            Rule65ColumnMapping? mappingIn,
            out string cancellationTable,
            out string clientTable,
            out Rule65ColumnMapping mapping)
        {
            cancellationTable = (cancellationTableIn ?? "canceliation list").Trim();
            clientTable = (clientTableIn ?? "CENSUS_LIST_CLIENT").Trim();
            mapping = mappingIn ?? new Rule65ColumnMapping();

            mapping.StudentNoCol = ColumnOrDefault(mapping.StudentNoCol, "STD_NO");
            mapping.QualificationCol = ColumnOrDefault(mapping.QualificationCol, "QUAL");
            mapping.SubjectCol = ColumnOrDefault(mapping.SubjectCol, "SUBJ");
            mapping.CancelDateCol = ColumnOrDefault(mapping.CancelDateCol, "CANCEL");
            mapping.CensusDateCol = ColumnOrDefault(mapping.CensusDateCol, "CENSUS");
            mapping.CurrentCensusCol = ColumnOrDefault(mapping.CurrentCensusCol, "CURRENT_CENSUS");

            ValidateObjectName(cancellationTable);
            ValidateObjectName(clientTable);
            ValidateObjectName(mapping.StudentNoCol);
            ValidateObjectName(mapping.QualificationCol);
            ValidateObjectName(mapping.SubjectCol);
            ValidateObjectName(mapping.CancelDateCol);
            ValidateObjectName(mapping.CensusDateCol);
            ValidateObjectName(mapping.CurrentCensusCol);
        }

        private static string ColumnOrDefault(string? value, string defaultValue) =>
            string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

        private static string? FindFirst(List<string> items, string[] exactMatches, string[] partials)
        {
            foreach (var exact in exactMatches)
            {
                var match = items.FirstOrDefault(item => string.Equals(item, exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var partial in partials)
            {
                var match = items.FirstOrDefault(item => item.Contains(partial, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return items.FirstOrDefault();
        }

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static void ValidateObjectName(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Object name cannot be blank.");

            if (name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-' || ch == ' ')))
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
            if (string.IsNullOrWhiteSpace(email))
                return null;

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
            command.CommandText = "SELECT ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 65;";
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
