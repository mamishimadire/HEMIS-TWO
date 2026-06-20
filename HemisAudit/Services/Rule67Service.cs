using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule67Service : IRule67Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule67Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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
                while (await reader.ReadAsync()) items.Add(reader.GetString(0));
                return new DatabaseListResult { Success = true, Databases = items };
            }
            catch (Exception ex) { return new DatabaseListResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule67TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                return new Rule67TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoCregTable = FindFirst(tables, ["dbo_CREG"], ["dbo_creg", "creg"]),
                    AutoStudTable = FindFirst(tables, ["dbo_STUD_VALPAC"], ["stud_valpac", "dbo_stud"])
                };
            }
            catch (Exception ex) { return new Rule67TableDiscoveryResult { Success = false, Error = ex.Message }; }
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

        public async Task<Rule67VerifyResult> VerifyTablesAsync(Rule67VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var cregTable = Sanitise(request.CregTable);
                var studTable = Sanitise(request.StudTable);

                var cregCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{cregTable}];");
                var studCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{studTable}];");

                return new Rule67VerifyResult { Success = true, CregRecordCount = cregCount, StudRecordCount = studCount };
            }
            catch (Exception ex) { return new Rule67VerifyResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule67ValidationSummary> RunValidationAsync(Rule67ValidationRequest request, string? userEmail = null, string? userName = null)
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
                            _pendingValidationCache.ClearPending(67, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(67, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);
                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full CREG pair population. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Rule 67 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(browserSummary);
                return browserSummary;
            }
            catch (Exception ex) { return new Rule67ValidationSummary { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule67ValidationSummary> GetExportSummaryAsync(Rule67ValidationRequest request)
        {
            ValidateRequest(request);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule67ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule67ValidationRequest, Rule67ValidationSummary>(67, clientId, reviewerEmail);
            if (pending == null) return Task.FromResult<Rule67ValidationSummary?>(null);
            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 67 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule67ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(67, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule67WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS CregTable,
    ISNULL(vr.DeceasedTable, '') AS StudTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 67 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var runId                = reader.GetInt32(0);
            var workspaceClientId    = reader.GetInt32(1);
            var server               = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database             = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var cregTable            = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var studTable            = reader.IsDBNull(5) ? "dbo_STUD_VALPAC" : reader.GetString(5);
            var currentStatus        = reader.IsDBNull(6) ? "" : reader.GetString(6);
            var lastEditedByUserName = reader.IsDBNull(7) ? null : reader.GetString(7);
            DateTime? lastEditedAt   = reader.IsDBNull(8) ? null : reader.GetDateTime(8);
            var encodedSummary       = reader.IsDBNull(9) ? null : reader.GetString(9);

            await reader.CloseAsync();

            var deserializedSummary = DeserializeSummary(encodedSummary);
            if (deserializedSummary != null && includeSummary)
            {
                deserializedSummary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, deserializedSummary, server);
                ApplyBrowserPreview(deserializedSummary);
            }
            var summary = includeSummary ? deserializedSummary : null;

            var workspace = new Rule67WorkspaceStateViewModel
            {
                ClientId             = workspaceClientId,
                RunId                = runId,
                Server               = server,
                Database             = database,
                CregTable            = deserializedSummary?.CregTable ?? cregTable,
                StudTable            = deserializedSummary?.StudTable ?? studTable,
                CregStudentNoCol     = deserializedSummary?.CregStudentNoCol ?? "_007",
                CregQualCol          = deserializedSummary?.CregQualCol ?? "_001",
                CregE051Col          = deserializedSummary?.CregE051Col ?? "_051",
                StudStudentNoCol     = deserializedSummary?.StudStudentNoCol ?? "_007",
                StudQualCol          = deserializedSummary?.StudQualCol ?? "_001",
                E051FilterValues     = deserializedSummary?.E051FilterValues ?? "E",
                CurrentStatus        = currentStatus,
                LastEditedByUserName = lastEditedByUserName,
                LastEditedAt         = lastEditedAt,
                Summary              = summary
            };

            if (summary != null) workspace.CurrentStatus = summary.Status;
            workspace.Driver = "ODBC Driver 17 for SQL Server";
            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "" : "";

            var signoffs = await GetRunSignoffsAsync(connection, workspace.RunId!.Value, currentUserId);
            workspace.HasDataAnalystSignoff = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var currentRoleSignoff = signoffs.FirstOrDefault(s =>
                HemisAudit.Helpers.ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff = currentRoleSignoff != null;
            workspace.CurrentUserSignoffComment = currentRoleSignoff?.Comment ?? "";
            workspace.IsWorkspaceSaved = await IsWorkspaceSavedAsync(connection, workspace.RunId!.Value);
            if (workspace.Summary != null) workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule67RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 67;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var savedRunId     = reader.GetInt32(0);
            var clientId       = reader.GetInt32(1);
            var isCurrentRun   = !reader.IsDBNull(2) && reader.GetBoolean(2);
            var engagementName = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var maconomyNumber = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var sourceServer   = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var encodedSummary = reader.IsDBNull(6) ? null : reader.GetString(6);
            var summary        = DeserializeSummary(encodedSummary);
            if (summary == null) return null;

            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0) summary.SavedRunId = runId;
            await reader.CloseAsync();

            summary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, summary, sourceServer);

            if (includeFullResults) { summary.DisplayedCount = summary.ReviewRows.Count; summary.IsPreviewOnly = false; summary.PreviewLimit = 0; }
            else { ApplyBrowserPreview(summary); }

            var review = new Rule67RunReviewViewModel
            {
                RunId = savedRunId, ClientId = clientId, IsCurrentRun = isCurrentRun,
                EngagementName = engagementName, MaconomyNumber = maconomyNumber,
                SourceServer = sourceServer, Summary = summary
            };

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "" : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return review;
        }

        public async Task<Rule67WorkspaceSaveResult> SaveWorkspaceAsync(Rule67ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId.HasValue && request.RunId.Value > 0)
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                    if (!clientId.HasValue || clientId.Value != request.ClientId)
                        return new Rule67WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

                    await EnsureClientNotArchivedAsync(connection, request.ClientId);
                    var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, request.RunId.Value);
                    var previousHash    = await GetValidationRecordHashAsync(connection, request.RunId.Value);

                    await using var cmd = connection.CreateConfiguredCommand();
                    cmd.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName=@LastEditedByUserName, LastEditedAt=GETDATE(), WorkspaceSavedAt=GETDATE(),
    PreviousHash=@PreviousHash, RecordHash=@RecordHash, Status='Needs Review'
WHERE RunID=@RunID AND ClientID=@ClientID;";
                    cmd.Parameters.AddWithValue("@RunID", request.RunId.Value);
                    cmd.Parameters.AddWithValue("@ClientID", request.ClientId);
                    cmd.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule67|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await cmd.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(67, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule67WorkspaceSaveResult { Success = true, Message = clearedSignoffs > 0 ? "Workspace saved. Existing signoffs were removed." : "Workspace saved and marked for review.", SignoffsCleared = clearedSignoffs > 0, ClearedSignoffCount = clearedSignoffs, Workspace = currentWorkspace };
                }

                var pending = _pendingValidationCache.GetPending<Rule67ValidationRequest, Rule67ValidationSummary>(67, request.ClientId, reviewerEmail);
                if (pending == null)
                    return new Rule67WorkspaceSaveResult { Success = false, Error = "Run Rule 67 first so the current workspace is written to the system database." };

                if (!RequestsMatch(request, pending.Request))
                    return new Rule67WorkspaceSaveResult { Success = false, Error = "Workspace settings changed after validation. Run Rule 67 again before saving." };

                var summaryToSave = CloneSummary(pending.Summary);
                if (summaryToSave.IsPreviewOnly || summaryToSave.ReviewRows.Count < summaryToSave.TotalValidated)
                    summaryToSave = await AnalyseAsync(pending.Request, includeAllReviewRows: true);

                summaryToSave.SavedRunId = null;
                var savedRunId = await SaveValidationRunAsync(CloneRequest(pending.Request), summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(67, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule67WorkspaceSaveResult { Success = true, Message = $"Workspace saved as Run #{savedRunId}.", SignoffsCleared = false, ClearedSignoffCount = 0, Workspace = workspace };
            }
            catch (Exception ex) { return new Rule67WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule67WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue) return new Rule67WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

                await EnsureClientNotArchivedAsync(connection, clientId.Value);
                var clearedSignoffs = await ClearSignoffsAndFlagForReviewAsync(connection, runId);
                var previousHash    = await GetValidationRecordHashAsync(connection, runId);

                await using var cmd = connection.CreateConfiguredCommand();
                cmd.CommandText = @"
UPDATE dbo.ValidationRuns
SET LastEditedByUserName=@LastEditedByUserName, LastEditedAt=GETDATE(), WorkspaceSavedAt=NULL,
    PreviousHash=@PreviousHash, RecordHash=@RecordHash, Status='Needs Review'
WHERE RunID=@RunID;";
                cmd.Parameters.AddWithValue("@RunID", runId);
                cmd.Parameters.AddWithValue("@LastEditedByUserName", (object?)reviewerName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginEdit|Rule67|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await cmd.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(67, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule67WorkspaceSaveResult { Success = true, Message = clearedSignoffs > 0 ? "Editing begun. Existing signoffs removed." : "Editing begun. Save the workspace when ready.", SignoffsCleared = clearedSignoffs > 0, ClearedSignoffCount = clearedSignoffs, Workspace = workspace };
            }
            catch (Exception ex) { return new Rule67WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue) throw new InvalidOperationException("The reviewer could not be resolved.");
            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue) throw new InvalidOperationException("The selected Rule 67 run could not be found.");
            await EnsureClientNotArchivedAsync(connection, clientId.Value);
            if (!await IsWorkspaceSavedAsync(connection, runId)) throw new InvalidOperationException("The data analyst must save the workspace before signoff.");
            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole)) throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off.");
            if (!string.Equals(signoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) && !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
                throw new InvalidOperationException("The data analyst must sign off before this review can be completed.");

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND ReviewerID=@ReviewerID)
    UPDATE dbo.ReviewSignoffs SET SignoffRole=@SignoffRole,ReviewType='Final',Comment=@Comment,SignedOffAt=GETDATE() WHERE RunID=@RunID AND ReviewerID=@ReviewerID;
