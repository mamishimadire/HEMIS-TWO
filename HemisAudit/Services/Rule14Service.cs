using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule14Service : IRule14Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule14Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule14TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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

                return new Rule14TableDiscoveryResult
                {
                Success = true,
                Tables = tables,
                AutoStudTable = FindFirst(tables, ["dbo_CRSE", "CRSE"], ["crse"]),
                AutoBridgeTable = FindFirst(tables, ["dbo_CREG", "CREG"], ["creg"]),
                AutoCrseTable = FindFirst(tables, ["dbo_CREG", "CREG"], ["creg"])
                };
            }
            catch (Exception ex)
            {
                return new Rule14TableDiscoveryResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule14VerifyResult> VerifyTablesAsync(Rule14VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable, request.CrseApprovedCol ?? "_031", request.CrseLinkCol ?? "_030", request.CregLinkCol ?? "_030");

                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var studTable = Sanitise(request.StudTable);
                var bridgeTable = Sanitise(request.BridgeTable);

                var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");
                var bridgeCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{bridgeTable}];");

                await using var command = conn.CreateConfiguredCommand();
                command.CommandText = BuildVerifyCountSql(studTable, bridgeTable);
                await using var reader = await command.ExecuteReaderAsync();

                var result = new Rule14VerifyResult
                {
                    Success = true,
                    StudRecordCount = studCount,
                    BridgeRecordCount = bridgeCount,
                    CrseRecordCount = bridgeCount
                };

                if (await reader.ReadAsync())
                {
                    result.ApprovedQualificationCount = GetInt(reader, 0);
                    result.ApprovedCredentialCount = GetInt(reader, 1);
                    result.RegisteredCredentialCount = GetInt(reader, 2);
                    result.Control1PopulationCount = result.ApprovedCredentialCount;
                    result.Control2PopulationCount = 0;
                    result.Control3PopulationCount = 0;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Rule14VerifyResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule14ValidationSummary> RunValidationAsync(Rule14ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable, request.CrseApprovedCol ?? "_031", request.CrseLinkCol ?? "_030", request.CregLinkCol ?? "_030");

                // Run once with all rows — used for both save and browser preview
                var browserSummary = await AnalyseAsync(request, includeAllReviewRows: true);
                if (browserSummary.Success && request.ClientId > 0)
                {
                    try
                    {
                        var summaryToPersist = CloneSummary(browserSummary);
                        summaryToPersist.SavedRunId = null;
                        browserSummary.SavedRunId = await SaveValidationRunAsync(
                            CloneValidationRequest(request),
                            summaryToPersist,
                            userEmail,
                            userName,
                            markWorkspaceSaved: false);

                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(14, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(14, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);

                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full approved-course result set. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Rule 14 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(browserSummary);
                return browserSummary;
            }
            catch (Exception ex)
            {
                return new Rule14ValidationSummary
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule14ValidationSummary> GetExportSummaryAsync(Rule14ValidationRequest request)
        {
            ValidateRequest(request);
            await EnsureColumnsExistAsync(request.Server, request.Database, request.Driver, request.StudTable, request.BridgeTable, request.CrseTable);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule14ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule14ValidationRequest, Rule14ValidationSummary>(14, clientId, reviewerEmail);
            if (pending == null)
                return Task.FromResult<Rule14ValidationSummary?>(null);

            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 14 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule14ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(14, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule14WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
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
    ISNULL(vr.DeceasedTable, '') AS BridgeTable,
    ISNULL(vr.StudColumn, '') AS CrseTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName,
    vr.LastEditedAt,
    vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID
  AND vr.RuleNumber = 14
  AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var runId = reader.GetInt32(0);
            var workspaceClientId = reader.GetInt32(1);
            var server = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var studTable = reader.IsDBNull(4) ? "dbo_CRSE" : reader.GetString(4);
            var bridgeTable = reader.IsDBNull(5) ? "dbo_CREG" : reader.GetString(5);
            var crseTable = reader.IsDBNull(6) ? "dbo_CREG" : reader.GetString(6);
            var currentStatus = reader.IsDBNull(7) ? "" : reader.GetString(7);
            var lastEditedByUserName = reader.IsDBNull(8) ? null : reader.GetString(8);
            DateTime? lastEditedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9);
            var encodedSummary = reader.IsDBNull(10) ? null : reader.GetString(10);

            await reader.CloseAsync();

            var deserializedSummary = DeserializeSummary(encodedSummary);
            if (deserializedSummary != null && includeSummary)
            {
                deserializedSummary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, deserializedSummary, server);
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule14WorkspaceStateViewModel
            {
                ClientId = workspaceClientId,
                RunId = runId,
                Server = server,
                Database = database,
                StudTable = studTable,
                BridgeTable = bridgeTable,
                CrseTable = crseTable,
                CrseApprovedCol = deserializedSummary?.CrseApprovedCol ?? "_031",
                CrseApprovedVal = deserializedSummary?.CrseApprovedVal ?? "A",
                CrseLinkCol = deserializedSummary?.CrseLinkCol ?? "_030",
                CregLinkCol = deserializedSummary?.CregLinkCol ?? "_030",
                CurrentStatus = currentStatus,
                LastEditedByUserName = lastEditedByUserName,
                LastEditedAt = lastEditedAt,
                Summary = summary
            };

            if (summary != null)
                workspace.CurrentStatus = summary.Status;

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

        public async Task<Rule14RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID
  AND vr.RuleNumber = 14;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var savedRunId = reader.GetInt32(0);
            var clientId = reader.GetInt32(1);
            var isCurrentRun = !reader.IsDBNull(2) && reader.GetBoolean(2);
            var engagementName = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var maconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var sourceServer = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var encodedSummary = reader.IsDBNull(6) ? null : reader.GetString(6);
            var summary = DeserializeSummary(encodedSummary);
            if (summary == null)
                return null;

            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0)
                summary.SavedRunId = runId;

            await reader.CloseAsync();

            summary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, summary, sourceServer);

            if (includeFullResults)
            {
                summary.DisplayedCount = summary.ReviewRows.Count;
                summary.IsPreviewOnly = false;
                summary.PreviewLimit = 0;
            }
            else
            {
                ApplyBrowserPreview(summary);
            }

            var review = new Rule14RunReviewViewModel
            {
                RunId = savedRunId,
                ClientId = clientId,
                IsCurrentRun = isCurrentRun,
                EngagementName = engagementName,
                MaconomyNumber = maconomyNumber,
                SourceServer = sourceServer,
                Summary = summary
            };

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? ""
                : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return review;
        }

        public async Task<Rule14WorkspaceSaveResult> SaveWorkspaceAsync(Rule14ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId.HasValue && request.RunId.Value > 0)
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                    if (!clientId.HasValue || clientId.Value != request.ClientId)
                    {
                        return new Rule14WorkspaceSaveResult
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
SET LastEditedByUserName = @LastEditedByUserName,
    LastEditedAt = GETDATE(),
    WorkspaceSavedAt = GETDATE(),
    PreviousHash = @PreviousHash,
    RecordHash = @RecordHash,
    Status = 'Needs Review'
WHERE RunID = @RunID
  AND ClientID = @ClientID;";
                    command.Parameters.AddWithValue("@RunID", request.RunId.Value);
                    command.Parameters.AddWithValue("@ClientID", request.ClientId);
                    command.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                    command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    command.Parameters.AddWithValue("@RecordHash", ComputeHash($@"WorkspaceSave|Rule14|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await command.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(14, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule14WorkspaceSaveResult
                    {
                        Success = true,
                        Message = clearedSignoffs > 0
                            ? "Workspace saved. Existing signoffs were removed and the run must be reviewed again."
                            : "Workspace saved and marked for review again.",
                        SignoffsCleared = clearedSignoffs > 0,
                        ClearedSignoffCount = clearedSignoffs,
                        Workspace = currentWorkspace
                    };
                }

                var pending = _pendingValidationCache.GetPending<Rule14ValidationRequest, Rule14ValidationSummary>(14, request.ClientId, reviewerEmail);
                if (pending == null)
                {
                    return new Rule14WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Run Rule 14 first so the current workspace is written to the system database."
                    };
                }

                if (!RequestsMatchForPendingSave(request, pending.Request))
                {
                    return new Rule14WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Workspace settings changed after validation. Run Rule 14 again before saving."
                    };
                }

                var summaryToSave = CloneSummary(pending.Summary);
                if (summaryToSave.IsPreviewOnly || summaryToSave.ReviewRows.Count < summaryToSave.TotalValidated)
                {
                    summaryToSave = await AnalyseAsync(pending.Request, includeAllReviewRows: true);
                }

                summaryToSave.SavedRunId = null;
                var requestToSave = CloneValidationRequest(pending.Request);

                var savedRunId = await SaveValidationRunAsync(requestToSave, summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(14, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule14WorkspaceSaveResult
                {
                    Success = true,
                    Message = $"Workspace saved as Run #{savedRunId}. Sign off this saved workspace when you are ready.",
                    SignoffsCleared = false,
                    ClearedSignoffCount = 0,
                    Workspace = workspace
                };
            }
            catch (Exception ex)
            {
                return new Rule14WorkspaceSaveResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        public async Task<Rule14WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue)
                {
                    return new Rule14WorkspaceSaveResult
                    {
                        Success = false,
                        Error = "Saved workspace was not found."
                    };
                }

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
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
    Status = 'Needs Review'
WHERE RunID = @RunID;";
                markEdit.Parameters.AddWithValue("@RunID", runId);
                markEdit.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                markEdit.Parameters.AddWithValue("@RecordHash", ComputeHash($@"BeginWorkspaceEdit|Rule14|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await markEdit.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(14, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule14WorkspaceSaveResult
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
                return new Rule14WorkspaceSaveResult
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
                throw new InvalidOperationException("The selected Rule 14 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off a Rule 14 run.");

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
                throw new InvalidOperationException("The selected Rule 14 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);

            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff from this run.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
            if (removal.RemovedCount <= 0)
                return;
        }

        public Task<string> GenerateSqlAsync(Rule14ValidationRequest request)
        {
            ValidateRequest(request);

            var crseTable = Sanitise(request.StudTable);
            var cregTable = Sanitise(request.BridgeTable);
            var aCol = Sanitise(request.CrseApprovedCol ?? "_031");
            var aVal = request.CrseApprovedVal ?? "A";
            var lCol = Sanitise(request.CrseLinkCol ?? "_030");
            var rCol = Sanitise(request.CregLinkCol ?? "_030");

            var sql = $@"-- ============================================================================
-- HEMIS RULE 14: COURSE REGISTRATION VALIDATION - 100% POPULATION
-- ============================================================================
-- Rule:
--   Select 100% of approved courses where [{crseTable}].[{aCol}] = '{aVal}'
--   Match CRSE.[{lCol}] to CREG.[{rCol}]
--   PASS = approved course exists in [{cregTable}]
--   FAIL = approved course does not exist in [{cregTable}]
-- ============================================================================

IF OBJECT_ID('tempdb..#ApprovedCourses') IS NOT NULL DROP TABLE #ApprovedCourses;
IF OBJECT_ID('tempdb..#Rule14_Result')   IS NOT NULL DROP TABLE #Rule14_Result;

-- Step 1: Extract 100% approved course population
SELECT DISTINCT
    CAST(CRSE.[{lCol}]  AS NVARCHAR(255)) AS [Course_Code],
    CAST(CRSE.[_065]    AS NVARCHAR(255)) AS [Filler1],
    CAST(CRSE.[{aCol}]  AS NVARCHAR(255)) AS [Course_Approval_Status],
    CAST(CRSE.[_033]    AS NVARCHAR(255)) AS [Course_CESM],
    CAST(CRSE.[_034]    AS NVARCHAR(255)) AS [Course_Level_Code],
    CAST(CRSE.[_059]    AS NVARCHAR(255)) AS [Contact_Only_Availability],
    CAST(CRSE.[_060]    AS NVARCHAR(255)) AS [Distance_Only_Availability],
    CAST(CRSE.[_061]    AS NVARCHAR(255)) AS [Mixed_Mode_Availability],
    CAST(CRSE.[_062]    AS NVARCHAR(255)) AS [Experiential_Training_Indicator],
    CAST(CRSE.[_058]    AS NVARCHAR(500)) AS [Course_Name],
    CAST(CRSE.[_091]    AS NVARCHAR(255)) AS [Foundation_Course],
    CAST(CRSE.[_092]    AS NVARCHAR(255)) AS [NQF_Level],
    CAST(CRSE.[_093]    AS NVARCHAR(255)) AS [NQF_Credit]
INTO #ApprovedCourses
FROM [{crseTable}] CRSE
WHERE UPPER(LTRIM(RTRIM(CAST(CRSE.[{aCol}] AS NVARCHAR(50))))) = '{aVal}'
  AND CRSE.[{lCol}] IS NOT NULL;

-- Step 2: Cross-match approved courses to course registration
SELECT
    ROW_NUMBER() OVER (ORDER BY A.[Course_Code]) AS [Sample_Number],
    'Control_1'                                   AS [Control_Type],
    'CONTROL 1: CRSE.[{aCol}] = {aVal} and CREG.[{rCol}] = CRSE.[{lCol}]' AS [Control_Label],
    A.[Course_Code],
    A.[Course_Name],
    A.[Course_Approval_Status],
    A.[Course_CESM],
    A.[Course_Level_Code],
    A.[Contact_Only_Availability],
    A.[Distance_Only_Availability],
    A.[Mixed_Mode_Availability],
    A.[Experiential_Training_Indicator],
    A.[Foundation_Course],
    A.[NQF_Level],
    A.[NQF_Credit],
    CREG.[{rCol}] AS [CREG_Course_Code],
    CASE
        WHEN CREG.[{rCol}] IS NOT NULL THEN 'PASS'
        ELSE 'FAIL'
    END AS [Validation_Result],
    CASE
        WHEN CREG.[{rCol}] IS NOT NULL
            THEN 'Approved course found in [{cregTable}].'
        ELSE 'Approved course not found in [{cregTable}].'
    END AS [Validation_Reason]
INTO #Rule14_Result
FROM #ApprovedCourses A
LEFT JOIN [{cregTable}] CREG
    ON UPPER(LTRIM(RTRIM(CAST(A.[Course_Code] AS NVARCHAR(255)))))
     = UPPER(LTRIM(RTRIM(CAST(CREG.[{rCol}] AS NVARCHAR(255)))));

-- Step 3: Full extracted population result
SELECT * FROM #Rule14_Result ORDER BY [Course_Code];

-- Step 4: Summary
SELECT
    COUNT(*)                                                                                  AS [Total_Approved_Courses],
    SUM(CASE WHEN [Validation_Result] = 'PASS' THEN 1 ELSE 0 END)                           AS [PASS_Count],
    SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END)                           AS [FAIL_Count],
    CAST(SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END) * 100.0
         / NULLIF(COUNT(*), 0) AS DECIMAL(5,2))                                               AS [Exception_Rate_Pct]
FROM #Rule14_Result;

DROP TABLE #ApprovedCourses;
DROP TABLE #Rule14_Result;";

            return Task.FromResult(sql.Trim());
        }

        private async Task<Rule14ValidationSummary> AnalyseAsync(Rule14ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var crseTable = Sanitise(request.StudTable);
            var cregTable = Sanitise(request.BridgeTable);
            var crseApprovedCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseApprovedCol) ? "_031" : request.CrseApprovedCol);
            var crseApprovedVal = request.CrseApprovedVal ?? "A";
            var crseLinkCol = Sanitise(string.IsNullOrWhiteSpace(request.CrseLinkCol) ? "_030" : request.CrseLinkCol);
            var cregLinkCol = Sanitise(string.IsNullOrWhiteSpace(request.CregLinkCol) ? "_030" : request.CregLinkCol);

            var crseCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{crseTable}];");
            var cregCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{cregTable}];");

            // Build temp tables (#Rule14_ApprovedCourses, #Rule14_Result) on this connection
            await using (var prepCommand = conn.CreateConfiguredCommand())
            {
                prepCommand.CommandText = BuildRule14PrepSql(crseTable, cregTable, crseApprovedCol, crseApprovedVal, crseLinkCol, cregLinkCol);
                await prepCommand.ExecuteNonQueryAsync();
            }

            await using var countCommand = conn.CreateConfiguredCommand();
            countCommand.CommandText = BuildPopulationCountSql();
            await using var countReader = await countCommand.ExecuteReaderAsync();

            var approvedCourseCount = 0;
            var registeredCourseCount = 0;
            var missingRegistrationCount = 0;
            if (await countReader.ReadAsync())
            {
                approvedCourseCount = GetInt(countReader, 0);
                registeredCourseCount = GetInt(countReader, 1);
                missingRegistrationCount = GetInt(countReader, 2);
            }

            await countReader.CloseAsync();

            var reviewRows = await LoadControlRowsAsync(conn, crseTable, cregTable, includeAllReviewRows ? null : BrowserPreviewRowLimit, crseApprovedCol, crseApprovedVal, crseLinkCol, cregLinkCol);
            reviewRows = NormalizeReviewRows(reviewRows);

            var controlSummaries = BuildControlSummaries(approvedCourseCount, registeredCourseCount, crseApprovedCol, crseApprovedVal, crseLinkCol, cregLinkCol);
            var totalValidated = controlSummaries.Sum(x => x.TotalCount);
            var passCount = controlSummaries.Sum(x => x.PassCount);
            var failCount = controlSummaries.Sum(x => x.FailCount);
            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;

            return new Rule14ValidationSummary
            {
                Success = true,
                StudRecordCount = crseCount,
                CrseRecordCount = cregCount,
                BridgeRecordCount = cregCount,
                ApprovedQualificationCount = approvedCourseCount,
                ApprovedCredentialCount = approvedCourseCount,
                RegisteredCredentialCount = registeredCourseCount,
                UnfulfilledPopulationCount = approvedCourseCount,
                TotalRequested = totalValidated,
                TotalValidated = totalValidated,
                DisplayedCount = reviewRows.Count,
                IsPreviewOnly = isPreviewOnly,
                PreviewLimit = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount = passCount,
                FailCount = failCount,
                ExceptionRate = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status = failCount == 0 ? "PASS" : "FAIL",
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database = request.Database,
                StudTable = request.StudTable,
                BridgeTable = request.BridgeTable,
                CrseTable = request.CrseTable,
                CrseApprovedCol = crseApprovedCol,
                CrseApprovedVal = crseApprovedVal,
                CrseLinkCol = crseLinkCol,
                CregLinkCol = cregLinkCol,
                TableLinkageText = $"{request.StudTable} -> {request.BridgeTable}",
                RuleModeText = "100% population testing of approved courses",
                ProcedureSteps = BuildProcedureSteps(request.StudTable, request.BridgeTable),
                ClientId = request.ClientId,
                ControlSummaries = controlSummaries,
                ReviewRows = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 14 completed with the full approved-course result set."
                    : "Counts reflect the full approved-course result set. Browser review rows are limited for performance."
            };
        }

        private async Task<int> SaveValidationRunAsync(Rule14ValidationRequest request, Rule14ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 14);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue)
                throw new InvalidOperationException("The current analyst could not be resolved in the system database.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 14);
            var failRows = summary.ReviewRows.Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
INSERT INTO dbo.ValidationRuns
(
    ClientID, UserID, RuleNumber, RuleName, Status, TotalRecords, PassCount, FailCount, ExceptionRate, RunTimestamp,
    HemisServer, AuditDatabase, StudTable, DeceasedTable, StudColumn, DeceasedColumn,
    ExceptionsJSON, ResultsJSON, RunByUserName, LastEditedByUserName, LastEditedAt, PreviousHash, RecordHash, WorkspaceSavedAt, IsCurrent
)
VALUES
(
    @ClientID, @UserID, 14, @RuleName, @Status, @TotalRecords, @PassCount, @FailCount, @ExceptionRate, GETDATE(),
    @HemisServer, @AuditDatabase, @StudTable, @BridgeTable, @CrseTable, NULL,
    @ExceptionsJSON, @ResultsJSON, @RunByUserName, NULL, NULL, @PreviousHash, NULL, @WorkspaceSavedAt, 1
);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            command.Parameters.AddWithValue("@ClientID", request.ClientId);
            command.Parameters.AddWithValue("@UserID", systemUserId.Value);
            command.Parameters.AddWithValue("@RuleName", "Course Registration Validation");
            command.Parameters.AddWithValue("@Status", summary.Status);
            command.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            command.Parameters.AddWithValue("@PassCount", summary.PassCount);
            command.Parameters.AddWithValue("@FailCount", summary.FailCount);
            command.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            command.Parameters.AddWithValue("@HemisServer", request.Server);
            command.Parameters.AddWithValue("@AuditDatabase", request.Database);
            command.Parameters.AddWithValue("@StudTable", request.StudTable);
            command.Parameters.AddWithValue("@BridgeTable", request.BridgeTable);
            command.Parameters.AddWithValue("@CrseTable", request.CrseTable);
            var persistedSummary = CloneSummary(summary);
            persistedSummary.SavedRunId = summary.SavedRunId;
            command.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            command.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(persistedSummary)));
            command.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@WorkspaceSavedAt", markWorkspaceSaved ? DateTime.UtcNow : (object)DBNull.Value);

            var runId = Convert.ToInt32(await command.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCommand = connection.CreateConfiguredCommand();
            hashCommand.CommandText = @"
UPDATE dbo.ValidationRuns
SET RecordHash = @RecordHash
WHERE RunID = @RunID;";
            hashCommand.Parameters.AddWithValue("@RunID", runId);
            hashCommand.Parameters.AddWithValue("@RecordHash", ComputeHash($@"ValidationRun|Rule14|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.ExceptionRate}|{summary.Timestamp}|{previousHash}"));
            await hashCommand.ExecuteNonQueryAsync();

            await UpdateStoredSummaryAsync(connection, runId, summary);

            return runId;
        }

        private static string BuildVerifyCountSql(string crseTable, string cregTable) => $@"
