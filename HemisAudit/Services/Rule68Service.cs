using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule68Service : IRule68Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        private static readonly long[] DefaultExclusionCodes = { 2202L, 2301L, 2302L, 708L, 7201L, 1501L };
        private const long TargetErrorCode = 3603L; // 03603

        public Rule68Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule68TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                return new Rule68TableDiscoveryResult
                {
                    Success        = true,
                    Tables         = tables,
                    AutoStudTable  = FindFirst(tables, ["dbo_STUD"],                    ["stud"]),
                    AutoCregTable  = FindFirst(tables, ["dbo_CREG"],                    ["creg"]),
                    AutoQualTable  = FindFirst(tables, ["dbo_QUAL"],                    ["qual"]),
                    AutoCredTable  = FindFirst(tables, ["dbo_CRED"],                    ["cred"]),
                    AutoCrseTable  = FindFirst(tables, ["dbo_CRSE"],                    ["crse"]),
                    AutoDetailTable= FindFirst(tables, ["dbo_STUD_VALIDATION_DETAIL"],  ["stud_validation", "validation_detail"])
                };
            }
            catch (Exception ex) { return new Rule68TableDiscoveryResult { Success = false, Error = ex.Message }; }
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

        public async Task<Rule68VerifyResult> VerifyTablesAsync(Rule68VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                return new Rule68VerifyResult
                {
                    Success          = true,
                    StudRecordCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.StudTable)}];"),
                    CregRecordCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.CregTable)}];"),
                    QualRecordCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.QualTable)}];"),
                    CredRecordCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.CredTable)}];"),
                    CrseRecordCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM [{Sanitise(request.CrseTable)}];"),
                };
            }
            catch (Exception ex) { return new Rule68VerifyResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule68ValidationSummary> RunValidationAsync(Rule68ValidationRequest request, string? userEmail = null, string? userName = null)
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
                            _pendingValidationCache.ClearPending(68, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(68, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);
                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full population. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Rule 68 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(browserSummary);
                return browserSummary;
            }
            catch (Exception ex) { return new Rule68ValidationSummary { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule68ValidationSummary> GetExportSummaryAsync(Rule68ValidationRequest request)
        {
            ValidateRequest(request);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule68ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule68ValidationRequest, Rule68ValidationSummary>(68, clientId, reviewerEmail);
            if (pending == null) return Task.FromResult<Rule68ValidationSummary?>(null);
            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 68 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule68ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(68, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule68WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer,'')  AS HemisServer,
    ISNULL(vr.AuditDatabase,'') AS AuditDatabase,
    ISNULL(vr.StudTable,'')    AS CregTable,
    ISNULL(vr.DeceasedTable,'') AS StudTable,
    ISNULL(vr.Status,'')       AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 68 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var runId                = reader.GetInt32(0);
            var workspaceClientId    = reader.GetInt32(1);
            var server               = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database             = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var cregTable            = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var studTable            = reader.IsDBNull(5) ? "dbo_STUD" : reader.GetString(5);
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

            var workspace = new Rule68WorkspaceStateViewModel
            {
                ClientId             = workspaceClientId,
                RunId                = runId,
                Server               = server,
                Database             = database,
                CregTable            = deserializedSummary?.CregTable  ?? cregTable,
                StudTable            = deserializedSummary?.StudTable   ?? studTable,
                QualTable            = deserializedSummary?.QualTable   ?? "dbo_QUAL",
                CredTable            = deserializedSummary?.CredTable   ?? "dbo_CRED",
                CrseTable            = deserializedSummary?.CrseTable   ?? "dbo_CRSE",
                DetailTable          = deserializedSummary?.DetailTable  ?? "dbo_STUD_VALIDATION_DETAIL",
                CregStudNoCol        = deserializedSummary?.CregStudNoCol  ?? "_007",
                CregQualCol          = deserializedSummary?.CregQualCol   ?? "_001",
                CregCourseCol        = deserializedSummary?.CregCourseCol ?? "_030",
                QualQualCol          = deserializedSummary?.QualQualCol   ?? "_001",
                QualNameCol          = deserializedSummary?.QualNameCol   ?? "_003",
                CredQualCol          = deserializedSummary?.CredQualCol   ?? "_001",
                CredCourseCol        = deserializedSummary?.CredCourseCol ?? "_030",
                CredCreditsCol       = deserializedSummary?.CredCreditsCol ?? "_036",
                CrseCourseCol        = deserializedSummary?.CrseCourseCol ?? "_030",
                CrseNameCol          = deserializedSummary?.CrseNameCol   ?? "_058",
                DetailErrorTypeCol   = deserializedSummary?.DetailErrorTypeCol   ?? "",
                DetailErrorCol       = deserializedSummary?.DetailErrorCol       ?? "Error",
                DetailErrorTypeValue = deserializedSummary?.DetailErrorTypeValue ?? "Fatal",
                DetailExclusionCodes = deserializedSummary?.DetailExclusionCodes ?? "02202,02301,02302,00708,07201,01501",
                DetailElementInfoCol = deserializedSummary?.DetailElementInfoCol ?? "Element_Information",
                MaxTotalCredits      = deserializedSummary?.MaxTotalCredits ?? 1.0m,
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

        public async Task<Rule68RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 68;";
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

            var review = new Rule68RunReviewViewModel
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

        public async Task<Rule68WorkspaceSaveResult> SaveWorkspaceAsync(Rule68ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId.HasValue && request.RunId.Value > 0)
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                    if (!clientId.HasValue || clientId.Value != request.ClientId)
                        return new Rule68WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

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
                    cmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule68|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await cmd.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(68, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule68WorkspaceSaveResult { Success = true, Message = clearedSignoffs > 0 ? "Workspace saved. Existing signoffs were removed." : "Workspace saved and marked for review.", SignoffsCleared = clearedSignoffs > 0, ClearedSignoffCount = clearedSignoffs, Workspace = currentWorkspace };
                }

                var pending = _pendingValidationCache.GetPending<Rule68ValidationRequest, Rule68ValidationSummary>(68, request.ClientId, reviewerEmail);
                if (pending == null)
                    return new Rule68WorkspaceSaveResult { Success = false, Error = "Run Rule 68 first so the current workspace is written to the system database." };

                if (!RequestsMatch(request, pending.Request))
                    return new Rule68WorkspaceSaveResult { Success = false, Error = "Workspace settings changed after validation. Run Rule 68 again before saving." };

                var summaryToSave = CloneSummary(pending.Summary);
                if (summaryToSave.IsPreviewOnly || summaryToSave.ReviewRows.Count < summaryToSave.TotalValidated)
                    summaryToSave = await AnalyseAsync(pending.Request, includeAllReviewRows: true);

                summaryToSave.SavedRunId = null;
                var savedRunId = await SaveValidationRunAsync(CloneRequest(pending.Request), summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(68, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule68WorkspaceSaveResult { Success = true, Message = $"Workspace saved as Run #{savedRunId}.", SignoffsCleared = false, ClearedSignoffCount = 0, Workspace = workspace };
            }
            catch (Exception ex) { return new Rule68WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule68WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue) return new Rule68WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

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
                cmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginEdit|Rule68|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await cmd.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(68, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule68WorkspaceSaveResult { Success = true, Message = clearedSignoffs > 0 ? "Editing begun. Existing signoffs removed." : "Editing begun. Save the workspace when ready.", SignoffsCleared = clearedSignoffs > 0, ClearedSignoffCount = clearedSignoffs, Workspace = workspace };
            }
            catch (Exception ex) { return new Rule68WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue) throw new InvalidOperationException("The reviewer could not be resolved.");
            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue) throw new InvalidOperationException("The selected Rule 68 run could not be found.");
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
            if (!clientId.HasValue) throw new InvalidOperationException("The selected Rule 68 run could not be found.");
            await EnsureClientNotArchivedAsync(connection, clientId.Value);
            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff.");
            await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
        }

        public Task<string> GenerateSqlAsync(Rule68ValidationRequest request)
        {
            ValidateRequest(request);
            var maxTotalCredits = NormalizeThreshold(request.MaxTotalCredits);
            var thresholdText = maxTotalCredits.ToString(CultureInfo.InvariantCulture);
            var cregTable      = Sanitise(request.CregTable);
            var studTable      = Sanitise(request.StudTable);
            var qualTable      = Sanitise(request.QualTable);
            var credTable      = Sanitise(request.CredTable);
            var crseTable      = Sanitise(request.CrseTable);
            var cregStudNoCol  = Sanitise(string.IsNullOrWhiteSpace(request.CregStudNoCol)  ? "_007" : request.CregStudNoCol);
            var cregQualCol    = Sanitise(string.IsNullOrWhiteSpace(request.CregQualCol)    ? "_001" : request.CregQualCol);
            var cregCourseCol  = Sanitise(string.IsNullOrWhiteSpace(request.CregCourseCol)  ? "_030" : request.CregCourseCol);
            var qualQualCol    = Sanitise(string.IsNullOrWhiteSpace(request.QualQualCol)    ? "_001" : request.QualQualCol);
            var qualNameCol    = Sanitise(string.IsNullOrWhiteSpace(request.QualNameCol)    ? "_003" : request.QualNameCol);
            var credQualCol    = Sanitise(string.IsNullOrWhiteSpace(request.CredQualCol)    ? "_001" : request.CredQualCol);
            var credCourseCol  = Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol)  ? "_030" : request.CredCourseCol);
            var credCreditsCol = Sanitise(string.IsNullOrWhiteSpace(request.CredCreditsCol) ? "_036" : request.CredCreditsCol);
            var crseCourseCol  = Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol)  ? "_030" : request.CrseCourseCol);
            var crseNameCol    = Sanitise(string.IsNullOrWhiteSpace(request.CrseNameCol)    ? "_058" : request.CrseNameCol);

            var sql = $@"-- HEMIS RULE 68: CREDIT OVERLOAD VALIDATION
-- Check: SUM of CRED.[{credCreditsCol}] per student/qualification must be <= {thresholdText}
-- Error code 03603 when total credits exceed {thresholdText}
-- Tables: {request.StudTable}, {request.CregTable}, {request.QualTable}, {request.CredTable}, {request.CrseTable}

WITH CreditBase AS
(
    SELECT
        LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregStudNoCol}])))        AS Student_No,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregQualCol}]))))   AS Qual_Code,
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregCourseCol}]))))  AS Course_Code,
        ISNULL(TRY_CAST(CR.[{credCreditsCol}] AS decimal(18,4)), 0)       AS Credits,
        ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(255), CS.[{crseNameCol}]))), '')  AS Course_Name,
        ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qualNameCol}]))), '') AS Qual_Name
    FROM [{cregTable}] CG
    LEFT JOIN [{qualTable}] Q  ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qualQualCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregQualCol}]))))
    LEFT JOIN [{credTable}] CR ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CR.[{credCourseCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregCourseCol}]))))
                                AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CR.[{credQualCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregQualCol}]))))
    LEFT JOIN [{crseTable}] CS ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CS.[{crseCourseCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregCourseCol}]))))
    WHERE CG.[{cregStudNoCol}] IS NOT NULL
      AND LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregStudNoCol}]))) <> ''
      AND CG.[{cregQualCol}] IS NOT NULL
),
CreditSummary AS
(
    SELECT
        Student_No, Qual_Code, MAX(Qual_Name) AS Qual_Name,
        COUNT(DISTINCT Course_Code) AS Course_Count,
        CAST(SUM(Credits) AS decimal(18,4)) AS Total_Credits
    FROM CreditBase
    GROUP BY Student_No, Qual_Code
)
SELECT
    Student_No, Qual_Code, Qual_Name, Course_Count, Total_Credits,
    CASE WHEN Total_Credits > {thresholdText} THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
    CASE WHEN Total_Credits > {thresholdText} THEN '03603' ELSE '' END AS Error_Code
