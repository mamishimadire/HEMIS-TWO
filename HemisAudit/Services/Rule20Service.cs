using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule20Service : IRule20Service
    {
        private const int SqlCommandTimeoutSeconds = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
        private const int BrowserPreviewRowLimit = 10;
        private const string ScopeCode = "ALL";
        private const string ScopeTitle = "All Students";
        private const string ScopeDescription = "";
        private static readonly string[] PartOrder = [ScopeCode];
        private static readonly string[] DefaultPgTypes = ["07", "27", "28", "49", "72", "73", "08", "30", "50", "74", "75"];
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule20Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule20ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName)
        {
            try
            {
                tableName = (tableName ?? "").Trim();
                ValidateObjectName(tableName);
                var columns = await GetTableColumnsAsync(server, database, driver, tableName);
                return new Rule20ColumnDiscoveryResult { Success = true, Columns = columns };
            }
            catch (Exception ex)
            {
                return new Rule20ColumnDiscoveryResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<Rule20TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule20TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoStudTable = FindFirst(tables, ["dbo_STUD", "STUD"], ["stud"]),
                    AutoQualTable = FindFirst(tables, ["dbo_QUAL", "QUAL"], ["qual"]),
                    AutoCregTable = FindFirst(tables, ["dbo_CRED", "CRED", "dbo_CREG", "CREG"], ["cred", "creg"]),
                    AutoCrseTable = FindFirst(tables, ["dbo_CRSE", "CRSE"], ["crse"])
                };
            }
            catch (Exception ex)
            {
                return new Rule20TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule20VerifyResult> VerifyTablesAsync(Rule20VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.QualTable, request.CregTable, request.CrseTable, request.ColumnMapping);

                var summary = await AnalyseAsync(new Rule20ValidationRequest
                {
                    Server = request.Server,
                    Database = request.Database,
                    Driver = request.Driver,
                    StudTable = request.StudTable,
                    QualTable = request.QualTable,
                    CregTable = request.CregTable,
                    CrseTable = request.CrseTable,
                    PgTypesText = request.PgTypesText
                }, includeAllReviewRows: false);

                return new Rule20VerifyResult
                {
                    Success = true,
                    FoundationStudentCount = summary.FoundationStudentCount,
                    ValidatedRowCount = summary.TotalValidated
                };
            }
            catch (Exception ex)
            {
                return new Rule20VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule20ValidationSummary> RunValidationAsync(Rule20ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.QualTable, request.CregTable, request.CrseTable, request.ColumnMapping);

                var summary = await AnalyseAsync(request, includeAllReviewRows: true);
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
                return new Rule20ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule20ValidationSummary> GetExportSummaryAsync(Rule20ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.QualTable, request.CregTable, request.CrseTable, request.ColumnMapping);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule20WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.DeceasedTable, '') AS QualTable,
    ISNULL(vr.StudColumn, '') AS CregTable,
    ISNULL(vr.DeceasedColumn, '') AS CrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 20
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var deserializedSummary = DeserializeSummary(reader.IsDBNull(11) ? null : reader.GetString(11));
            if (deserializedSummary != null && includeSummary)
            {
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule20WorkspaceStateViewModel
            {
                ClientId = reader.GetInt32(1),
                RunId = reader.GetInt32(0),
                Server = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
                StudTable = reader.IsDBNull(4) ? "dbo_STUD" : reader.GetString(4),
                QualTable = reader.IsDBNull(5) ? "dbo_QUAL" : reader.GetString(5),
                CregTable = reader.IsDBNull(6) ? "dbo_CRED" : reader.GetString(6),
                CrseTable = reader.IsDBNull(7) ? "dbo_CRSE" : reader.GetString(7),
                CurrentStatus = reader.IsDBNull(8) ? "" : reader.GetString(8),
                LastEditedByUserName = reader.IsDBNull(9) ? null : reader.GetString(9),
                LastEditedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Summary = summary
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

            if (deserializedSummary != null)
            {
                workspace.PgTypesText = string.IsNullOrWhiteSpace(deserializedSummary.PgTypesText)
                    ? string.Join(", ", deserializedSummary.PgTypes)
                    : deserializedSummary.PgTypesText;
                workspace.GoverningPartCodes = NormalizeGoverningPartCodes(deserializedSummary.GoverningPartCodes);
                workspace.ColumnMapping = deserializedSummary.ColumnMapping ?? new Rule20ColumnMapping();
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

            if (workspace.Summary != null)
                workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule20RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var summary = DeserializeSummary(reader.IsDBNull(6) ? null : reader.GetString(6));
            if (summary == null)
                return null;

            if (includeFullResults)
            {
                summary = await ExpandSavedSummaryIfNeededAsync(summary, sourceServer);
                summary.DisplayedCount = summary.ReviewRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var clientId = reader.GetInt32(1);
            var review = new Rule20RunReviewViewModel
            {
                RunId = reader.GetInt32(0),
                ClientId = clientId,
                IsCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2),
                EngagementName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MaconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SourceServer = sourceServer,
                Summary = summary
            };

            await reader.CloseAsync();

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return review;
        }

        public async Task<Rule20WorkspaceSaveResult> SaveWorkspaceAsync(Rule20ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId is null || request.RunId <= 0)
                {
                    return new Rule20WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run the validation first so the workspace can be saved."
                    };
                }

                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                if (!clientId.HasValue || clientId.Value != request.ClientId)
                {
                    return new Rule20WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "The saved workspace could not be found for this engagement."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, request.ClientId);

                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                var previousHash = await GetValidationRecordHashAsync(connection, request.RunId.Value);
                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET HemisServer = @HemisServer,
    AuditDatabase = @AuditDatabase,
    StudTable = @StudTable,
    DeceasedTable = @QualTable,
    StudColumn = @CregTable,
    DeceasedColumn = @CrseTable,
    LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID
  AND RuleNumber = 20;";
                command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                command.Parameters.AddWithValue("@ClientID", request.ClientId);
                command.Parameters.AddWithValue("@HemisServer", request.Server);
                command.Parameters.AddWithValue("@AuditDatabase", request.Database);
                command.Parameters.AddWithValue("@StudTable", request.StudTable);
                command.Parameters.AddWithValue("@QualTable", request.QualTable);
                command.Parameters.AddWithValue("@CregTable", request.CregTable);
                command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)(reviewerName ?? reviewerEmail) ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule20|{request.RunId.Value}|{request.ClientId}|{request.Server}|{request.Database}|{request.StudTable}|{request.QualTable}|{request.CregTable}|{request.CrseTable}|{request.PgTypesText}|{string.Join(",", NormalizeGoverningPartCodes(request.GoverningPartCodes))}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule20WorkspaceSaveResult
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
                return new Rule20WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule20WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule20WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash = await GetValidationRecordHashAsync(connection, runId);

                await using var command = connection.CreateConfiguredCommand();
                command.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = NULL,
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                command.Parameters.AddWithValue("@RunID", runId);
                command.Parameters.AddWithValue("@LastEditedByUserName", (object?)(reviewerName ?? reviewerEmail) ?? DBNull.Value);
                command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule20|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await command.ExecuteNonQueryAsync();

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule20WorkspaceSaveResult
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
                return new Rule20WorkspaceSaveResult
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
                throw new InvalidOperationException("The reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("The selected Rule 20 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 20 run.");

            if (!string.Equals(signoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
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
            command.Parameters.AddWithValue("@SignoffRole", signoffRole!);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue)
                throw new InvalidOperationException("The reviewer could not be resolved in the system database.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue)
                throw new InvalidOperationException("The selected Rule 20 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule20ValidationRequest request)
        {
            ValidateRequest(request);

            var m = request.ColumnMapping ?? new Rule20ColumnMapping();
            var studTable = Sanitise(request.StudTable);
            var qualTable = Sanitise(request.QualTable);
            var bridgeTable = Sanitise(request.CregTable);
            var crseTable = Sanitise(request.CrseTable);
            var pgTypes = ParsePgTypes(request.PgTypesText);
            var governingPartCodes = NormalizeGoverningPartCodes(request.GoverningPartCodes);
            var pgTypeSqlList = BuildSqlStringList(pgTypes);
            var procedureSteps = BuildProcedureSteps(request.StudTable, request.QualTable, request.CregTable, request.CrseTable, m);

            var sql = $@"-- ============================================================================
-- HEMIS RULE 20: FOUNDATION VALIDATION
-- 100% POPULATION - NO SAMPLING
-- ============================================================================
-- Database : {request.Database}
-- Rule     : Foundation Students must be linked to Foundation Courses
-- Population:
--      {request.StudTable}.{m.StudFoundationFlag} = '{m.StudFoundationValue}'
-- Validation:
--      {request.CrseTable}.{m.CrseFoundationFlag} = '{m.CrseFoundationValue}'
-- Joins:
--      {request.StudTable}.{m.StudQualCode} = {request.CregTable}.{m.CregQualCode}
--      {request.CregTable}.{m.CregCourseCode} = {request.CrseTable}.{m.CrseCourseCode}
--      {request.StudTable}.{m.StudQualCode} = {request.QualTable}.{m.QualQualCode}
-- ============================================================================

SELECT

    --------------------------------------------------------------------------
    -- Student Information
    --------------------------------------------------------------------------
    S.[{m.StudStudentNo}]      AS [Student_Number],
    S.[{m.StudColumn008}]      AS [South_African_Identity_Number],
    S.[{m.StudColumn066}]      AS [Surname],
    S.[{m.StudColumn067}]      AS [First_Name],
    S.[{m.StudColumn068}]      AS [Middle_Name],
    S.[{m.StudColumn012}]      AS [Date_of_Birth],
    S.[{m.StudColumn013}]      AS [Gender],
    S.[{m.StudColumn014}]      AS [Race],
    S.[{m.StudColumn015}]      AS [Nationality],
    S.[{m.StudColumn010}]      AS [Entrance_Category],
    S.[{m.StudColumn026}]      AS [CESSM_Category],
    S.[{m.StudColumn025}]      AS [Qualification_Requirements_Status],

    --------------------------------------------------------------------------
    -- Qualification Information
    --------------------------------------------------------------------------
    S.[{m.StudQualCode}]       AS [Qualification_Code],
    Q.[{m.QualDescription}]    AS [Qualification_Description],

    --------------------------------------------------------------------------
    -- Foundation Information
    --------------------------------------------------------------------------
    S.[{m.StudFoundationFlag}] AS [Foundation_Student_Indicator],
    BRIDGE.[{m.CregCourseCode}] AS [Course_Code],
    CRSE.[{m.CrseCourseName}]  AS [Course_Name],
    CRSE.[{m.CrseFoundationFlag}] AS [Foundation_Course_Indicator],

    --------------------------------------------------------------------------
    -- Audit Validation
    --------------------------------------------------------------------------
    CASE
        WHEN S.[{m.StudFoundationFlag}] = '{m.StudFoundationValue}'
         AND CRSE.[{m.CrseFoundationFlag}] = '{m.CrseFoundationValue}'
        THEN 'PASS'
        ELSE 'FAIL'
    END AS [Validation_Result],

    --------------------------------------------------------------------------
    -- Audit Rule
    --------------------------------------------------------------------------
    'Rule 20 - Foundation Validation' AS [Audit_Rule]

{BuildBaseJoinClause(studTable, qualTable, bridgeTable, crseTable, m)}
WHERE {FoundationStudentCondition("S", m)}
  AND {IsFoundationCourseCondition("CRSE", m)}
ORDER BY S.[{m.StudStudentNo}],
         BRIDGE.[{m.CregCourseCode}];";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule20ValidationSummary> AnalyseAsync(Rule20ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var m = request.ColumnMapping ?? new Rule20ColumnMapping();
            var studTable = Sanitise(request.StudTable);
            var qualTable = Sanitise(request.QualTable);
            var bridgeTable = Sanitise(request.CregTable);
            var crseTable = Sanitise(request.CrseTable);
            var pgTypes = ParsePgTypes(request.PgTypesText);
            var governingPartCodes = NormalizeGoverningPartCodes(request.GoverningPartCodes);
            var pgTypeSqlList = BuildSqlStringList(pgTypes);
            var foundationStudentCount = await CountAsync(conn, BuildFoundationStudentCountQuery(studTable, qualTable, bridgeTable, crseTable, m));
            List<Rule20PartSummaryItemViewModel> partSummaries;
            List<Rule20ReviewRowViewModel> reviewRows;

            if (includeAllReviewRows)
            {
                reviewRows = await LoadNotebookRowsAsync(
                    conn,
                    BuildNotebookFullQuery(studTable, qualTable, bridgeTable, crseTable, pgTypeSqlList, m));
                partSummaries = BuildNotebookPartSummaries(
                    reviewRows
                        .GroupBy(row => row.PartCode ?? "", StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                (partSummaries, reviewRows) = await LoadNotebookPreviewAnalysisAsync(
                    conn,
                    studTable,
                    qualTable,
                    bridgeTable,
                    crseTable,
                    pgTypeSqlList,
                    m);
            }

            reviewRows = NormalizeReviewRows(reviewRows);

            var totalValidated = partSummaries.Sum(x => x.TotalCount);
            var passCount = partSummaries.Sum(x => x.PassCount);
            var failCount = partSummaries.Sum(x => x.FailCount);
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;

            return new Rule20ValidationSummary
            {
                Success = true,
                FoundationStudentCount = foundationStudentCount,
                TotalValidated = totalValidated,
                DisplayedCount = reviewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionRate = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status = DetermineOverallStatus(partSummaries, governingPartCodes),
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                ColumnMapping = m,
                PgTypesText = string.Join(", ", pgTypes),
                PgTypes = pgTypes,
                GoverningPartCodes = governingPartCodes,
                GoverningPartCodesText = FormatGoverningPartCodes(governingPartCodes),
                OverallStatusRuleText = BuildOverallStatusRuleText(governingPartCodes),
                TableLinkageText = BuildTableLinkageText(request.StudTable, request.QualTable, request.CregTable, request.CrseTable),
                ProcedureSteps = BuildProcedureSteps(request.StudTable, request.QualTable, request.CregTable, request.CrseTable),
                ClientId = request.ClientId,
                PartSummaries = partSummaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 20 completed with the full notebook-equivalent result set."
                    : "Counts reflect the full notebook-equivalent result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule20ValidationRequest request, Rule20ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 20);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 20);
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
    @ClientID, @UserID, 20, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @QualTable, @CregTable, @CrseTable,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Foundation Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@QualTable", request.QualTable);
            command.Parameters.AddWithValue("@CregTable", request.CregTable);
            command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
            var persistedSummary = CreateBrowserPreview(summary);
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persistedSummary)));
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
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule20|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            return runId;
        }

        private async Task<Dictionary<string, List<Rule20ReviewRowViewModel>>> LoadNotebookRowsByPartAsync(
            SqlConnection connection,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            List<string> pgTypes,
            string pgTypeSqlList,
            Rule20ColumnMapping? m = null)
        {
            var rowsByPart = new Dictionary<string, List<Rule20ReviewRowViewModel>>(StringComparer.OrdinalIgnoreCase);
            foreach (var partCode in PartOrder)
            {
                rowsByPart[partCode] = await LoadNotebookPartRowsAsync(
                    connection,
                    partCode,
                    studTable,
                    qualTable,
                    cregTable,
                    crseTable,
                    pgTypes,
                    pgTypeSqlList,
                    m);
            }

            return rowsByPart;
        }

        private static async Task<List<Rule20ReviewRowViewModel>> LoadNotebookPartRowsAsync(
            SqlConnection connection,
            string partCode,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            List<string> pgTypes,
            string pgTypeSqlList,
            Rule20ColumnMapping? m = null)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = BuildNotebookPartQuery(partCode, studTable, qualTable, cregTable, crseTable, pgTypeSqlList, m);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule20ReviewRowViewModel>();
            while (await reader.ReadAsync())
            {
                var qualificationType = ReadString(reader, "QualificationType005");
                var foundationCourse = ReadString(reader, "FoundationCourse091");
                var notebookStatus = IsNotebookFoundationCourse(foundationCourse) ? "VALID" : "INVALID";
                var row = MapRule20ReviewRow(reader);
                row.PartCode = partCode;
                row.PartTitle = GetPartTitle(partCode);
                row.PartDescription = GetPartDescription(partCode);
                row.QualificationType005 = qualificationType;
                row.FoundationCourse091 = foundationCourse;
                row.StudentType = GetNotebookStudentType(partCode, qualificationType, pgTypes);
                row.NotebookStatus = notebookStatus;
                row.ValidationResult = notebookStatus == "VALID" ? "PASS" : "FAIL";
                row.ValidationExplanation = BuildValidationExplanation(
                    row.FoundationFlag106,
                    row.CourseCode030,
                    foundationCourse);
                rows.Add(row);
            }

            return rows;
        }

        private static string BuildNotebookPartQuery(
            string partCode,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            string pgTypeSqlList,
            Rule20ColumnMapping? m = null)
        {
            return $@"
SELECT
    '{partCode}' AS PartCode,
    '{GetPartTitle(partCode)}' AS PartTitle,
    '{GetPartDescription(partCode)}' AS PartDescription,
    CAST(S.[{ColOrDefault(m?.StudStudentNo, "_007")}] AS nvarchar(255)) AS StudentNumber007,
    CAST(S.[{ColOrDefault(m?.StudColumn008, "_008")}] AS nvarchar(255)) AS StudentColumn008,
    CAST(S.[{ColOrDefault(m?.StudColumn066, "_066")}] AS nvarchar(255)) AS StudentColumn066,
    CAST(S.[{ColOrDefault(m?.StudColumn067, "_067")}] AS nvarchar(255)) AS StudentColumn067,
    CAST(S.[{ColOrDefault(m?.StudColumn068, "_068")}] AS nvarchar(255)) AS StudentColumn068,
    CAST(S.[{ColOrDefault(m?.StudColumn012, "_012")}] AS nvarchar(255)) AS StudentColumn012,
    CAST(S.[{ColOrDefault(m?.StudColumn013, "_013")}] AS nvarchar(255)) AS StudentColumn013,
    CAST(S.[{ColOrDefault(m?.StudColumn014, "_014")}] AS nvarchar(255)) AS StudentColumn014,
    CAST(S.[{ColOrDefault(m?.StudColumn015, "_015")}] AS nvarchar(255)) AS StudentColumn015,
    CAST(S.[{ColOrDefault(m?.StudColumn010, "_010")}] AS nvarchar(255)) AS StudentColumn010,
    CAST(S.[{ColOrDefault(m?.StudColumn026, "_026")}] AS nvarchar(255)) AS StudentColumn026,
    CAST(S.[{ColOrDefault(m?.StudColumn025, "_025")}] AS nvarchar(255)) AS StudentColumn025,
    CAST(S.[{ColOrDefault(m?.StudQualCode, "_001")}] AS nvarchar(255)) AS QualificationCode001,
    CAST(S.[{ColOrDefault(m?.StudName, "_019")}] AS nvarchar(255)) AS Name019,
    CAST(S.[{ColOrDefault(m?.StudIdNo, "_024")}] AS nvarchar(255)) AS IdNumber024,
    CAST(S.[{ColOrDefault(m?.StudFoundationFlag, "_106")}] AS nvarchar(255)) AS FoundationFlag106,
    CAST(Q.[{ColOrDefault(m?.QualDescription, "_003")}] AS nvarchar(255)) AS QualificationDescription003,
    CAST(Q.[{ColOrDefault(m?.QualType, "_005")}] AS nvarchar(255)) AS QualificationType005,
    CAST(BRIDGE.[{ColOrDefault(m?.CregQualCode, "_001")}] AS nvarchar(255)) AS BridgeQualificationCode001,
    CAST(BRIDGE.[{ColOrDefault(m?.CregCourseCode, "_030")}] AS nvarchar(255)) AS CourseCode030,
    CAST(CRSE.[{ColOrDefault(m?.CrseCourseName, "_058")}] AS nvarchar(255)) AS CourseName058,
    CAST(CRSE.[{ColOrDefault(m?.CrseCourseCode, "_030")}] AS nvarchar(255)) AS CrseCourseCode030,
    CAST(CRSE.[{ColOrDefault(m?.CrseFoundationFlag, "_091")}] AS nvarchar(255)) AS FoundationCourse091,
    CASE WHEN {IsPostgraduateQualificationCondition("Q", pgTypeSqlList, m)} THEN 'Postgraduate' ELSE 'Undergraduate' END AS StudentType,
    CASE WHEN {IsFoundationValidationPassCondition("CRSE", m)} THEN 'VALID' ELSE 'INVALID' END AS NotebookStatus,
    CASE WHEN {IsFoundationValidationPassCondition("CRSE", m)} THEN 'PASS' ELSE 'FAIL' END AS ValidationResult
{BuildBaseJoinClause(studTable, qualTable, cregTable, crseTable, m)}
WHERE {FoundationStudentCondition("S", m)}
  AND {IsFoundationCourseCondition("CRSE", m)}
ORDER BY S.[{ColOrDefault(m?.StudStudentNo, "_007")}];";
        }

        private static List<Rule20PartSummaryItemViewModel> BuildNotebookPartSummaries(
            IReadOnlyDictionary<string, List<Rule20ReviewRowViewModel>> rowsByPart) =>
            PartOrder
                .Select(partCode =>
                {
                    var rows = rowsByPart.TryGetValue(partCode, out var partRows)
                        ? partRows
                        : new List<Rule20ReviewRowViewModel>();
                    var passCount = rows.Count(row => string.Equals(row.ValidationResult, "PASS", StringComparison.Ordinal));
                    var failCount = rows.Count - passCount;

                    return new Rule20PartSummaryItemViewModel
                    {
                        PartCode = partCode,
                        PartTitle = GetPartTitle(partCode),
                        PartDescription = GetPartDescription(partCode),
                        TotalCount = rows.Count,
                        PassCount = passCount,
                        FailCount = failCount,
                        Status = failCount == 0 ? "PASS" : "FAIL"
                    };
                })
                .ToList();

        private static async Task<(List<Rule20PartSummaryItemViewModel> PartSummaries, List<Rule20ReviewRowViewModel> ReviewRows)> LoadNotebookPreviewAnalysisAsync(
            SqlConnection connection,
            string studTable,
            string qualTable,
            string cregTable,
            string crseTable,
            string pgTypeSqlList,
            Rule20ColumnMapping? m = null)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = BuildNotebookPreviewAnalysisQuery(studTable, qualTable, cregTable, crseTable, pgTypeSqlList, m);

            var partSummaries = new List<Rule20PartSummaryItemViewModel>();
            var reviewRows = new List<Rule20ReviewRowViewModel>();

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var passCount = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));
                var failCount = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5));

                partSummaries.Add(new Rule20PartSummaryItemViewModel
                {
                    PartCode = ReadString(reader, 0),
                    PartTitle = ReadString(reader, 1),
                    PartDescription = ReadString(reader, 2),
                    TotalCount = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                    PassCount = passCount,
                    FailCount = failCount,
                    Status = failCount == 0 ? "PASS" : "FAIL"
                });
            }

            if (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    reviewRows.Add(MapRule20ReviewRow(reader));
                }
            }

            return
            (
                PartOrder
                    .Select(partCode => partSummaries.FirstOrDefault(item => string.Equals(item.PartCode, partCode, StringComparison.OrdinalIgnoreCase))
                        ?? new Rule20PartSummaryItemViewModel
                        {
                            PartCode = partCode,
                            PartTitle = GetPartTitle(partCode),
                            PartDescription = GetPartDescription(partCode),
                            TotalCount = 0,
                            PassCount = 0,
                            FailCount = 0,
                            Status = "PASS"
                        })
                    .ToList(),
                reviewRows
            );
        }

        private static async Task<List<Rule20ReviewRowViewModel>> LoadNotebookRowsAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule20ReviewRowViewModel>();
            while (await reader.ReadAsync())
            {
                rows.Add(MapRule20ReviewRow(reader));
            }

            return rows;
        }

        private static string GetNotebookStudentType(string partCode, string qualificationType, IEnumerable<string> pgTypes)
        {
            if (string.Equals(partCode, "A", StringComparison.OrdinalIgnoreCase))
                return "Undergraduate";
            if (string.Equals(partCode, "C", StringComparison.OrdinalIgnoreCase))
                return "Postgraduate";

            var notebookValue = (qualificationType ?? "").Trim();
            return pgTypes.Any(pgType => string.Equals(pgType, notebookValue, StringComparison.Ordinal))
                ? "Postgraduate"
                : "Undergraduate";
        }

        private static bool IsNotebookFoundationCourse(string? foundationCourseValue) =>
            string.Equals(NormalizeRowText(foundationCourseValue), "Y", StringComparison.OrdinalIgnoreCase);

        private static string ReadString(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal)) ?? "";

        private static string ReadString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal)) ?? "";
        }

        private static Rule20ReviewRowViewModel MapRule20ReviewRow(SqlDataReader reader)
        {
            var row = new Rule20ReviewRowViewModel
            {
                PartCode = ReadString(reader, "PartCode"),
                PartTitle = ReadString(reader, "PartTitle"),
                PartDescription = ReadString(reader, "PartDescription"),
                StudentNumber007 = ReadString(reader, "StudentNumber007"),
                StudentColumn008 = ReadString(reader, "StudentColumn008"),
                StudentColumn066 = ReadString(reader, "StudentColumn066"),
                StudentColumn067 = ReadString(reader, "StudentColumn067"),
                StudentColumn068 = ReadString(reader, "StudentColumn068"),
                StudentColumn012 = ReadString(reader, "StudentColumn012"),
                StudentColumn013 = ReadString(reader, "StudentColumn013"),
                StudentColumn014 = ReadString(reader, "StudentColumn014"),
                StudentColumn015 = ReadString(reader, "StudentColumn015"),
                StudentColumn010 = ReadString(reader, "StudentColumn010"),
                StudentColumn026 = ReadString(reader, "StudentColumn026"),
                StudentColumn025 = ReadString(reader, "StudentColumn025"),
                QualificationCode001 = ReadString(reader, "QualificationCode001"),
                Name019 = ReadString(reader, "Name019"),
                IdNumber024 = ReadString(reader, "IdNumber024"),
                FoundationFlag106 = ReadString(reader, "FoundationFlag106"),
                QualificationDescription003 = ReadString(reader, "QualificationDescription003"),
                QualificationType005 = ReadString(reader, "QualificationType005"),
                BridgeQualificationCode001 = ReadString(reader, "BridgeQualificationCode001"),
                CourseCode030 = ReadString(reader, "CourseCode030"),
                CourseName058 = ReadString(reader, "CourseName058"),
                CrseCourseCode030 = ReadString(reader, "CrseCourseCode030"),
                FoundationCourse091 = ReadString(reader, "FoundationCourse091"),
                StudentType = ReadString(reader, "StudentType"),
                NotebookStatus = ReadString(reader, "NotebookStatus"),
                ValidationResult = ReadString(reader, "ValidationResult")
            };

            row.ValidationExplanation = BuildValidationExplanation(
                row.FoundationFlag106,
                row.CourseCode030,
                row.FoundationCourse091);

            return row;
        }

        private async Task EnsureColumnsExistAsync(string server, string database, string driver, string studTable, string qualTable, string cregTable, string crseTable, Rule20ColumnMapping? m = null)
        {
            m ??= new Rule20ColumnMapping();
            var studColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var qualColumns = await GetTableColumnsAsync(server, database, driver, qualTable);
            var cregColumns = await GetTableColumnsAsync(server, database, driver, cregTable);
            var crseColumns = await GetTableColumnsAsync(server, database, driver, crseTable);

            EnsureRequiredColumns(studTable, studColumns, [m.StudStudentNo, m.StudColumn008, m.StudColumn066, m.StudColumn067, m.StudColumn068, m.StudColumn012, m.StudColumn013, m.StudColumn014, m.StudColumn015, m.StudColumn010, m.StudColumn026, m.StudColumn025, m.StudQualCode, m.StudFoundationFlag]);
            EnsureRequiredColumns(qualTable, qualColumns, [m.QualQualCode, m.QualDescription, m.QualType]);
            EnsureRequiredColumns(cregTable, cregColumns, [m.CregQualCode, m.CregCourseCode]);
            EnsureRequiredColumns(crseTable, crseColumns, [m.CrseCourseCode, m.CrseCourseName, m.CrseFoundationFlag]);
        }

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

        private static void EnsureRequiredColumns(string tableName, List<string> actualColumns, IEnumerable<string> requiredColumns)
        {
            foreach (var requiredColumn in requiredColumns)
            {
                if (!actualColumns.Any(c => c.Equals(requiredColumn, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Column '{requiredColumn}' was not found on table '{tableName}'.");
            }
        }

        private static void ValidateRequest(Rule20ValidationRequest request)
        {
            request.StudTable = (request.StudTable ?? "").Trim();
            request.QualTable = (request.QualTable ?? "").Trim();
            request.CregTable = (request.CregTable ?? "").Trim();
            request.CrseTable = (request.CrseTable ?? "").Trim();

            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable) ||
                string.IsNullOrWhiteSpace(request.QualTable) ||
                string.IsNullOrWhiteSpace(request.CregTable) ||
                string.IsNullOrWhiteSpace(request.CrseTable))
            {
                throw new InvalidOperationException("All four Rule 20 tables are required.");
            }

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.QualTable);
            ValidateObjectName(request.CregTable);
            ValidateObjectName(request.CrseTable);
            ParsePgTypes(request.PgTypesText);
            NormalizeGoverningPartCodes(request.GoverningPartCodes);

            request.ColumnMapping ??= new Rule20ColumnMapping();
            NormalizeColumnMapping(request.ColumnMapping);
        }

        private static void NormalizeColumnMapping(Rule20ColumnMapping m)
        {
            m.StudStudentNo = ColOrDefault(m.StudStudentNo, "_007");
            m.StudColumn008 = ColOrDefault(m.StudColumn008, "_008");
            m.StudColumn066 = ColOrDefault(m.StudColumn066, "_066");
            m.StudColumn067 = ColOrDefault(m.StudColumn067, "_067");
            m.StudColumn068 = ColOrDefault(m.StudColumn068, "_068");
            m.StudColumn012 = ColOrDefault(m.StudColumn012, "_012");
            m.StudColumn013 = ColOrDefault(m.StudColumn013, "_013");
            m.StudColumn014 = ColOrDefault(m.StudColumn014, "_014");
            m.StudColumn015 = ColOrDefault(m.StudColumn015, "_015");
            m.StudColumn010 = ColOrDefault(m.StudColumn010, "_010");
            m.StudColumn026 = ColOrDefault(m.StudColumn026, "_026");
            m.StudColumn025 = ColOrDefault(m.StudColumn025, "_025");
            m.StudQualCode = ColOrDefault(m.StudQualCode, "_001");
            m.StudName = ColOrDefault(m.StudName, "_019");
            m.StudIdNo = ColOrDefault(m.StudIdNo, "_024");
            m.StudFoundationFlag = ColOrDefault(m.StudFoundationFlag, "_106");
            m.StudFoundationValue = ValOrDefault(m.StudFoundationValue, "Y");
            m.QualQualCode = ColOrDefault(m.QualQualCode, "_001");
            m.QualDescription = ColOrDefault(m.QualDescription, "_003");
            m.QualType = ColOrDefault(m.QualType, "_005");
            m.CregQualCode = ColOrDefault(m.CregQualCode, "_001");
            m.CregCourseCode = ColOrDefault(m.CregCourseCode, "_030");
            m.CrseCourseCode = ColOrDefault(m.CrseCourseCode, "_030");
            m.CrseCourseName = ColOrDefault(m.CrseCourseName, "_058");
            m.CrseFoundationFlag = ColOrDefault(m.CrseFoundationFlag, "_091");
            m.CrseFoundationValue = ValOrDefault(m.CrseFoundationValue, "Y");

            ValidateColumnName(m.StudStudentNo);
            ValidateColumnName(m.StudColumn008);
            ValidateColumnName(m.StudColumn066);
            ValidateColumnName(m.StudColumn067);
            ValidateColumnName(m.StudColumn068);
            ValidateColumnName(m.StudColumn012);
            ValidateColumnName(m.StudColumn013);
            ValidateColumnName(m.StudColumn014);
            ValidateColumnName(m.StudColumn015);
            ValidateColumnName(m.StudColumn010);
            ValidateColumnName(m.StudColumn026);
            ValidateColumnName(m.StudColumn025);
            ValidateColumnName(m.StudQualCode);
            ValidateColumnName(m.StudName);
            ValidateColumnName(m.StudIdNo);
            ValidateColumnName(m.StudFoundationFlag);
            ValidateColumnName(m.QualQualCode);
            ValidateColumnName(m.QualDescription);
            ValidateColumnName(m.QualType);
            ValidateColumnName(m.CregQualCode);
            ValidateColumnName(m.CregCourseCode);
            ValidateColumnName(m.CrseCourseCode);
            ValidateColumnName(m.CrseCourseName);
            ValidateColumnName(m.CrseFoundationFlag);
        }

        private static string ColOrDefault(string? val, string def) =>
            string.IsNullOrWhiteSpace(val) ? def : val.Trim();

        private static string ValOrDefault(string? val, string def) =>
            string.IsNullOrWhiteSpace(val) ? def : val.Trim();

        private static void ValidateRequest(Rule20VerifyRequest request) =>
            ValidateRequest(new Rule20ValidationRequest
            {
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                StudTable = request.StudTable,
                QualTable = request.QualTable,
                CregTable = request.CregTable,
                CrseTable = request.CrseTable,
                PgTypesText = request.PgTypesText,
                GoverningPartCodes = request.GoverningPartCodes,
                ColumnMapping = request.ColumnMapping ?? new Rule20ColumnMapping()
            });

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

        private async Task<Rule20ValidationSummary?> GetValidationSummaryAsync(SqlConnection connection, int runId)
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

        private static string BuildNotebookFullQuery(string studTable, string qualTable, string cregTable, string crseTable, string pgTypeSqlList, Rule20ColumnMapping? m = null) =>
            $@"