ELSE
    INSERT INTO dbo.ReviewSignoffs (ClientID,RunID,ReviewerID,SignoffRole,ReviewType,Comment,SignedOffAt) VALUES (@ClientID,@RunID,@ReviewerID,@SignoffRole,'Final',@Comment,GETDATE());";
            cmd.Parameters.AddWithValue("@ClientID", clientId.Value);
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@ReviewerID", reviewerId.Value);
            cmd.Parameters.AddWithValue("@SignoffRole", signoffRole!);
            cmd.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim());
            await cmd.ExecuteNonQueryAsync();
            await UpdateRunStatusFromSignoffsAsync(connection, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue) throw new InvalidOperationException("The reviewer could not be resolved.");
            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue) throw new InvalidOperationException("The selected Rule 67 run could not be found.");
            await EnsureClientNotArchivedAsync(connection, clientId.Value);
            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff.");
            await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
        }

        public Task<string> GenerateSqlAsync(Rule67ValidationRequest request)
        {
            ValidateRequest(request);
            var cregTable        = Sanitise(request.CregTable);
            var studTable        = Sanitise(request.StudTable);
            var cregStudentCol   = Sanitise(request.CregStudentNoCol);
            var cregQualCol      = Sanitise(request.CregQualCol);
            var cregE051Col      = Sanitise(string.IsNullOrWhiteSpace(request.CregE051Col) ? "_051" : request.CregE051Col);
            var studStudentCol   = Sanitise(request.StudStudentNoCol);
            var studQualCol      = Sanitise(request.StudQualCol);
            var e051Values       = ParseFilterValues(request.E051FilterValues);
            var e051ValuesText   = e051Values.Count > 0 ? string.Join(", ", e051Values.Select(v => $"'{v}'")) : "ALL — no filter applied";
            // Inside a SQL string literal, single quotes must be doubled to avoid breaking the string
            var e051ValuesLiteral = e051ValuesText.Replace("'", "''");

            var sql = $@"-- HEMIS RULE 67: CREG-STUD PAIR VALIDATION
-- Check: CREG.[{cregStudentCol}] + [{cregQualCol}] pair must exist in STUD.[{studStudentCol}] + [{studQualCol}]
-- AND CREG.[{cregE051Col}] must be IN ({e051ValuesText})
-- PASS when pair found in STUD and E051 matches; FAIL otherwise (Exception code: 00708)

{BuildSourceCtes(cregTable, studTable, cregStudentCol, cregQualCol, cregE051Col, studStudentCol, studQualCol, e051Values)}
SELECT
    'Control_1' AS Control_Type,
    'CONTROL 1: CREG [{cregTable}].[{cregStudentCol}]+[{cregQualCol}] pair in STUD [{studTable}].[{studStudentCol}]+[{studQualCol}] with [{cregE051Col}] IN ({e051ValuesLiteral})' AS Control_Label,
    CREG_STUD_NO, CREG_QUAL, CREG_E051, STUD_NO, STUD_QUAL, IN_STUD, STUD_STUDENT_EXISTS, GHOST_STUDENT, GHOST_STUDENT_NOTE, E051_VALID,
    ValidationResult,
    CASE WHEN ValidationResult = 'FAIL' THEN '00708' ELSE '' END AS Exception_Code,
    FailReason
FROM ValidationResults
ORDER BY CASE WHEN ValidationResult = 'FAIL' THEN 0 ELSE 1 END, CREG_STUD_NO, CREG_QUAL;

SELECT
    (SELECT COUNT(1) FROM CregPairs)                                                    AS CREG_Total,
    (SELECT COUNT(1) FROM ValidationResults WHERE ValidationResult = 'PASS')            AS Pass_Count,
    (SELECT COUNT(1) FROM ValidationResults WHERE ValidationResult = 'FAIL')            AS Fail_Count,
    (SELECT COUNT(1) FROM ValidationResults WHERE FailReason = 'Not found in STUD')    AS Not_In_STUD,
    (SELECT COUNT(1) FROM ValidationResults WHERE FailReason = 'E051 code not in expected values') AS Invalid_E051,
    (SELECT COUNT(1) FROM ValidationResults WHERE FailReason = 'Not found in STUD' AND E051_VALID = 'Yes') AS Not_In_STUD_E051_Valid,
    (SELECT COUNT(1) FROM ValidationResults WHERE FailReason = 'Not found in STUD' AND E051_VALID = 'No') AS Not_In_STUD_E051_Invalid,
    (SELECT COUNT(1) FROM ValidationResults WHERE FailReason = 'Not found in STUD' AND GHOST_STUDENT = 'Yes') AS Ghost_Students;";

            return Task.FromResult(sql.Trim());
        }

        // ─── Core Analysis ────────────────────────────────────────────────────

        private async Task<Rule67ValidationSummary> AnalyseAsync(Rule67ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var cregTable           = Sanitise(request.CregTable);
            var studTable           = Sanitise(request.StudTable);
            var detailTable         = Sanitise(request.DetailTable ?? "");
            var detailErrCodes      = ParseDetailErrorCodes(request.DetailErrorCode ?? "00708");
            var detailErrorCol      = Sanitise(string.IsNullOrWhiteSpace(request.DetailErrorCol)      ? "Error"               : request.DetailErrorCol);
            var detailElementInfoCol= Sanitise(string.IsNullOrWhiteSpace(request.DetailElementInfoCol)? "Element_Information" : request.DetailElementInfoCol);
            var cregStudentCol      = Sanitise(request.CregStudentNoCol);
            var cregQualCol         = Sanitise(request.CregQualCol);
            var cregE051Col         = Sanitise(string.IsNullOrWhiteSpace(request.CregE051Col) ? "_051" : request.CregE051Col);
            var studStudentCol      = Sanitise(request.StudStudentNoCol);
            var studQualCol         = Sanitise(request.StudQualCol);
            var e051Values          = ParseFilterValues(request.E051FilterValues);
            var e051ValuesText      = e051Values.Count > 0 ? string.Join(", ", e051Values) : "ALL — no filter applied";

            // Single batch: RS1=source counts, RS2=validation+reconciliation counts, RS3=rows, RS4=Rule29-only
            await using var batchCmd = conn.CreateConfiguredCommand();
            batchCmd.CommandText = BuildSingleBatchSql(cregTable, studTable, detailTable, detailErrCodes, cregStudentCol, cregQualCol, cregE051Col, studStudentCol, studQualCol, e051Values, includeAllReviewRows ? null : BrowserPreviewRowLimit, detailErrorCol, detailElementInfoCol);
            await using var batchReader = await batchCmd.ExecuteReaderAsync();

            // RS 1: source table row counts + detail count
            int cregCount = 0, studCount = 0, detailCount = 0;
            if (await batchReader.ReadAsync())
            {
                cregCount   = batchReader.IsDBNull(0) ? 0 : Convert.ToInt32(batchReader.GetValue(0), CultureInfo.InvariantCulture);
                studCount   = batchReader.IsDBNull(1) ? 0 : Convert.ToInt32(batchReader.GetValue(1), CultureInfo.InvariantCulture);
                detailCount = batchReader.IsDBNull(2) ? 0 : Convert.ToInt32(batchReader.GetValue(2), CultureInfo.InvariantCulture);
            }

            // RS 2: validation counts + reconciliation counts
            int totalChecked = 0, passCount = 0, notInStudCount = 0, invalidE051Count = 0, notInStudE051ValidCount = 0, notInStudE051InvalidCount = 0, ghostStudentCount = 0;
            int confirmedByRule29Count = 0, notInRule29Count = 0, rule29OnlyCount = 0;
            if (await batchReader.NextResultAsync() && await batchReader.ReadAsync())
            {
                totalChecked            = GetInt(batchReader, 0);
                passCount               = GetInt(batchReader, 1);
                notInStudCount          = GetInt(batchReader, 2);
                invalidE051Count        = GetInt(batchReader, 3);
                notInStudE051ValidCount = GetInt(batchReader, 4);
                notInStudE051InvalidCount = GetInt(batchReader, 5);
                ghostStudentCount       = GetInt(batchReader, 6);
                confirmedByRule29Count  = GetInt(batchReader, 7);
                notInRule29Count        = GetInt(batchReader, 8);
                rule29OnlyCount         = GetInt(batchReader, 9);
            }

            // RS 3: preview / full rows (now includes Reconciliation_Status column)
            var reviewRows = new List<Rule67ValidationRowRecord>();
            if (await batchReader.NextResultAsync())
            {
                while (await batchReader.ReadAsync())
                {
                    var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < batchReader.FieldCount; i++)
                        displayValues[batchReader.GetName(i)] = batchReader.IsDBNull(i) ? null : Convert.ToString(batchReader.GetValue(i), CultureInfo.InvariantCulture);
                    var row = new Rule67ValidationRowRecord
                    {
                        ValidationNumber      = reviewRows.Count + 1,
                        ControlType           = displayValues.TryGetValue("Control_Type", out var ct) ? ct ?? "" : "",
                        ControlLabel          = displayValues.TryGetValue("Control_Label", out var cl) ? cl ?? "" : "",
                        ValidationResult      = displayValues.TryGetValue("ValidationResult", out var vr) ? vr ?? "" : "",
                        ValidationExplanation = displayValues.TryGetValue("Validation_Explanation", out var ve) ? ve ?? "" : "",
                        ExceptionCode         = displayValues.TryGetValue("Exception_Code", out var ec) ? ec ?? "" : "",
                        DisplayValues         = displayValues
                    };
                    EnrichDisplayValues(row);
                    reviewRows.Add(row);
                }
            }

            // RS 4: Rule 29-only rows (in detail table 00708 but not in Rule 67 FAIL)
            var rule29OnlyRows = new List<Rule67Rule29OnlyRow>();
            if (await batchReader.NextResultAsync())
            {
                while (await batchReader.ReadAsync())
                {
                    // RS4 columns: 0=RowNum, 1=DETAIL_STUD_NO, 2=DETAIL_QUAL, 3=DETAIL_ERROR_MSG, 4=ConfirmedByR67
                    rule29OnlyRows.Add(new Rule67Rule29OnlyRow
                    {
                        RowNumber      = rule29OnlyRows.Count + 1,
                        StudentNo      = batchReader.IsDBNull(1) ? "" : Convert.ToString(batchReader.GetValue(1), CultureInfo.InvariantCulture) ?? "",
                        QualCode       = batchReader.IsDBNull(2) ? "" : Convert.ToString(batchReader.GetValue(2), CultureInfo.InvariantCulture) ?? "",
                        ErrorMessage   = batchReader.FieldCount > 3 && !batchReader.IsDBNull(3) ? Convert.ToString(batchReader.GetValue(3), CultureInfo.InvariantCulture) ?? "" : "",
                        ConfirmedByR67 = batchReader.FieldCount > 4 && !batchReader.IsDBNull(4) ? Convert.ToString(batchReader.GetValue(4), CultureInfo.InvariantCulture) ?? "No" : "No"
                    });
                }
            }
            await batchReader.CloseAsync();
            reviewRows = NormalizeRows(reviewRows);
            if (!includeAllReviewRows && rule29OnlyRows.Count > BrowserPreviewRowLimit)
                rule29OnlyRows = rule29OnlyRows.Take(BrowserPreviewRowLimit).ToList();
            var failCount     = notInStudCount + invalidE051Count;
            var isPreviewOnly = !includeAllReviewRows && totalChecked > reviewRows.Count;

            var controlSummaries = new List<Rule67ControlSummaryItemViewModel>
            {
                new()
                {
                    ControlType  = "Control_1",
                    ControlLabel = "Control 1",
                    CriteriaText = $"CREG [{cregStudentCol}]+[{cregQualCol}] pair must exist in STUD and [{cregE051Col}] IN ({e051ValuesText})",
                    TotalCount   = totalChecked,
                    PassCount    = passCount,
                    FailCount    = failCount,
                    Status       = failCount == 0 ? "PASS" : "FAIL"
                }
            };

            return new Rule67ValidationSummary
            {
                Success                = true,
                CregRecordCount        = cregCount,
                StudRecordCount        = studCount,
                TotalValidated         = totalChecked,
                DisplayedCount         = reviewRows.Count,
                IsPreviewOnly          = isPreviewOnly,
                PreviewLimit           = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount              = passCount,
                FailCount              = failCount,
                NotInStudCount         = notInStudCount,
                NotInStudE051ValidCount = notInStudE051ValidCount,
                NotInStudE051InvalidCount = notInStudE051InvalidCount,
                GhostStudentCount      = ghostStudentCount,
                InvalidE051Count       = invalidE051Count,
                ExceptionRate          = totalChecked == 0 ? 0m : Math.Round(failCount * 100m / totalChecked, 2),
                Status                 = failCount == 0 ? "PASS" : "FAIL",
                Timestamp              = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database               = request.Database,
                CregTable              = request.CregTable,
                StudTable              = request.StudTable,
                CregStudentNoCol       = cregStudentCol,
                CregQualCol            = cregQualCol,
                CregE051Col            = cregE051Col,
                StudStudentNoCol       = studStudentCol,
                StudQualCol            = studQualCol,
                E051FilterValues       = e051Values.Count > 0 ? string.Join(",", e051Values) : "",
                DetailTable            = detailTable,
                DetailErrorCode        = detailErrCodes.Count > 0 ? string.Join(",", detailErrCodes.Select(c => c.ToString())) : "00708",
                DetailErrorCol         = detailErrorCol,
                DetailElementInfoCol   = detailElementInfoCol,
                DetailRecordCount      = detailCount,
                ConfirmedByRule29Count = confirmedByRule29Count,
                NotInRule29Count       = notInRule29Count,
                Rule29OnlyCount        = rule29OnlyCount,
                Rule29OnlyRows         = rule29OnlyRows,
                TableLinkageText  = $"{request.CregTable}.[{cregStudentCol}]+[{cregQualCol}] <> {request.StudTable}.[{studStudentCol}]+[{studQualCol}] (E051 filter: [{cregE051Col}] IN {e051ValuesText})",
                RuleModeText      = $"CREG pairs checked against STUD, [{cregE051Col}] must be IN ({e051ValuesText})",
                ProcedureSteps    = new List<string>
                {
                    $"Extract all distinct [{cregStudentCol}]+[{cregQualCol}]+[{cregE051Col}] combinations from {request.CregTable}.",
                    $"For each CREG pair, check if [{studStudentCol}]+[{studQualCol}] exists in {request.StudTable}.",
                    $"Also check that [{cregE051Col}] is IN ({e051ValuesText}).",
                    "Mark PASS when the pair is found in STUD AND E051 is in the filter. FAIL otherwise (exception code 00708).",
                    "A FAIL indicates: pair missing from STUD, or E051 code does not match the expected value(s).",
                    string.IsNullOrWhiteSpace(detailTable) ? "" : $"Reconciliation: cross-reference FAIL results against {detailTable} (error 00708) to confirm findings."
                }.Where(s => !string.IsNullOrEmpty(s)).ToList(),
                ClientId          = request.ClientId,
                ControlSummaries  = controlSummaries,
                ReviewRows        = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 67 completed with the full CREG pair result set."
                    : "Counts reflect the full CREG pair population. Browser review rows are limited for performance."
            };
        }

        // ─── SQL Builders ─────────────────────────────────────────────────────

        // Materialise temp tables once, return RS1=source counts, RS2=validation counts, RS3=rows, RS4=Rule29-only.
        private static string BuildSingleBatchSql(string cregTable, string studTable, string detailTable, IReadOnlyList<long> detailErrCodes, string cregStudentCol, string cregQualCol, string cregE051Col, string studStudentCol, string studQualCol, IReadOnlyList<string> e051Values, int? maxRows, string detailErrorCol = "Error", string detailElementInfoCol = "Element_Information")
        {
            var e051ValidExpr = e051Values.Count > 0
                ? $"CASE WHEN CP.CREG_E051 IN ({BuildInClauseSql(e051Values)}) THEN 'Yes' ELSE 'No' END"
                : "'Yes'";
            var passExpr = e051Values.Count > 0
                ? $"SP.STUD_NO IS NOT NULL AND CP.CREG_E051 IN ({BuildInClauseSql(e051Values)})"
                : "SP.STUD_NO IS NOT NULL";
            var failReasonExpr = e051Values.Count > 0
                ? $"CASE WHEN SP.STUD_NO IS NULL THEN 'Not found in STUD' WHEN CP.CREG_E051 NOT IN ({BuildInClauseSql(e051Values)}) THEN 'E051 code not in expected values' ELSE '' END"
                : "CASE WHEN SP.STUD_NO IS NULL THEN 'Not found in STUD' ELSE '' END";
            const string studStudentExistsExpr = "CASE WHEN SSN.STUD_STUDENT_NO IS NOT NULL THEN 'Yes' ELSE 'No' END";
            const string ghostStudentExpr = "CASE WHEN SSN.STUD_STUDENT_NO IS NULL THEN 'Yes' ELSE 'No' END";
            const string ghostStudentNoteExpr = "CASE WHEN SSN.STUD_STUDENT_NO IS NULL THEN 'Ghost student - tested in Rule 9.' WHEN SP.STUD_NO IS NULL THEN 'Student found in STUD with different qualification code' ELSE '' END";
            var top     = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;
            var orderBy = maxRows.HasValue && maxRows.Value > 0
                ? "CASE WHEN ValidationResult = 'FAIL' THEN 0 ELSE 1 END, CREG_STUD_NO, CREG_QUAL"
                : "CREG_STUD_NO, CREG_QUAL";

            var hasDetail     = !string.IsNullOrWhiteSpace(detailTable);
            // Reconciliation column — '' when no detail table so the column always exists
            var reconExpr     = hasDetail
                ? $"CASE WHEN {passExpr} THEN '' WHEN DP.DETAIL_STUD_NO IS NOT NULL THEN 'Confirmed by Rule 29' ELSE 'Not in Rule 29' END"
                : "''";
            var detailJoin    = hasDetail ? "LEFT JOIN #R67DetailPairs DP ON DP.DETAIL_STUD_NO = CP.CREG_STUD_NO AND DP.DETAIL_QUAL = CP.CREG_QUAL" : "";
            var detailErrInSql  = hasDetail ? BuildDetailErrInSql(detailErrCodes) : "0";
            var detailCountExpr = hasDetail
                ? $"(SELECT COUNT(*) FROM [{detailTable}] WITH (NOLOCK) WHERE TRY_CAST(LTRIM(RTRIM(CAST([{detailErrorCol}] AS nvarchar(50)))) AS bigint) IN ({detailErrInSql}))"
                : "0";
            var reconCountsSql  = hasDetail
                ? @"SUM(CASE WHEN Reconciliation_Status = 'Confirmed by Rule 29' THEN 1 ELSE 0 END) AS ConfirmedByRule29Count,
    SUM(CASE WHEN Reconciliation_Status = 'Not in Rule 29'       THEN 1 ELSE 0 END) AS NotInRule29Count,
    (SELECT COUNT(*) FROM #R67DetailPairs DP2 WHERE DP2.DETAIL_STUD_NO IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #R67Results R2 WHERE R2.CREG_STUD_NO = DP2.DETAIL_STUD_NO AND R2.CREG_QUAL = DP2.DETAIL_QUAL AND R2.ValidationResult = 'FAIL')) AS Rule29OnlyCount"
                : "0 AS ConfirmedByRule29Count, 0 AS NotInRule29Count, 0 AS Rule29OnlyCount";
            var detailSetupSql  = hasDetail ? $@"
IF OBJECT_ID('tempdb..#R67DetailPairs') IS NOT NULL DROP TABLE #R67DetailPairs;
IF OBJECT_ID('tempdb..#R67DetailRaw')   IS NOT NULL DROP TABLE #R67DetailRaw;

-- Step 1: parse E007/E001 from [{detailElementInfoCol}] — no reference to other columns
SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), CASE
        WHEN CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
         AND CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
        THEN SUBSTRING(CONVERT(nvarchar(500), [{detailElementInfoCol}]),
            CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5,
            CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}]))
                - (CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5))
    END)))                                         AS RAW_STUD_NO,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CASE
        WHEN CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
        THEN SUBSTRING(CONVERT(nvarchar(500), [{detailElementInfoCol}]),
            CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5,
            LEN(CONVERT(nvarchar(500), [{detailElementInfoCol}])))
    END))))                                        AS RAW_QUAL
