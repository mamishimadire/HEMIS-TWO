using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;

namespace HemisAudit.Services
{
    public class Rule22Service : IRule22Service
    {
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
        private const int ReviewPreviewRowsPerControl = 100;
        private const int BrowserPreviewRowLimit = 10;
        private static readonly string[] IncludedControlTypes = ["Control 1", "Control 2", "Control 3"];
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule22Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

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
                while (await reader.ReadAsync())
                    items.Add(reader.GetString(0));

                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex)
            {
                return new DatabaseListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule22TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                while (await reader.ReadAsync())
                    tables.Add(reader.GetString(0));

                return new Rule22TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoProfTable = FindFirst(tables, ["dbo_PROF", "PROF"], ["prof"])
                };
            }
            catch (Exception ex)
            {
                return new Rule22TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule22ColumnResult> GetProfColumnsAsync(string server, string database, string driver, string profTable)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, profTable);
                return new Rule22ColumnResult
                {
                    Success = true,
                    Columns = columns,
                    AutoColumn037 = FindFirst(columns, ["_037"], ["037", "staff"]),
                    AutoColumn038 = FindFirst(columns, ["_038"], ["038", "year", "commencement"]),
                    AutoColumn039 = FindFirst(columns, ["_039"], ["039", "personnel", "category"]),
                    AutoColumn040 = FindFirst(columns, ["_040"], ["040", "rank"]),
                    AutoColumn011 = FindFirst(columns, ["_011"], ["011", "birth"]),
                    AutoColumn012 = FindFirst(columns, ["_012"], ["012", "gender"]),
                    AutoColumn013 = FindFirst(columns, ["_013"], ["013", "race"]),
                    AutoColumn014 = FindFirst(columns, ["_014"], ["014", "national"]),
                    AutoColumn041 = FindFirst(columns, ["_041"], ["041", "employment", "permanent", "temporary"]),
                    AutoColumn042 = FindFirst(columns, ["_042"], ["042", "part", "full", "status"]),
                    AutoColumn046 = FindFirst(columns, ["_046"], ["046", "qual"]),
                    AutoColumn047 = FindFirst(columns, ["_047"], ["047", "joint", "appoint"]),
                    AutoColumn048 = FindFirst(columns, ["_048"], ["048", "payroll"]),
                    AutoColumn094 = FindFirst(columns, ["_094"], ["094", "research", "fellow"])
                };
            }
            catch (Exception ex)
            {
                return new Rule22ColumnResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule22VerifyResult> VerifyTablesAsync(Rule22VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request);

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var profTable = Sanitise(request.ProfTable);
                var col041 = Sanitise(request.Column041);
                var col039 = Sanitise(request.Column039);
                var fv041 = SafeFilterValue(request.FilterValue041);
                var fv039 = SafeFilterValue(request.FilterValue039);
                var control1Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{profTable}] WHERE {BuildControl1Condition(col041, col039, fv041, fv039)};");
                var control2Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{profTable}] WHERE {BuildControl2Condition(col041, col039, fv041, fv039)};");
                var control3Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{profTable}] WHERE {BuildControl3Condition(col041, col039, fv041, fv039)};");

                return new Rule22VerifyResult
                {
                    Success = true,
                    TotalCount = control1Count + control2Count + control3Count,
                    Control1Count = control1Count,
                    Control2Count = control2Count,
                    Control3Count = control3Count,
                    UnclassifiedCount = 0
                };
            }
            catch (Exception ex)
            {
                return new Rule22VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule22ValidationSummary> RunValidationAsync(Rule22ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request);

                var summary = await AnalyseAsync(request, includeAllReviewRows: false);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error = $"Validation completed, but the saved run could not be written to the system database: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule22ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule22ValidationSummary> GetExportSummaryAsync(Rule22ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule22WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID,
    vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS ProfTable,
    ISNULL(vr.DeceasedTable, '') AS Column041,
    ISNULL(vr.StudColumn, '') AS Column039,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 22
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(10) ? null : reader.GetString(10));
            if (includeSummary && deserializedSummary != null)
            {
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule22WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ProfTable = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Column041 = reader.IsDBNull(5) ? "_041" : reader.GetString(5),
                Column039 = reader.IsDBNull(6) ? "_039" : reader.GetString(6),
                CurrentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7),
                LastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8),
                LastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                Summary = summary
            };

            if (deserializedSummary != null)
            {
                workspace.Column037 = deserializedSummary.Column037;
                workspace.Column038 = deserializedSummary.Column038;
                workspace.Column039 = deserializedSummary.Column039;
                workspace.Column040 = deserializedSummary.Column040;
                workspace.Column011 = deserializedSummary.Column011;
                workspace.Column012 = deserializedSummary.Column012;
                workspace.Column013 = deserializedSummary.Column013;
                workspace.Column014 = string.IsNullOrWhiteSpace(deserializedSummary.Column014) ? "_014" : deserializedSummary.Column014;
                workspace.Column041 = deserializedSummary.Column041;
                workspace.Column042 = deserializedSummary.Column042;
                workspace.Column046 = deserializedSummary.Column046;
                workspace.Column047 = string.IsNullOrWhiteSpace(deserializedSummary.Column047) ? "_047" : deserializedSummary.Column047;
                workspace.Column048 = deserializedSummary.Column048;
                workspace.Column094 = string.IsNullOrWhiteSpace(deserializedSummary.Column094) ? "_094" : deserializedSummary.Column094;
                workspace.Control1SampleSize = deserializedSummary.Control1SampleSize;
                workspace.Control2SampleSize = deserializedSummary.Control2SampleSize;
                workspace.Control3SampleSize = deserializedSummary.Control3SampleSize;
                workspace.FilterValue041 = string.IsNullOrWhiteSpace(deserializedSummary.FilterValue041) ? "PE" : deserializedSummary.FilterValue041;
                workspace.FilterValue039 = string.IsNullOrWhiteSpace(deserializedSummary.FilterValue039) ? "01" : deserializedSummary.FilterValue039;
            }

            await reader.CloseAsync();

            workspace.Driver = "ODBC Driver 17 for SQL Server";
            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);

            return workspace;
        }

        public async Task<Rule22RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 22;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            var viewModel = new Rule22RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = reader.GetInt32(1),
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Summary = summary
            };

            await reader.CloseAsync();

            viewModel.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, viewModel.ClientId, currentUserId.Value) ?? ""
                : "";
            viewModel.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            viewModel.HasDataAnalystSignoff = viewModel.Signoffs.Any(s =>
                string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return viewModel;
        }

        public async Task<Rule22WorkspaceSaveResult> SaveWorkspaceAsync(Rule22ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule22WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the validation first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule22WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found for this engagement."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 22);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @ProfTable,
    DeceasedTable = @Column041,
    StudColumn = @Column039,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review',
    IsCurrent = 1
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@ProfTable", request.ProfTable);
                command.Parameters.AddWithValue("@Column041", request.Column041);
                command.Parameters.AddWithValue("@Column039", request.Column039);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule22|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.ProfTable}|{request.Column037}|{request.Column038}|{request.Column011}|{request.Column012}|{request.Column013}|{request.Column039}|{request.Column040}|{request.Column041}|{request.Column042}|{request.Column046}|{request.Column048}|{request.Control1SampleSize}|{request.Control2SampleSize}|{request.Control3SampleSize}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule22WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                        : "Workspace saved and marked for review again.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule22WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule22WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule22WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                await MarkPreviousRunsHistoricalAsync(connection, clientId.Value, 22);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var markEdit = connection.CreateConfiguredCommand();
                markEdit.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = NULL,
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review',
    IsCurrent = 1