WITH Source AS (
{BuildUnionQuery(studTable, qualTable, cregTable, crseTable, pgTypeSqlList, m)}
)
SELECT
    PartOrder,
    PartCode,
    PartTitle,
    PartDescription,
    StudentNumber007,
    StudentColumn008,
    StudentColumn066,
    StudentColumn067,
    StudentColumn068,
    StudentColumn012,
    StudentColumn013,
    StudentColumn014,
    StudentColumn015,
    StudentColumn010,
    StudentColumn026,
    StudentColumn025,
    QualificationCode001,
    Name019,
    IdNumber024,
    FoundationFlag106,
    QualificationDescription003,
    QualificationType005,
    BridgeQualificationCode001,
    CourseCode030,
    CourseName058,
    CrseCourseCode030,
    FoundationCourse091,
    StudentType,
    NotebookStatus,
    ValidationResult
FROM Source
ORDER BY PartOrder,
         CASE WHEN ValidationResult = 'PASS' THEN 0 ELSE 1 END,
         CASE WHEN TRY_CONVERT(bigint, StudentNumber007) IS NULL THEN 1 ELSE 0 END,
         TRY_CONVERT(bigint, StudentNumber007),
         StudentNumber007,
         QualificationCode001,
         CourseCode030;";

        private static string BuildNotebookPreviewAnalysisQuery(string studTable, string qualTable, string cregTable, string crseTable, string pgTypeSqlList, Rule20ColumnMapping? m = null) =>
            $@"