FROM CreditSummary
ORDER BY CASE WHEN Total_Credits > {thresholdText} THEN 0 ELSE 1 END, Total_Credits DESC, Student_No, Qual_Code;

-- Summary counts
SELECT
    COUNT(1) AS Total_Validated,
    SUM(CASE WHEN Total_Credits <= {thresholdText} THEN 1 ELSE 0 END) AS Pass_Count,
    SUM(CASE WHEN Total_Credits  > {thresholdText} THEN 1 ELSE 0 END) AS Fail_Count,
    SUM(CASE WHEN Total_Credits  > {thresholdText} THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(1), 0) AS Exception_Rate
FROM
(
    SELECT
        Student_No, Qual_Code,
        CAST(SUM(Credits) AS decimal(18,4)) AS Total_Credits
    FROM CreditBase
    GROUP BY Student_No, Qual_Code
) AS Agg;";

            return Task.FromResult(sql.Trim());
        }

        // ─── Core Analysis ────────────────────────────────────────────────────

        private async Task<Rule68ValidationSummary> AnalyseAsync(Rule68ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var cregTable      = Sanitise(request.CregTable);
            var studTable      = Sanitise(string.IsNullOrWhiteSpace(request.StudTable)  ? "dbo_STUD" : request.StudTable);
            var qualTable      = Sanitise(string.IsNullOrWhiteSpace(request.QualTable)  ? "dbo_QUAL" : request.QualTable);
            var credTable      = Sanitise(string.IsNullOrWhiteSpace(request.CredTable)  ? "dbo_CRED" : request.CredTable);
            var crseTable      = Sanitise(string.IsNullOrWhiteSpace(request.CrseTable)  ? "dbo_CRSE" : request.CrseTable);
            var detailTable    = Sanitise(request.DetailTable ?? "");
            var cregStudNoCol  = Sanitise(string.IsNullOrWhiteSpace(request.CregStudNoCol)  ? "_007" : request.CregStudNoCol);
            var cregQualCol    = Sanitise(string.IsNullOrWhiteSpace(request.CregQualCol)    ? "_001" : request.CregQualCol);
            var cregCourseCol  = Sanitise(string.IsNullOrWhiteSpace(request.CregCourseCol)  ? "_030" : request.CregCourseCol);
            var qualQualCol    = Sanitise(string.IsNullOrWhiteSpace(request.QualQualCol)    ? "_001" : request.QualQualCol);
            var qualNameCol    = Sanitise(string.IsNullOrWhiteSpace(request.QualNameCol)    ? "_003" : request.QualNameCol);
            var credQualCol    = Sanitise(string.IsNullOrWhiteSpace(request.CredQualCol)    ? "_001" : request.CredQualCol);
            var credCourseCol  = Sanitise(string.IsNullOrWhiteSpace(request.CredCourseCol)  ? "_030" : request.CredCourseCol);
            var credCreditsCol = Sanitise(string.IsNullOrWhiteSpace(request.CredCreditsCol) ? "_036" : request.CredCreditsCol);
            var crseCourseCol  = Sanitise(string.IsNullOrWhiteSpace(request.CrseCourseCol)  ? "_030" : request.CrseCourseCol);
            var crseNameCol    = Sanitise(string.IsNullOrWhiteSpace(request.CrseNameCol)    ? "_058" : request.CrseNameCol);
            var detailErrorTypeCol   = Sanitise(string.IsNullOrWhiteSpace(request.DetailErrorTypeCol)   ? "" : request.DetailErrorTypeCol);
            var maxTotalCredits = NormalizeThreshold(request.MaxTotalCredits);
            var thresholdText = maxTotalCredits.ToString(CultureInfo.InvariantCulture);
            var detailErrorCol       = Sanitise(string.IsNullOrWhiteSpace(request.DetailErrorCol)       ? "Error" : request.DetailErrorCol);
            var detailErrorTypeValue = (string.IsNullOrWhiteSpace(request.DetailErrorTypeValue) ? "Fatal" : request.DetailErrorTypeValue).Replace("'", "''");
            var detailElementInfoCol = Sanitise(string.IsNullOrWhiteSpace(request.DetailElementInfoCol) ? "Element_Information" : request.DetailElementInfoCol);
            var exclusionCodes = ParseExclusionCodes(request.DetailExclusionCodes);

            await using var batchCmd = conn.CreateConfiguredCommand();
            batchCmd.CommandText = BuildSingleBatchSql(
                studTable, cregTable, qualTable, credTable, crseTable, detailTable,
                cregStudNoCol, cregQualCol, cregCourseCol,
                qualQualCol, qualNameCol,
                credQualCol, credCourseCol, credCreditsCol,
                crseCourseCol, crseNameCol,
                detailErrorTypeCol, detailErrorCol, detailErrorTypeValue, detailElementInfoCol,
                exclusionCodes, includeAllReviewRows ? null : BrowserPreviewRowLimit, maxTotalCredits);
            batchCmd.CommandTimeout = 600;
            await using var batchReader = await batchCmd.ExecuteReaderAsync();

            // RS 1: source counts
            int studCount = 0, cregCount = 0, qualCount = 0, credCount = 0, crseCount = 0, detailCount = 0;
            if (await batchReader.ReadAsync())
            {
                studCount   = GetInt(batchReader, 0);
                cregCount   = GetInt(batchReader, 1);
                qualCount   = GetInt(batchReader, 2);
                credCount   = GetInt(batchReader, 3);
                crseCount   = GetInt(batchReader, 4);
                detailCount = GetInt(batchReader, 5);
            }

            // RS 2: validation counts
            int totalValidated = 0, passCount = 0, failCount = 0;
            int confirmedByRule32Count = 0, notInRule32Count = 0, rule32OnlyCount = 0;
            if (await batchReader.NextResultAsync() && await batchReader.ReadAsync())
            {
                totalValidated        = GetInt(batchReader, 0);
                passCount             = GetInt(batchReader, 1);
                failCount             = GetInt(batchReader, 2);
                confirmedByRule32Count= GetInt(batchReader, 3);
                notInRule32Count      = GetInt(batchReader, 4);
                rule32OnlyCount       = GetInt(batchReader, 5);
            }

            // RS 3: review rows
            var reviewRows = new List<Rule68ValidationRowRecord>();
            if (await batchReader.NextResultAsync())
            {
                while (await batchReader.ReadAsync())
                {
                    var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < batchReader.FieldCount; i++)
                        displayValues[batchReader.GetName(i)] = batchReader.IsDBNull(i) ? null : Convert.ToString(batchReader.GetValue(i), CultureInfo.InvariantCulture);

                    reviewRows.Add(new Rule68ValidationRowRecord
                    {
                        ValidationNumber    = reviewRows.Count + 1,
                        ValidationResult    = displayValues.TryGetValue("Validation_Result",    out var vr) ? vr ?? "" : "",
                        ErrorCode           = displayValues.TryGetValue("Error_Code",           out var ec) ? ec ?? "" : "",
                        ReconciliationStatus= displayValues.TryGetValue("Reconciliation_Status",out var rs) ? rs ?? "" : "",
                        DisplayValues       = displayValues
                    });
                }
            }

            // RS 4: Rule 32-only rows (in detail with 03603 but not in Rule 68 FAIL)
            var rule32OnlyRows = new List<Rule68Rule32OnlyRow>();
            if (await batchReader.NextResultAsync())
            {
                while (await batchReader.ReadAsync())
                {
                    rule32OnlyRows.Add(new Rule68Rule32OnlyRow
                    {
                        RowNumber      = rule32OnlyRows.Count + 1,
                        StudentNo      = batchReader.IsDBNull(1) ? "" : Convert.ToString(batchReader.GetValue(1), CultureInfo.InvariantCulture) ?? "",
                        QualCode       = batchReader.IsDBNull(2) ? "" : Convert.ToString(batchReader.GetValue(2), CultureInfo.InvariantCulture) ?? "",
                        SumCredits     = batchReader.FieldCount > 3 && !batchReader.IsDBNull(3) ? Convert.ToString(batchReader.GetValue(3), CultureInfo.InvariantCulture) ?? "" : "",
                        ConfirmedByR68 = batchReader.FieldCount > 4 && !batchReader.IsDBNull(4) ? Convert.ToString(batchReader.GetValue(4), CultureInfo.InvariantCulture) ?? "No" : "No"
                    });
                }
            }
            await batchReader.CloseAsync();

            reviewRows = NormalizeRows(reviewRows);
            var r32ConfirmedTotal  = rule32OnlyRows.Count(r => string.Equals(r.ConfirmedByR68, "Yes",         StringComparison.OrdinalIgnoreCase));
            var r32NotInCregTotal  = rule32OnlyRows.Count(r => string.Equals(r.ConfirmedByR68, "Not in CREG", StringComparison.OrdinalIgnoreCase));
            if (!includeAllReviewRows && rule32OnlyRows.Count > BrowserPreviewRowLimit)
                rule32OnlyRows = rule32OnlyRows.Take(BrowserPreviewRowLimit).ToList();

            var isPreviewOnly = !includeAllReviewRows && totalValidated > reviewRows.Count;
            var exRate = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2);

            return new Rule68ValidationSummary
            {
                Success            = true,
                StudRecordCount    = studCount,
                CregRecordCount    = cregCount,
                QualRecordCount    = qualCount,
                CredRecordCount    = credCount,
                CrseRecordCount    = crseCount,
                DetailRecordCount  = detailCount,
                TotalValidated     = totalValidated,
                PassCount          = passCount,
                FailCount          = failCount,
                DisplayedCount     = reviewRows.Count,
                IsPreviewOnly      = isPreviewOnly,
                PreviewLimit       = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                ConfirmedByRule32Count  = confirmedByRule32Count,
                NotInRule32Count        = notInRule32Count,
                Rule32OnlyCount         = rule32OnlyCount,
                Rule32ConfirmedByR68Count = r32ConfirmedTotal,
                Rule32NotInCregCount    = r32NotInCregTotal,
                ExceptionRate      = exRate,
                Status             = failCount == 0 ? "PASS" : "FAIL",
                Timestamp          = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database           = request.Database,
                StudTable          = studTable,
                CregTable          = cregTable,
                QualTable          = qualTable,
                CredTable          = credTable,
                CrseTable          = crseTable,
                DetailTable        = detailTable,
                CregStudNoCol      = cregStudNoCol,
                CregQualCol        = cregQualCol,
                CregCourseCol      = cregCourseCol,
                QualQualCol        = qualQualCol,
                QualNameCol        = qualNameCol,
                CredQualCol        = credQualCol,
                CredCourseCol      = credCourseCol,
                CredCreditsCol     = credCreditsCol,
                CrseCourseCol      = crseCourseCol,
                CrseNameCol        = crseNameCol,
                DetailErrorTypeCol   = detailErrorTypeCol,
                DetailErrorCol       = detailErrorCol,
                DetailErrorTypeValue = detailErrorTypeValue,
                DetailExclusionCodes = exclusionCodes.Count > 0 ? string.Join(",", exclusionCodes) : "",
                DetailElementInfoCol = detailElementInfoCol,
                MaxTotalCredits = maxTotalCredits,
                TableLinkageText   = $"{request.CregTable}.[{cregStudNoCol}]+[{cregQualCol}] → CRED.[{credCreditsCol}] (SUM > {thresholdText} = error 03603)",
                RuleModeText       = $"Sum of CRED.[{credCreditsCol}] per student-qualification must be ≤ {thresholdText}",
                ProcedureSteps     = new List<string>
                {
                    $"Extract all student-course registrations from {request.CregTable} using [{cregStudNoCol}] (student) and [{cregQualCol}] (qualification).",
                    $"Join to {request.CredTable}.[{credCreditsCol}] via [{credCourseCol}] to get the credit value per course.",
                    $"Also join {request.QualTable} for qualification names and {request.CrseTable} for course names.",
                    $"Group by (Student_No, Qual_Code) and compute SUM([{credCreditsCol}]).",
                    $"If SUM > {thresholdText} → FAIL (error code 03603). Otherwise PASS.",
                    string.IsNullOrWhiteSpace(detailTable) ? "" : $"Reconciliation: cross-reference FAIL results against {request.DetailTable} (error 03603) to confirm findings."
                }.Where(s => !string.IsNullOrEmpty(s)).ToList(),
                ClientId      = request.ClientId,
                ReviewRows    = reviewRows,
                Rule32OnlyRows= rule32OnlyRows,
                Warning = includeAllReviewRows
                    ? "Rule 68 completed with the full population."
                    : "Counts reflect the full student-qualification population. Browser review rows are limited for performance."
            };
        }

        // ─── SQL Builders ─────────────────────────────────────────────────────

        private static string BuildSingleBatchSql(
            string studTable, string cregTable, string qualTable, string credTable, string crseTable,
            string detailTable,
            string cregStudNoCol, string cregQualCol, string cregCourseCol,
            string qualQualCol, string qualNameCol,
            string credQualCol, string credCourseCol, string credCreditsCol,
            string crseCourseCol, string crseNameCol,
            string detailErrorTypeCol, string detailErrorCol, string detailErrorTypeValue,
            string detailElementInfoCol,
            IReadOnlyList<long> exclusionCodes, int? maxRows, decimal maxTotalCredits)
        {
            var top     = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;
            var thresholdText = maxTotalCredits.ToString(CultureInfo.InvariantCulture);
            var orderBy = maxRows.HasValue && maxRows.Value > 0
                ? "CASE WHEN Validation_Result = 'FAIL' THEN 0 ELSE 1 END, Total_Credits DESC, Student_No, Qual_Code"
                : "CASE WHEN Validation_Result = 'FAIL' THEN 0 ELSE 1 END, Total_Credits DESC, Student_No, Qual_Code";

            var hasDetail = !string.IsNullOrWhiteSpace(detailTable);
            var hasErrorTypeFilter = hasDetail && !string.IsNullOrWhiteSpace(detailErrorTypeCol);
            var exclusionInSql = exclusionCodes.Count > 0 ? string.Join(",", exclusionCodes) : "0";

            var detailCountExpr = hasDetail
                ? $@"(SELECT COUNT(*) FROM [{detailTable}] WITH (NOLOCK)
                       WHERE TRY_CAST(LTRIM(RTRIM(CAST([{detailErrorCol}] AS nvarchar(50)))) AS bigint) = {TargetErrorCode})"
                : "0";

            var reconExpr = hasDetail
                ? $"CASE WHEN Validation_Result = 'PASS' THEN '' WHEN DP.DETAIL_STUD_NO IS NOT NULL THEN 'Confirmed by Rule 32' ELSE 'Not in Rule 32' END"
                : "''";

            var detailJoin = hasDetail
                ? "LEFT JOIN #R68DetailPairs DP ON DP.DETAIL_STUD_NO = V.Student_No AND DP.DETAIL_QUAL = V.Qual_Code"
                : "";

            var reconCountsSql = hasDetail
                ? $@"SUM(CASE WHEN Reconciliation_Status = 'Confirmed by Rule 32' THEN 1 ELSE 0 END) AS ConfirmedByRule32Count,
    SUM(CASE WHEN Reconciliation_Status = 'Not in Rule 32'       THEN 1 ELSE 0 END) AS NotInRule32Count,
    (SELECT COUNT(*) FROM #R68DetailPairs DP2 WHERE NOT EXISTS (SELECT 1 FROM #R68Results R2 WHERE R2.Student_No = DP2.DETAIL_STUD_NO AND R2.Qual_Code = DP2.DETAIL_QUAL AND R2.Validation_Result = 'FAIL')) AS Rule32OnlyCount"
                : "0 AS ConfirmedByRule32Count, 0 AS NotInRule32Count, 0 AS Rule32OnlyCount";

            var errorTypeFilterSql = hasErrorTypeFilter
                ? $"AND UPPER(LTRIM(RTRIM(CAST([{detailErrorTypeCol}] AS nvarchar(255))))) = UPPER('{detailErrorTypeValue}')"
                : "";

            var exclusionFilterSql = exclusionCodes.Count > 0
                ? $"AND TRY_CAST(LTRIM(RTRIM(CAST([{detailErrorCol}] AS nvarchar(50)))) AS bigint) NOT IN ({exclusionInSql})"
                : "";

            var detailSetupSql = hasDetail ? $@"
IF OBJECT_ID('tempdb..#R68DetailPairs') IS NOT NULL DROP TABLE #R68DetailPairs;
IF OBJECT_ID('tempdb..#R68DetailRaw')   IS NOT NULL DROP TABLE #R68DetailRaw;

SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), CASE
        WHEN CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
         AND CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
        THEN SUBSTRING(CONVERT(nvarchar(500), [{detailElementInfoCol}]),
            CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5,
            CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}]))
                - (CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5))
    END)))                                                         AS RAW_STUD_NO,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CASE
        WHEN CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
         AND CHARINDEX('Sum E036:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
        THEN SUBSTRING(CONVERT(nvarchar(500), [{detailElementInfoCol}]),
            CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5,
            CHARINDEX('Sum E036:', CONVERT(nvarchar(500), [{detailElementInfoCol}]))
                - (CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5))
        WHEN CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
        THEN SUBSTRING(CONVERT(nvarchar(500), [{detailElementInfoCol}]),
            CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) + 5, 50)
    END))))                                                        AS RAW_QUAL