INTO #R67DetailRaw
FROM [{detailTable}] WITH (NOLOCK)
WHERE CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
  AND CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
  AND TRY_CAST(LTRIM(RTRIM(CAST([{detailErrorCol}] AS nvarchar(50)))) AS bigint) IN ({detailErrInSql})
OPTION (MAXDOP 4);

-- Step 2: deduplicate by student+qual
SELECT RAW_STUD_NO AS DETAIL_STUD_NO, RAW_QUAL AS DETAIL_QUAL
INTO #R67DetailPairs
FROM #R67DetailRaw
WHERE RAW_STUD_NO IS NOT NULL AND RAW_STUD_NO <> '' AND RAW_QUAL IS NOT NULL AND RAW_QUAL <> ''
GROUP BY RAW_STUD_NO, RAW_QUAL
OPTION (MAXDOP 4);
DROP TABLE #R67DetailRaw;" : "";
            var rule29OnlySql   = hasDetail ? $@"
-- RS 4: Rule 29 Only pairs — LEFT JOIN to CREG (same pattern as detailJoin/reconExpr for 'Not in Rule 29')
-- CS.CREG_STUD_NO IS NULL  → student number not found in CREG at all  → 'Not in CREG'
-- FailR.CREG_STUD_NO IS NOT NULL → found in CREG and Rule 67 is FAIL  → 'Yes'
-- Otherwise                → found in CREG but Rule 67 is PASS         → 'No'
SELECT
    ROW_NUMBER() OVER (ORDER BY DP.DETAIL_STUD_NO, DP.DETAIL_QUAL) AS RowNum,
    DP.DETAIL_STUD_NO, DP.DETAIL_QUAL,
    CAST('' AS nvarchar(2000))                                      AS DETAIL_ERROR_MSG,
    CASE
        WHEN CS.CREG_STUD_NO IS NULL         THEN 'Not in CREG'
        WHEN FailR.CREG_STUD_NO IS NOT NULL  THEN 'Yes'
        ELSE                                      'No'
    END AS ConfirmedByR67