WITH Source AS (
{BuildUnionQuery(studTable, qualTable, cregTable, crseTable, pgTypeSqlList, m)}
)
SELECT
    PartOrder,
    PartCode,
    PartTitle,
    PartDescription,
    StudentNumber007,
    StudentColumn008,
    StudentColumn066,
    StudentColumn067,
    StudentColumn068,
    StudentColumn012,
    StudentColumn013,
    StudentColumn014,
    StudentColumn015,
    StudentColumn010,
    StudentColumn026,
    StudentColumn025,
    QualificationCode001,
    Name019,
    IdNumber024,
    FoundationFlag106,
    QualificationDescription003,
    QualificationType005,
    BridgeQualificationCode001,
    CourseCode030,
    CourseName058,
    CrseCourseCode030,
    FoundationCourse091,
    StudentType,
    NotebookStatus,
    ValidationResult
INTO #Rule20Source
FROM Source;

SELECT
    PartCode,
    MAX(PartTitle) AS PartTitle,
    MAX(PartDescription) AS PartDescription,
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN ValidationResult = 'PASS' THEN 1 ELSE 0 END) AS PassCount,
    SUM(CASE WHEN ValidationResult = 'FAIL' THEN 1 ELSE 0 END) AS FailCount
FROM #Rule20Source
GROUP BY PartOrder, PartCode
ORDER BY PartOrder;