WHERE RunID = @RunID;";
                markEdit.Parameters.AddWithValue("@RunID", runId);
                markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule22|{runId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule22WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0
                        ? "Editing has begun. Existing signoffs were removed."
                        : "Editing has begun. Save the workspace when you are ready.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule22WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(engagementRole))
                throw new InvalidOperationException("Only assigned data analysts, managers, and directors can sign off a validation run.");

            if (!string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
            {
                throw new InvalidOperationException("The assigned data analyst must sign off before this review can be completed.");
            }

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
IF EXISTS (
    SELECT 1
    FROM dbo.ReviewSignoffs
    WHERE RunID = @RunID
      AND ReviewerID = @ReviewerID
)
BEGIN
    UPDATE dbo.ReviewSignoffs
    SET SignoffRole = @SignoffRole,
        ReviewType = 'Final',
        Comment = @Comment,
        SignedOffAt = GETDATE()
    WHERE RunID = @RunID
      AND ReviewerID = @ReviewerID;
END
ELSE
BEGIN
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @SignoffRole, 'Final', @Comment, GETDATE());
END";
            command.Parameters.AddWithValue("@ClientID", clientId.Value);
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId.Value);
            command.Parameters.AddWithValue("@SignoffRole", engagementRole!);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("Reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("Validation run was not found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule22ValidationRequest request)
        {
            ValidateSqlRequest(request);

            var profTable = Sanitise(request.ProfTable);
            var col037 = Sanitise(request.Column037);
            var col038 = Sanitise(request.Column038);
            var col039 = Sanitise(request.Column039);
            var col040 = Sanitise(request.Column040);
            var col011 = Sanitise(request.Column011);
            var col012 = Sanitise(request.Column012);
            var col013 = Sanitise(request.Column013);
            var col014 = Sanitise(string.IsNullOrWhiteSpace(request.Column014) ? "_014" : request.Column014);
            var col041 = Sanitise(request.Column041);
            var col042 = Sanitise(request.Column042);
            var col046 = Sanitise(request.Column046);
            var col047 = Sanitise(string.IsNullOrWhiteSpace(request.Column047) ? "_047" : request.Column047);
            var col048 = Sanitise(request.Column048);
            var col094 = Sanitise(string.IsNullOrWhiteSpace(request.Column094) ? "_094" : request.Column094);
            var fv041 = SafeFilterValue(request.FilterValue041);
            var fv039 = SafeFilterValue(request.FilterValue039);

            var sql = $@"-- ================================================================
-- HEMIS RULE 22: STAFF VALIDATION (dbo_PROF)
-- ================================================================
-- Database : {request.Database}
-- Table    : [{profTable}]
-- Scope    : 100% Population Validation
--
-- Control 1: [{col041}] = '{fv041}' AND [{col039}] = '{fv039}'
-- Control 2: [{col041}] = '{fv041}' AND [{col039}] <> '{fv039}'
-- Control 3: [{col041}] <> '{fv041}' AND [{col039}] <> '{fv039}'
-- ================================================================

SELECT COUNT(*) AS Control1Available
FROM [{profTable}]
WHERE {BuildControl1Condition(col041, col039, fv041, fv039)};

SELECT COUNT(*) AS Control2Available
FROM [{profTable}]
WHERE {BuildControl2Condition(col041, col039, fv041, fv039)};

SELECT COUNT(*) AS Control3Available
FROM [{profTable}]
WHERE {BuildControl3Condition(col041, col039, fv041, fv039)};

WITH Rule22Classified AS
(
    SELECT
        {BuildControlTypeCase(col041, col039, fv041, fv039)} AS Control_Type,
        {BuildControlDefinitionCase(col041, col039, fv041, fv039)} AS Control_Definition,
        ROW_NUMBER() OVER
        (
            PARTITION BY {BuildControlTypeCase(col041, col039, fv041, fv039)}
            ORDER BY
                CASE WHEN TRY_CONVERT(BIGINT, [{col037}]) IS NULL THEN 1 ELSE 0 END,
                TRY_CONVERT(BIGINT, [{col037}]),
                CAST([{col037}] AS NVARCHAR(255)),
                CAST([{col038}] AS NVARCHAR(255))
        ) AS Control_Row_Number,
        CAST([{col037}] AS NVARCHAR(255)) AS Staff_Number,
        CAST([{col038}] AS NVARCHAR(255)) AS Employment_Commencement_Year,
        CAST([{col039}] AS NVARCHAR(255)) AS Personnel_Category,
        CAST([{col040}] AS NVARCHAR(255)) AS Rank_of_Staff_Member,
        CAST([{col011}] AS NVARCHAR(255)) AS Date_of_Birth,
        CAST([{col012}] AS NVARCHAR(255)) AS Gender,
        CAST([{col013}] AS NVARCHAR(255)) AS Race,
        CAST([{col014}] AS NVARCHAR(255)) AS Nationality,
        CAST([{col041}] AS NVARCHAR(255)) AS Permanent_Temporary_Status,
        CAST([{col042}] AS NVARCHAR(255)) AS Full_Time_Part_Time_Status,
        CAST([{col046}] AS NVARCHAR(255)) AS Staff_Qualification,
        CAST([{col047}] AS NVARCHAR(255)) AS Joint_Appointment,
        CAST([{col048}] AS NVARCHAR(255)) AS On_Payroll_Code,
        CAST([{col094}] AS NVARCHAR(255)) AS Research_Fellow,
        'PASS' AS Validation_Result
    FROM [{profTable}]
    WHERE {BuildIncludedCondition(col041, col039, fv041, fv039)}
)
SELECT
    ROW_NUMBER() OVER
    (
        ORDER BY
            CASE Control_Type
                WHEN 'Control 1' THEN 1
                WHEN 'Control 2' THEN 2
                WHEN 'Control 3' THEN 3
                ELSE 4
            END,
            Control_Row_Number
    ) AS Validation_Number,
    Control_Type,
    Control_Definition,
    Control_Row_Number,
    Staff_Number,
    Employment_Commencement_Year,
    Personnel_Category,
    Rank_of_Staff_Member,
    Date_of_Birth,
    Gender,
    Race,
    Nationality,
    Permanent_Temporary_Status,
    Full_Time_Part_Time_Status,
    Staff_Qualification,
    Joint_Appointment,
    On_Payroll_Code,
    Research_Fellow,
    Validation_Result
FROM Rule22Classified
ORDER BY
    CASE Control_Type
        WHEN 'Control 1' THEN 1
        WHEN 'Control 2' THEN 2
        WHEN 'Control 3' THEN 3
        ELSE 4
    END,
    Control_Row_Number;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule22ValidationSummary> AnalyseAsync(Rule22ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var profTable = Sanitise(request.ProfTable);
            var col037 = Sanitise(request.Column037);
            var col038 = Sanitise(request.Column038);
            var col039 = Sanitise(request.Column039);
            var col040 = Sanitise(request.Column040);
            var col011 = Sanitise(request.Column011);
            var col012 = Sanitise(request.Column012);
            var col013 = Sanitise(request.Column013);
            var col014 = Sanitise(string.IsNullOrWhiteSpace(request.Column014) ? "_014" : request.Column014);
            var col041 = Sanitise(request.Column041);
            var col042 = Sanitise(request.Column042);
            var col046 = Sanitise(request.Column046);
            var col047 = Sanitise(string.IsNullOrWhiteSpace(request.Column047) ? "_047" : request.Column047);
            var col048 = Sanitise(request.Column048);
            var col094 = Sanitise(string.IsNullOrWhiteSpace(request.Column094) ? "_094" : request.Column094);
            var fv041 = SafeFilterValue(request.FilterValue041);
            var fv039 = SafeFilterValue(request.FilterValue039);

            var control1Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{profTable}] WHERE {BuildControl1Condition(col041, col039, fv041, fv039)};");
            var control2Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{profTable}] WHERE {BuildControl2Condition(col041, col039, fv041, fv039)};");
            var control3Count = await CountAsync(conn, $"SELECT COUNT(*) FROM [{profTable}] WHERE {BuildControl3Condition(col041, col039, fv041, fv039)};");
            var reviewRows = NormalizeReviewRows(await LoadValidationRowsAsync(
                conn,
                profTable,
                col037,
                col038,
                col039,
                col040,
                col011,
                col012,
                col013,
                col014,
                col041,
                col042,
                col046,
                col047,
                col048,
                col094,
                fv041,
                fv039,
                includeAllReviewRows ? null : ReviewPreviewRowsPerControl));
            var previewControl1Count = reviewRows.Count(r => r.ControlType == "Control 1");
            var previewControl2Count = reviewRows.Count(r => r.ControlType == "Control 2");
            var previewControl3Count = reviewRows.Count(r => r.ControlType == "Control 3");
            var totalValidated = control1Count + control2Count + control3Count;

            return new Rule22ValidationSummary
            {
                Success = true,
                TotalValidated = totalValidated,
                PassCount = totalValidated,
                FailCount = 0,
                ExceptionRate = 0m,
                Status = "PASS",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                ProfTable = request.ProfTable,
                Column037 = col037,
                Column038 = col038,
                Column039 = col039,
                Column040 = col040,
                Column011 = col011,
                Column012 = col012,
                Column013 = col013,
                Column014 = col014,
                Column041 = col041,
                Column042 = col042,
                Column046 = col046,
                Column047 = col047,
                Column048 = col048,
                Column094 = col094,
                FilterValue041 = fv041,
                FilterValue039 = fv039,
                Control1SampleSize = 0,
                Control2SampleSize = 0,
                Control3SampleSize = 0,
                Control1Count = control1Count,
                Control2Count = control2Count,
                Control3Count = control3Count,
                UnclassifiedCount = 0,
                ClientId = request.ClientId,
                ControlSummaries = new List<Rule22ControlSummaryItemViewModel>
                {
                    new()
                    {
                        ControlType = "Control 1",
                        ControlDefinition = BuildControl1DefinitionText(request.Column041, request.Column039, fv041, fv039),
                        AvailableCount = control1Count,
                        RequestedCount = control1Count,
                        SampleCount = includeAllReviewRows ? control1Count : previewControl1Count,
                        PassCount = control1Count,
                        FailCount = 0
                    },
                    new()
                    {
                        ControlType = "Control 2",
                        ControlDefinition = BuildControl2DefinitionText(request.Column041, request.Column039, fv041, fv039),
                        AvailableCount = control2Count,
                        RequestedCount = control2Count,
                        SampleCount = includeAllReviewRows ? control2Count : previewControl2Count,
                        PassCount = control2Count,
                        FailCount = 0
                    },
                    new()
                    {
                        ControlType = "Control 3",
                        ControlDefinition = BuildControl3DefinitionText(request.Column041, request.Column039, fv041, fv039),
                        AvailableCount = control3Count,
                        RequestedCount = control3Count,
                        SampleCount = includeAllReviewRows ? control3Count : previewControl3Count,
                        PassCount = control3Count,
                        FailCount = 0
                    }
                },
                MappedColumns = Rule22ColumnMappingHelper.Build(request),
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "100% validation completed for all Control 1, Control 2, and Control 3 rows."
                    : $"100% validation completed. Showing the first {ReviewPreviewRowsPerControl:N0} row(s) per control in the browser."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule22ValidationRequest request, Rule22ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 22);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 22);
            var failRows = summary.ReviewRows.Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, IsCurrent
)
VALUES
(
    @ClientID, @UserID, 22, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @ProfTable, @Column041, @Column039, @SampleConfig,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Staff Validation (dbo_PROF)");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@ProfTable", request.ProfTable);
            command.Parameters.AddWithValue("@Column041", request.Column041);
            command.Parameters.AddWithValue("@Column039", request.Column039);
            command.Parameters.AddWithValue("@SampleConfig", $"{request.Control1SampleSize}|{request.Control2SampleSize}|{request.Control3SampleSize}");
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule22|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, Rule22VerifyRequest request)
        {
            var columns = await GetTableColumnsAsync(server, database, driver, request.ProfTable);
            var requiredColumns = Rule22ColumnMappingHelper.GetSelectedColumns(request);

            foreach (var column in requiredColumns)
            {
                if (!columns.Any(c => c.Equals(column, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Column '{column}' was not found on table '{request.ProfTable}'.");
            }
        }

        private Task EnsureColumnsExistAsync(string server, string database, string driver, Rule22ValidationRequest request) =>
            EnsureColumnsExistAsync(server, database, driver, new Rule22VerifyRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                ProfTable = request.ProfTable,
                Column037 = request.Column037,
                Column038 = request.Column038,
                Column039 = request.Column039,
                Column040 = request.Column040,
                Column011 = request.Column011,
                Column012 = request.Column012,
                Column013 = request.Column013,
                Column014 = string.IsNullOrWhiteSpace(request.Column014) ? "_014" : request.Column014,
                Column041 = request.Column041,
                Column042 = request.Column042,
                Column046 = request.Column046,
                Column047 = string.IsNullOrWhiteSpace(request.Column047) ? "_047" : request.Column047,
                Column048 = request.Column048,
                Column094 = string.IsNullOrWhiteSpace(request.Column094) ? "_094" : request.Column094,
                Control1SampleSize = request.Control1SampleSize,
                Control2SampleSize = request.Control2SampleSize,
                Control3SampleSize = request.Control3SampleSize
            });

        private async Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName)
        {
            ValidateObjectName(tableName);

            var connStr = BuildConnectionString(server, database, driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @TableName
ORDER BY ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@TableName", Sanitise(tableName));

            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0))
                    columns.Add(reader.GetString(0));
            }

            return columns;
        }

        private static async Task<List<Rule22ReviewRowViewModel>> LoadValidationRowsAsync(
            SqlConnection connection,
            string profTable,
            string col037,
            string col038,
            string col039,
            string col040,
            string col011,
            string col012,
            string col013,
            string col014,
            string col041,
            string col042,
            string col046,
            string col047,
            string col048,
            string col094,
            string fv041,
            string fv039,
            int? perControlLimit)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = $@"