INTO #R68DetailRaw
FROM [{detailTable}] WITH (NOLOCK)
WHERE CHARINDEX('E007:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
  AND CHARINDEX('E001:', CONVERT(nvarchar(500), [{detailElementInfoCol}])) > 0
  AND TRY_CAST(LTRIM(RTRIM(CAST([{detailErrorCol}] AS nvarchar(50)))) AS bigint) = {TargetErrorCode}
  {errorTypeFilterSql}
  {exclusionFilterSql}
OPTION (MAXDOP 4);

SELECT RAW_STUD_NO AS DETAIL_STUD_NO, RAW_QUAL AS DETAIL_QUAL
INTO #R68DetailPairs
FROM #R68DetailRaw
WHERE RAW_STUD_NO IS NOT NULL AND RAW_STUD_NO <> '' AND RAW_QUAL IS NOT NULL AND RAW_QUAL <> ''
GROUP BY RAW_STUD_NO, RAW_QUAL
OPTION (MAXDOP 4);
DROP TABLE #R68DetailRaw;" : "";

            var rule32OnlySql = hasDetail ? $@"
SELECT
    ROW_NUMBER() OVER (ORDER BY DP.DETAIL_STUD_NO, DP.DETAIL_QUAL) AS RowNum,
    DP.DETAIL_STUD_NO, DP.DETAIL_QUAL,
    CAST('' AS nvarchar(50)) AS Detail_Sum,
    CASE
        WHEN FailR.Student_No IS NOT NULL THEN 'Yes'
        ELSE                                   'No'
    END AS ConfirmedByR68
FROM #R68DetailPairs DP
LEFT JOIN (
    SELECT DISTINCT Student_No, Qual_Code
    FROM   #R68Results
    WHERE  Validation_Result = 'FAIL'
) AS FailR ON FailR.Student_No = DP.DETAIL_STUD_NO AND FailR.Qual_Code = DP.DETAIL_QUAL
ORDER BY DP.DETAIL_STUD_NO, DP.DETAIL_QUAL
OPTION (MAXDOP 4);"
                : "SELECT CAST(0 AS int) AS RowNum, CAST('' AS nvarchar(255)) AS DETAIL_STUD_NO, CAST('' AS nvarchar(255)) AS DETAIL_QUAL, CAST('' AS nvarchar(50)) AS Detail_Sum, CAST('' AS nvarchar(12)) AS ConfirmedByR68 WHERE 1=0;";

            var detailDropSql = hasDetail ? "DROP TABLE IF EXISTS #R68DetailPairs;" : "";

            return $@"
IF OBJECT_ID('tempdb..#R68Base')       IS NOT NULL DROP TABLE #R68Base;
IF OBJECT_ID('tempdb..#R68Validation') IS NOT NULL DROP TABLE #R68Validation;
IF OBJECT_ID('tempdb..#R68Results')    IS NOT NULL DROP TABLE #R68Results;
{detailSetupSql}

-- RS 1: source table row counts
SELECT
    (SELECT COUNT(*) FROM [{studTable}] WITH (NOLOCK))  AS StudCount,
    (SELECT COUNT(*) FROM [{cregTable}] WITH (NOLOCK))  AS CregCount,
    (SELECT COUNT(*) FROM [{qualTable}] WITH (NOLOCK))  AS QualCount,
    (SELECT COUNT(*) FROM [{credTable}] WITH (NOLOCK))  AS CredCount,
    (SELECT COUNT(*) FROM [{crseTable}] WITH (NOLOCK))  AS CrseCount,
    {detailCountExpr}                                   AS DetailCount;

-- Build base data: CREG join QUAL, CRED, CRSE
SELECT
    LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregStudNoCol}])))        AS Student_No,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregQualCol}]))))   AS Qual_Code,
    UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregCourseCol}]))))  AS Course_Code,
    ISNULL(TRY_CAST(CR.[{credCreditsCol}] AS decimal(18,4)), 0)       AS Credits,
    ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(255), CS.[{crseNameCol}]))), '') AS Course_Name,
    ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qualNameCol}]))), '') AS Qual_Name