WITH Ranked AS (
    SELECT
        PartOrder,
        PartCode,
        PartTitle,
        PartDescription,
        StudentNumber007,
        StudentColumn008,
        StudentColumn066,
        StudentColumn067,
        StudentColumn068,
        StudentColumn012,
        StudentColumn013,
        StudentColumn014,
        StudentColumn015,
        StudentColumn010,
        StudentColumn026,
        StudentColumn025,
        QualificationCode001,
        Name019,
        IdNumber024,
        FoundationFlag106,
        QualificationDescription003,
        QualificationType005,
        BridgeQualificationCode001,
        CourseCode030,
        CourseName058,
        CrseCourseCode030,
        FoundationCourse091,
        StudentType,
        NotebookStatus,
        ValidationResult,
        ROW_NUMBER() OVER (
            ORDER BY PartOrder,
                     CASE WHEN TRY_CONVERT(bigint, StudentNumber007) IS NULL THEN 1 ELSE 0 END,
                     TRY_CONVERT(bigint, StudentNumber007),
                     StudentNumber007,
                     QualificationCode001,
                     CourseCode030
        ) AS PreviewRowNumber
    FROM #Rule20Source
)
SELECT
    PartOrder,
    PartCode,
    PartTitle,
    PartDescription,
    StudentNumber007,
    StudentColumn008,
    StudentColumn066,
    StudentColumn067,
    StudentColumn068,
    StudentColumn012,
    StudentColumn013,
    StudentColumn014,
    StudentColumn015,
    StudentColumn010,
    StudentColumn026,
    StudentColumn025,
    QualificationCode001,
    Name019,
    IdNumber024,
    FoundationFlag106,
    QualificationDescription003,
    QualificationType005,
    BridgeQualificationCode001,
    CourseCode030,
    CourseName058,
    CrseCourseCode030,
    FoundationCourse091,
    StudentType,
    NotebookStatus,
    ValidationResult