FROM #R67DetailPairs DP
LEFT JOIN #R67CregStudNos CS
    ON CS.CREG_STUD_NO = DP.DETAIL_STUD_NO
LEFT JOIN (
    SELECT DISTINCT CREG_STUD_NO, CREG_QUAL
    FROM   #R67Results
    WHERE  ValidationResult = 'FAIL'
) AS FailR
    ON FailR.CREG_STUD_NO = DP.DETAIL_STUD_NO
   AND FailR.CREG_QUAL    = DP.DETAIL_QUAL
ORDER BY DP.DETAIL_STUD_NO, DP.DETAIL_QUAL
OPTION (MAXDOP 4);"
                : "SELECT CAST(0 AS int) AS RowNum, CAST('' AS nvarchar(255)) AS DETAIL_STUD_NO, CAST('' AS nvarchar(255)) AS DETAIL_QUAL, CAST('' AS nvarchar(2000)) AS DETAIL_ERROR_MSG, CAST('' AS nvarchar(12)) AS ConfirmedByR67 WHERE 1=0;";
            var detailDropSql   = hasDetail ? "DROP TABLE IF EXISTS #R67DetailPairs;" : "";

            return $@"
IF OBJECT_ID('tempdb..#R67CregPairs')    IS NOT NULL DROP TABLE #R67CregPairs;
IF OBJECT_ID('tempdb..#R67StudPairs')    IS NOT NULL DROP TABLE #R67StudPairs;
IF OBJECT_ID('tempdb..#R67StudNos')      IS NOT NULL DROP TABLE #R67StudNos;
IF OBJECT_ID('tempdb..#R67Results')      IS NOT NULL DROP TABLE #R67Results;
IF OBJECT_ID('tempdb..#R67CregStudNos') IS NOT NULL DROP TABLE #R67CregStudNos;