INTO #R68Base
FROM [{cregTable}] CG WITH (NOLOCK)
LEFT JOIN [{qualTable}] Q WITH (NOLOCK)
    ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), Q.[{qualQualCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregQualCol}]))))
LEFT JOIN [{credTable}] CR WITH (NOLOCK)
    ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CR.[{credCourseCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregCourseCol}]))))
   AND UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CR.[{credQualCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregQualCol}]))))
LEFT JOIN [{crseTable}] CS WITH (NOLOCK)
    ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CS.[{crseCourseCol}])))) = UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregCourseCol}]))))
WHERE CG.[{cregStudNoCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregStudNoCol}]))) <> ''
  AND CG.[{cregQualCol}] IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(nvarchar(255), CG.[{cregQualCol}]))) <> ''
OPTION (MAXDOP 4);

CREATE INDEX IX_R68Base ON #R68Base(Student_No, Qual_Code);

-- Aggregate credits per student per qualification
SELECT
    Student_No,
    Qual_Code,
    MAX(Qual_Name)                       AS Qual_Name,
    COUNT(DISTINCT Course_Code)          AS Course_Count,
    CAST(SUM(Credits) AS decimal(18,4))  AS Total_Credits,
    CASE WHEN SUM(Credits) > {thresholdText} THEN 'FAIL' ELSE 'PASS' END AS Validation_Result,
    CASE WHEN SUM(Credits) > {thresholdText} THEN '03603' ELSE ''    END AS Error_Code