FROM Ranked
WHERE PreviewRowNumber <= {BrowserPreviewRowLimit}
ORDER BY PartOrder,
         CASE WHEN ValidationResult = 'PASS' THEN 0 ELSE 1 END,
         PreviewRowNumber,
         StudentNumber007,
         QualificationCode001,
         CourseCode030;

DROP TABLE #Rule20Source;";

        private static string BuildUnionQuery(string studTable, string qualTable, string cregTable, string crseTable, string pgTypeSqlList, Rule20ColumnMapping? m = null) =>
            $@"
SELECT
    1 AS PartOrder,
    '{ScopeCode}' AS PartCode,
    '{ScopeTitle}' AS PartTitle,
    '{ScopeDescription}' AS PartDescription,
    CAST(S.[{ColOrDefault(m?.StudStudentNo, "_007")}] AS nvarchar(255)) AS StudentNumber007,
    CAST(S.[{ColOrDefault(m?.StudColumn008, "_008")}] AS nvarchar(255)) AS StudentColumn008,
    CAST(S.[{ColOrDefault(m?.StudColumn066, "_066")}] AS nvarchar(255)) AS StudentColumn066,
    CAST(S.[{ColOrDefault(m?.StudColumn067, "_067")}] AS nvarchar(255)) AS StudentColumn067,
    CAST(S.[{ColOrDefault(m?.StudColumn068, "_068")}] AS nvarchar(255)) AS StudentColumn068,
    CAST(S.[{ColOrDefault(m?.StudColumn012, "_012")}] AS nvarchar(255)) AS StudentColumn012,
    CAST(S.[{ColOrDefault(m?.StudColumn013, "_013")}] AS nvarchar(255)) AS StudentColumn013,
    CAST(S.[{ColOrDefault(m?.StudColumn014, "_014")}] AS nvarchar(255)) AS StudentColumn014,
    CAST(S.[{ColOrDefault(m?.StudColumn015, "_015")}] AS nvarchar(255)) AS StudentColumn015,
    CAST(S.[{ColOrDefault(m?.StudColumn010, "_010")}] AS nvarchar(255)) AS StudentColumn010,
    CAST(S.[{ColOrDefault(m?.StudColumn026, "_026")}] AS nvarchar(255)) AS StudentColumn026,
    CAST(S.[{ColOrDefault(m?.StudColumn025, "_025")}] AS nvarchar(255)) AS StudentColumn025,
    CAST(S.[{ColOrDefault(m?.StudQualCode, "_001")}] AS nvarchar(255)) AS QualificationCode001,
    CAST(S.[{ColOrDefault(m?.StudName, "_019")}] AS nvarchar(255)) AS Name019,
    CAST(S.[{ColOrDefault(m?.StudIdNo, "_024")}] AS nvarchar(255)) AS IdNumber024,
    CAST(S.[{ColOrDefault(m?.StudFoundationFlag, "_106")}] AS nvarchar(255)) AS FoundationFlag106,
    CAST(Q.[{ColOrDefault(m?.QualDescription, "_003")}] AS nvarchar(255)) AS QualificationDescription003,
    CAST(Q.[{ColOrDefault(m?.QualType, "_005")}] AS nvarchar(255)) AS QualificationType005,
    CAST(BRIDGE.[{ColOrDefault(m?.CregQualCode, "_001")}] AS nvarchar(255)) AS BridgeQualificationCode001,
    CAST(BRIDGE.[{ColOrDefault(m?.CregCourseCode, "_030")}] AS nvarchar(255)) AS CourseCode030,
    CAST(CRSE.[{ColOrDefault(m?.CrseCourseName, "_058")}] AS nvarchar(255)) AS CourseName058,
    CAST(CRSE.[{ColOrDefault(m?.CrseCourseCode, "_030")}] AS nvarchar(255)) AS CrseCourseCode030,
    CAST(CRSE.[{ColOrDefault(m?.CrseFoundationFlag, "_091")}] AS nvarchar(255)) AS FoundationCourse091,
    CASE WHEN {IsPostgraduateQualificationCondition("Q", pgTypeSqlList, m)} THEN 'Postgraduate' ELSE 'Undergraduate' END AS StudentType,
    CASE WHEN {IsFoundationValidationPassCondition("CRSE", m)} THEN 'VALID' ELSE 'INVALID' END AS NotebookStatus,
    CASE WHEN {IsFoundationValidationPassCondition("CRSE", m)} THEN 'PASS' ELSE 'FAIL' END AS ValidationResult
{BuildBaseJoinClause(studTable, qualTable, cregTable, crseTable, m)}
WHERE {FoundationStudentCondition("S", m)}
  AND {IsFoundationCourseCondition("CRSE", m)}";

        private static string BuildFoundationStudentCountQuery(string studTable, string qualTable, string bridgeTable, string crseTable, Rule20ColumnMapping? m = null) =>
            $@"