SELECT
    (SELECT COUNT(DISTINCT [{crseTable}].[_030]) FROM [{crseTable}]) AS ApprovedQualificationCount,
    (SELECT COUNT(1) FROM [{cregTable}])                             AS ApprovedCredentialCount,
    (SELECT COUNT(1) FROM [{cregTable}])                             AS RegisteredCredentialCount;";

        private static string BuildPopulationCountSql() => @"
SELECT
    COUNT(1)                                                              AS ApprovedCourseCount,
    SUM(CASE WHEN [Validation_Result] = 'PASS' THEN 1 ELSE 0 END)        AS RegisteredCourseCount,
    SUM(CASE WHEN [Validation_Result] = 'FAIL' THEN 1 ELSE 0 END)        AS MissingRegistrationCount
FROM #Rule14_Result;";

        private async Task<List<Rule14ValidationRowRecord>> LoadControlRowsAsync(
            SqlConnection connection, string crseTable, string cregTable, int? maxRows,
            string crseApprovedCol = "_031", string crseApprovedVal = "A",
            string crseLinkCol = "_030", string cregLinkCol = "_030")
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = BuildAllControlsSql(crseTable, cregTable, maxRows, crseApprovedCol, crseApprovedVal, crseLinkCol, cregLinkCol);

            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule14ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i)
                        ? null
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                }

                rows.Add(new Rule14ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Reason"),
                    DisplayValues = displayValues
                });

                EnrichRule14DisplayValues(rows[^1]);
            }

            return rows;
        }

        private static string BuildRule14PrepSql(
            string crseTable, string cregTable,
            string crseApprovedCol = "_031", string crseApprovedVal = "A",
            string crseLinkCol = "_030", string cregLinkCol = "_030") => $@"