INTO #R68Validation
FROM #R68Base
GROUP BY Student_No, Qual_Code
OPTION (MAXDOP 4);

CREATE INDEX IX_R68Val ON #R68Validation(Validation_Result, Student_No, Qual_Code);

-- Materialise results with reconciliation status
SELECT
    V.Student_No, V.Qual_Code, V.Qual_Name, V.Course_Count, V.Total_Credits,
    V.Validation_Result, V.Error_Code,
    {reconExpr} AS Reconciliation_Status
INTO #R68Results
FROM #R68Validation V
{detailJoin}
OPTION (MAXDOP 4);

CREATE INDEX IX_R68Res ON #R68Results(Validation_Result, Reconciliation_Status);

-- RS 2: validation counts
SELECT
    COUNT(1)                                                             AS TotalValidated,
    SUM(CASE WHEN Validation_Result = 'PASS' THEN 1 ELSE 0 END)        AS PassCount,
    SUM(CASE WHEN Validation_Result = 'FAIL' THEN 1 ELSE 0 END)        AS FailCount,
    {reconCountsSql}
FROM #R68Results;

-- RS 3: preview / full rows
SELECT {top}
    Student_No, Qual_Code, Qual_Name, Course_Count, Total_Credits,
    Validation_Result, Error_Code, Reconciliation_Status,
    CASE
        WHEN Validation_Result = 'PASS'
        THEN 'PASS: Total credits (' + CAST(Total_Credits AS nvarchar(20)) + ') is within the {thresholdText} limit for qualification ' + Qual_Code + '.'
        ELSE 'FAIL (03603): Total credits (' + CAST(Total_Credits AS nvarchar(20)) + ') exceed {thresholdText} for student ' + Student_No + ' / qualification ' + Qual_Code + '.'
    END AS Validation_Explanation