WITH Rule22Classified AS
(
    SELECT
        {BuildControlTypeCase(col041, col039, fv041, fv039)} AS Control_Type,
        {BuildControlDefinitionCase(col041, col039, fv041, fv039)} AS Control_Definition,
        ROW_NUMBER() OVER
        (
            PARTITION BY {BuildControlTypeCase(col041, col039, fv041, fv039)}
            ORDER BY
                CASE WHEN TRY_CONVERT(bigint, [{col037}]) IS NULL THEN 1 ELSE 0 END,
                TRY_CONVERT(bigint, [{col037}]),
                CAST([{col037}] AS nvarchar(255)),
                CAST([{col038}] AS nvarchar(255))
        ) AS Sample_Number,
        CAST([{col037}] AS nvarchar(255)) AS Staff_Number,
        CAST([{col038}] AS nvarchar(255)) AS Employment_Commencement_Year,
        CAST([{col039}] AS nvarchar(255)) AS Personnel_Category,
        CAST([{col040}] AS nvarchar(255)) AS Rank_of_Staff_Member,
        CAST([{col011}] AS nvarchar(255)) AS Date_of_Birth,
        CAST([{col012}] AS nvarchar(255)) AS Gender,
        CAST([{col013}] AS nvarchar(255)) AS Race,
        CAST([{col014}] AS nvarchar(255)) AS Nationality,
        CAST([{col041}] AS nvarchar(255)) AS Permanent_Temporary_Status,
        CAST([{col042}] AS nvarchar(255)) AS Full_Time_Part_Time_Status,
        CAST([{col046}] AS nvarchar(255)) AS Staff_Qualification,
        CAST([{col047}] AS nvarchar(255)) AS Joint_Appointment,
        CAST([{col048}] AS nvarchar(255)) AS On_Payroll_Code,
        CAST([{col094}] AS nvarchar(255)) AS Research_Fellow,
        {BuildValidationResultCase(col041, col039, fv041, fv039)} AS Validation_Result,
        {BuildExceptionReasonCase(col041, col039, fv041, fv039)} AS Exception_Reason
    FROM [{profTable}]
    WHERE {BuildIncludedCondition(col041, col039, fv041, fv039)}
)
SELECT
    Control_Type,
    Control_Definition,
    Sample_Number,
    Staff_Number,
    Employment_Commencement_Year,
    Personnel_Category,
    Rank_of_Staff_Member,
    Date_of_Birth,
    Gender,
    Race,
    Nationality,
    Permanent_Temporary_Status,
    Full_Time_Part_Time_Status,
    Staff_Qualification,
    Joint_Appointment,
    On_Payroll_Code,
    Research_Fellow,
    Validation_Result,
    Exception_Reason