SELECT COUNT(*) AS Foundation_Students
FROM [{studTable}] S
WHERE {FoundationStudentCondition("S", m)};";

        private static string BuildBaseJoinClause(string studTable, string qualTable, string bridgeTable, string crseTable, Rule20ColumnMapping? m = null) =>
            $@"
FROM [{studTable}] S
LEFT JOIN [{qualTable}] Q ON S.[{ColOrDefault(m?.StudQualCode, "_001")}] = Q.[{ColOrDefault(m?.QualQualCode, "_001")}]
LEFT JOIN [{bridgeTable}] BRIDGE ON S.[{ColOrDefault(m?.StudQualCode, "_001")}] = BRIDGE.[{ColOrDefault(m?.CregQualCode, "_001")}]
LEFT JOIN [{crseTable}] CRSE ON BRIDGE.[{ColOrDefault(m?.CregCourseCode, "_030")}] = CRSE.[{ColOrDefault(m?.CrseCourseCode, "_030")}]";

        private static List<string> BuildProcedureSteps(string studTable, string qualTable, string bridgeTable, string crseTable, Rule20ColumnMapping? m = null) =>
            new()
            {
                $"Step 1: start from {studTable} and keep only foundation students where {studTable}.{ColOrDefault(m?.StudFoundationFlag,"_106")} = '{ValOrDefault(m?.StudFoundationValue,"Y")}'.",
                $"Step 2: match the filtered {studTable} rows directly to {bridgeTable} (CRED) on {ColOrDefault(m?.StudQualCode,"_001")}/{ColOrDefault(m?.CregQualCode,"_001")} and carry {bridgeTable}.{ColOrDefault(m?.CregCourseCode,"_030")} forward.",
                $"Step 3: join {bridgeTable}.{ColOrDefault(m?.CregCourseCode,"_030")} to {crseTable}.{ColOrDefault(m?.CrseCourseCode,"_030")} and keep only rows where {crseTable}.{ColOrDefault(m?.CrseFoundationFlag,"_091")} = '{ValOrDefault(m?.CrseFoundationValue,"Y")}'.",
                $"Step 4: match {studTable} to {qualTable} on {ColOrDefault(m?.StudQualCode,"_001")}/{ColOrDefault(m?.QualQualCode,"_001")} only to enrich the rows for qualification description.",
                "Step 5: treat rows that remain in the filtered STUD -> CRED -> CRSE linkage as PASS."
            };

        private static string BuildTableLinkageText(string studTable, string qualTable, string bridgeTable, string crseTable) =>
            $"{studTable}._001 -> {bridgeTable} (CRED)._001 -> {crseTable}._030";

        private static string BuildValidationExplanation(string foundationFlag106, string courseCode030, string foundationCourse091)
        {
            var failedChecks = new List<string>();
            foundationFlag106 = NormalizeRowText(foundationFlag106);
            courseCode030 = NormalizeRowText(courseCode030);
            foundationCourse091 = NormalizeRowText(foundationCourse091);

            if (!string.Equals(foundationFlag106, "Y", StringComparison.OrdinalIgnoreCase))
                failedChecks.Add("STUD._106 is not 'Y'");
            if (string.IsNullOrWhiteSpace(courseCode030))
                failedChecks.Add("no CRED bridge course code matched STUD._001");
            else if (string.IsNullOrWhiteSpace(foundationCourse091))
                failedChecks.Add($"no CRSE row matched bridge course code '{courseCode030}'");
            if (!string.Equals(foundationCourse091, "Y", StringComparison.OrdinalIgnoreCase))
                failedChecks.Add("CRSE._091 is not 'Y'");

            return failedChecks.Count == 0
                ? "PASS: the student is in the Rule 20 filtered population through the STUD -> CRED -> CRSE linkage (STUD._106 = 'Y' and CRSE._091 = 'Y')."
                : $"FAIL: {string.Join("; ", failedChecks)}.";
        }

        private static string FoundationStudentCondition(string alias, Rule20ColumnMapping? m = null)
        {
            var col = ColOrDefault(m?.StudFoundationFlag, "_106");
            var val = ValOrDefault(m?.StudFoundationValue, "Y").Replace("'", "''");
            return $"CAST({alias}.[{col}] AS nvarchar(10)) = '{val}'";
        }

        private static string IsFoundationCourseCondition(string alias, Rule20ColumnMapping? m = null)
        {
            var col = ColOrDefault(m?.CrseFoundationFlag, "_091");
            var val = ValOrDefault(m?.CrseFoundationValue, "Y").Replace("'", "''");
            return $"CAST({alias}.[{col}] AS nvarchar(10)) = '{val}'";
        }

        private static string IsFoundationValidationPassCondition(string alias, Rule20ColumnMapping? m = null) =>
            IsFoundationCourseCondition(alias, m);

        private static string QualificationTypeValue(string alias, Rule20ColumnMapping? m = null) =>
            $"LTRIM(RTRIM(ISNULL(CAST({alias}.[{ColOrDefault(m?.QualType, "_005")}] AS nvarchar(50)), '')))";

        private static string IsPostgraduateQualificationCondition(string alias, string pgTypeSqlList, Rule20ColumnMapping? m = null) =>
            string.IsNullOrWhiteSpace(pgTypeSqlList)
                ? "1 = 0"
                : $"{QualificationTypeValue(alias, m)} IN ({pgTypeSqlList})";

        private static string BuildSqlStringList(List<string> values) =>
            values.Count == 0
                ? ""
                : string.Join(", ", values.Select(value => $"'{value.Replace("'", "''")}'"));

        private static List<string> ParsePgTypes(string? pgTypesText)
        {
            var values = (pgTypesText ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count == 0)
                values = DefaultPgTypes.ToList();

            foreach (var value in values)
            {
                if (value.Length > 10 || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')))
                    throw new InvalidOperationException($"Invalid postgraduate type value '{value}'.");
            }

            return values;
        }

        private static List<string> NormalizeGoverningPartCodes(IEnumerable<string>? governingPartCodes)
        {
            _ = (governingPartCodes ?? Enumerable.Empty<string>())
                .Select(code => (code ?? "").Trim().ToUpperInvariant())
                .Where(code => code == ScopeCode || code == "A" || code == "B" || code == "C")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return [ScopeCode];
        }

        private static string FormatGoverningPartCodes(IEnumerable<string>? governingPartCodes)
        {
            var normalized = NormalizeGoverningPartCodes(governingPartCodes);
            return string.Join(", ", normalized.Select(GetPartTitle));
        }

        private static string BuildOverallStatusRuleText(IEnumerable<string>? governingPartCodes)
            => "Overall PASS requires the filtered STUD -> CRED -> CRSE population to PASS.";

        private static string? FindFirst(IEnumerable<string> columns, IEnumerable<string> exactMatches, IEnumerable<string> partialMatches)
        {
            var list = columns.ToList();

            foreach (var exact in exactMatches)
            {
                var match = list.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var partial in partialMatches)
            {
                var match = list.FirstOrDefault(c => c.Contains(partial, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return list.FirstOrDefault();
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

        private static void ValidateColumnName(string colName)
        {
            colName = (colName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(colName))
                throw new InvalidOperationException("Column name cannot be blank.");
            if (colName.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
                throw new InvalidOperationException($"Invalid column name '{colName}'.");
        }

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandTimeout = SqlCommandTimeoutSeconds;
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule20ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                var summary = JsonConvert.DeserializeObject<Rule20ValidationSummary>(
                    decoded,
                    new JsonSerializerSettings
                    {
                        ObjectCreationHandling = ObjectCreationHandling.Replace
                    });
                return summary == null ? null : NormalizeSummary(summary);
            }
            catch
            {
                return null;
            }
        }

        private static Rule20ValidationSummary NormalizeSummary(Rule20ValidationSummary summary)
        {
            summary.PgTypes ??= new List<string>();
            if (summary.PgTypes.Count == 0)
                summary.PgTypes = ParsePgTypes(summary.PgTypesText);

            summary.PgTypesText = string.IsNullOrWhiteSpace(summary.PgTypesText)
                ? string.Join(", ", summary.PgTypes)
                : summary.PgTypesText;
            summary.GoverningPartCodes = NormalizeGoverningPartCodes(summary.GoverningPartCodes);
            summary.GoverningPartCodesText = FormatGoverningPartCodes(summary.GoverningPartCodes);
            summary.OverallStatusRuleText = BuildOverallStatusRuleText(summary.GoverningPartCodes);
            summary.TableLinkageText = BuildTableLinkageText(summary.StudTable, summary.QualTable, summary.CregTable, summary.CrseTable);
            summary.ProcedureSteps = BuildProcedureSteps(summary.StudTable, summary.QualTable, summary.CregTable, summary.CrseTable);
            summary.ReviewRows = NormalizeReviewRows(summary.ReviewRows);

            var partSummaries = summary.PartSummaries ?? new List<Rule20PartSummaryItemViewModel>();
            var aggregateTotalCount = partSummaries.Any()
                ? partSummaries.Sum(x => x.TotalCount)
                : summary.ReviewRows.Count;
            var aggregatePassCount = partSummaries.Any()
                ? partSummaries.Sum(x => x.PassCount)
                : summary.ReviewRows.Count(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase));
            var aggregateFailCount = partSummaries.Any()
                ? partSummaries.Sum(x => x.FailCount)
                : summary.ReviewRows.Count(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase));

            summary.PartSummaries =
            [
                new Rule20PartSummaryItemViewModel
                {
                    PartCode = ScopeCode,
                    PartTitle = ScopeTitle,
                    PartDescription = ScopeDescription,
                    TotalCount = aggregateTotalCount,
                    PassCount = aggregatePassCount,
                    FailCount = aggregateFailCount,
                    Status = aggregateFailCount == 0 ? "PASS" : "FAIL"
                }
            ];

            summary.TotalValidated = summary.PartSummaries.Sum(x => x.TotalCount);
            summary.PassCount = summary.PartSummaries.Sum(x => x.PassCount);
            summary.FailCount = summary.PartSummaries.Sum(x => x.FailCount);
            summary.ExceptionRate = summary.TotalValidated == 0 ? 0m : Math.Round(summary.FailCount * 100m / summary.TotalValidated, 2);
            summary.Status = DetermineOverallStatus(summary.PartSummaries, summary.GoverningPartCodes);
            summary.DisplayedCount = summary.ReviewRows.Count;
            summary.IsPreviewOnly = summary.TotalValidated > summary.DisplayedCount;
            summary.PreviewLimit = summary.IsPreviewOnly ? summary.DisplayedCount : 0;
            summary.ColumnMapping ??= new Rule20ColumnMapping();
            summary.TableLinkageText = BuildTableLinkageText(summary.StudTable, summary.QualTable, summary.CregTable, summary.CrseTable);
            summary.ProcedureSteps = BuildProcedureSteps(summary.StudTable, summary.QualTable, summary.CregTable, summary.CrseTable, summary.ColumnMapping);

            return summary;
        }

        private async Task<Rule20ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule20ValidationSummary summary, string? server)
        {
            var maxPreviewRows = BrowserPreviewRowLimit;
            var looksLikeStoredPreviewSample =
                summary.ReviewRows.Count > 0 &&
                summary.ReviewRows.Count <= maxPreviewRows &&
                summary.TotalValidated > 0;

            if (!summary.IsPreviewOnly &&
                summary.ReviewRows.Count >= summary.TotalValidated &&
                !looksLikeStoredPreviewSample)
                return summary;

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.StudTable) ||
                string.IsNullOrWhiteSpace(summary.QualTable) ||
                string.IsNullOrWhiteSpace(summary.CregTable) ||
                string.IsNullOrWhiteSpace(summary.CrseTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule20ValidationRequest
                    {
                        ClientId = summary.ClientId,
                        RunId = summary.SavedRunId,
                        Server = server,
                        Database = summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        StudTable = summary.StudTable,
                        QualTable = summary.QualTable,
                        CregTable = summary.CregTable,
                        CrseTable = summary.CrseTable,
                        PgTypesText = summary.PgTypesText,
                        GoverningPartCodes = summary.GoverningPartCodes?.ToList() ?? new List<string>(),
                        ColumnMapping = summary.ColumnMapping ?? new Rule20ColumnMapping()
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 20 results were expanded from the stored sample to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 20 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private static Rule20ValidationSummary CreateBrowserPreview(Rule20ValidationSummary summary)
        {
            var normalizedRows = NormalizeReviewRows(summary.ReviewRows);
            var previewRows = BuildBrowserPreviewRows(normalizedRows);

            return new Rule20ValidationSummary
            {
                Success = summary.Success,
                FoundationStudentCount = summary.FoundationStudentCount,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = previewRows.Count,
                IsPreviewOnly = summary.TotalValidated > previewRows.Count,
                PreviewLimit = BrowserPreviewRowLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                QualTable = summary.QualTable,
                CregTable = summary.CregTable,
                CrseTable = summary.CrseTable,
                PgTypesText = summary.PgTypesText,
                PgTypes = summary.PgTypes.ToList(),
                GoverningPartCodes = summary.GoverningPartCodes.ToList(),
                GoverningPartCodesText = summary.GoverningPartCodesText,
                OverallStatusRuleText = summary.OverallStatusRuleText,
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ColumnMapping = summary.ColumnMapping ?? new Rule20ColumnMapping(),
                TableLinkageText = summary.TableLinkageText,
                ProcedureSteps = summary.ProcedureSteps?.ToList() ?? new List<string>(),
                PartSummaries = (summary.PartSummaries ?? new List<Rule20PartSummaryItemViewModel>())
                    .Select(item => new Rule20PartSummaryItemViewModel
                    {
                        PartCode = item.PartCode,
                        PartTitle = item.PartTitle,
                        PartDescription = item.PartDescription,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = previewRows,
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static List<Rule20ReviewRowViewModel> BuildBrowserPreviewRows(IEnumerable<Rule20ReviewRowViewModel>? rows)
        {
            var normalizedRows = NormalizeReviewRows(rows);
            return NormalizeReviewRows(
                normalizedRows
                    .OrderBy(row => GetPartSortOrder(row.PartCode))
                    .ThenBy(row => TryParseLong(row.StudentNumber007).HasValue ? 0 : 1)
                    .ThenBy(row => TryParseLong(row.StudentNumber007))
                    .ThenBy(row => row.StudentNumber007)
                    .ThenBy(row => row.QualificationCode001)
                    .ThenBy(row => row.CourseCode030)
                    .Take(BrowserPreviewRowLimit));
        }

        private static void ApplyBrowserPreview(Rule20ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static string DetermineOverallStatus(
            IEnumerable<Rule20PartSummaryItemViewModel>? partSummaries,
            IEnumerable<string>? governingPartCodes)
        {
            var summaries = (partSummaries ?? Enumerable.Empty<Rule20PartSummaryItemViewModel>()).ToList();
            var governingParts = NormalizeGoverningPartCodes(governingPartCodes);

            return governingParts.All(partCode =>
            {
                var summary = summaries.FirstOrDefault(x => string.Equals(x.PartCode, partCode, StringComparison.OrdinalIgnoreCase));
                return string.Equals(summary?.Status, "PASS", StringComparison.OrdinalIgnoreCase);
            })
                ? "PASS"
                : "FAIL";
        }

        private static string DetermineOverallStatus(IEnumerable<Rule20PartSummaryItemViewModel>? partSummaries)
            => DetermineOverallStatus(partSummaries, [ScopeCode]);

        private static List<Rule20ReviewRowViewModel> NormalizeReviewRows(IEnumerable<Rule20ReviewRowViewModel>? rows)
        {
            var normalizedRows = (rows ?? Enumerable.Empty<Rule20ReviewRowViewModel>())
                .ToList();

            for (var i = 0; i < normalizedRows.Count; i++)
            {
                normalizedRows[i].StudentNumber007 = NormalizeRowText(normalizedRows[i].StudentNumber007);
                normalizedRows[i].StudentColumn008 = NormalizeRowText(normalizedRows[i].StudentColumn008);
                normalizedRows[i].StudentColumn066 = NormalizeRowText(normalizedRows[i].StudentColumn066);
                normalizedRows[i].StudentColumn067 = NormalizeRowText(normalizedRows[i].StudentColumn067);
                normalizedRows[i].StudentColumn068 = NormalizeRowText(normalizedRows[i].StudentColumn068);
                normalizedRows[i].StudentColumn012 = NormalizeRowText(normalizedRows[i].StudentColumn012);
                normalizedRows[i].StudentColumn013 = NormalizeRowText(normalizedRows[i].StudentColumn013);
                normalizedRows[i].StudentColumn014 = NormalizeRowText(normalizedRows[i].StudentColumn014);
                normalizedRows[i].StudentColumn015 = NormalizeRowText(normalizedRows[i].StudentColumn015);
                normalizedRows[i].StudentColumn010 = NormalizeRowText(normalizedRows[i].StudentColumn010);
                normalizedRows[i].StudentColumn026 = NormalizeRowText(normalizedRows[i].StudentColumn026);
                normalizedRows[i].StudentColumn025 = NormalizeRowText(normalizedRows[i].StudentColumn025);
                normalizedRows[i].QualificationCode001 = NormalizeRowText(normalizedRows[i].QualificationCode001);
                normalizedRows[i].Name019 = NormalizeRowText(normalizedRows[i].Name019);
                normalizedRows[i].IdNumber024 = NormalizeRowText(normalizedRows[i].IdNumber024);
                normalizedRows[i].FoundationFlag106 = NormalizeRowText(normalizedRows[i].FoundationFlag106);
                normalizedRows[i].QualificationDescription003 = NormalizeRowText(normalizedRows[i].QualificationDescription003);
                normalizedRows[i].QualificationType005 = NormalizeRowText(normalizedRows[i].QualificationType005);
                normalizedRows[i].BridgeQualificationCode001 = NormalizeRowText(normalizedRows[i].BridgeQualificationCode001);
                normalizedRows[i].CourseCode030 = NormalizeRowText(normalizedRows[i].CourseCode030);
                normalizedRows[i].CourseName058 = NormalizeRowText(normalizedRows[i].CourseName058);
                normalizedRows[i].CrseCourseCode030 = NormalizeRowText(normalizedRows[i].CrseCourseCode030);
                normalizedRows[i].FoundationCourse091 = NormalizeRowText(normalizedRows[i].FoundationCourse091);
                normalizedRows[i].StudentType = NormalizeRowText(normalizedRows[i].StudentType);
                normalizedRows[i].NotebookStatus = NormalizeRowText(normalizedRows[i].NotebookStatus);
                normalizedRows[i].ValidationResult = NormalizeRowText(normalizedRows[i].ValidationResult);
                normalizedRows[i].ValidationExplanation = NormalizeRowText(normalizedRows[i].ValidationExplanation);
                normalizedRows[i].PartCode = ScopeCode;
                normalizedRows[i].PartTitle = ScopeTitle;
                normalizedRows[i].PartDescription = ScopeDescription;
            }

            normalizedRows = normalizedRows
                .OrderBy(r => TryParseLong(r.StudentNumber007).HasValue ? 0 : 1)
                .ThenBy(r => TryParseLong(r.StudentNumber007))
                .ThenBy(r => r.StudentNumber007)
                .ThenBy(r => r.QualificationCode001)
                .ThenBy(r => r.CourseCode030)
                .ToList();

            for (var i = 0; i < normalizedRows.Count; i++)
            {
                normalizedRows[i].ValidationNumber = i + 1;
                var isPass = IsRule20RowPass(normalizedRows[i]) ||
                    string.Equals(normalizedRows[i].ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedRows[i].NotebookStatus, "VALID", StringComparison.OrdinalIgnoreCase);
                normalizedRows[i].NotebookStatus = isPass ? "VALID" : "INVALID";
                normalizedRows[i].ValidationResult = isPass ? "PASS" : "FAIL";
                normalizedRows[i].ValidationExplanation = string.IsNullOrWhiteSpace(normalizedRows[i].ValidationExplanation)
                    ? BuildValidationExplanation(
                        normalizedRows[i].FoundationFlag106,
                        normalizedRows[i].CourseCode030,
                        normalizedRows[i].FoundationCourse091)
                    : normalizedRows[i].ValidationExplanation;
            }

            return normalizedRows;
        }

        private static string GetPartTitle(string partCode) => partCode.ToUpperInvariant() switch
        {
            ScopeCode => ScopeTitle,
            "A" => "Part A",
            "B" => "Part B",
            "C" => "Part C",
            _ => partCode
        };

        private static string GetPartDescription(string partCode) => partCode.ToUpperInvariant() switch
        {
            ScopeCode => ScopeDescription,
            _ => ""
        };

        private static int GetPartSortOrder(string? partCode) =>
            Array.IndexOf(PartOrder, (partCode ?? "").Trim().ToUpperInvariant()) switch
            {
                >= 0 and var idx => idx,
                _ => PartOrder.Length
            };

        private static long? TryParseLong(string? value) =>
            long.TryParse(value, out var parsed) ? parsed : null;

        private static string NormalizeRowText(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

        private static bool IsRule20RowPass(Rule20ReviewRowViewModel row) =>
            string.Equals(NormalizeRowText(row.FoundationFlag106), "Y", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(NormalizeRowText(row.CourseCode030)) &&
            !string.IsNullOrWhiteSpace(NormalizeRowText(row.CrseCourseCode030)) &&
            string.Equals(NormalizeRowText(row.FoundationCourse091), "Y", StringComparison.OrdinalIgnoreCase);

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