IF OBJECT_ID('tempdb..#ApprovedCourses') IS NOT NULL DROP TABLE #ApprovedCourses;
IF OBJECT_ID('tempdb..#Rule14_Result')   IS NOT NULL DROP TABLE #Rule14_Result;

SELECT DISTINCT
    CAST(CRSE.[{crseLinkCol}]    AS NVARCHAR(255)) AS [Course_Code],
    CAST(CRSE.[_065]             AS NVARCHAR(255)) AS [Filler1],
    CAST(CRSE.[{crseApprovedCol}] AS NVARCHAR(255)) AS [Course_Approval_Status],
    CAST(CRSE.[_033]             AS NVARCHAR(255)) AS [Course_CESM],
    CAST(CRSE.[_034]             AS NVARCHAR(255)) AS [Course_Level_Code],
    CAST(CRSE.[_059]             AS NVARCHAR(255)) AS [Contact_Only_Availability],
    CAST(CRSE.[_060]             AS NVARCHAR(255)) AS [Distance_Only_Availability],
    CAST(CRSE.[_061]             AS NVARCHAR(255)) AS [Mixed_Mode_Availability],
    CAST(CRSE.[_062]             AS NVARCHAR(255)) AS [Experiential_Training_Indicator],
    CAST(CRSE.[_058]             AS NVARCHAR(500)) AS [Course_Name],
    CAST(CRSE.[_091]             AS NVARCHAR(255)) AS [Foundation_Course],
    CAST(CRSE.[_092]             AS NVARCHAR(255)) AS [NQF_Level],
    CAST(CRSE.[_093]             AS NVARCHAR(255)) AS [NQF_Credit]