FROM Rule22Classified
{(perControlLimit.HasValue ? "WHERE Sample_Number <= @PerControlLimit" : "")}
ORDER BY
    CASE Control_Type
        WHEN 'Control 1' THEN 1
        WHEN 'Control 2' THEN 2
        WHEN 'Control 3' THEN 3
        ELSE 4
    END,
    Sample_Number;";
            if (perControlLimit.HasValue)
                command.Parameters.AddWithValue("@PerControlLimit", perControlLimit.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule22ReviewRowViewModel>();
            while (await reader.ReadAsync())
            {
                rows.Add(new Rule22ReviewRowViewModel
                {
                    ControlType       = reader.IsDBNull(0)  ? "" : reader.GetString(0),
                    ControlDefinition = reader.IsDBNull(1)  ? "" : reader.GetString(1),
                    SampleNumber      = reader.IsDBNull(2)  ? 0  : Convert.ToInt32(reader.GetValue(2)),
                    StaffNumber037    = reader.IsDBNull(3)  ? "" : reader.GetString(3),
                    Year038           = reader.IsDBNull(4)  ? "" : reader.GetString(4),
                    Col039            = reader.IsDBNull(5)  ? "" : reader.GetString(5),
                    Col040            = reader.IsDBNull(6)  ? "" : reader.GetString(6),
                    Col011            = reader.IsDBNull(7)  ? "" : reader.GetString(7),
                    Col012            = reader.IsDBNull(8)  ? "" : reader.GetString(8),
                    Col013            = reader.IsDBNull(9)  ? "" : reader.GetString(9),
                    Col014            = reader.IsDBNull(10) ? "" : reader.GetString(10),
                    Col041            = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    Col042            = reader.IsDBNull(12) ? "" : reader.GetString(12),
                    Col046            = reader.IsDBNull(13) ? "" : reader.GetString(13),
                    Col047            = reader.IsDBNull(14) ? "" : reader.GetString(14),
                    Col048            = reader.IsDBNull(15) ? "" : reader.GetString(15),
                    Col094            = reader.IsDBNull(16) ? "" : reader.GetString(16),
                    ValidationResult  = reader.IsDBNull(17) ? "" : reader.GetString(17),
                    ExceptionReason   = reader.IsDBNull(18) ? "" : reader.GetString(18)
                });
            }

            return rows;
        }

        private static void ValidateRequest(Rule22ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.ProfTable))
                throw new InvalidOperationException("PROF table is required.");
            if (new[]
                {
                    request.Column037,
                    request.Column038,
                    request.Column011,
                    request.Column012,
                    request.Column013,
                    request.Column039,
                    request.Column040,
                    request.Column041,
                    request.Column042,
                    request.Column046,
                    request.Column048
                }.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException("All mapped Rule 22 PROF columns are required.");
            }

            ValidateObjectName(request.ProfTable);
            foreach (var column in Rule22ColumnMappingHelper.Build(request).Select(item => item.ColumnName))
                ValidateObjectName(column);

            foreach (var size in new[] { request.Control1SampleSize, request.Control2SampleSize, request.Control3SampleSize })
            {
                if (size < 0 || size > 500)
                    throw new InvalidOperationException("Sample sizes must be between 0 and 500.");
            }
        }

        private static void ValidateRequest(Rule22VerifyRequest request)
        {
            ValidateRequest(new Rule22ValidationRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                ProfTable = request.ProfTable,
                Column037 = request.Column037,
                Column038 = request.Column038,
                Column011 = request.Column011,
                Column012 = request.Column012,
                Column013 = request.Column013,
                Column041 = request.Column041,
                Column039 = request.Column039,
                Column040 = request.Column040,
                Column042 = request.Column042,
                Column046 = request.Column046,
                Column048 = request.Column048,
                FilterValue041 = request.FilterValue041,
                FilterValue039 = request.FilterValue039,
                Control1SampleSize = request.Control1SampleSize,
                Control2SampleSize = request.Control2SampleSize,
                Control3SampleSize = request.Control3SampleSize
            });
        }

        private static void ValidateSqlRequest(Rule22ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");

            ValidateRequest(new Rule22ValidationRequest
            {
                Server = "sql-preview",
                Database = request.Database,
                Driver = request.Driver,
                ProfTable = request.ProfTable,
                Column037 = request.Column037,
                Column038 = request.Column038,
                Column039 = request.Column039,
                Column040 = request.Column040,
                Column011 = request.Column011,
                Column012 = request.Column012,
                Column013 = request.Column013,
                Column014 = string.IsNullOrWhiteSpace(request.Column014) ? "_014" : request.Column014,
                Column041 = request.Column041,
                Column042 = request.Column042,
                Column046 = request.Column046,
                Column047 = string.IsNullOrWhiteSpace(request.Column047) ? "_047" : request.Column047,
                Column048 = request.Column048,
                Column094 = string.IsNullOrWhiteSpace(request.Column094) ? "_094" : request.Column094,
                Control1SampleSize = request.Control1SampleSize,
                Control2SampleSize = request.Control2SampleSize,
                Control3SampleSize = request.Control3SampleSize
            });
        }

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Table or column name is required.");

            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
            {
                if (value.Contains(bad, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe table or column name was provided.");
            }
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = values.FirstOrDefault(v => v.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var fragment in containsMatches)
            {
                var match = values.FirstOrDefault(v => v.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return values.FirstOrDefault();
        }

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCommand = connection.CreateConfiguredCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCommand.Parameters.AddWithValue("@RunID", runId);
            var clearedSignoffs = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await SetRunStatusAsync(connection, runId, "Needs Review");
            return clearedSignoffs;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAllSignoffs = await HasAllRequiredSignoffsAsync(connection, runId);
            await SetRunStatusAsync(connection, runId, hasAllSignoffs ? "Reviewed and Completed" : "Needs Review");
        }

        private async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director') THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return false;

            return reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1;
        }

        private async Task SetRunStatusAsync(SqlConnection connection, int runId, string status)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET Status = @Status WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@Status", status);
            await command.ExecuteNonQueryAsync();
        }

        private async Task SetRunCurrentStateAsync(SqlConnection connection, int runId, bool isCurrent)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = @IsCurrent WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@IsCurrent", isCurrent);
            await command.ExecuteNonQueryAsync();
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET IsCurrent = 0
WHERE ClientID = @ClientID
  AND RuleNumber = @RuleNumber
  AND IsCurrent = 1;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT rs.SignoffID,
       ISNULL(rs.SignoffRole, '') AS SignoffRole,
       LTRIM(RTRIM(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, ''))) AS ReviewerName,
       ISNULL(u.Email, '') AS ReviewerEmail,
       ISNULL(rs.Comment, '') AS Comment,
       rs.SignedOffAt,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole, '')
            WHEN 'DataAnalyst' THEN 1
            WHEN 'Manager' THEN 2
            WHEN 'Director' THEN 3
            ELSE 4
         END,
         rs.SignedOffAt DESC;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync();
            var signoffs = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
            {
                signoffs.Add(new RunSignoffViewModel
                {
                    Id = reader.GetInt32(0),
                    SignoffRole = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ReviewerName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comment = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SignedOffAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6)
                });
            }

            return signoffs;
        }

        private async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 EngagementRole
FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID
  AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM dbo.ValidationRuns
        WHERE RunID = @RunID
          AND (
                WorkspaceSavedAt IS NOT NULL
                OR EXISTS (
                    SELECT 1
                    FROM dbo.ReviewSignoffs rs
                    WHERE rs.RunID = ValidationRuns.RunID
                      AND rs.SignoffRole = 'DataAnalyst'
                )
          )
    ) THEN 1
    ELSE 0
END;";
            command.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
        private async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM dbo.ReviewSignoffs
    WHERE RunID = @RunID
      AND SignoffRole = @SignoffRole
) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }

        private async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private async Task<Rule22ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ResultsJSON FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value
                ? null
                : DeserializeSummary(Convert.ToString(value));
        }

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 RecordHash
FROM dbo.ValidationRuns
WHERE ClientID = @ClientID
  AND RuleNumber = @RuleNumber
  AND RecordHash IS NOT NULL
ORDER BY RunTimestamp DESC, RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID = @ClientID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            var status = Convert.ToString(await command.ExecuteScalarAsync());
            if (string.Equals(status, "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
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

        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string SafeFilterValue(string? value) =>
            (value ?? "").Replace("'", "''").Trim();

        private static string BuildControl1Condition(string col041, string col039, string fv041, string fv039) =>
            $"{SqlValue(col041)} = '{fv041}' AND {SqlValue(col039)} = '{fv039}'";

        private static string BuildControl2Condition(string col041, string col039, string fv041, string fv039) =>
            $"{SqlValue(col041)} = '{fv041}' AND {SqlValue(col039)} <> '{fv039}'";

        private static string BuildControl3Condition(string col041, string col039, string fv041, string fv039) =>
            $"{SqlValue(col041)} <> '{fv041}' AND {SqlValue(col039)} <> '{fv039}'";

        private static string BuildIncludedCondition(string col041, string col039, string fv041, string fv039) =>
            $"(({BuildControl1Condition(col041, col039, fv041, fv039)}) OR ({BuildControl2Condition(col041, col039, fv041, fv039)}) OR ({BuildControl3Condition(col041, col039, fv041, fv039)}))";

        private static string BuildUnclassifiedCondition(string col041, string col039, string fv041, string fv039) =>
            $"NOT {BuildIncludedCondition(col041, col039, fv041, fv039)}";

        private static string BuildControlTypeCase(string col041, string col039, string fv041, string fv039) =>
            $@"CASE
        WHEN {BuildControl1Condition(col041, col039, fv041, fv039)} THEN 'Control 1'
        WHEN {BuildControl2Condition(col041, col039, fv041, fv039)} THEN 'Control 2'
        WHEN {BuildControl3Condition(col041, col039, fv041, fv039)} THEN 'Control 3'
        ELSE 'Unclassified'
    END";

        private static string BuildControlDefinitionCase(string col041, string col039, string fv041, string fv039) =>
            $@"CASE
        WHEN {BuildControl1Condition(col041, col039, fv041, fv039)} THEN '{BuildControl1DefinitionText(col041, col039, fv041, fv039).Replace("'", "''")}'
        WHEN {BuildControl2Condition(col041, col039, fv041, fv039)} THEN '{BuildControl2DefinitionText(col041, col039, fv041, fv039).Replace("'", "''")}'
        WHEN {BuildControl3Condition(col041, col039, fv041, fv039)} THEN '{BuildControl3DefinitionText(col041, col039, fv041, fv039).Replace("'", "''")}'
        ELSE 'Did not match Control 1, Control 2, or Control 3'
    END";

        private static string BuildValidationResultCase(string col041, string col039, string fv041, string fv039) =>
            $@"CASE
        WHEN {BuildIncludedCondition(col041, col039, fv041, fv039)} THEN 'PASS'
        ELSE 'FAIL'
    END";

        private static string BuildExceptionReasonCase(string col041, string col039, string fv041, string fv039) =>
            $@"CASE
        WHEN {BuildIncludedCondition(col041, col039, fv041, fv039)} THEN ''
        ELSE 'Row did not match any Rule 22 control.'
    END";

        private static string BuildControl1DefinitionText(string column041, string column039, string fv041, string fv039) =>
            $"{column041}='{fv041}' AND {column039}='{fv039}'";

        private static string BuildControl2DefinitionText(string column041, string column039, string fv041, string fv039) =>
            $"{column041}='{fv041}' AND {column039}<>'{fv039}'";

        private static string BuildControl3DefinitionText(string column041, string column039, string fv041, string fv039) =>
            $"{column041}<>'{fv041}' AND {column039}<>'{fv039}'";

        private static string SqlValue(string columnName) =>
            $"ISNULL(CAST([{columnName}] AS VARCHAR(10)), '')";

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule22ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                var summary = JsonConvert.DeserializeObject<Rule22ValidationSummary>(decoded);
                return summary == null ? null : NormalizeSummary(summary);
            }
            catch
            {
                return null;
            }
        }

        private static Rule22ValidationSummary NormalizeSummary(Rule22ValidationSummary summary)
        {
            var reviewRows = NormalizeReviewRows(summary.ReviewRows);
            var control1Count = summary.Control1Count > 0 ? summary.Control1Count : reviewRows.Count(r => r.ControlType == "Control 1");
            var control2Count = summary.Control2Count > 0 ? summary.Control2Count : reviewRows.Count(r => r.ControlType == "Control 2");
            var control3Count = summary.Control3Count > 0 ? summary.Control3Count : reviewRows.Count(r => r.ControlType == "Control 3");
            var totalValidated = control1Count + control2Count + control3Count;
            var fv041 = string.IsNullOrWhiteSpace(summary.FilterValue041) ? "PE" : summary.FilterValue041;
            var fv039 = string.IsNullOrWhiteSpace(summary.FilterValue039) ? "01" : summary.FilterValue039;

            summary.Column037 = Rule22ColumnMappingHelper.Normalize(summary.Column037, "_037");
            summary.Column038 = Rule22ColumnMappingHelper.Normalize(summary.Column038, "_038");
            summary.Column039 = Rule22ColumnMappingHelper.Normalize(summary.Column039, "_039");
            summary.Column040 = Rule22ColumnMappingHelper.Normalize(summary.Column040, "_040");
            summary.Column011 = Rule22ColumnMappingHelper.Normalize(summary.Column011, "_011");
            summary.Column012 = Rule22ColumnMappingHelper.Normalize(summary.Column012, "_012");
            summary.Column013 = Rule22ColumnMappingHelper.Normalize(summary.Column013, "_013");
            summary.Column014 = Rule22ColumnMappingHelper.Normalize(summary.Column014, "_014");
            summary.Column041 = Rule22ColumnMappingHelper.Normalize(summary.Column041, "_041");
            summary.Column042 = Rule22ColumnMappingHelper.Normalize(summary.Column042, "_042");
            summary.Column046 = Rule22ColumnMappingHelper.Normalize(summary.Column046, "_046");
            summary.Column047 = Rule22ColumnMappingHelper.Normalize(summary.Column047, "_047");
            summary.Column048 = Rule22ColumnMappingHelper.Normalize(summary.Column048, "_048");
            summary.Column094 = Rule22ColumnMappingHelper.Normalize(summary.Column094, "_094");
            summary.FilterValue041 = fv041;
            summary.FilterValue039 = fv039;
            summary.Control1SampleSize = 0;
            summary.Control2SampleSize = 0;
            summary.Control3SampleSize = 0;
            summary.Control1Count = control1Count;
            summary.Control2Count = control2Count;
            summary.Control3Count = control3Count;
            summary.UnclassifiedCount = 0;
            summary.TotalValidated = totalValidated;
            summary.PassCount = totalValidated;
            summary.FailCount = 0;
            summary.ExceptionRate = 0m;
            summary.Status = "PASS";
            summary.MappedColumns = Rule22ColumnMappingHelper.Build(summary);
            summary.ReviewRows = reviewRows;
            summary.ControlSummaries = new List<Rule22ControlSummaryItemViewModel>
            {
                BuildControlSummary("Control 1", BuildControl1DefinitionText(summary.Column041, summary.Column039, fv041, fv039), control1Count, control1Count, reviewRows),
                BuildControlSummary("Control 2", BuildControl2DefinitionText(summary.Column041, summary.Column039, fv041, fv039), control2Count, control2Count, reviewRows),
                BuildControlSummary("Control 3", BuildControl3DefinitionText(summary.Column041, summary.Column039, fv041, fv039), control3Count, control3Count, reviewRows)
            };

            return summary;
        }

        private static void ApplyBrowserPreview(Rule22ValidationSummary summary)
        {
            summary.ReviewRows = summary.ReviewRows
                .Take(BrowserPreviewRowLimit)
                .ToList();
        }

        private static Rule22ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlDefinition,
            int count,
            int requestedCount,
            List<Rule22ReviewRowViewModel> reviewRows) =>
            new()
            {
                ControlType = controlType,
                ControlDefinition = controlDefinition,
                AvailableCount = count,
                RequestedCount = requestedCount,
                SampleCount = reviewRows.Count(r => r.ControlType == controlType),
                PassCount = reviewRows.Count(r => r.ControlType == controlType && r.ValidationResult == "PASS"),
                FailCount = reviewRows.Count(r => r.ControlType == controlType && r.ValidationResult == "FAIL")
            };

        private static List<Rule22ReviewRowViewModel> NormalizeReviewRows(IEnumerable<Rule22ReviewRowViewModel>? rows)
        {
            var normalizedRows = (rows ?? Enumerable.Empty<Rule22ReviewRowViewModel>())
                .Where(r => IsIncludedControlType(r.ControlType))
                .OrderBy(r => GetControlSortOrder(r.ControlType))
                .ThenBy(r => r.SampleNumber)
                .ToList();

            var perControlCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < normalizedRows.Count; i++)
            {
                normalizedRows[i].ValidationNumber = i + 1;
                if (!perControlCounters.ContainsKey(normalizedRows[i].ControlType))
                    perControlCounters[normalizedRows[i].ControlType] = 0;

                perControlCounters[normalizedRows[i].ControlType]++;
                if (normalizedRows[i].SampleNumber <= 0)
                    normalizedRows[i].SampleNumber = perControlCounters[normalizedRows[i].ControlType];

                normalizedRows[i].ValidationResult = string.Equals(normalizedRows[i].ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)
                    ? "FAIL"
                    : "PASS";
                normalizedRows[i].ExceptionReason = normalizedRows[i].ValidationResult == "FAIL"
                    ? normalizedRows[i].ExceptionReason ?? ""
                    : "";
            }

            return normalizedRows;
        }


        private static bool IsIncludedControlType(string? controlType) =>
            IncludedControlTypes.Any(c => string.Equals(c, controlType, StringComparison.OrdinalIgnoreCase));

        private static int GetControlSortOrder(string? controlType) =>
            Array.FindIndex(IncludedControlTypes, c => string.Equals(c, controlType, StringComparison.OrdinalIgnoreCase)) switch
            {
                >= 0 and var idx => idx,
                _ => IncludedControlTypes.Length
            };

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
    }
}