FROM #R68Results
ORDER BY {orderBy};

{rule32OnlySql}

DROP TABLE IF EXISTS #R68Base;
DROP TABLE IF EXISTS #R68Validation;
DROP TABLE IF EXISTS #R68Results;
{detailDropSql}";
        }

        // ─── Save / Persist ───────────────────────────────────────────────────

        private async Task<int> SaveValidationRunAsync(Rule68ValidationRequest request, Rule68ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 68);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue) throw new InvalidOperationException("The current analyst could not be resolved.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 68);
            var failRows     = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID,UserID,RuleNumber,RuleName,Status,TotalRecords,PassCount,FailCount,ExceptionRate,RunTimestamp,
 HemisServer,AuditDatabase,StudTable,DeceasedTable,StudColumn,DeceasedColumn,
 ExceptionsJSON,ResultsJSON,RunByUserName,LastEditedByUserName,LastEditedAt,PreviousHash,RecordHash,WorkspaceSavedAt,IsCurrent)
VALUES
(@ClientID,@UserID,68,@RuleName,@Status,@TotalRecords,@PassCount,@FailCount,@ExceptionRate,GETDATE(),
 @HemisServer,@AuditDatabase,@StudTable,@DeceasedTable,@StudColumn,@DeceasedColumn,
 @ExceptionsJSON,@ResultsJSON,@RunByUserName,NULL,NULL,@PreviousHash,NULL,@WorkspaceSavedAt,1);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            cmd.Parameters.AddWithValue("@ClientID", request.ClientId);
            cmd.Parameters.AddWithValue("@UserID", systemUserId.Value);
            cmd.Parameters.AddWithValue("@RuleName", "Credit Overload Validation");
            cmd.Parameters.AddWithValue("@Status", summary.Status);
            cmd.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            cmd.Parameters.AddWithValue("@PassCount", summary.PassCount);
            cmd.Parameters.AddWithValue("@FailCount", summary.FailCount);
            cmd.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            cmd.Parameters.AddWithValue("@HemisServer", request.Server);
            cmd.Parameters.AddWithValue("@AuditDatabase", request.Database);
            cmd.Parameters.AddWithValue("@StudTable", request.CregTable);
            cmd.Parameters.AddWithValue("@DeceasedTable", request.StudTable);
            cmd.Parameters.AddWithValue("@StudColumn", request.CregStudNoCol);
            cmd.Parameters.AddWithValue("@DeceasedColumn", request.CregQualCol);
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
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule68|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            await UpdateStoredSummaryAsync(connection, runId, summary);
            return runId;
        }

        // ─── Preview / Clone ──────────────────────────────────────────────────

        private static void ApplyBrowserPreview(Rule68ValidationSummary summary)
        {
            if (summary.Rule32OnlyRows.Count > BrowserPreviewRowLimit)
                summary.Rule32OnlyRows = summary.Rule32OnlyRows.Take(BrowserPreviewRowLimit).ToList();

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

        private static Rule68ValidationSummary CloneSummary(Rule68ValidationSummary s) => new()
        {
            Success = s.Success, StudRecordCount = s.StudRecordCount, CregRecordCount = s.CregRecordCount,
            QualRecordCount = s.QualRecordCount, CredRecordCount = s.CredRecordCount, CrseRecordCount = s.CrseRecordCount,
            DetailRecordCount = s.DetailRecordCount, TotalValidated = s.TotalValidated,
            PassCount = s.PassCount, FailCount = s.FailCount, DisplayedCount = s.DisplayedCount,
            IsPreviewOnly = s.IsPreviewOnly, PreviewLimit = s.PreviewLimit,
            ConfirmedByRule32Count = s.ConfirmedByRule32Count, NotInRule32Count = s.NotInRule32Count,
            Rule32OnlyCount = s.Rule32OnlyCount, Rule32ConfirmedByR68Count = s.Rule32ConfirmedByR68Count,
            Rule32NotInCregCount = s.Rule32NotInCregCount,
            ExceptionRate = s.ExceptionRate, Status = s.Status, Timestamp = s.Timestamp, Database = s.Database,
            StudTable = s.StudTable, CregTable = s.CregTable, QualTable = s.QualTable, CredTable = s.CredTable, CrseTable = s.CrseTable,
            DetailTable = s.DetailTable,
            CregStudNoCol = s.CregStudNoCol, CregQualCol = s.CregQualCol, CregCourseCol = s.CregCourseCol,
            QualQualCol = s.QualQualCol, QualNameCol = s.QualNameCol,
            CredCourseCol = s.CredCourseCol, CredCreditsCol = s.CredCreditsCol,
            CrseCourseCol = s.CrseCourseCol, CrseNameCol = s.CrseNameCol,
            DetailErrorTypeCol = s.DetailErrorTypeCol, DetailErrorCol = s.DetailErrorCol,
            DetailErrorTypeValue = s.DetailErrorTypeValue, DetailExclusionCodes = s.DetailExclusionCodes,
            DetailElementInfoCol = s.DetailElementInfoCol, MaxTotalCredits = s.MaxTotalCredits,
            TableLinkageText = s.TableLinkageText, RuleModeText = s.RuleModeText, ProcedureSteps = s.ProcedureSteps.ToList(),
            ClientId = s.ClientId, SavedRunId = s.SavedRunId,
            ReviewRows = s.ReviewRows.Select(r => new Rule68ValidationRowRecord
            {
                ValidationNumber = r.ValidationNumber, ValidationResult = r.ValidationResult,
                ErrorCode = r.ErrorCode, ReconciliationStatus = r.ReconciliationStatus,
                DisplayValues = new Dictionary<string, string?>(r.DisplayValues, StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            Rule32OnlyRows = s.Rule32OnlyRows.Select(r => new Rule68Rule32OnlyRow
            {
                RowNumber = r.RowNumber, StudentNo = r.StudentNo, QualCode = r.QualCode,
                SumCredits = r.SumCredits, ConfirmedByR68 = r.ConfirmedByR68
            }).ToList(),
            Warning = s.Warning, Error = s.Error
        };

        private static Rule68ValidationRequest CloneRequest(Rule68ValidationRequest r) => new()
        {
            ClientId = r.ClientId, RunId = r.RunId, Server = r.Server, Database = r.Database, Driver = r.Driver,
            StudTable = r.StudTable, CregTable = r.CregTable, QualTable = r.QualTable, CredTable = r.CredTable, CrseTable = r.CrseTable,
            DetailTable = r.DetailTable,
            CregStudNoCol = r.CregStudNoCol, CregQualCol = r.CregQualCol, CregCourseCol = r.CregCourseCol,
            QualQualCol = r.QualQualCol, QualNameCol = r.QualNameCol,
            CredCourseCol = r.CredCourseCol, CredCreditsCol = r.CredCreditsCol,
            CrseCourseCol = r.CrseCourseCol, CrseNameCol = r.CrseNameCol,
            DetailErrorTypeCol = r.DetailErrorTypeCol, DetailErrorCol = r.DetailErrorCol,
            DetailErrorTypeValue = r.DetailErrorTypeValue, DetailExclusionCodes = r.DetailExclusionCodes,
            DetailElementInfoCol = r.DetailElementInfoCol, MaxTotalCredits = r.MaxTotalCredits
        };

        private static bool RequestsMatch(Rule68ValidationRequest a, Rule68ValidationRequest b) =>
            a.ClientId == b.ClientId &&
            string.Equals(a.Server?.Trim(),    b.Server?.Trim(),    StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Database?.Trim(),  b.Database?.Trim(),  StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.CregTable?.Trim(), b.CregTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.StudTable?.Trim(), b.StudTable?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static List<Rule68ValidationRowRecord> NormalizeRows(IEnumerable<Rule68ValidationRowRecord> rows) =>
            rows.Select((r, i) => { r.ValidationNumber = i + 1; return r; }).ToList();

        // ─── Expand Saved Summary ─────────────────────────────────────────────

        private async Task<Rule68ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule68ValidationSummary summary, string? server)
        {
            if (!summary.IsPreviewOnly && summary.ReviewRows.Count >= summary.TotalValidated) return summary;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(summary.Database) || string.IsNullOrWhiteSpace(summary.CregTable)) return summary;
            try
            {
                var expanded = await AnalyseAsync(new Rule68ValidationRequest
                {
                    ClientId = summary.ClientId, RunId = summary.SavedRunId, Server = server, Database = summary.Database,
                    Driver = "ODBC Driver 17 for SQL Server",
                    StudTable = summary.StudTable, CregTable = summary.CregTable,
                    QualTable = summary.QualTable, CredTable = summary.CredTable, CrseTable = summary.CrseTable,
                    DetailTable = summary.DetailTable,
                    CregStudNoCol = summary.CregStudNoCol, CregQualCol = summary.CregQualCol, CregCourseCol = summary.CregCourseCol,
                    QualQualCol = summary.QualQualCol, QualNameCol = summary.QualNameCol,
                    CredCourseCol = summary.CredCourseCol, CredCreditsCol = summary.CredCreditsCol,
                    CrseCourseCol = summary.CrseCourseCol, CrseNameCol = summary.CrseNameCol,
                    DetailErrorTypeCol = summary.DetailErrorTypeCol, DetailErrorCol = summary.DetailErrorCol,
                    DetailErrorTypeValue = summary.DetailErrorTypeValue,
                    DetailExclusionCodes = summary.DetailExclusionCodes, DetailElementInfoCol = summary.DetailElementInfoCol,
                    MaxTotalCredits = summary.MaxTotalCredits
                }, includeAllReviewRows: true);
                expanded.Timestamp  = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId   = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                return expanded;
            }
            catch { return summary; }
        }

        private async Task<Rule68ValidationSummary> ExpandAndPersistSavedSummaryIfNeededAsync(SqlConnection connection, int runId, Rule68ValidationSummary summary, string? server)
        {
            var expanded = await ExpandSavedSummaryIfNeededAsync(summary, server);
            if (!ReferenceEquals(expanded, summary)) { expanded.SavedRunId = runId; await UpdateStoredSummaryAsync(connection, runId, expanded); }
            return expanded;
        }

        // ─── DB Helpers ───────────────────────────────────────────────────────

        private static decimal NormalizeThreshold(decimal value) => value < 0m ? 0m : value;

        private static void ValidateRequest(Rule68ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))   throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database)) throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable)) throw new InvalidOperationException("CREG table is required.");
            ValidateObjectName(request.CregTable);
            if (!string.IsNullOrWhiteSpace(request.StudTable))  ValidateObjectName(request.StudTable);
            if (!string.IsNullOrWhiteSpace(request.QualTable))  ValidateObjectName(request.QualTable);
            if (!string.IsNullOrWhiteSpace(request.CredTable))  ValidateObjectName(request.CredTable);
            if (!string.IsNullOrWhiteSpace(request.CrseTable))  ValidateObjectName(request.CrseTable);
            if (!string.IsNullOrWhiteSpace(request.DetailTable)) ValidateObjectName(request.DetailTable);
        }

        private static void ValidateRequest(Rule68VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))    throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database))  throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.CregTable)) throw new InvalidOperationException("CREG table is required.");
            ValidateObjectName(request.CregTable);
        }

        private static IReadOnlyList<long> ParseExclusionCodes(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DefaultExclusionCodes;
            var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => { var t = s.TrimStart('0'); return long.TryParse(t == "" ? "0" : t, out var n) ? n : (long?)null; })
                .Where(n => n.HasValue)
                .Select(n => n!.Value)
                .Distinct()
                .ToList();
            return parsed.Count > 0 ? parsed : DefaultExclusionCodes;
        }

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
        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"]   ?? "HEMISBaseSystem";
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

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule68ValidationSummary summary)
        {
            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET ResultsJSON=@ResultsJSON WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@ResultsJSON", ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary)));
            await cmd.ExecuteNonQueryAsync();
        }

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);

        private static Rule68ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { var decoded = ValidationPayloadCodec.Decode(json); return string.IsNullOrWhiteSpace(decoded) ? null : JsonConvert.DeserializeObject<Rule68ValidationSummary>(decoded); }
            catch { return null; }
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)));
        }
    }
}