-- All CREG student numbers (no E051 or qual filter) — used for direct 'Not in CREG' detection
SELECT DISTINCT LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregStudentCol}]))) AS CREG_STUD_NO
INTO #R67CregStudNos
FROM [{cregTable}] C WITH (NOLOCK)
WHERE C.[{cregStudentCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregStudentCol}]))) <> ''
OPTION (MAXDOP 4);
CREATE INDEX IX_R67CSN ON #R67CregStudNos (CREG_STUD_NO);

{detailSetupSql}

-- RS 1: source table row counts + optional detail count
SELECT
    (SELECT COUNT(*) FROM [{cregTable}] WITH (NOLOCK)) AS CregCount,
    (SELECT COUNT(*) FROM [{studTable}] WITH (NOLOCK)) AS StudCount,
    {detailCountExpr}                                  AS DetailCount;

-- Materialise CREG distinct pairs (LTRIM/RTRIM on student no ensures match with parsed detail values)
SELECT DISTINCT
    LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregStudentCol}])))         AS CREG_STUD_NO,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregQualCol}]))))     AS CREG_QUAL,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregE051Col}]))))     AS CREG_E051
INTO #R67CregPairs
FROM [{cregTable}] C WITH (NOLOCK)
WHERE C.[{cregStudentCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregStudentCol}]))) <> ''
  AND C.[{cregQualCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregQualCol}]))) <> ''
OPTION (MAXDOP 4);

CREATE INDEX IX_R67CP ON #R67CregPairs (CREG_STUD_NO, CREG_QUAL);

-- Materialise STUD distinct pairs
SELECT DISTINCT
    CONVERT(nvarchar(255), S.[{studStudentCol}])                       AS STUD_NO,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), S.[{studQualCol}]))))     AS STUD_QUAL
INTO #R67StudPairs
FROM [{studTable}] S WITH (NOLOCK)
WHERE S.[{studStudentCol}] IS NOT NULL
  AND S.[{studQualCol}] IS NOT NULL
OPTION (MAXDOP 4);

CREATE INDEX IX_R67SP ON #R67StudPairs (STUD_NO, STUD_QUAL);

-- Materialise STUD student numbers for ghost-student detection
SELECT DISTINCT
    CONVERT(nvarchar(255), S.[{studStudentCol}]) AS STUD_STUDENT_NO
INTO #R67StudNos
FROM [{studTable}] S WITH (NOLOCK)
WHERE S.[{studStudentCol}] IS NOT NULL
OPTION (MAXDOP 4);

CREATE INDEX IX_R67SN ON #R67StudNos (STUD_STUDENT_NO);

-- Materialise join results + reconciliation column
SELECT
    CP.CREG_STUD_NO, CP.CREG_QUAL, CP.CREG_E051,
    -- Show actual STUD values even when qual differs (non-ghost student)
    CASE WHEN SP.STUD_NO   IS NOT NULL THEN SP.STUD_NO
         WHEN SSN.STUD_STUDENT_NO IS NOT NULL THEN CP.CREG_STUD_NO
         ELSE NULL END AS STUD_NO,
    CASE WHEN SP.STUD_QUAL IS NOT NULL THEN SP.STUD_QUAL
         WHEN SSN.STUD_STUDENT_NO IS NOT NULL THEN SPF.STUD_ACTUAL_QUAL
         ELSE NULL END AS STUD_QUAL,
    CASE WHEN SP.STUD_NO IS NOT NULL THEN 'Yes' ELSE 'No' END AS IN_STUD,
    {studStudentExistsExpr}                                     AS STUD_STUDENT_EXISTS,
    {ghostStudentExpr}                                          AS GHOST_STUDENT,
    {ghostStudentNoteExpr}                                      AS GHOST_STUDENT_NOTE,
    {e051ValidExpr}                                            AS E051_VALID,
    CASE WHEN {passExpr}  THEN 'PASS' ELSE 'FAIL' END         AS ValidationResult,
    {failReasonExpr}                                           AS FailReason,
    {reconExpr}                                                AS Reconciliation_Status
