using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Security.Cryptography;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule21Service : IRule21Service
    {
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
        private const int BrowserPreviewPerResultLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule21Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        public async Task<DatabaseListResult> GetDatabasesAsync(string server, string driver)
        {
            try
            {
                await using var conn = new SqlConnection(BuildConnectionString(server, "master", driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT name FROM sys.databases WHERE name NOT IN ('master','tempdb','model','msdb') ORDER BY name;";
                await using var reader = await cmd.ExecuteReaderAsync();
                var items = new List<string>();
                while (await reader.ReadAsync()) items.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex)
            {
                return new DatabaseListResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule21TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
        {
            try
            {
                await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME;";
                await using var reader = await cmd.ExecuteReaderAsync();
                var tables = new List<string>();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

                return new Rule21TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables,
                        ["dbo_STUD", "STUD"],
                        ["stud"]),
                    AutoQualTable = FindFirst(tables,
                        ["dbo_QUAL", "QUAL"],
                        ["qual"]),
                    AutoNalTable = FindFirst(tables,
                        ["Non_Aligned_Qualifications", "NonAligned_Qualifications", "NON_ALIGNED_QUALIFICATIONS"],
                        ["non_aligned", "nonaligned", "nal_qual"])
                };
            }
            catch (Exception ex)
            {
                return new Rule21TableDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule21ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole)
        {
            try
            {
                var columns = await GetTableColumnsAsync(server, database, driver, tableName);
                var result = new Rule21ColumnDiscoveryResult { Success = true, Columns = columns };

                if (string.Equals(tableRole, "stud", StringComparison.OrdinalIgnoreCase))
                {
                    result.AutoQualRefColumn  = FindFirst(columns, ["_001"], ["_001", "qual_ref", "qualcode"]);
                    result.AutoFirstTimeColumn = FindFirst(columns, ["_010"], ["_010", "firsttime", "first_time"]);
                    result.AutoStud007Column = FindFirst(columns, ["_007"], ["_007"]);
                    result.AutoStud008Column = FindFirst(columns, ["_008"], ["_008"]);
                    result.AutoStud012Column = FindFirst(columns, ["_012"], ["_012"]);
                    result.AutoStud026Column = FindFirst(columns, ["_026"], ["_026"]);
                }
                else if (string.Equals(tableRole, "qual", StringComparison.OrdinalIgnoreCase))
                {
                    result.AutoQualCodeColumn = FindFirst(columns, ["_001"], ["_001", "qual_code", "qualification_code"]);
                    result.AutoQualNameColumn = FindFirst(columns, ["_003"], ["_003", "qual_name", "qualification_name"]);
                }
                else
                {
                    result.AutoNalRefColumn      = FindFirst(columns, ["Qualification_reference_number"], ["qual_ref", "qualification_ref", "qualref"]);
                    result.AutoNalNameColumn     = FindFirst(columns, ["Existing_qualification_name"], ["exist_qual", "qual_name", "qualname"]);
                    result.AutoNalAlignedColumn  = FindFirst(columns, ["Aligned_qualification_name"], ["aligned_qual", "aligned"]);
                    result.AutoNalCategoryColumn = FindFirst(columns, ["Category"], ["category", "cat"]);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule21ColumnDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule21DistinctValuesResult> GetDistinctValuesAsync(string server, string database, string driver, string tableName, string columnName, string? preferredValue)
        {
            try
            {
                ValidateObjectName(tableName);
                ValidateObjectName(columnName);
                var tbl = Sanitise(tableName);
                var col = Sanitise(columnName);

                await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandTimeout = SqlCommandTimeoutSeconds;
                cmd.CommandText = $"SELECT DISTINCT TOP 100 CAST([{col}] AS nvarchar(200)) FROM [{tbl}] WHERE [{col}] IS NOT NULL ORDER BY 1;";
                await using var reader = await cmd.ExecuteReaderAsync();

                var values = new List<string>();
                while (await reader.ReadAsync())
                {
                    var v = reader.IsDBNull(0) ? null : reader.GetString(0).Trim();
                    if (!string.IsNullOrEmpty(v)) values.Add(v);
                }

                var autoValue = !string.IsNullOrWhiteSpace(preferredValue) && values.Any(v => string.Equals(v, preferredValue, StringComparison.OrdinalIgnoreCase))
                    ? values.First(v => string.Equals(v, preferredValue, StringComparison.OrdinalIgnoreCase))
                    : values.FirstOrDefault();

                return new Rule21DistinctValuesResult { Success = true, Values = values, AutoValue = autoValue };
            }
            catch (Exception ex)
            {
                return new Rule21DistinctValuesResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule21VerifyResult> VerifyTablesAsync(Rule21VerifyRequest request)
        {
            try
            {
                ValidateObjectNames(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var st = Sanitise(request.StudTable);
                var qt = Sanitise(request.QualTable);
                var nt = Sanitise(request.NalTable);
                var s10 = Sanitise(request.StudFirstTimeColumn);
                var nc = Sanitise(request.NalCategoryColumn);
                var ftv = request.StudFirstTimeValue.Replace("'", "''");
                var catv = request.NalCategoryValue.Replace("'", "''");

                return new Rule21VerifyResult
                {
                    Success = true,
                    StudTotalCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{st}];"),
                    StudFilteredCount = await CountAsync(conn,
                        $"SELECT COUNT(*) FROM [{st}] WHERE UPPER(LTRIM(RTRIM(CAST([{s10}] AS nvarchar(50))))) = UPPER('{ftv}');"),
                    QualTotalCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{qt}];"),
                    NalTotalCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{nt}];"),
                    NalFilteredCount = await CountAsync(conn,
                        $"SELECT COUNT(*) FROM [{nt}] WHERE UPPER(LTRIM(RTRIM(CAST([{nc}] AS nvarchar(50))))) = UPPER('{catv}');")
                };
            }
            catch (Exception ex)
            {
                return new Rule21VerifyResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule21ValidationSummary> RunValidationAsync(Rule21ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateObjectNames(request);
                var summary = await AnalyseAsync(request);
                if (summary.Success && request.ClientId > 0)
                {
                    try
                    {
                        summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);
                    }
                    catch (Exception ex)
                    {
                        summary.Success = false;
                        summary.Error = $"Analysis completed, but the saved run could not be written to the system database: {ex.Message}";
                        return summary;
                    }
                }

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex)
            {
                return new Rule21ValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule21WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
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
    ISNULL(vr.StudTable, '') AS StudTable,
    ISNULL(vr.DeceasedTable, '') AS NalTable,
    ISNULL(vr.StudColumn, '') AS StudQualRefColumn,
    ISNULL(vr.DeceasedColumn, '') AS StudFirstTimeColumn,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 21
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            ApplyBrowserPreview(deserializedSummary);

            var workspace = new Rule21WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "" : reader.GetString(4),
                QualTable = "dbo_QUAL",
                NalTable = reader.IsDBNull(5) ? "" : reader.GetString(5),
                StudQualRefColumn = reader.IsDBNull(6) ? "_001" : reader.GetString(6),
                StudFirstTimeColumn = reader.IsDBNull(7) ? "_010" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = deserializedSummary
            };

            if (deserializedSummary != null)
            {
                workspace.StudFirstTimeValue = deserializedSummary.StudFirstTimeValue;
                workspace.QualTable          = deserializedSummary.QualTable;
                workspace.QualCodeColumn     = deserializedSummary.QualCodeColumn;
                workspace.QualNameColumn     = deserializedSummary.QualNameColumn;
                workspace.Stud007Column      = deserializedSummary.Stud007Column;
                workspace.Stud008Column      = deserializedSummary.Stud008Column;
                workspace.Stud012Column      = deserializedSummary.Stud012Column;
                workspace.Stud026Column      = deserializedSummary.Stud026Column;
                workspace.NalRefColumn       = deserializedSummary.NalRefColumn;
                workspace.NalNameColumn      = deserializedSummary.NalNameColumn;
                workspace.NalAlignedColumn   = deserializedSummary.NalAlignedColumn;
                workspace.NalCategoryColumn  = deserializedSummary.NalCategoryColumn;
                workspace.NalCategoryValue   = deserializedSummary.NalCategoryValue;
                workspace.NalHeqsfRefColumn  = deserializedSummary.NalHeqsfRefColumn;
                workspace.NalSaqaIdColumn    = deserializedSummary.NalSaqaIdColumn;
                workspace.NalNqfColumn       = deserializedSummary.NalNqfColumn;
                workspace.NalCreditsColumn   = deserializedSummary.NalCreditsColumn;
                workspace.NalOutcomeColumn   = deserializedSummary.NalOutcomeColumn;
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

        public async Task<Rule21RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 21;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null) return null;

            var viewModel = new Rule21RunReviewViewModel
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

        public async Task<Rule21WorkspaceSaveResult> SaveWorkspaceAsync(Rule21ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                if (request.RunId is null || request.RunId <= 0)
                    return new Rule21WorkspaceSaveResult { Success = false, Error = "Run the validation first so the workspace can be saved." };

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                    return new Rule21WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

                await EnsureClientNotArchivedAsync(connection, request.ClientId);
                await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 39);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @NalTable,
    StudColumn = @StudQualRefColumn,
    DeceasedColumn = @StudFirstTimeColumn,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review',
    IsCurrent = 1
WHERE RunID = @RunID AND ClientID = @ClientID;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@NalTable", request.NalTable);
                command.Parameters.AddWithValue("@StudQualRefColumn", request.StudQualRefColumn);
                command.Parameters.AddWithValue("@StudFirstTimeColumn", request.StudFirstTimeColumn);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule21|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.QualTable}|{request.NalTable}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail);
                return new Rule21WorkspaceSaveResult
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
                return new Rule21WorkspaceSaveResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule21WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                    return new Rule21WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                await MarkPreviousRunsHistoricalAsync(connection, clientId.Value, 39);

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
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginWorkspaceEdit|Rule21|{runId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail);
                return new Rule21WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0 ? "Editing has begun. Existing signoffs were removed." : "Editing has begun. Save the workspace when you are ready.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule21WorkspaceSaveResult { Success = false, Error = ex.Message };
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
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
BEGIN
    UPDATE dbo.ReviewSignoffs SET SignoffRole = @SignoffRole, ReviewType = 'Final', Comment = @Comment, SignedOffAt = GETDATE()
    WHERE RunID = @RunID AND ReviewerID = @ReviewerID;
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

        public string GenerateSql(Rule21ValidationRequest request)
        {
            var st  = Sanitise(request.StudTable);
            var qt  = Sanitise(request.QualTable);
            var nt  = Sanitise(request.NalTable);
            var sc  = Sanitise(request.StudQualRefColumn);
            var s10 = Sanitise(request.StudFirstTimeColumn);
            var qc  = Sanitise(request.QualCodeColumn);
            var qn  = Sanitise(request.QualNameColumn);
            var nr  = Sanitise(request.NalRefColumn);
            var nn  = Sanitise(request.NalNameColumn);
            var na  = Sanitise(request.NalAlignedColumn);
            var nc  = Sanitise(request.NalCategoryColumn);
            var ftv = request.StudFirstTimeValue.Replace("'", "''");
            var catv = request.NalCategoryValue.Replace("'", "''");

            var optionalStudCols = BuildOptionalStudColsSql(request, "");

            return $@"-- ================================================================
-- HEMIS RULE 21: First-Time Entering Students vs Non-Aligned Qualifications
-- ================================================================
-- Database : {request.Database}
-- STUD Table: [{st}]  |  Filter: [{s10}] = '{ftv}'
-- QUAL Table: [{qt}]  |  Join: STUD.[{sc}] = QUAL.[{qc}]
-- NAL Table : [{nt}]  |  Filter: [{nc}] = '{catv}'
-- Join key  : QUAL.[{qc}] = NAL.[{nr}]
-- ================================================================

IF OBJECT_ID('tempdb..#FTE_Stud')   IS NOT NULL DROP TABLE #FTE_Stud;
IF OBJECT_ID('tempdb..#QUAL_Map')   IS NOT NULL DROP TABLE #QUAL_Map;
IF OBJECT_ID('tempdb..#NAL_Cat')    IS NOT NULL DROP TABLE #NAL_Cat;

-- Step 1: First-Time Entering students (Procedure 5.3)
SELECT
    [{sc}] AS STUD_QualRef,
    {optionalStudCols}[{s10}] AS STUD_010
INTO #FTE_Stud
FROM [{st}]
WHERE UPPER(LTRIM(RTRIM(CAST([{s10}] AS nvarchar(50))))) = UPPER('{ftv}');

SELECT COUNT(*) AS FTE_Count FROM #FTE_Stud;

-- Step 2: Qualification lookup for names (QUAL._001 -> QUAL._003)
SELECT
    [{qc}] AS QUAL_Code,
    [{qn}] AS QUAL_Name
INTO #QUAL_Map
FROM [{qt}];

SELECT COUNT(*) AS QUAL_Count FROM #QUAL_Map;

-- Step 3: Category '{catv}' Non-Aligned Qualifications (Procedure 5.3.1)
SELECT [{nr}] AS NAL_QualRef, [{nn}] AS NAL_QualName, [{na}] AS NAL_AlignedName, [{nc}] AS NAL_Category
INTO #NAL_Cat
FROM [{nt}]
WHERE UPPER(LTRIM(RTRIM(CAST([{nc}] AS nvarchar(50))))) = UPPER('{catv}');

SELECT COUNT(*) AS NAL_Category_Count FROM #NAL_Cat;

-- Step 4: Cross-match and flag (Procedure 5.3.2)
SELECT
    ROW_NUMBER() OVER (ORDER BY s.STUD_QualRef, q.QUAL_Name) AS Row_No,
    s.STUD_QualRef,
    s.Stud007Value,
    s.Stud008Value,
    s.STUD_010,
    s.Stud012Value,
    s.Stud026Value,
    q.QUAL_Code,
    q.QUAL_Name,
    n.NAL_QualName,
    n.NAL_AlignedName,
    n.NAL_Category,
    CASE WHEN n.NAL_QualRef IS NOT NULL THEN 'FLAGGED' ELSE 'CLEAR' END AS Result,
    CASE WHEN n.NAL_QualRef IS NOT NULL
         THEN N'Qualification ' + CAST(COALESCE(NULLIF(CAST(q.QUAL_Code AS nvarchar(255)), ''), CAST(s.STUD_QualRef AS nvarchar(255))) AS nvarchar(255)) +
              N' (' + ISNULL(CAST(q.QUAL_Name AS nvarchar(500)), N'Unknown qualification') + N')' +
              N' found in Category {catv} Non-Aligned list: ' + ISNULL(n.NAL_QualName, '')
         ELSE NULL
    END AS Exception_Reason
FROM #FTE_Stud s
LEFT JOIN #QUAL_Map q
    ON UPPER(LTRIM(RTRIM(CAST(s.STUD_QualRef AS nvarchar(255)))))
     = UPPER(LTRIM(RTRIM(CAST(q.QUAL_Code AS nvarchar(255)))))
LEFT JOIN #NAL_Cat n
    ON UPPER(COALESCE(NULLIF(LTRIM(RTRIM(CAST(q.QUAL_Code AS nvarchar(255)))), ''), LTRIM(RTRIM(CAST(s.STUD_QualRef AS nvarchar(255))))))
     = UPPER(LTRIM(RTRIM(CAST(n.NAL_QualRef AS nvarchar(255)))));

-- Step 5: Summary
SELECT
    COUNT(*) AS Total_FTE,
    SUM(CASE WHEN n.NAL_QualRef IS NOT NULL THEN 1 ELSE 0 END) AS FLAGGED,
    SUM(CASE WHEN n.NAL_QualRef IS NULL THEN 1 ELSE 0 END) AS CLEAR,
    CAST(SUM(CASE WHEN n.NAL_QualRef IS NOT NULL THEN 1 ELSE 0 END)
         * 100.0 / NULLIF(COUNT(*), 0) AS DECIMAL(5,2)) AS Exception_Rate_Pct
FROM #FTE_Stud s
LEFT JOIN #QUAL_Map q
    ON UPPER(LTRIM(RTRIM(CAST(s.STUD_QualRef AS nvarchar(255)))))
     = UPPER(LTRIM(RTRIM(CAST(q.QUAL_Code AS nvarchar(255)))))
LEFT JOIN #NAL_Cat n
    ON UPPER(COALESCE(NULLIF(LTRIM(RTRIM(CAST(q.QUAL_Code AS nvarchar(255)))), ''), LTRIM(RTRIM(CAST(s.STUD_QualRef AS nvarchar(255))))))
     = UPPER(LTRIM(RTRIM(CAST(n.NAL_QualRef AS nvarchar(255)))));

DROP TABLE #FTE_Stud; DROP TABLE #QUAL_Map; DROP TABLE #NAL_Cat;
-- ================================================================
-- END RULE 21
-- ================================================================".Trim();
        }

        // â”€â”€ Analysis â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task<Rule21ValidationSummary> AnalyseAsync(Rule21ValidationRequest request)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var st  = Sanitise(request.StudTable);
            var qt  = Sanitise(request.QualTable);
            var nt  = Sanitise(request.NalTable);
            var sc  = Sanitise(request.StudQualRefColumn);
            var s10 = Sanitise(request.StudFirstTimeColumn);
            var qc  = Sanitise(request.QualCodeColumn);
            var qn  = Sanitise(request.QualNameColumn);
            var nr  = Sanitise(request.NalRefColumn);
            var nn  = Sanitise(request.NalNameColumn);
            var na  = Sanitise(request.NalAlignedColumn);
            var nc  = Sanitise(request.NalCategoryColumn);
            var ftv = request.StudFirstTimeValue.Replace("'", "''");
            var catv = request.NalCategoryValue.Replace("'", "''");

            var studTotal    = await CountAsync(conn, $"SELECT COUNT(*) FROM [{st}];");
            var studFiltered = await CountAsync(conn,
                $"SELECT COUNT(*) FROM [{st}] WHERE UPPER(LTRIM(RTRIM(CAST([{s10}] AS nvarchar(50))))) = UPPER('{ftv}');");
            var qualTotal    = await CountAsync(conn, $"SELECT COUNT(*) FROM [{qt}];");
            var nalCategory  = await CountAsync(conn,
                $"SELECT COUNT(*) FROM [{nt}] WHERE UPPER(LTRIM(RTRIM(CAST([{nc}] AS nvarchar(50))))) = UPPER('{catv}');");

            var optionalStudCols = BuildOptionalStudColsSql(request, "s");
            var optionalNalCols = BuildOptionalNalColsSql(request, "n");

            var sql = $@"
SELECT
    ROW_NUMBER() OVER (ORDER BY s.[{sc}], q.[{qn}]) AS RowNumber,
    CAST(s.[{sc}] AS nvarchar(255)) AS StudQualRef,
    {optionalStudCols}
    CAST(s.[{s10}] AS nvarchar(50)) AS Stud010Value,
    CAST(q.[{qc}] AS nvarchar(255)) AS QualCodeValue,
    CAST(q.[{qn}] AS nvarchar(500)) AS QualNameValue,
    CAST(n.[{nn}] AS nvarchar(500)) AS NalQualName,
    CAST(n.[{na}] AS nvarchar(500)) AS NalAlignedName,
    CAST(n.[{nc}] AS nvarchar(50)) AS NalCategory,
    {optionalNalCols}
    CASE WHEN n.[{nr}] IS NOT NULL THEN 'FLAGGED' ELSE 'CLEAR' END AS Result
FROM [{st}] s
LEFT JOIN [{qt}] q
    ON UPPER(LTRIM(RTRIM(CAST(s.[{sc}] AS nvarchar(255)))))
     = UPPER(LTRIM(RTRIM(CAST(q.[{qc}] AS nvarchar(255)))))
LEFT JOIN [{nt}] n
    ON UPPER(COALESCE(NULLIF(LTRIM(RTRIM(CAST(q.[{qc}] AS nvarchar(255)))), ''), LTRIM(RTRIM(CAST(s.[{sc}] AS nvarchar(255))))))
     = UPPER(LTRIM(RTRIM(CAST(n.[{nr}] AS nvarchar(255)))))
    AND UPPER(LTRIM(RTRIM(CAST(n.[{nc}] AS nvarchar(50))))) = UPPER('{catv}')
WHERE UPPER(LTRIM(RTRIM(CAST(s.[{s10}] AS nvarchar(50))))) = UPPER('{ftv}')
ORDER BY CASE WHEN n.[{nr}] IS NOT NULL THEN 0 ELSE 1 END, s.[{sc}], q.[{qn}];";

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandTimeout = SqlCommandTimeoutSeconds;
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync();
            var allRows = new List<Rule21ValidationRowViewModel>();
            var rowNum = 0;

            while (await reader.ReadAsync())
            {
                rowNum++;
                // ordinals: 0=RowNumber,1=StudQualRef,2=Stud007Value,3=Stud008Value,4=Stud012Value,5=Stud026Value,
                //           6=Stud010Value,7=QualCodeValue,8=QualNameValue,9=NalQualName,10=NalAlignedName,11=NalCategory,
                //           12=NalHeqsfRef,13=NalSaqaId,14=NalNqf,15=NalCredits,16=NalOutcome,17=Result
                var result = reader.IsDBNull(17) ? "CLEAR" : reader.GetString(17);
                var qualRef = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var qualCode = reader.IsDBNull(7) ? "" : reader.GetString(7);
                var qualName = reader.IsDBNull(8) ? null : reader.GetString(8);
                var nalName = reader.IsDBNull(9) ? null : reader.GetString(9);
                var cat     = reader.IsDBNull(11) ? null : reader.GetString(11);

                allRows.Add(new Rule21ValidationRowViewModel
                {
                    RowNumber    = rowNum,
                    StudQualRef  = qualRef,
                    Stud007Value = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Stud008Value = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Stud012Value = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Stud026Value = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Stud010Value = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    QualCodeValue = qualCode,
                    QualNameValue = qualName ?? "",
                    NalQualName  = nalName,
                    NalAlignedName = reader.IsDBNull(10) ? null : reader.GetString(10),
                    NalCategory  = cat,
                    NalHeqsfRef  = ReadOptional(reader, 12),
                    NalSaqaId    = ReadOptional(reader, 13),
                    NalNqf       = ReadOptional(reader, 14),
                    NalCredits   = ReadOptional(reader, 15),
                    NalOutcome   = ReadOptional(reader, 16),
                    Result       = result,
                    ExceptionReason = string.Equals(result, "FLAGGED", StringComparison.OrdinalIgnoreCase)
                        ? $"Qualification '{(string.IsNullOrWhiteSpace(qualCode) ? qualRef : qualCode)}' ({(string.IsNullOrWhiteSpace(qualName) ? "Unknown qualification" : qualName)}) found in Category '{cat}' Non-Aligned list: '{nalName}'"
                        : null
                });
            }

            var flaggedRows  = allRows.Where(r => string.Equals(r.Result, "FLAGGED", StringComparison.OrdinalIgnoreCase)).ToList();
            var clearSample  = allRows.Where(r => string.Equals(r.Result, "CLEAR", StringComparison.OrdinalIgnoreCase)).ToList();
            var flaggedCount = flaggedRows.Count;
            var totalFte     = studFiltered;
            var clearCount   = totalFte - flaggedCount;
            var rate         = totalFte == 0 ? 0m : Math.Round((decimal)flaggedCount / totalFte * 100m, 2);

            return new Rule21ValidationSummary
            {
                Success = true,
                TotalValidated = totalFte,
                FlaggedCount = flaggedCount,
                ClearCount = clearCount < 0 ? 0 : clearCount,
                ExceptionRate = rate,
                Status = flaggedCount == 0 ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                NalTable = request.NalTable,
                StudQualRefColumn = request.StudQualRefColumn,
                Stud007Column = request.Stud007Column,
                Stud008Column = request.Stud008Column,
                StudFirstTimeColumn = request.StudFirstTimeColumn,
                Stud012Column = request.Stud012Column,
                Stud026Column = request.Stud026Column,
                StudFirstTimeValue = request.StudFirstTimeValue,
                QualCodeColumn = request.QualCodeColumn,
                QualNameColumn = request.QualNameColumn,
                NalRefColumn = request.NalRefColumn,
                NalNameColumn = request.NalNameColumn,
                NalAlignedColumn = request.NalAlignedColumn,
                NalCategoryColumn = request.NalCategoryColumn,
                NalCategoryValue = request.NalCategoryValue,
                NalHeqsfRefColumn = request.NalHeqsfRefColumn,
                NalSaqaIdColumn = request.NalSaqaIdColumn,
                NalNqfColumn = request.NalNqfColumn,
                NalCreditsColumn = request.NalCreditsColumn,
                NalOutcomeColumn = request.NalOutcomeColumn,
                StudTotalCount = studTotal,
                QualTotalCount = qualTotal,
                NalCategoryCount = nalCategory,
                ClientId = request.ClientId,
                FlaggedRows = flaggedRows,
                ClearSampleRows = clearSample
            };
        }

        private static string BuildOptionalStudColsSql(Rule21ValidationRequest request, string alias)
        {
            string Build(string? columnName, string targetAlias)
            {
                if (string.IsNullOrWhiteSpace(columnName))
                    return $"CAST(NULL AS nvarchar(255)) AS {targetAlias}";

                var source = string.IsNullOrWhiteSpace(alias)
                    ? $"[{Sanitise(columnName)}]"
                    : $"{alias}.[{Sanitise(columnName)}]";
                return $"CAST({source} AS nvarchar(255)) AS {targetAlias}";
            }

            return string.Join(",\n    ", new[]
            {
                Build(request.Stud007Column, "Stud007Value"),
                Build(request.Stud008Column, "Stud008Value"),
                Build(request.Stud012Column, "Stud012Value"),
                Build(request.Stud026Column, "Stud026Value")
            }) + ",\n    ";
        }

        private static string BuildOptionalNalColsSql(Rule21ValidationRequest request, string alias)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(request.NalHeqsfRefColumn))
                parts.Add($"CAST({alias}.[{Sanitise(request.NalHeqsfRefColumn)}] AS nvarchar(255)) AS NalHeqsfRef");
            else
                parts.Add("CAST(NULL AS nvarchar(255)) AS NalHeqsfRef");

            if (!string.IsNullOrWhiteSpace(request.NalSaqaIdColumn))
                parts.Add($"CAST({alias}.[{Sanitise(request.NalSaqaIdColumn)}] AS nvarchar(255)) AS NalSaqaId");
            else
                parts.Add("CAST(NULL AS nvarchar(255)) AS NalSaqaId");

            if (!string.IsNullOrWhiteSpace(request.NalNqfColumn))
                parts.Add($"CAST({alias}.[{Sanitise(request.NalNqfColumn)}] AS nvarchar(50)) AS NalNqf");
            else
                parts.Add("CAST(NULL AS nvarchar(50)) AS NalNqf");

            if (!string.IsNullOrWhiteSpace(request.NalCreditsColumn))
                parts.Add($"CAST({alias}.[{Sanitise(request.NalCreditsColumn)}] AS nvarchar(50)) AS NalCredits");
            else
                parts.Add("CAST(NULL AS nvarchar(50)) AS NalCredits");

            if (!string.IsNullOrWhiteSpace(request.NalOutcomeColumn))
                parts.Add($"CAST({alias}.[{Sanitise(request.NalOutcomeColumn)}] AS nvarchar(255)) AS NalOutcome");
            else
                parts.Add("CAST(NULL AS nvarchar(255)) AS NalOutcome");

            return string.Join(",\n    ", parts) + ",";
        }

        private static string? ReadOptional(SqlDataReader reader, int ordinal)
        {
            if (ordinal >= reader.FieldCount || reader.IsDBNull(ordinal)) return null;
            var v = reader.GetString(ordinal).Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }

        private static void ApplyBrowserPreview(Rule21ValidationSummary? summary)
        {
            if (summary == null)
                return;

            var flaggedRows = summary.FlaggedRows ?? new List<Rule21ValidationRowViewModel>();
            var clearRows = summary.ClearSampleRows ?? new List<Rule21ValidationRowViewModel>();

            if (flaggedRows.Count <= BrowserPreviewPerResultLimit && clearRows.Count <= BrowserPreviewPerResultLimit)
            {
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = BrowserPreviewPerResultLimit;
                return;
            }

            summary.FlaggedRows = flaggedRows.Take(BrowserPreviewPerResultLimit).ToList();
            summary.ClearSampleRows = clearRows.Take(BrowserPreviewPerResultLimit).ToList();
            summary.IsPreviewOnly = true;
            summary.PreviewLimit = BrowserPreviewPerResultLimit;
        }

        // â”€â”€ Save / Load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task<int> SaveValidationRunAsync(Rule21ValidationRequest request, Rule21ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 39);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 39);

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
    @ClientID, @UserID, 21, 'First-Time Entering vs Non-Aligned', @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @NalTable, @StudQualRefColumn, @StudFirstTimeColumn,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.ClearCount);
            command.Parameters.AddWithValue("@FailCount", summary.FlaggedCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@NalTable", request.NalTable);
            command.Parameters.AddWithValue("@StudQualRefColumn", request.StudQualRefColumn);
            command.Parameters.AddWithValue("@StudFirstTimeColumn", request.StudFirstTimeColumn);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary.FlaggedRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash = @RecordHash WHERE RunID = @RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule21|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FlaggedCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            return runId;
        }

        private static Rule21ValidationSummary CloneSummary(Rule21ValidationSummary src)
        {
            var json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<Rule21ValidationSummary>(json) ?? new Rule21ValidationSummary();
        }

        // â”€â”€ System DB helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCmd = connection.CreateConfiguredCommand();
            countCmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            countCmd.Parameters.AddWithValue("@RunID", runId);
            var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            await using var deleteCmd = connection.CreateConfiguredCommand();
            deleteCmd.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCmd.Parameters.AddWithValue("@RunID", runId);
            await deleteCmd.ExecuteNonQueryAsync();

            await SetRunStatusAsync(connection, runId, "Needs Review");
            return count;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAll = await HasAllRequiredSignoffsAsync(connection, runId);
            await SetRunStatusAsync(connection, runId, hasAll ? "Reviewed and Completed" : "Needs Review");
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
            if (!await reader.ReadAsync()) return false;
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

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent = 0 WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND IsCurrent = 1;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT rs.SignoffID, ISNULL(rs.SignoffRole,'') AS SignoffRole,
       LTRIM(RTRIM(ISNULL(u.FirstName,'')+' '+ISNULL(u.LastName,''))) AS ReviewerName,
       ISNULL(u.Email,'') AS ReviewerEmail, ISNULL(rs.Comment,'') AS Comment, rs.SignedOffAt,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID = @CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID = rs.ReviewerID
WHERE rs.RunID = @RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
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
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email = @Email;";
            command.Parameters.AddWithValue("@Email", email);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments WHERE ClientID = @ClientID AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
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

        private async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = @SignoffRole) THEN 1 ELSE 0 END;";
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

        private async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID = @ClientID AND RuleNumber = @RuleNumber AND RecordHash IS NOT NULL ORDER BY RunTimestamp DESC, RunID DESC;";
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

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server, InitialCatalog = database,
                IntegratedSecurity = true, TrustServerCertificate = trust,
                Encrypt = false, ConnectTimeout = 180
            };

            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private async Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName)
        {
            ValidateObjectName(tableName);
            await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
            await conn.OpenAsync();
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION;";
            cmd.Parameters.AddWithValue("@TableName", Sanitise(tableName));
            await using var reader = await cmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
                if (!reader.IsDBNull(0)) columns.Add(reader.GetString(0));
            return columns;
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

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Table or column name is required.");
            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
                if (value.Contains(bad, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe table or column name was provided.");
        }

        private static void ValidateObjectNames(Rule21VerifyRequest r)
        {
            ValidateObjectName(r.StudTable); ValidateObjectName(r.QualTable); ValidateObjectName(r.NalTable);
            ValidateObjectName(r.StudQualRefColumn); ValidateObjectName(r.StudFirstTimeColumn);
            ValidateObjectName(r.QualCodeColumn); ValidateObjectName(r.QualNameColumn);
            ValidateObjectName(r.NalCategoryColumn);
        }

        private static void ValidateObjectNames(Rule21ValidationRequest r)
        {
            ValidateObjectName(r.StudTable); ValidateObjectName(r.QualTable); ValidateObjectName(r.NalTable);
            ValidateObjectName(r.StudQualRefColumn); ValidateObjectName(r.StudFirstTimeColumn);
            ValidateObjectName(r.QualCodeColumn); ValidateObjectName(r.QualNameColumn);
            ValidateObjectName(r.NalRefColumn); ValidateObjectName(r.NalNameColumn);
            ValidateObjectName(r.NalCategoryColumn);
            if (!string.IsNullOrWhiteSpace(r.Stud007Column))      ValidateObjectName(r.Stud007Column);
            if (!string.IsNullOrWhiteSpace(r.Stud008Column))      ValidateObjectName(r.Stud008Column);
            if (!string.IsNullOrWhiteSpace(r.Stud012Column))      ValidateObjectName(r.Stud012Column);
            if (!string.IsNullOrWhiteSpace(r.Stud026Column))      ValidateObjectName(r.Stud026Column);
            if (!string.IsNullOrWhiteSpace(r.NalAlignedColumn))  ValidateObjectName(r.NalAlignedColumn);
            if (!string.IsNullOrWhiteSpace(r.NalHeqsfRefColumn)) ValidateObjectName(r.NalHeqsfRefColumn);
            if (!string.IsNullOrWhiteSpace(r.NalSaqaIdColumn))   ValidateObjectName(r.NalSaqaIdColumn);
            if (!string.IsNullOrWhiteSpace(r.NalNqfColumn))      ValidateObjectName(r.NalNqfColumn);
            if (!string.IsNullOrWhiteSpace(r.NalCreditsColumn))  ValidateObjectName(r.NalCreditsColumn);
            if (!string.IsNullOrWhiteSpace(r.NalOutcomeColumn))  ValidateObjectName(r.NalOutcomeColumn);
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = values.FirstOrDefault(v => v.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match)) return match;
            }
            foreach (var fragment in containsMatches)
            {
                var match = values.FirstOrDefault(v => v.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match)) return match;
            }
            return values.FirstOrDefault();
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)));
        }

        private static Rule21ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<Rule21ValidationSummary>(ValidationPayloadCodec.Decode(json));
            }
            catch { return null; }
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
    }
}