INTO #ApprovedCourses
FROM [{crseTable}] CRSE
WHERE UPPER(LTRIM(RTRIM(CAST(CRSE.[{crseApprovedCol}] AS NVARCHAR(50))))) = '{crseApprovedVal}'
  AND CRSE.[{crseLinkCol}] IS NOT NULL;

SELECT
    ROW_NUMBER() OVER (ORDER BY A.[Course_Code]) AS [Sample_Number],
    'Control_1' AS [Control_Type],
    'CONTROL 1: CRSE.[{crseApprovedCol}] = {crseApprovedVal} and CREG.[{cregLinkCol}] = CRSE.[{crseLinkCol}]' AS [Control_Label],
    A.[Course_Code],
    A.[Course_Name],
    A.[Course_Approval_Status],
    A.[Course_CESM],
    A.[Course_Level_Code],
    A.[Contact_Only_Availability],
    A.[Distance_Only_Availability],
    A.[Mixed_Mode_Availability],
    A.[Experiential_Training_Indicator],
    A.[Foundation_Course],
    A.[NQF_Level],
    A.[NQF_Credit],
    CREG.[{cregLinkCol}] AS [CREG_Course_Code],
    CASE WHEN CREG.[{cregLinkCol}] IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS [Validation_Result],
    CASE
        WHEN CREG.[{cregLinkCol}] IS NOT NULL
            THEN 'Approved course found in [{cregTable}].'
        ELSE 'Approved course not found in [{cregTable}].'
    END AS [Validation_Reason]