INTO #R67Results
FROM #R67CregPairs CP
LEFT JOIN #R67StudPairs SP ON SP.STUD_NO = CP.CREG_STUD_NO AND SP.STUD_QUAL = CP.CREG_QUAL
LEFT JOIN #R67StudNos SSN ON SSN.STUD_STUDENT_NO = CP.CREG_STUD_NO
LEFT JOIN (SELECT STUD_NO, MIN(STUD_QUAL) AS STUD_ACTUAL_QUAL FROM #R67StudPairs GROUP BY STUD_NO) SPF
    ON SPF.STUD_NO = CP.CREG_STUD_NO
{detailJoin}
OPTION (MAXDOP 4);

CREATE INDEX IX_R67VR ON #R67Results (ValidationResult, FailReason, Reconciliation_Status);

-- RS 2: validation counts + reconciliation counts
SELECT
    COUNT(1)                                                                           AS TotalChecked,
    SUM(CASE WHEN ValidationResult = 'PASS'                        THEN 1 ELSE 0 END) AS PassCount,
    SUM(CASE WHEN FailReason = 'Not found in STUD'                 THEN 1 ELSE 0 END) AS NotInStudCount,
    SUM(CASE WHEN FailReason = 'E051 code not in expected values'  THEN 1 ELSE 0 END) AS InvalidE051Count,
    SUM(CASE WHEN FailReason = 'Not found in STUD' AND E051_VALID = 'Yes' THEN 1 ELSE 0 END) AS NotInStudE051ValidCount,
    SUM(CASE WHEN FailReason = 'Not found in STUD' AND E051_VALID = 'No'  THEN 1 ELSE 0 END) AS NotInStudE051InvalidCount,
    SUM(CASE WHEN FailReason = 'Not found in STUD' AND GHOST_STUDENT = 'Yes' THEN 1 ELSE 0 END) AS GhostStudentCount,
    {reconCountsSql}
FROM #R67Results;

-- RS 3: preview / full rows
SELECT {top}
    1 AS Control_Sort, 'Control_1' AS Control_Type,
    'CONTROL 1: CREG [{cregTable}].[{cregStudentCol}]+[{cregQualCol}] pair in STUD with [{cregE051Col}] filter' AS Control_Label,
    ValidationResult,
    CASE WHEN ValidationResult = 'PASS'
        THEN 'PASS: CREG pair found in STUD and E051 code matches.'
        ELSE 'FAIL (00708): ' + ISNULL(FailReason, 'Validation failed.')
    END AS Validation_Explanation,
    CREG_STUD_NO, CREG_QUAL, CREG_E051, STUD_NO, STUD_QUAL, IN_STUD, STUD_STUDENT_EXISTS, GHOST_STUDENT, GHOST_STUDENT_NOTE, E051_VALID, FailReason,
    CASE WHEN ValidationResult = 'FAIL' THEN '00708' ELSE '' END AS Exception_Code,
    Reconciliation_Status
FROM #R67Results
ORDER BY {orderBy};
{rule29OnlySql}

DROP TABLE IF EXISTS #R67CregPairs;
DROP TABLE IF EXISTS #R67StudPairs;
DROP TABLE IF EXISTS #R67StudNos;
DROP TABLE IF EXISTS #R67Results;
DROP TABLE IF EXISTS #R67CregStudNos;
{detailDropSql}";
        }

        // BuildSourceCtes is retained only for the standalone GenerateSqlAsync / Export path.
        private static string BuildSourceCtes(string cregTable, string studTable, string cregStudentCol, string cregQualCol, string cregE051Col, string studStudentCol, string studQualCol, IReadOnlyList<string> e051Values)
        {
            var e051ValidExpr = e051Values.Count > 0
                ? $"CASE WHEN CP.CREG_E051 IN ({BuildInClauseSql(e051Values)}) THEN 'Yes' ELSE 'No' END"
                : "'Yes'";
            var passExpr = e051Values.Count > 0
                ? $"SP.STUD_NO IS NOT NULL AND CP.CREG_E051 IN ({BuildInClauseSql(e051Values)})"
                : "SP.STUD_NO IS NOT NULL";
            var failReasonExpr = e051Values.Count > 0
                ? $"CASE WHEN SP.STUD_NO IS NULL THEN 'Not found in STUD' WHEN CP.CREG_E051 NOT IN ({BuildInClauseSql(e051Values)}) THEN 'E051 code not in expected values' ELSE '' END"
                : "CASE WHEN SP.STUD_NO IS NULL THEN 'Not found in STUD' ELSE '' END";
            const string studStudentExistsExpr = "CASE WHEN SSN.STUD_STUDENT_NO IS NOT NULL THEN 'Yes' ELSE 'No' END";
            const string ghostStudentExpr = "CASE WHEN SSN.STUD_STUDENT_NO IS NULL THEN 'Yes' ELSE 'No' END";
            const string ghostStudentNoteExpr = "CASE WHEN SSN.STUD_STUDENT_NO IS NULL THEN 'Ghost student - tested in Rule 9.' WHEN SP.STUD_NO IS NULL THEN 'Student found in STUD with different qualification code' ELSE '' END";

            return $@"
WITH CregPairs AS
(
    SELECT DISTINCT
        CONVERT(nvarchar(255), C.[{cregStudentCol}]) AS CREG_STUD_NO,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregQualCol}])))) AS CREG_QUAL,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregE051Col}])))) AS CREG_E051
    FROM [{cregTable}] C
    WHERE C.[{cregStudentCol}] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregStudentCol}]))) <> ''
      AND C.[{cregQualCol}] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), C.[{cregQualCol}]))) <> ''
),
StudPairs AS
(
    SELECT DISTINCT
        CONVERT(nvarchar(255), S.[{studStudentCol}]) AS STUD_NO,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), S.[{studQualCol}])))) AS STUD_QUAL
    FROM [{studTable}] S
    WHERE S.[{studStudentCol}] IS NOT NULL
      AND S.[{studQualCol}] IS NOT NULL
),
StudStudents AS
(
    SELECT DISTINCT
        CONVERT(nvarchar(255), S.[{studStudentCol}]) AS STUD_STUDENT_NO
    FROM [{studTable}] S
    WHERE S.[{studStudentCol}] IS NOT NULL
),
StudFirstQual AS
(
    SELECT STUD_NO, MIN(STUD_QUAL) AS STUD_ACTUAL_QUAL
    FROM StudPairs
    GROUP BY STUD_NO
),
ValidationResults AS
(
    SELECT
        CP.CREG_STUD_NO,
        CP.CREG_QUAL,
        CP.CREG_E051,
        CASE WHEN SP.STUD_NO   IS NOT NULL THEN SP.STUD_NO
             WHEN SSN.STUD_STUDENT_NO IS NOT NULL THEN CP.CREG_STUD_NO
             ELSE NULL END AS STUD_NO,
        CASE WHEN SP.STUD_QUAL IS NOT NULL THEN SP.STUD_QUAL
             WHEN SSN.STUD_STUDENT_NO IS NOT NULL THEN SPF.STUD_ACTUAL_QUAL
             ELSE NULL END AS STUD_QUAL,
        CASE WHEN SP.STUD_NO IS NOT NULL THEN 'Yes' ELSE 'No' END AS IN_STUD,
        {studStudentExistsExpr} AS STUD_STUDENT_EXISTS,
        {ghostStudentExpr} AS GHOST_STUDENT,
        {ghostStudentNoteExpr} AS GHOST_STUDENT_NOTE,
        {e051ValidExpr} AS E051_VALID,
        CASE WHEN {passExpr} THEN 'PASS' ELSE 'FAIL' END AS ValidationResult,
        {failReasonExpr} AS FailReason
    FROM CregPairs CP
    LEFT JOIN StudPairs SP
        ON SP.STUD_NO  = CP.CREG_STUD_NO
       AND SP.STUD_QUAL = CP.CREG_QUAL
    LEFT JOIN StudStudents SSN
        ON SSN.STUD_STUDENT_NO = CP.CREG_STUD_NO
    LEFT JOIN StudFirstQual SPF
        ON SPF.STUD_NO = CP.CREG_STUD_NO
)";
        }

        private static void EnrichDisplayValues(Rule67ValidationRowRecord row)
        {
            var v      = row.DisplayValues;
            var isPass = string.Equals(ReadValue(v, "ValidationResult"), "PASS", StringComparison.OrdinalIgnoreCase);
            var studNo = ReadValue(v, "CREG_STUD_NO");
            var qual   = ReadValue(v, "CREG_QUAL");
            var e051   = ReadValue(v, "CREG_E051");
            var reason = ReadValue(v, "FailReason");
            var ghost  = string.Equals(ReadValue(v, "GHOST_STUDENT"), "Yes", StringComparison.OrdinalIgnoreCase);
            var studStudentExists = string.Equals(ReadValue(v, "STUD_STUDENT_EXISTS"), "Yes", StringComparison.OrdinalIgnoreCase);
            var ghostNote = ReadValue(v, "GHOST_STUDENT_NOTE");

            v["FINAL_RESULT_MESSAGE"] = isPass
                ? $"PASS: CREG pair '{studNo}' / '{qual}' (E051: {e051}) found in STUD with valid E051 code."
                : reason switch
                {
                    "Not found in STUD" when ghost
                        => $"FAIL (00708): CREG pair '{studNo}' / '{qual}' (E051: {e051}) - student number not found in STUD. Marked as Ghost Student. {ghostNote}",
                    "Not found in STUD" when studStudentExists
                        => $"FAIL (00708): CREG pair '{studNo}' / '{qual}' (E051: {e051}) - student number exists in STUD, but the qualification pair was not found.",
                    _
                        => $"FAIL (00708): CREG pair '{studNo}' / '{qual}' (E051: {e051}) - {reason}."
                };
            row.ValidationExplanation = ReadValue(v, "FINAL_RESULT_MESSAGE");
        }

        // ─── Save / Persist ───────────────────────────────────────────────────

        private async Task<int> SaveValidationRunAsync(Rule67ValidationRequest request, Rule67ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 67);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue) throw new InvalidOperationException("The current analyst could not be resolved.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 67);
            var failRows     = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID,UserID,RuleNumber,RuleName,Status,TotalRecords,PassCount,FailCount,ExceptionRate,RunTimestamp,
 HemisServer,AuditDatabase,StudTable,DeceasedTable,StudColumn,DeceasedColumn,
 ExceptionsJSON,ResultsJSON,RunByUserName,LastEditedByUserName,LastEditedAt,PreviousHash,RecordHash,WorkspaceSavedAt,IsCurrent)