INTO #Rule14_Result
FROM #ApprovedCourses A
LEFT JOIN [{cregTable}] CREG
    ON UPPER(LTRIM(RTRIM(CAST(A.[Course_Code] AS NVARCHAR(255)))))
     = UPPER(LTRIM(RTRIM(CAST(CREG.[{cregLinkCol}] AS NVARCHAR(255)))));";

        private static string BuildAllControlsSql(
            string crseTable, string cregTable, int? maxRows,
            string crseApprovedCol = "_031", string crseApprovedVal = "A",
            string crseLinkCol = "_030", string cregLinkCol = "_030")
        {
            var topClause = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;
            return $"SELECT {topClause} * FROM #Rule14_Result ORDER BY [Course_Code];";
        }

        private static List<Rule14ControlSummaryItemViewModel> BuildControlSummaries(
            int approvedCourseCount, int registeredCourseCount,
            string crseApprovedCol = "_031", string crseApprovedVal = "A",
            string crseLinkCol = "_030", string cregLinkCol = "_030")
        {
            return new List<Rule14ControlSummaryItemViewModel>
            {
                BuildControlSummary(
                    "Control_1",
                    "Control 1",
                    $"CRSE.{crseApprovedCol}='{crseApprovedVal}' AND CREG.{cregLinkCol}=CRSE.{crseLinkCol}",
                    approvedCourseCount,
                    registeredCourseCount)
            };
        }

        private static Rule14ControlSummaryItemViewModel BuildControlSummary(
            string controlType,
            string controlLabel,
            string criteriaText,
            int totalCount,
            int passCount)
        {
            var failCount = Math.Max(totalCount - passCount, 0);
            return new Rule14ControlSummaryItemViewModel
            {
                ControlType = controlType,
                ControlLabel = controlLabel,
                CriteriaText = criteriaText,
                RequestedCount = totalCount,
                AvailableCount = totalCount,
                AchievedCount = totalCount,
                TotalCount = totalCount,
                PassCount = passCount,
                FailCount = failCount,
                Status = failCount == 0 ? "PASS" : "FAIL"
            };
        }

        private static List<Rule14ValidationRowRecord> NormalizeReviewRows(IEnumerable<Rule14ValidationRowRecord>? rows)
        {
            var normalized = (rows ?? Enumerable.Empty<Rule14ValidationRowRecord>())
                .Select((row, index) =>
                {
                    row.ValidationNumber = index + 1;
                    return row;
                })
                .ToList();

            return normalized;
        }

        private async Task<Rule14ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule14ValidationSummary summary, string? server)
        {
            if (!summary.IsPreviewOnly &&
                summary.ReviewRows.Count >= summary.TotalValidated)
            {
                return summary;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(summary.Database) ||
                string.IsNullOrWhiteSpace(summary.StudTable) ||
                string.IsNullOrWhiteSpace(summary.BridgeTable) ||
                string.IsNullOrWhiteSpace(summary.CrseTable))
            {
                return summary;
            }

            try
            {
                var expanded = await AnalyseAsync(
                    new Rule14ValidationRequest
                    {
                        ClientId = summary.ClientId,
                        RunId = summary.SavedRunId,
                        Server = server,
                        Database = summary.Database,
                        Driver = "ODBC Driver 17 for SQL Server",
                        StudTable = summary.StudTable,
                        BridgeTable = summary.BridgeTable,
                        CrseTable = summary.CrseTable
                    },
                    includeAllReviewRows: true);

                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                expanded.Warning = string.IsNullOrWhiteSpace(summary.Warning)
                    ? "Saved Rule 14 results were expanded from the stored browser preview to the full result set."
                    : $"{summary.Warning} Full saved results were reloaded from the saved Rule 14 configuration.";

                return expanded;
            }
            catch
            {
                return summary;
            }
        }

        private async Task<Rule14ValidationSummary> ExpandAndPersistSavedSummaryIfNeededAsync(SqlConnection connection, int runId, Rule14ValidationSummary summary, string? server)
        {
            var expanded = await ExpandSavedSummaryIfNeededAsync(summary, server);
            if (!ReferenceEquals(expanded, summary))
            {
                expanded.SavedRunId = runId;
                await UpdateStoredSummaryAsync(connection, runId, expanded);
            }

            return expanded;
        }

        private static Rule14ValidationSummary CloneSummary(Rule14ValidationSummary summary)
        {
            return new Rule14ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                UnfulfilledPopulationCount = summary.UnfulfilledPopulationCount,
                ApprovedQualificationCount = summary.ApprovedQualificationCount,
                ApprovedCredentialCount = summary.ApprovedCredentialCount,
                RegisteredCredentialCount = summary.RegisteredCredentialCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = summary.DisplayedCount,
                IsPreviewOnly = summary.IsPreviewOnly,
                PreviewLimit = summary.PreviewLimit,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule14ControlSummaryItemViewModel
                    {
                        ControlType = item.ControlType,
                        ControlLabel = item.ControlLabel,
                        CriteriaText = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount = item.AchievedCount,
                        TotalCount = item.TotalCount,
                        PassCount = item.PassCount,
                        FailCount = item.FailCount,
                        Status = item.Status
                    })
                    .ToList(),
                ReviewRows = summary.ReviewRows
                    .Select(CloneReviewRow)
                    .ToList(),
                Warning = summary.Warning,
                Error = summary.Error
            };
        }

        private static Rule14ValidationRowRecord CloneReviewRow(Rule14ValidationRowRecord row)
        {
            return new Rule14ValidationRowRecord
            {
                ValidationNumber = row.ValidationNumber,
                ControlType = row.ControlType,
                ControlLabel = row.ControlLabel,
                ValidationResult = row.ValidationResult,
                ValidationExplanation = row.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(row.DisplayValues, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static Rule14ValidationSummary CreateBrowserPreview(Rule14ValidationSummary summary)
        {
            var controlGroups = summary.ReviewRows
                .GroupBy(row => row.ControlType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => GetControlSort(group.Key))
                .ToList();

            var controlCount = Math.Max(controlGroups.Count, 1);
            var baseLimit = Math.Max(BrowserPreviewRowLimit / controlCount, 1);
            var remainder = Math.Max(BrowserPreviewRowLimit - (baseLimit * controlGroups.Count), 0);
            var previewRows = controlGroups
                .SelectMany((group, index) => TakePreviewRowsForControl(
                    group.OrderBy(row => row.ValidationNumber).ToList(),
                    baseLimit + (index < remainder ? 1 : 0)))
                .Take(BrowserPreviewRowLimit)
                .ToList();

            return new Rule14ValidationSummary
            {
                Success = summary.Success,
                StudRecordCount = summary.StudRecordCount,
                BridgeRecordCount = summary.BridgeRecordCount,
                CrseRecordCount = summary.CrseRecordCount,
                UnfulfilledPopulationCount = summary.UnfulfilledPopulationCount,
                ApprovedQualificationCount = summary.ApprovedQualificationCount,
                ApprovedCredentialCount = summary.ApprovedCredentialCount,
                RegisteredCredentialCount = summary.RegisteredCredentialCount,
                TotalRequested = summary.TotalRequested,
                TotalValidated = summary.TotalValidated,
                DisplayedCount = previewRows.Count,
                IsPreviewOnly = summary.TotalValidated > previewRows.Count,
                PreviewLimit = summary.TotalValidated > previewRows.Count ? previewRows.Count : 0,
                PassCount = summary.PassCount,
                FailCount = summary.FailCount,
                ExceptionRate = summary.ExceptionRate,
                Status = summary.Status,
                Timestamp = summary.Timestamp,
                Database = summary.Database,
                StudTable = summary.StudTable,
                BridgeTable = summary.BridgeTable,
                CrseTable = summary.CrseTable,
                TableLinkageText = summary.TableLinkageText,
                RuleModeText = summary.RuleModeText,
                ProcedureSteps = summary.ProcedureSteps.ToList(),
                ClientId = summary.ClientId,
                SavedRunId = summary.SavedRunId,
                ControlSummaries = summary.ControlSummaries
                    .Select(item => new Rule14ControlSummaryItemViewModel
                    {
                        ControlType = item.ControlType,
                        ControlLabel = item.ControlLabel,
                        CriteriaText = item.CriteriaText,
                        RequestedCount = item.RequestedCount,
                        AvailableCount = item.AvailableCount,
                        AchievedCount = item.AchievedCount,
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

        private static List<Rule14ValidationRowRecord> TakePreviewRowsForControl(
            IReadOnlyList<Rule14ValidationRowRecord> rows,
            int limit)
        {
            if (limit <= 0 || rows.Count == 0)
                return new List<Rule14ValidationRowRecord>();

            var failRows = rows
                .Where(row => string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var passRows = rows
                .Where(row => !string.Equals(row.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selected = new List<Rule14ValidationRowRecord>(limit);
            var failTake = failRows.Count == 0
                ? 0
                : Math.Min(failRows.Count, Math.Max(1, limit / 2));

            selected.AddRange(failRows.Take(failTake));

            var passTake = Math.Min(passRows.Count, limit - selected.Count);
            selected.AddRange(passRows.Take(passTake));

            if (selected.Count < limit)
                selected.AddRange(failRows.Skip(failTake).Take(limit - selected.Count));

            if (selected.Count < limit)
                selected.AddRange(passRows.Skip(passTake).Take(limit - selected.Count));

            return selected
                .OrderBy(row => row.ValidationNumber)
                .ToList();
        }

        private static void ApplyBrowserPreview(Rule14ValidationSummary summary)
        {
            var preview = CreateBrowserPreview(summary);
            summary.DisplayedCount = preview.DisplayedCount;
            summary.IsPreviewOnly = preview.IsPreviewOnly;
            summary.PreviewLimit = preview.PreviewLimit;
            summary.ReviewRows = preview.ReviewRows;
        }

        private static Rule14ValidationRequest CloneValidationRequest(Rule14ValidationRequest request)
        {
            return new Rule14ValidationRequest
            {
                ClientId = request.ClientId,
                RunId = request.RunId,
                Server = request.Server,
                Database = request.Database,
                Driver = request.Driver,
                StudTable = request.StudTable,
                BridgeTable = request.BridgeTable,
                CrseTable = request.CrseTable
            };
        }

        private static bool RequestsMatchForPendingSave(Rule14ValidationRequest current, Rule14ValidationRequest pending)
        {
            return current.ClientId == pending.ClientId &&
                   string.Equals(current.Server?.Trim(), pending.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Database?.Trim(), pending.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.Driver?.Trim(), pending.Driver?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.StudTable?.Trim(), pending.StudTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.BridgeTable?.Trim(), pending.BridgeTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(current.CrseTable?.Trim(), pending.CrseTable?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static int GetControlSort(string? controlType) => controlType switch
        {
            "Control_1" => 1,
            _ => 99
        };

        private static List<string> BuildProcedureSteps(string crseTable, string cregTable) =>
            new()
            {
                $"Filter {crseTable} to approved courses where _031 = 'A'.",
                $"Keep the distinct approved course codes from {crseTable}._030.",
                $"For each approved course code, test whether a matching {cregTable} row exists on _030.",
                "Mark rows PASS when a matching registration exists and FAIL when no registration exists.",
                "Return the full approved-course population; no sampling is applied."
            };

        private async Task EnsureColumnsExistAsync(
            string server, string database, string driver, string studTable, string bridgeTable, string crseTable,
            string crseApprovedCol = "_031", string crseLinkCol = "_030", string cregLinkCol = "_030")
        {
            var studColumns = await GetTableColumnsAsync(server, database, driver, studTable);
            var bridgeColumns = await GetTableColumnsAsync(server, database, driver, bridgeTable);

            EnsureHasColumns(studTable, studColumns, crseLinkCol, crseApprovedCol);
            EnsureHasColumns(bridgeTable, bridgeColumns, cregLinkCol);
        }

        public Task<List<string>> GetTableColumnsListAsync(string server, string database, string driver, string tableName)
            => GetTableColumnsAsync(server, database, driver, tableName);

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

        private static void EnsureHasColumns(string tableName, IReadOnlyCollection<string> availableColumns, params string[] requiredColumns)
        {
            var missing = requiredColumns
                .Where(required => !availableColumns.Contains(required, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (missing.Count > 0)
                throw new InvalidOperationException($"Table {tableName} is missing required column(s): {string.Join(", ", missing)}.");
        }

        private static void ValidateRequest(Rule14ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("CRSE table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("CREG table is required.");

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.BridgeTable);
        }

        private static void ValidateRequest(Rule14VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable))
                throw new InvalidOperationException("CRSE table is required.");
            if (string.IsNullOrWhiteSpace(request.BridgeTable))
                throw new InvalidOperationException("CREG table is required.");

            ValidateObjectName(request.StudTable);
            ValidateObjectName(request.BridgeTable);
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
                var match = values.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            foreach (var fragment in containsMatches)
            {
                var match = values.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase));
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
            var existingCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

            await using var deleteCommand = connection.CreateConfiguredCommand();
            deleteCommand.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
            deleteCommand.Parameters.AddWithValue("@RunID", runId);
            await deleteCommand.ExecuteNonQueryAsync();

            await using var updateCommand = connection.CreateConfiguredCommand();
            updateCommand.CommandText = "UPDATE dbo.ValidationRuns SET Status = 'Needs Review' WHERE RunID = @RunID;";
            updateCommand.Parameters.AddWithValue("@RunID", runId);
            await updateCommand.ExecuteNonQueryAsync();

            return existingCount;
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
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }

        private static Rule14ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded))
                    return null;
                return JsonConvert.DeserializeObject<Rule14ValidationSummary>(decoded);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule14ValidationSummary summary)
        {
            await using var update = connection.CreateConfiguredCommand();
            update.CommandText = @"
UPDATE dbo.ValidationRuns
SET ResultsJSON = @ResultsJSON
WHERE RunID = @RunID;";
            update.Parameters.AddWithValue("@RunID", runId);
            update.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            await update.ExecuteNonQueryAsync();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static void EnrichRule14DisplayValues(Rule14ValidationRowRecord row)
        {
            var values = row.DisplayValues;
            var validationResult = ReadValue(values, "Validation_Result");
            var isPass       = string.Equals(validationResult, "PASS", StringComparison.OrdinalIgnoreCase);
            var courseCode   = FormatRule14ColumnValue(ReadValue(values, "Course_Code"));
            var courseStatus = FormatRule14ColumnValue(ReadValue(values, "Course_Approval_Status"));
            var cregCode     = FormatRule14ColumnValue(ReadValue(values, "CREG_Course_Code"));
            var controlLabel = ReadValue(values, "Control_Label");

            var validationExplanation = isPass
                ? $"CRSE approval: FOUND ('{courseStatus}') | CREG registration: FOUND (matched on '{cregCode}')"
                : $"CRSE approval: FOUND ('{courseStatus}') | CREG registration: NOT FOUND (no match for course '{courseCode}')";
            var registrationMessage = isPass
                ? $"Matched registration: CREG code='{cregCode}'."
                : $"No matching CREG registration row found for course code '{courseCode}'.";

            // Keys the Razor view reads directly from DisplayValues
            values["CRSE__030"] = ReadValue(values, "Course_Code");
            values["CRSE__031"] = ReadValue(values, "Course_Approval_Status");
            values["CREG__030"] = ReadValue(values, "CREG_Course_Code");
            values["CRSE_CRITERIA_MESSAGE"] = isPass
                ? $"_031 = '{courseStatus}'"
                : $"_031 = '{courseStatus}'";

            values["FINAL_RULE_TEXT"]        = controlLabel;
            values["Validation_Reason"]      = ReadValue(values, "Validation_Reason").Length > 0
                                                   ? ReadValue(values, "Validation_Reason")
                                                   : validationExplanation;
            values["Validation_Explanation"] = validationExplanation;
            values["CREG_LINK_MESSAGE"]      = registrationMessage;
            values["FINAL_RESULT_MESSAGE"]   = isPass
                ? "Passed: approved course found in CREG."
                : "Failed: approved course not found in CREG.";
            row.ValidationExplanation = validationExplanation;
        }

        private static string FormatRule14ColumnValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "[blank]" : value.Trim();

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
            values.TryGetValue(key, out var value) ? value ?? "" : "";

    }
}