VALUES
(@ClientID,@UserID,67,@RuleName,@Status,@TotalRecords,@PassCount,@FailCount,@ExceptionRate,GETDATE(),
 @HemisServer,@AuditDatabase,@StudTable,@DeceasedTable,@StudColumn,@DeceasedColumn,
 @ExceptionsJSON,@ResultsJSON,@RunByUserName,NULL,NULL,@PreviousHash,NULL,@WorkspaceSavedAt,1);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            cmd.Parameters.AddWithValue("@ClientID", request.ClientId);
            cmd.Parameters.AddWithValue("@UserID", systemUserId.Value);
            cmd.Parameters.AddWithValue("@RuleName", "CREG-STUD Pair Validation");
            cmd.Parameters.AddWithValue("@Status", summary.Status);
            cmd.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            cmd.Parameters.AddWithValue("@PassCount", summary.PassCount);
            cmd.Parameters.AddWithValue("@FailCount", summary.FailCount);
            cmd.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            cmd.Parameters.AddWithValue("@HemisServer", request.Server);
            cmd.Parameters.AddWithValue("@AuditDatabase", request.Database);
            cmd.Parameters.AddWithValue("@StudTable", request.CregTable);       // StudTable column → CregTable
            cmd.Parameters.AddWithValue("@DeceasedTable", request.StudTable);   // DeceasedTable column → StudTable
            cmd.Parameters.AddWithValue("@StudColumn", request.CregStudentNoCol);
            cmd.Parameters.AddWithValue("@DeceasedColumn", request.StudStudentNoCol);
            cmd.Parameters.AddWithValue("@ExceptionsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(failRows)));
            cmd.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            cmd.Parameters.AddWithValue("@RunByUserName", (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PreviousHash", (object?)previousHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@WorkspaceSavedAt", markWorkspaceSaved ? DateTime.UtcNow : (object)DBNull.Value);

            var runId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCmd = connection.CreateConfiguredCommand();
            hashCmd.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash=@RecordHash WHERE RunID=@RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule67|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            await UpdateStoredSummaryAsync(connection, runId, summary);
            return runId;
        }

        // ─── Preview / Clone ──────────────────────────────────────────────────

        private static void ApplyBrowserPreview(Rule67ValidationSummary summary)
        {
            // Cap Rule29Only rows
            if (summary.Rule29OnlyRows.Count > BrowserPreviewRowLimit)
                summary.Rule29OnlyRows = summary.Rule29OnlyRows.Take(BrowserPreviewRowLimit).ToList();

            var rows = summary.ReviewRows;
            if (rows.Count <= BrowserPreviewRowLimit) { summary.DisplayedCount = rows.Count; summary.IsPreviewOnly = false; summary.PreviewLimit = 0; return; }

            var failRows = rows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.ValidationNumber).ToList();
            var passRows = rows.Where(r => !string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.ValidationNumber).ToList();

            int halfLimit = BrowserPreviewRowLimit / 2;
            int failTake  = Math.Min(failRows.Count, passRows.Count > 0 ? halfLimit : BrowserPreviewRowLimit);
            int passTake  = Math.Min(passRows.Count, BrowserPreviewRowLimit - failTake);

            var preview = failRows.Take(failTake).Concat(passRows.Take(passTake)).ToList();
            summary.ReviewRows     = preview;
            summary.DisplayedCount = preview.Count;
            summary.IsPreviewOnly  = summary.TotalValidated > preview.Count;
            summary.PreviewLimit   = preview.Count;
        }

        private static Rule67ValidationSummary CloneSummary(Rule67ValidationSummary s) => new()
        {
            Success = s.Success, CregRecordCount = s.CregRecordCount, StudRecordCount = s.StudRecordCount,
            TotalValidated = s.TotalValidated, DisplayedCount = s.DisplayedCount, IsPreviewOnly = s.IsPreviewOnly, PreviewLimit = s.PreviewLimit,
            PassCount = s.PassCount, FailCount = s.FailCount, NotInStudCount = s.NotInStudCount, NotInStudE051ValidCount = s.NotInStudE051ValidCount, NotInStudE051InvalidCount = s.NotInStudE051InvalidCount, GhostStudentCount = s.GhostStudentCount, InvalidE051Count = s.InvalidE051Count,
            ExceptionRate = s.ExceptionRate, Status = s.Status, Timestamp = s.Timestamp, Database = s.Database,
            CregTable = s.CregTable, StudTable = s.StudTable,
            CregStudentNoCol = s.CregStudentNoCol, CregQualCol = s.CregQualCol, CregE051Col = s.CregE051Col,
            StudStudentNoCol = s.StudStudentNoCol, StudQualCol = s.StudQualCol, E051FilterValues = s.E051FilterValues,
            DetailTable = s.DetailTable, DetailErrorCode = s.DetailErrorCode, DetailErrorCol = s.DetailErrorCol, DetailElementInfoCol = s.DetailElementInfoCol,
            TableLinkageText = s.TableLinkageText, RuleModeText = s.RuleModeText, ProcedureSteps = s.ProcedureSteps.ToList(),
            ClientId = s.ClientId, SavedRunId = s.SavedRunId,
            ControlSummaries = s.ControlSummaries.Select(i => new Rule67ControlSummaryItemViewModel
            {
                ControlType = i.ControlType, ControlLabel = i.ControlLabel, CriteriaText = i.CriteriaText,
                TotalCount = i.TotalCount, PassCount = i.PassCount, FailCount = i.FailCount, Status = i.Status
            }).ToList(),
            ReviewRows = s.ReviewRows.Select(r => new Rule67ValidationRowRecord
            {
                ValidationNumber = r.ValidationNumber, ControlType = r.ControlType, ControlLabel = r.ControlLabel,
                ValidationResult = r.ValidationResult, ValidationExplanation = r.ValidationExplanation,
                ExceptionCode = r.ExceptionCode,
                DisplayValues = new Dictionary<string, string?>(r.DisplayValues, StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            DetailRecordCount = s.DetailRecordCount, ConfirmedByRule29Count = s.ConfirmedByRule29Count,
            NotInRule29Count = s.NotInRule29Count, Rule29OnlyCount = s.Rule29OnlyCount,
            Rule29OnlyRows = s.Rule29OnlyRows.Select(r => new Rule67Rule29OnlyRow
            {
                RowNumber = r.RowNumber, StudentNo = r.StudentNo, QualCode = r.QualCode,
                ErrorMessage = r.ErrorMessage, ConfirmedByR67 = r.ConfirmedByR67
            }).ToList(),
            Warning = s.Warning, Error = s.Error
        };

        private static Rule67ValidationRequest CloneRequest(Rule67ValidationRequest r) => new()
        {
            ClientId = r.ClientId, RunId = r.RunId, Server = r.Server, Database = r.Database, Driver = r.Driver,
            CregTable = r.CregTable, StudTable = r.StudTable,
            CregStudentNoCol = r.CregStudentNoCol, CregQualCol = r.CregQualCol, CregE051Col = r.CregE051Col,
            StudStudentNoCol = r.StudStudentNoCol, StudQualCol = r.StudQualCol, E051FilterValues = r.E051FilterValues,
            DetailTable = r.DetailTable, DetailErrorCode = r.DetailErrorCode,
            DetailErrorCol = r.DetailErrorCol, DetailElementInfoCol = r.DetailElementInfoCol
        };

        private static bool RequestsMatch(Rule67ValidationRequest a, Rule67ValidationRequest b) =>
            a.ClientId == b.ClientId &&
            string.Equals(a.Server?.Trim(), b.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Database?.Trim(), b.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.CregTable?.Trim(), b.CregTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.StudTable?.Trim(), b.StudTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.CregE051Col?.Trim(), b.CregE051Col?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.E051FilterValues?.Trim(), b.E051FilterValues?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static List<Rule67ValidationRowRecord> NormalizeRows(IEnumerable<Rule67ValidationRowRecord> rows) =>
            rows.Select((r, i) => { r.ValidationNumber = i + 1; return r; }).ToList();

        // ─── Expand Saved Summary ─────────────────────────────────────────────

        private async Task<Rule67ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule67ValidationSummary summary, string? server)
        {
            if (!summary.IsPreviewOnly && summary.ReviewRows.Count >= summary.TotalValidated) return summary;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(summary.Database) || string.IsNullOrWhiteSpace(summary.CregTable) || string.IsNullOrWhiteSpace(summary.StudTable)) return summary;
            try
            {
                var expanded = await AnalyseAsync(new Rule67ValidationRequest
                {
                    ClientId = summary.ClientId, RunId = summary.SavedRunId, Server = server, Database = summary.Database,
                    Driver = "ODBC Driver 17 for SQL Server", CregTable = summary.CregTable, StudTable = summary.StudTable,
                    CregStudentNoCol = summary.CregStudentNoCol, CregQualCol = summary.CregQualCol, CregE051Col = summary.CregE051Col,
                    StudStudentNoCol = summary.StudStudentNoCol, StudQualCol = summary.StudQualCol, E051FilterValues = summary.E051FilterValues
                }, includeAllReviewRows: true);
                expanded.Timestamp  = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId   = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                return expanded;
            }
            catch { return summary; }
        }

        private async Task<Rule67ValidationSummary> ExpandAndPersistSavedSummaryIfNeededAsync(SqlConnection connection, int runId, Rule67ValidationSummary summary, string? server)
        {
            var expanded = await ExpandSavedSummaryIfNeededAsync(summary, server);
            if (!ReferenceEquals(expanded, summary)) { expanded.SavedRunId = runId; await UpdateStoredSummaryAsync(connection, runId, expanded); }
            return expanded;
        }

        // ─── DB Helpers ───────────────────────────────────────────────────────

        private static void ValidateRequest(Rule67ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server)) throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database)) throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable)) throw new InvalidOperationException("CREG table is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable)) throw new InvalidOperationException("STUD table is required.");
            ValidateObjectName(request.CregTable); ValidateObjectName(request.StudTable);
            if (!string.IsNullOrWhiteSpace(request.CregE051Col)) ValidateObjectName(request.CregE051Col);
        }

        private static void ValidateRequest(Rule67VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server)) throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database)) throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable)) throw new InvalidOperationException("CREG table is required.");
            if (string.IsNullOrWhiteSpace(request.StudTable)) throw new InvalidOperationException("STUD table is required.");
            ValidateObjectName(request.CregTable); ValidateObjectName(request.StudTable);
        }

        private static List<string> ParseFilterValues(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitiseFilterValue)
                .Where(v => v.Length > 0)
                .Select(v => v.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string SanitiseFilterValue(string value) =>
            value.Replace("'", "").Replace(";", "").Replace("--", "").Replace("[", "").Replace("]", "").Trim();

        private static string BuildInClauseSql(IReadOnlyList<string> values) =>
            string.Join(",", values.Select(v => $"'{v}'"));

        // Parse comma-separated error codes like "00708,00159" → [708, 159].
        // Normalises leading zeros so "00708" == "708" in the IN clause.
        private static IReadOnlyList<long> ParseDetailErrorCodes(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new[] { 708L };
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => { return long.TryParse(s.TrimStart('0') == "" ? "0" : s.TrimStart('0'), out var n) ? n : (long?)null; })
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .ToList();
        }

        private static string BuildDetailErrInSql(IReadOnlyList<long> codes) =>
            codes.Count == 0 ? "0" : string.Join(",", codes);

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Table or column name is required.");
            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
                if (value.Contains(bad, StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe table or column name.");
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var match = values.FirstOrDefault(c => c.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match)) return match;
            }
            foreach (var fragment in containsMatches)
            {
                var match = values.FirstOrDefault(c => c.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match)) return match;
            }
            return values.FirstOrDefault();
        }

        private static string Sanitise(string name) => name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();
        private static string BuildConnectionString(string server, string database, string driver) =>
            $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        private static string ReadValue(Dictionary<string, string?> d, string key) =>
            d.TryGetValue(key, out var v) ? v ?? "" : "";

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);
            var builder  = new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = database, IntegratedSecurity = true, TrustServerCertificate = trust, Encrypt = false, ConnectTimeout = 180 };
            var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private static async Task<int> CountAsync(SqlConnection connection, string sql)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToInt32(val);
        }

        private static async Task<int?> GetSystemUserIdByEmailAsync(SqlConnection connection, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email=@Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToInt32(val);
        }

        private static async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments WHERE ClientID=@ClientID AND UserID=@UserID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId); cmd.Parameters.AddWithValue("@UserID", userId);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToString(val);
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ValidationRuns WHERE RunID=@RunID AND (WorkspaceSavedAt IS NOT NULL OR EXISTS(SELECT 1 FROM dbo.ReviewSignoffs rs WHERE rs.RunID=ValidationRuns.RunID AND rs.SignoffRole='DataAnalyst'))) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole=@SignoffRole) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId); cmd.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<string?> GetValidationRecordHashAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToString(val);
        }

        private static async Task<string?> GetLatestValidationHashAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID=@ClientID AND RuleNumber=@RuleNumber AND RecordHash IS NOT NULL ORDER BY RunTimestamp DESC, RunID DESC;";
            cmd.Parameters.AddWithValue("@ClientID", clientId); cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            var val = await cmd.ExecuteScalarAsync();
            return val == null || val == DBNull.Value ? null : Convert.ToString(val);
        }

        private async Task EnsureClientNotArchivedAsync(SqlConnection connection, int clientId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID=@ClientID;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);
            if (string.Equals(Convert.ToString(await cmd.ExecuteScalarAsync()), "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private static async Task<int> ClearSignoffsAndFlagForReviewAsync(SqlConnection connection, int runId)
        {
            await using var countCmd = connection.CreateConfiguredCommand();
            countCmd.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID=@RunID;";
            countCmd.Parameters.AddWithValue("@RunID", runId);
            var existing = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            await using var delCmd = connection.CreateConfiguredCommand();
            delCmd.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID=@RunID;";
            delCmd.Parameters.AddWithValue("@RunID", runId);
            await delCmd.ExecuteNonQueryAsync();
            await using var updCmd = connection.CreateConfiguredCommand();
            updCmd.CommandText = "UPDATE dbo.ValidationRuns SET Status='Needs Review' WHERE RunID=@RunID;";
            updCmd.Parameters.AddWithValue("@RunID", runId);
            await updCmd.ExecuteNonQueryAsync();
            return existing;
        }

        private async Task UpdateRunStatusFromSignoffsAsync(SqlConnection connection, int runId)
        {
            var hasAll = await HasAllRequiredSignoffsAsync(connection, runId);
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET Status=@Status WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId); cmd.Parameters.AddWithValue("@Status", hasAll ? "Reviewed and Completed" : "Needs Review");
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<bool> HasAllRequiredSignoffsAsync(SqlConnection connection, int runId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"SELECT
                CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='DataAnalyst') THEN 1 ELSE 0 END,
                CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='Manager') THEN 1 ELSE 0 END,
                CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='Director') THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return false;
            return reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1;
        }

        private async Task MarkPreviousRunsHistoricalAsync(SqlConnection connection, int clientId, int ruleNumber)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent=0 WHERE ClientID=@ClientID AND RuleNumber=@RuleNumber AND IsCurrent=1;";
            cmd.Parameters.AddWithValue("@ClientID", clientId); cmd.Parameters.AddWithValue("@RuleNumber", ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<List<RunSignoffViewModel>> GetRunSignoffsAsync(SqlConnection connection, int runId, int? currentUserId)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT rs.SignoffID, ISNULL(rs.SignoffRole,'') AS SignoffRole,
       LTRIM(RTRIM(ISNULL(u.FirstName,'')+' '+ISNULL(u.LastName,''))) AS ReviewerName,
       ISNULL(u.Email,'') AS ReviewerEmail, ISNULL(rs.Comment,'') AS Comment,
       rs.SignedOffAt,
       CASE WHEN @CurrentUserID IS NOT NULL AND rs.ReviewerID=@CurrentUserID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsCurrentUser
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID=rs.ReviewerID
WHERE rs.RunID=@RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@CurrentUserID", currentUserId.HasValue ? currentUserId.Value : DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync();
            var signoffs = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
                signoffs.Add(new RunSignoffViewModel
                {
                    Id = reader.GetInt32(0), SignoffRole = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ReviewerName = reader.IsDBNull(2) ? "" : reader.GetString(2), ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comment = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SignedOffAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6)
                });
            return signoffs;
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule67ValidationSummary summary)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET ResultsJSON=@ResultsJSON WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            await cmd.ExecuteNonQueryAsync();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static Rule67ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { var decoded = ValidationPayloadCodec.Decode(json); return string.IsNullOrWhiteSpace(decoded) ? null : JsonConvert.DeserializeObject<Rule67ValidationSummary>(decoded); }
            catch { return null; }
        }

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)));
        }
    }
}
