using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class Rule51Service : IRule51Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private static readonly Rule51ColumnMapping[] DefaultColumnMappings =
        {
            new() { ValpacColumn = "_007", ProdColumn = "IAGSTNO", Label = "Student No" },
            new() { ValpacColumn = "_008", ProdColumn = "IADIDNO", Label = "ID No" },
            new() { ValpacColumn = "_001", ProdColumn = "IAGQUAL", Label = "Qualification" },
            new() { ValpacColumn = "ColYear", ProdColumn = "IAGCYR", Label = "Year" }
        };
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public Rule51Service(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<Rule51TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                return new Rule51TableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoValpacTable = FindFirst(tables, ["dbo_STUD_VALPAC", "STUD_VALPAC", "VALPAC"], ["valpac"]),
                    AutoProdTable   = FindFirst(tables, ["dbo_STUD_PRODUCTION", "STUD_PRODUCTION", "PRODUCTION"], ["production", "prod"])
                };
            }
            catch (Exception ex) { return new Rule51TableDiscoveryResult { Success = false, Error = ex.Message }; }
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

        public async Task<Rule51VerifyResult> VerifyTablesAsync(Rule51VerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var valpacTable = Sanitise(request.ValpacTable);
                var prodTable   = Sanitise(request.ProdTable);
                var mappings    = SanitizeMappings(GetMappings(request));

                var valpacCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{valpacTable}];");
                var prodCount   = await CountAsync(conn, $"SELECT COUNT(*) FROM [{prodTable}];");

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = BuildPopulationCountSql(valpacTable, prodTable, mappings);
                await using var reader = await cmd.ExecuteReaderAsync();

                var result = new Rule51VerifyResult { Success = true, ValpacRecordCount = valpacCount, ProdRecordCount = prodCount };
                if (await reader.ReadAsync())
                {
                    result.TotalTested   = GetInt(reader, 0);
                    result.MatchedCount  = GetInt(reader, 1);
                    result.MissingCount  = GetInt(reader, 2);
                }
                return result;
            }
            catch (Exception ex) { return new Rule51VerifyResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule51ValidationSummary> RunValidationAsync(Rule51ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateRequest(request);
                // Run once with all rows — no second query needed for saving
                var full = await AnalyseAsync(request, includeAllReviewRows: true);
                var browserSummary = CloneSummary(full);

                if (full.Success && request.ClientId > 0)
                {
                    try
                    {
                        full.SavedRunId = null;
                        browserSummary.SavedRunId = await SaveValidationRunAsync(CloneRequest(request), full, userEmail, userName, markWorkspaceSaved: false);

                        if (!string.IsNullOrWhiteSpace(userEmail))
                            _pendingValidationCache.ClearPending(51, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(51, request.ClientId, userEmail!, request, CloneSummary(full), userName);
                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full VALPAC population. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Rule 51 run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }

                ApplyBrowserPreview(browserSummary);
                return browserSummary;
            }
            catch (Exception ex) { return new Rule51ValidationSummary { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule51ValidationSummary> GetExportSummaryAsync(Rule51ValidationRequest request)
        {
            ValidateRequest(request);
            return await AnalyseAsync(request, includeAllReviewRows: true);
        }

        public Task<Rule51ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail)
        {
            var pending = _pendingValidationCache.GetPending<Rule51ValidationRequest, Rule51ValidationSummary>(51, clientId, reviewerEmail);
            if (pending == null) return Task.FromResult<Rule51ValidationSummary?>(null);
            var preview = CloneSummary(pending.Summary);
            preview.SavedRunId = null;
            preview.Warning = "This Rule 51 validation is still pending. Click Save Workspace to write it to the system database.";
            ApplyBrowserPreview(preview);
            return Task.FromResult<Rule51ValidationSummary?>(preview);
        }

        public Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail)
            => Task.FromResult(_pendingValidationCache.HasPending(51, clientId, reviewerEmail));

        public async Task<int?> GetClientIdForRunAsync(int runId)
        {
            await using var connection = await OpenSystemConnectionAsync();
            return await GetClientIdForRunAsync(connection, runId);
        }

        public async Task<Rule51WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer, '') AS HemisServer,
    ISNULL(vr.AuditDatabase, '') AS AuditDatabase,
    ISNULL(vr.StudTable, '') AS ValpacTable,
    ISNULL(vr.DeceasedTable, '') AS ProdTable,
    ISNULL(vr.Status, '') AS Status,
    vr.LastEditedByUserName, vr.LastEditedAt, vr.ResultsJSON
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 51 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            command.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var runId             = reader.GetInt32(0);
            var workspaceClientId = reader.GetInt32(1);
            var server            = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var database          = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var valpacTable       = reader.IsDBNull(4) ? "dbo_STUD_VALPAC" : reader.GetString(4);
            var prodTable         = reader.IsDBNull(5) ? "dbo_STUD_PRODUCTION" : reader.GetString(5);
            var currentStatus     = reader.IsDBNull(6) ? "" : reader.GetString(6);
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
            var mappings = deserializedSummary != null
                ? GetMappings(deserializedSummary)
                : BuildDefaultMappings();

            var workspace = new Rule51WorkspaceStateViewModel
            {
                ClientId             = workspaceClientId,
                RunId                = runId,
                Server               = server,
                Database             = database,
                ValpacTable          = deserializedSummary?.ValpacTable ?? valpacTable,
                ProdTable            = deserializedSummary?.ProdTable ?? prodTable,
                ValpacCol007         = deserializedSummary?.ValpacCol007 ?? mappings.ElementAtOrDefault(0)?.ValpacColumn ?? "_007",
                ValpacCol008         = deserializedSummary?.ValpacCol008 ?? mappings.ElementAtOrDefault(1)?.ValpacColumn ?? "_008",
                ValpacCol001         = deserializedSummary?.ValpacCol001 ?? mappings.ElementAtOrDefault(2)?.ValpacColumn ?? "_001",
                ValpacColYear        = deserializedSummary?.ValpacColYear ?? mappings.ElementAtOrDefault(3)?.ValpacColumn ?? "ColYear",
                ProdColStNo          = deserializedSummary?.ProdColStNo ?? mappings.ElementAtOrDefault(0)?.ProdColumn ?? "IAGSTNO",
                ProdColIdNo          = deserializedSummary?.ProdColIdNo ?? mappings.ElementAtOrDefault(1)?.ProdColumn ?? "IADIDNO",
                ProdColQual          = deserializedSummary?.ProdColQual ?? mappings.ElementAtOrDefault(2)?.ProdColumn ?? "IAGQUAL",
                ProdColYear          = deserializedSummary?.ProdColYear ?? mappings.ElementAtOrDefault(3)?.ProdColumn ?? "IAGCYR",
                CregTable            = deserializedSummary?.CregTable ?? "",
                CregIdCol            = deserializedSummary?.CregIdCol ?? "_007",
                CregCompletionStatusCol    = deserializedSummary?.CregCompletionStatusCol ?? "_032",
                CregCompletionStatusValues = deserializedSummary?.CregCompletionStatusValues ?? "W",
                ColumnMappings       = CloneMappings(mappings),
                CurrentStatus        = currentStatus,
                LastEditedByUserName = lastEditedByUserName,
                LastEditedAt         = lastEditedAt,
                Summary              = summary
            };

            ApplyMappings(workspace, mappings);

            if (summary != null) workspace.CurrentStatus = summary.Status;

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
            if (workspace.Summary != null) workspace.Summary.SavedRunId = workspace.RunId;

            return workspace;
        }

        public async Task<Rule51RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var currentUserId = await GetSystemUserIdByEmailAsync(connection, currentUserEmail);

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT vr.RunID, vr.ClientID, vr.IsCurrent, c.EngagementName, c.MaconomyNumber, vr.HemisServer, vr.ResultsJSON
FROM dbo.ValidationRuns vr
INNER JOIN dbo.Clients c ON c.ClientID = vr.ClientID
WHERE vr.RunID = @RunID AND vr.RuleNumber = 51;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var savedRunId      = reader.GetInt32(0);
            var clientId        = reader.GetInt32(1);
            var isCurrentRun    = !reader.IsDBNull(2) && reader.GetBoolean(2);
            var engagementName  = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var maconomyNumber  = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var sourceServer    = reader.IsDBNull(5) ? "" : reader.GetString(5);
            var encodedSummary  = reader.IsDBNull(6) ? null : reader.GetString(6);
            var summary         = DeserializeSummary(encodedSummary);
            if (summary == null) return null;

            summary.ClientId = clientId;
            if (summary.SavedRunId.GetValueOrDefault() <= 0) summary.SavedRunId = runId;
            await reader.CloseAsync();

            summary = await ExpandAndPersistSavedSummaryIfNeededAsync(connection, runId, summary, sourceServer);

            if (includeFullResults) { summary.DisplayedCount = summary.ReviewRows.Count; summary.IsPreviewOnly = false; summary.PreviewLimit = 0; }
            else { ApplyBrowserPreview(summary); }

            var review = new Rule51RunReviewViewModel
            {
                RunId           = savedRunId,
                ClientId        = clientId,
                IsCurrentRun    = isCurrentRun,
                EngagementName  = engagementName,
                MaconomyNumber  = maconomyNumber,
                SourceServer    = sourceServer,
                Summary         = summary
            };

            review.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(connection, clientId, currentUserId.Value) ?? "" : "";
            review.Signoffs = await GetRunSignoffsAsync(connection, runId, currentUserId);
            review.HasDataAnalystSignoff = review.Signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            return review;
        }

        public async Task<Rule51WorkspaceSaveResult> SaveWorkspaceAsync(Rule51ValidationRequest request, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                ValidateRequest(request);

                if (request.RunId.HasValue && request.RunId.Value > 0)
                {
                    await using var connection = await OpenSystemConnectionAsync();
                    var clientId = await GetClientIdForRunAsync(connection, request.RunId.Value);
                    if (!clientId.HasValue || clientId.Value != request.ClientId)
                        return new Rule51WorkspaceSaveResult { Success = false, Error = "The saved workspace could not be found for this engagement." };

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
                    cmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"WorkspaceSave|Rule51|{request.RunId.Value}|{request.ClientId}|{(reviewerName ?? reviewerEmail)}|{DateTime.UtcNow:o}|{previousHash}"));
                    await cmd.ExecuteNonQueryAsync();

                    if (!string.IsNullOrWhiteSpace(reviewerEmail))
                        _pendingValidationCache.ClearPending(51, request.ClientId, reviewerEmail);

                    var currentWorkspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                    return new Rule51WorkspaceSaveResult
                    {
                        Success = true,
                        Message = clearedSignoffs > 0 ? "Workspace saved. Existing signoffs were removed." : "Workspace saved and marked for review.",
                        SignoffsCleared = clearedSignoffs > 0,
                        ClearedSignoffCount = clearedSignoffs,
                        Workspace = currentWorkspace
                    };
                }

                var pending = _pendingValidationCache.GetPending<Rule51ValidationRequest, Rule51ValidationSummary>(51, request.ClientId, reviewerEmail);
                if (pending == null)
                    return new Rule51WorkspaceSaveResult { Success = false, Error = "Run Rule 51 first so the current workspace is written to the system database." };

                if (!RequestsMatch(request, pending.Request))
                    return new Rule51WorkspaceSaveResult { Success = false, Error = "Workspace settings changed after validation. Run Rule 51 again before saving." };

                var requestMappings = GetMappings(request);
                var requestToSave = CloneRequest(pending.Request);
                ApplyMappings(requestToSave, requestMappings);

                var summaryToSave = CloneSummary(pending.Summary);
                if (summaryToSave.IsPreviewOnly || summaryToSave.ReviewRows.Count < summaryToSave.TotalValidated)
                    summaryToSave = await AnalyseAsync(requestToSave, includeAllReviewRows: true);

                ApplyMappings(summaryToSave, requestMappings);

                summaryToSave.SavedRunId = null;
                var savedRunId = await SaveValidationRunAsync(requestToSave, summaryToSave, reviewerEmail, reviewerName ?? pending.ReviewerName, markWorkspaceSaved: true);
                _pendingValidationCache.ClearPending(51, request.ClientId, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(request.ClientId, reviewerEmail, includeSummary: false);
                return new Rule51WorkspaceSaveResult
                {
                    Success = true,
                    Message = $"Workspace saved as Run #{savedRunId}.",
                    SignoffsCleared = false,
                    ClearedSignoffCount = 0,
                    Workspace = workspace
                };
            }
            catch (Exception ex) { return new Rule51WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule51WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null)
        {
            try
            {
                await using var connection = await OpenSystemConnectionAsync();
                var clientId = await GetClientIdForRunAsync(connection, runId);
                if (!clientId.HasValue) return new Rule51WorkspaceSaveResult { Success = false, Error = "Saved workspace was not found." };

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
                cmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"BeginEdit|Rule51|{runId}|{reviewerEmail}|{DateTime.UtcNow:o}|{previousHash}"));
                await cmd.ExecuteNonQueryAsync();

                if (!string.IsNullOrWhiteSpace(reviewerEmail))
                    _pendingValidationCache.ClearPending(51, clientId.Value, reviewerEmail);

                var workspace = await GetCurrentWorkspaceStateAsync(clientId.Value, reviewerEmail, includeSummary: false);
                return new Rule51WorkspaceSaveResult
                {
                    Success = true,
                    Message = clearedSignoffs > 0 ? "Editing begun. Existing signoffs removed." : "Editing begun. Save the workspace when ready.",
                    SignoffsCleared = clearedSignoffs > 0,
                    ClearedSignoffCount = clearedSignoffs,
                    Workspace = workspace
                };
            }
            catch (Exception ex) { return new Rule51WorkspaceSaveResult { Success = false, Error = ex.Message }; }
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var connection = await OpenSystemConnectionAsync();
            var reviewerId = await GetSystemUserIdByEmailAsync(connection, reviewerEmail);
            if (!reviewerId.HasValue) throw new InvalidOperationException("The reviewer could not be resolved.");

            var clientId = await GetClientIdForRunAsync(connection, runId);
            if (!clientId.HasValue) throw new InvalidOperationException("The selected Rule 51 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);
            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff.");

            var signoffRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!CanSignOffAsRole(signoffRole)) throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off.");

            if (!string.Equals(signoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
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
            if (!clientId.HasValue) throw new InvalidOperationException("The selected Rule 51 run could not be found.");

            await EnsureClientNotArchivedAsync(connection, clientId.Value);
            var engagementRole = await GetEngagementRoleAsync(connection, clientId.Value, reviewerId.Value);
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff.");

            await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(connection, runId, engagementRole!, reviewerEmail);
        }

        public Task<string> GenerateSqlAsync(Rule51ValidationRequest request)
        {
            ValidateRequest(request);

            var valpacTable  = Sanitise(request.ValpacTable);
            var prodTable    = Sanitise(request.ProdTable);
            var mappings     = SanitizeMappings(GetMappings(request));
            var mappingNotes = string.Join(Environment.NewLine, mappings.Select(m =>
                $"--   [{valpacTable}].[{m.ValpacColumn}] <> [{prodTable}].[{m.ProdColumn}]"));
            var selectColumns = mappings
                .SelectMany((_, index) => new[] { ValpacDisplayAlias(index), ProdDisplayAlias(index) })
                .ToList();

            var sql = $@"-- HEMIS RULE 51: VALPAC DATA IN PRODUCTION
-- Check: ALL data from [{valpacTable}] must exist in [{prodTable}]
-- Mapped columns:
{mappingNotes}
-- PASS when all {mappings.Count} mapped column pair{(mappings.Count == 1 ? "" : "s")} match a row in [{prodTable}]

{BuildSourceCtes(valpacTable, prodTable, mappings)}
SELECT
    'Control_1' AS Control_Type,
    'CONTROL 1: [{valpacTable}] data exists in [{prodTable}]' AS Control_Label,
    {string.Join("," + Environment.NewLine + "    ", selectColumns)},
    CASE WHEN {MatchMarkerAlias} IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Validation_Result
FROM ValidationResults
ORDER BY {BuildOrderByClause(mappings.Count, failFirst: false)};

SELECT
    (SELECT COUNT(1) FROM ValpacData) AS Valpac_Total,
    (SELECT COUNT(1) FROM ValidationResults WHERE {MatchMarkerAlias} IS NOT NULL) AS Matched,
    (SELECT COUNT(1) FROM ValidationResults WHERE {MatchMarkerAlias} IS NULL) AS Missing;";

            return Task.FromResult(sql.Trim());
        }

        // ─── Core Analysis ──────────────────────────────────────────────────────

        private async Task<Rule51ValidationSummary> AnalyseAsync(Rule51ValidationRequest request, bool includeAllReviewRows)
        {
            var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var valpacTable   = Sanitise(request.ValpacTable);
            var prodTable     = Sanitise(request.ProdTable);
            var mappings      = SanitizeMappings(GetMappings(request));
            var col049        = !string.IsNullOrWhiteSpace(request.ValpacCol049) ? Sanitise(request.ValpacCol049) : null;
            var saValues      = ParseSaValues(request.SaNationalValues);
            var zPlaceholders = ParseZPlaceholders(request.ValpacCol008ZPlaceholders);
            var cregTable     = !string.IsNullOrWhiteSpace(request.CregTable) ? Sanitise(request.CregTable) : null;
            var cregIdCol     = !string.IsNullOrWhiteSpace(request.CregIdCol) ? Sanitise(request.CregIdCol) : null;
            var cregCompletionCol    = !string.IsNullOrWhiteSpace(request.CregCompletionStatusCol) ? Sanitise(request.CregCompletionStatusCol) : null;
            var cregCompletionValues = ParseFilterValues(request.CregCompletionStatusValues);

            var valpacCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{valpacTable}];");
            var prodCount   = await CountAsync(conn, $"SELECT COUNT(*) FROM [{prodTable}];");
            var cregCount   = cregTable != null ? await CountAsync(conn, $"SELECT COUNT(*) FROM [{cregTable}];") : 0;

            int totalTested, matched, missing, passReviewCount, notInCregCount, cregWithdrawnCount;
            List<Rule51ValidationRowRecord> reviewRows;

            if (includeAllReviewRows)
            {
                reviewRows      = await LoadRowsAsync(conn, valpacTable, prodTable, null, mappings, col049, saValues, zPlaceholders, cregTable, cregIdCol, cregCompletionCol, cregCompletionValues);
                reviewRows      = NormalizeRows(reviewRows);
                totalTested     = reviewRows.Count;
                passReviewCount = reviewRows.Count(r => string.Equals(r.ValidationResult, "PASS_REVIEW", StringComparison.OrdinalIgnoreCase));
                notInCregCount  = reviewRows.Count(r => string.Equals(r.ValidationResult, "NOT_IN_CREG", StringComparison.OrdinalIgnoreCase));
                cregWithdrawnCount = reviewRows.Count(r => string.Equals(r.ValidationResult, "CREG_WITHDRAWN", StringComparison.OrdinalIgnoreCase));
                matched         = reviewRows.Count(r => string.Equals(r.ValidationResult, "PASS", StringComparison.OrdinalIgnoreCase)) + passReviewCount + notInCregCount + cregWithdrawnCount;
                missing         = reviewRows.Count(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                await using var countCmd = conn.CreateConfiguredCommand();
                countCmd.CommandText = BuildPopulationCountSql(valpacTable, prodTable, mappings, col049, saValues, zPlaceholders, cregTable, cregIdCol, cregCompletionCol, cregCompletionValues);
                await using var countReader = await countCmd.ExecuteReaderAsync();
                totalTested = 0; matched = 0; missing = 0; passReviewCount = 0; notInCregCount = 0; cregWithdrawnCount = 0;
                if (await countReader.ReadAsync())
                {
                    totalTested     = GetInt(countReader, 0);
                    matched         = GetInt(countReader, 1);
                    missing         = GetInt(countReader, 2);
                    passReviewCount = GetInt(countReader, 3);
                    notInCregCount  = GetInt(countReader, 4);
                    cregWithdrawnCount = GetInt(countReader, 5);
                }
                await countReader.CloseAsync();
                reviewRows = await LoadRowsAsync(conn, valpacTable, prodTable, BrowserPreviewRowLimit, mappings, col049, saValues, zPlaceholders, cregTable, cregIdCol, cregCompletionCol, cregCompletionValues);
                reviewRows = NormalizeRows(reviewRows);
            }

            var foreignExemptCount = reviewRows.Count(r =>
                string.Equals(ReadValue(r.DisplayValues, "FOREIGN_NATIONAL_EXEMPT"), "1"));
            // matched in BuildControlSummaries = PASS + PASS_REVIEW + NOT_IN_CREG (all non-FAIL)
            var controlSummaries = BuildControlSummaries(totalTested, matched, valpacTable, prodTable, mappings.Count);
            var totalValidated   = controlSummaries.Sum(x => x.TotalCount);
            var passCount        = controlSummaries.Sum(x => x.PassCount);
            var failCount        = controlSummaries.Sum(x => x.FailCount);
            var isPreviewOnly    = !includeAllReviewRows && totalValidated > reviewRows.Count;
            var exceptionCategories = BuildExceptionCategories(reviewRows, mappings);

            var summary = new Rule51ValidationSummary
            {
                Success           = true,
                ValpacRecordCount = valpacCount,
                ProdRecordCount   = prodCount,
                TotalRequested   = totalValidated,
                TotalValidated   = totalValidated,
                DisplayedCount   = reviewRows.Count,
                IsPreviewOnly    = isPreviewOnly,
                PreviewLimit     = isPreviewOnly ? BrowserPreviewRowLimit : 0,
                PassCount        = passCount,
                FailCount        = failCount,
                ExceptionRate    = totalValidated == 0 ? 0m : Math.Round(failCount * 100m / totalValidated, 2),
                Status           = failCount == 0 ? "PASS" : "FAIL",
                Timestamp        = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Database         = request.Database,
                ValpacTable      = request.ValpacTable,
                ProdTable        = request.ProdTable,
                ValpacCol049     = request.ValpacCol049 ?? "",
                SaNationalValues = request.SaNationalValues ?? "SA,PR",
                ForeignNationalExemptCount = foreignExemptCount,
                PassWithReviewCount = passReviewCount,
                NotInCregCount   = notInCregCount,
                CregWithdrawnCount = cregWithdrawnCount,
                CregTable        = request.CregTable ?? "",
                CregIdCol        = request.CregIdCol ?? "_007",
                CregCompletionStatusCol    = request.CregCompletionStatusCol ?? "_032",
                CregCompletionStatusValues = request.CregCompletionStatusValues ?? "W",
                ColumnMappings   = CloneMappings(mappings),
                TableLinkageText = BuildTableLinkageText(request.ValpacTable, request.ProdTable, mappings),
                RuleModeText     = $"100% population testing of {request.ValpacTable} against {request.ProdTable} on {mappings.Count} mapped column pair{(mappings.Count == 1 ? "" : "s")}",
                ProcedureSteps   = BuildProcedureSteps(request.ValpacTable, request.ProdTable, mappings),
                ClientId         = request.ClientId,
                ControlSummaries = controlSummaries,
                ExceptionCategories = exceptionCategories,
                ReviewRows       = reviewRows,
                Warning = includeAllReviewRows
                    ? "Rule 51 completed with the full VALPAC population result set."
                    : "Counts reflect the full VALPAC population. Browser review rows are limited for performance."
            };

            ApplyMappings(summary, mappings);
            return summary;
        }

        // ─── SQL Builders ────────────────────────────────────────────────────────

        private static string BuildSourceCtes(
            string valpacTable,
            string prodTable,
            IReadOnlyList<Rule51ColumnMapping> mappings,
            string? col049 = null)
        {
            // ValpacData: one row per VALPAC record with display values and normalised join keys
            var valpacSelectItems = new List<string>();
            for (var i = 0; i < mappings.Count; i++)
            {
                var m = mappings[i];
                valpacSelectItems.Add($"CONVERT(nvarchar(255), V.[{m.ValpacColumn}]) AS {ValpacDisplayAlias(i)}");
                valpacSelectItems.Add($"UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), V.[{m.ValpacColumn}])))) AS {ValpacKeyAlias(i)}");
            }
            // _049 (Citizen-Resident) — display + normalised key for foreign-national exemption
            if (!string.IsNullOrWhiteSpace(col049))
            {
                valpacSelectItems.Add($"CONVERT(nvarchar(255), V.[{col049}]) AS VALPAC_049_DISP");
                valpacSelectItems.Add($"UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), V.[{col049}])))) AS VALPAC_049_KEY");
            }

            // ProdUnique: one representative row per unique normalised key combination — full match (all columns)
            var prodSelectItems = new List<string>();
            var prodGroupByItems = new List<string>();
            for (var i = 0; i < mappings.Count; i++)
            {
                var m = mappings[i];
                var keyExpr = $"UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), P.[{m.ProdColumn}]))))";
                prodSelectItems.Add($"MIN(CONVERT(nvarchar(255), P.[{m.ProdColumn}])) AS {ProdDisplayAlias(i)}");
                prodSelectItems.Add($"{keyExpr} AS {ProdKeyAlias(i)}");
                prodGroupByItems.Add(keyExpr);
            }
            prodSelectItems.Add($"1 AS {MatchMarkerAlias}");

            // ProdPartial: join on first column only (student number) — used to identify WHAT differs
            // when a full match fails so we can show PROD values for FAIL rows
            var partialSelectItems = new List<string>
            {
                $"UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), P.[{mappings[0].ProdColumn}])))) AS PART_KEY_0"
            };
            for (var i = 0; i < mappings.Count; i++)
                partialSelectItems.Add($"MIN(CONVERT(nvarchar(255), P.[{mappings[i].ProdColumn}])) AS PART_DISP_{i}");

            // ValidationResults: full match via PU; COALESCE to partial match (PP) so FAIL rows show PROD values
            var validationSelectItems = new List<string>();
            var joinConditions = new List<string>();
            for (var i = 0; i < mappings.Count; i++)
            {
                validationSelectItems.Add($"VD.{ValpacDisplayAlias(i)}");
                validationSelectItems.Add($"VD.{ValpacKeyAlias(i)}");
                // Show exact match PROD values on PASS; fall back to partial-match values on FAIL
                validationSelectItems.Add($"COALESCE(PU.{ProdDisplayAlias(i)}, PP.PART_DISP_{i}) AS {ProdDisplayAlias(i)}");
                joinConditions.Add($"PU.{ProdKeyAlias(i)} = VD.{ValpacKeyAlias(i)}");
            }
            validationSelectItems.Add($"PU.{MatchMarkerAlias}");
            // Flag whether the first-column (student) partial match found anything — drives exception categorisation
            validationSelectItems.Add($"CASE WHEN PP.PART_KEY_0 IS NOT NULL THEN 1 ELSE 0 END AS PARTIAL_MATCH_FOUND");
            if (!string.IsNullOrWhiteSpace(col049))
            {
                validationSelectItems.Add("VD.VALPAC_049_DISP");
                validationSelectItems.Add("VD.VALPAC_049_KEY");
            }

            return $@"
WITH ValpacData AS
(
    SELECT
{BuildIndentedList(valpacSelectItems, "        ")}
    FROM [{valpacTable}] V
),
ProdUnique AS
(
    SELECT
{BuildIndentedList(prodSelectItems, "        ")}
    FROM [{prodTable}] P
    GROUP BY {string.Join("," + Environment.NewLine + "        ", prodGroupByItems)}
),
ProdPartial AS
(
    SELECT
{BuildIndentedList(partialSelectItems, "        ")}
    FROM [{prodTable}] P
    GROUP BY UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), P.[{mappings[0].ProdColumn}]))))
),
ValidationResults AS
(
    SELECT
{BuildIndentedList(validationSelectItems, "        ")}
    FROM ValpacData VD
    LEFT JOIN ProdUnique PU
        ON {string.Join(Environment.NewLine + "        AND ", joinConditions)}
    LEFT JOIN ProdPartial PP
        ON PP.PART_KEY_0 = VD.{ValpacKeyAlias(0)}
)";
        }

        private static string BuildPopulationCountSql(
            string valpacTable,
            string prodTable,
            IReadOnlyList<Rule51ColumnMapping> mappings,
            string? col049 = null,
            IReadOnlyList<string>? saNationalValues = null,
            IReadOnlyList<string>? zPlaceholders = null,
            string? cregTable = null,
            string? cregIdCol = null,
            string? cregCompletionCol = null,
            IReadOnlyList<string>? cregCompletionValues = null)
        {
            var exemptWhen = BuildForeignNationalExemptWhen(col049, saNationalValues, mappings, zPlaceholders);
            var hasCregCheck = !string.IsNullOrWhiteSpace(cregTable) && !string.IsNullOrWhiteSpace(cregIdCol);
            var hasCregCompletionCheck = hasCregCheck && !string.IsNullOrWhiteSpace(cregCompletionCol) && cregCompletionValues != null && cregCompletionValues.Count > 0;
            var passExpr = string.IsNullOrEmpty(exemptWhen)
                ? $"{MatchMarkerAlias} IS NOT NULL"
                : $"({MatchMarkerAlias} IS NOT NULL OR ({exemptWhen}))";
            var studentPassFlagsCte = string.IsNullOrEmpty(exemptWhen)
                ? $@",
StudentPassFlags AS
(
    SELECT DISTINCT {ValpacKeyAlias(0)} AS SPF_STUDENT_KEY
    FROM ValidationResults
    WHERE {MatchMarkerAlias} IS NOT NULL
)"
                : $@",
StudentPassFlags AS
(
    SELECT DISTINCT {ValpacKeyAlias(0)} AS SPF_STUDENT_KEY
    FROM ValidationResults
    WHERE {MatchMarkerAlias} IS NOT NULL
       OR ({exemptWhen})
)";
            var cregStudentsCte = hasCregCheck
                ? $@",
CregStudents AS
(
    SELECT DISTINCT UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{cregIdCol}])))) AS CREG_STUDENT_KEY
    FROM [{cregTable}]
)"
                : "";
            var cregWithdrawnCte = hasCregCompletionCheck
                ? $@",
CregWithdrawnStudents AS
(
    SELECT DISTINCT UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{cregIdCol}])))) AS CREG_WD_STUDENT_KEY
    FROM [{cregTable}]
    WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{cregCompletionCol}])))) IN ({BuildInClauseSql(cregCompletionValues!)})
)"
                : "";
            var cregJoin = hasCregCheck ? $"\nLEFT JOIN CregStudents ON CREG_STUDENT_KEY = {ValpacKeyAlias(0)}" : "";
            var cregWithdrawnJoin = hasCregCompletionCheck ? $"\nLEFT JOIN CregWithdrawnStudents ON CREG_WD_STUDENT_KEY = {ValpacKeyAlias(0)}" : "";
            var notInCregExpr = hasCregCheck
                ? $",\n    SUM(CASE WHEN NOT ({passExpr}) AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NULL THEN 1 ELSE 0 END) AS NotInCregCount"
                : ",\n    0 AS NotInCregCount";
            var cregWithdrawnExpr = hasCregCompletionCheck
                ? $",\n    SUM(CASE WHEN NOT ({passExpr}) AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NOT NULL AND CREG_WD_STUDENT_KEY IS NOT NULL THEN 1 ELSE 0 END) AS CregWithdrawnCount"
                : ",\n    0 AS CregWithdrawnCount";
            var missingExpr = hasCregCheck
                ? (hasCregCompletionCheck
                    ? $"CASE WHEN NOT ({passExpr}) AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NOT NULL AND CREG_WD_STUDENT_KEY IS NULL THEN 1 ELSE 0 END"
                    : $"CASE WHEN NOT ({passExpr}) AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NOT NULL THEN 1 ELSE 0 END")
                : $"CASE WHEN NOT ({passExpr}) AND SPF_STUDENT_KEY IS NULL THEN 1 ELSE 0 END";

            return $@"
{BuildSourceCtes(valpacTable, prodTable, mappings, col049)}{studentPassFlagsCte}{cregStudentsCte}{cregWithdrawnCte}
SELECT
    COUNT(1) AS TotalTested,
    SUM(CASE WHEN {passExpr} OR SPF_STUDENT_KEY IS NOT NULL THEN 1 ELSE 0 END) AS MatchedCount,
    SUM({missingExpr}) AS MissingCount,
    SUM(CASE WHEN NOT ({passExpr}) AND SPF_STUDENT_KEY IS NOT NULL THEN 1 ELSE 0 END) AS PassReviewCount{notInCregExpr}{cregWithdrawnExpr}
FROM ValidationResults
LEFT JOIN StudentPassFlags ON SPF_STUDENT_KEY = {ValpacKeyAlias(0)}{cregJoin}{cregWithdrawnJoin};";
        }

        private static string BuildInClauseSql(IReadOnlyList<string> values) =>
            string.Join(",", values.Select(v => $"'{v}'"));

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

        private static string BuildAllRowsSql(
            string valpacTable,
            string prodTable,
            int? maxRows,
            IReadOnlyList<Rule51ColumnMapping> mappings,
            string? col049 = null,
            IReadOnlyList<string>? saNationalValues = null,
            IReadOnlyList<string>? zPlaceholders = null,
            string? cregTable = null,
            string? cregIdCol = null,
            string? cregCompletionCol = null,
            IReadOnlyList<string>? cregCompletionValues = null)
        {
            var top = maxRows.HasValue && maxRows.Value > 0 ? $"TOP {maxRows.Value}" : string.Empty;
            var exemptWhen = BuildForeignNationalExemptWhen(col049, saNationalValues, mappings, zPlaceholders);
            var hasCregCheck = !string.IsNullOrWhiteSpace(cregTable) && !string.IsNullOrWhiteSpace(cregIdCol);
            var hasCregCompletionCheck = hasCregCheck && !string.IsNullOrWhiteSpace(cregCompletionCol) && cregCompletionValues != null && cregCompletionValues.Count > 0;

            // StudentPassFlags: students with at least one fully-matched (or exempt) PASS row.
            var studentPassFlagsCte = string.IsNullOrEmpty(exemptWhen)
                ? $@",
StudentPassFlags AS
(
    SELECT DISTINCT {ValpacKeyAlias(0)} AS SPF_STUDENT_KEY
    FROM ValidationResults
    WHERE {MatchMarkerAlias} IS NOT NULL
)"
                : $@",
StudentPassFlags AS
(
    SELECT DISTINCT {ValpacKeyAlias(0)} AS SPF_STUDENT_KEY
    FROM ValidationResults
    WHERE {MatchMarkerAlias} IS NOT NULL
       OR ({exemptWhen})
)";

            // CregStudents: all distinct student numbers present in CREG (optional)
            var cregStudentsCte = hasCregCheck
                ? $@",
CregStudents AS
(
    SELECT DISTINCT UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{cregIdCol}])))) AS CREG_STUDENT_KEY
    FROM [{cregTable}]
)"
                : "";

            // CregWithdrawnStudents: one row per student in CREG whose completion status (e.g. _032) matches the configured filter (e.g. 'W'),
            // pre-aggregated here (GROUP BY) so the main query only needs a single hash join — not a per-row correlated subquery.
            var cregWithdrawnCte = hasCregCompletionCheck
                ? $@",
CregWithdrawnStudents AS
(
    SELECT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{cregIdCol}])))) AS CREG_WD_STUDENT_KEY,
        MIN(CONVERT(nvarchar(255), [{cregCompletionCol}])) AS CREG_WD_STATUS_DISP
    FROM [{cregTable}]
    WHERE UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{cregCompletionCol}])))) IN ({BuildInClauseSql(cregCompletionValues!)})
    GROUP BY UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), [{cregIdCol}]))))
)"
                : "";

            // Build the Validation_Result / Validation_Explanation CASE expressions as ordered WHEN clauses:
            // PASS > Foreign-national exempt PASS > PASS_REVIEW (passed on primary qual) > NOT_IN_CREG > CREG_WITHDRAWN > FAIL
            var resultWhens = new List<string> { $"WHEN {MatchMarkerAlias} IS NOT NULL THEN 'PASS'" };
            var explanationWhens = new List<string> { $"WHEN {MatchMarkerAlias} IS NOT NULL THEN 'VALPAC record found in PRODUCTION.'" };

            if (!string.IsNullOrEmpty(exemptWhen))
            {
                resultWhens.Add($"WHEN {exemptWhen} THEN 'PASS'");
                explanationWhens.Add($"WHEN {exemptWhen} THEN 'Foreign national exemption: {col049} is not SA/PR, ID is all-Z placeholder, PROD IADIDNO is blank.'");
            }

            resultWhens.Add("WHEN SPF_STUDENT_KEY IS NOT NULL THEN 'PASS_REVIEW'");
            explanationWhens.Add("WHEN SPF_STUDENT_KEY IS NOT NULL THEN 'Student passed on primary qualification in PRODUCTION. This additional VALPAC qualification record is not an exception.'");

            if (hasCregCheck)
            {
                resultWhens.Add("WHEN CREG_STUDENT_KEY IS NULL THEN 'NOT_IN_CREG'");
                explanationWhens.Add($"WHEN CREG_STUDENT_KEY IS NULL THEN 'Student not found in CREG table [{cregTable}]. The university did not provide a service for this student, so absence from PRODUCTION is expected. Not an exception.'");
            }

            if (hasCregCompletionCheck)
            {
                resultWhens.Add("WHEN CREG_WD_STUDENT_KEY IS NOT NULL THEN 'CREG_WITHDRAWN'");
                explanationWhens.Add($"WHEN CREG_WD_STUDENT_KEY IS NOT NULL THEN 'Student found in CREG with completion status [{cregCompletionCol}] matching the configured withdrawal filter. The student withdrew from the course registration, so absence from PRODUCTION is expected. Not an exception.'");
            }

            var validationResultExpr = $"CASE\n        {string.Join("\n        ", resultWhens)}\n        ELSE 'FAIL'\n    END";
            var validationExplanationExpr = $"CASE\n        {string.Join("\n        ", explanationWhens)}\n        ELSE 'VALPAC record not found in PRODUCTION.'\n    END";

            var isForeignExemptExpr = string.IsNullOrEmpty(exemptWhen)
                ? "0"
                : $"CASE WHEN {MatchMarkerAlias} IS NULL AND ({exemptWhen}) THEN 1 ELSE 0 END";

            var selectItems = new List<string>
            {
                "1 AS Control_Sort",
                "'Control_1' AS Control_Type",
                $"'CONTROL 1: [{valpacTable}] data exists in [{prodTable}]' AS Control_Label",
                $"{validationResultExpr} AS Validation_Result",
                $"{validationExplanationExpr} AS Validation_Explanation"
            };

            selectItems.AddRange(mappings.Select((_, index) => ValpacDisplayAlias(index)));
            selectItems.AddRange(mappings.Select((_, index) => ProdDisplayAlias(index)));
            selectItems.Add("PARTIAL_MATCH_FOUND");
            selectItems.Add($"{isForeignExemptExpr} AS FOREIGN_NATIONAL_EXEMPT");
            if (!string.IsNullOrWhiteSpace(col049))
                selectItems.Add("VALPAC_049_DISP");
            if (hasCregCompletionCheck)
                selectItems.Add("CREG_WD_STATUS_DISP AS CREG_COMPLETION_STATUS_DISP");

            var cregJoin = hasCregCheck
                ? $"\nLEFT JOIN CregStudents ON CREG_STUDENT_KEY = {ValpacKeyAlias(0)}"
                : "";
            var cregWithdrawnJoin = hasCregCompletionCheck
                ? $"\nLEFT JOIN CregWithdrawnStudents ON CREG_WD_STUDENT_KEY = {ValpacKeyAlias(0)}"
                : "";

            return $@"
{BuildSourceCtes(valpacTable, prodTable, mappings, col049)}{studentPassFlagsCte}{cregStudentsCte}{cregWithdrawnCte}
SELECT {top}
{BuildIndentedList(selectItems, "    ")}
FROM ValidationResults
LEFT JOIN StudentPassFlags ON SPF_STUDENT_KEY = {ValpacKeyAlias(0)}{cregJoin}{cregWithdrawnJoin}
ORDER BY {BuildOrderByClause(mappings.Count, maxRows.HasValue && maxRows.Value > 0, hasCregCheck, hasCregCompletionCheck)};";
        }

        // Returns the SQL WHEN condition (without the WHEN keyword) for the foreign-national exemption.
        // Returns empty string if _049 is not configured.
        private static string BuildForeignNationalExemptWhen(
            string? col049,
            IReadOnlyList<string>? saNationalValues,
            IReadOnlyList<Rule51ColumnMapping> mappings,
            IReadOnlyList<string>? zPlaceholders = null)
        {
            if (string.IsNullOrWhiteSpace(col049) || mappings.Count < 2)
                return string.Empty;

            var saList = saNationalValues != null && saNationalValues.Count > 0
                ? string.Join(",", saNationalValues.Select(v => $"'{v.Trim().ToUpperInvariant()}'"))
                : "'SA','PR'";

            var zList = zPlaceholders != null && zPlaceholders.Count > 0
                ? string.Join(",", zPlaceholders.Select(v => $"'{v.Trim().ToUpperInvariant()}'"))
                : "'ZZZZZZZZZZZZZ'";

            var idKeyAlias  = ValpacKeyAlias(1);   // normalised _008 — now flows through ValidationResults
            var prodIdAlias = ProdDisplayAlias(1);  // COALESCE of full/partial PROD IADIDNO

            return $@"VALPAC_049_KEY NOT IN ({saList})
        AND {idKeyAlias} IN ({zList})
        AND COALESCE(UPPER(LTRIM(RTRIM(CONVERT(nvarchar(255), {prodIdAlias})))), '') = ''";
        }

        private async Task<List<Rule51ValidationRowRecord>> LoadRowsAsync(
            SqlConnection connection,
            string valpacTable,
            string prodTable,
            int? maxRows,
            IReadOnlyList<Rule51ColumnMapping> mappings,
            string? col049 = null,
            IReadOnlyList<string>? saValues = null,
            IReadOnlyList<string>? zPlaceholders = null,
            string? cregTable = null,
            string? cregIdCol = null,
            string? cregCompletionCol = null,
            IReadOnlyList<string>? cregCompletionValues = null)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = BuildAllRowsSql(valpacTable, prodTable, maxRows, mappings, col049, saValues, zPlaceholders, cregTable, cregIdCol, cregCompletionCol, cregCompletionValues);
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Rule51ValidationRowRecord>();
            while (await reader.ReadAsync())
            {
                var displayValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                    displayValues[reader.GetName(i)] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);

                rows.Add(new Rule51ValidationRowRecord
                {
                    ValidationNumber = rows.Count + 1,
                    ControlType = ReadValue(displayValues, "Control_Type"),
                    ControlLabel = ReadValue(displayValues, "Control_Label"),
                    ValidationResult = ReadValue(displayValues, "Validation_Result"),
                    ValidationExplanation = ReadValue(displayValues, "Validation_Explanation"),
                    DisplayValues = displayValues
                });
                EnrichDisplayValues(rows[^1], mappings);
            }
            return rows;
        }

        private static void EnrichDisplayValues(Rule51ValidationRowRecord row, IReadOnlyList<Rule51ColumnMapping> mappings)
        {
            var v = row.DisplayValues;
            var result = ReadValue(v, "Validation_Result") ?? "";
            var isPass = string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase);
            var isPassReview  = string.Equals(result, "PASS_REVIEW",  StringComparison.OrdinalIgnoreCase);
            var isNotInCreg   = string.Equals(result, "NOT_IN_CREG",  StringComparison.OrdinalIgnoreCase);
            var isCregWithdrawn = string.Equals(result, "CREG_WITHDRAWN", StringComparison.OrdinalIgnoreCase);
            var valpacRef = BuildDisplayReference(v, mappings, useProdValues: false);

            if (isPass)
            {
                var isForeignNationalExempt = string.Equals(ReadValue(v, "FOREIGN_NATIONAL_EXEMPT"), "1");
                if (isForeignNationalExempt)
                {
                    var citizenVal = ReadValue(v, "VALPAC_049_DISP") ?? "";
                    var idVal = ReadValue(v, ValpacDisplayAlias(1)) ?? "";
                    v["FINAL_RESULT_MESSAGE"] = $"PASS (Exempt): Foreign national — citizen/resident status ({citizenVal}) is not SA/PR, ID placeholder ({idVal}) is expected, PROD IADIDNO is blank.";
                    v["EXCEPTION_REASON"] = $"Exempt: _049 = '{citizenVal}' (not SA/PR), _008 = '{idVal}' (all-Z placeholder for foreign national with no SA ID), PRODUCTION IADIDNO is blank — no ID number required.";
                    v["EXCEPTION_CATEGORY"] = "PASS__FOREIGN_NATIONAL";
                }
                else
                {
                    var prodRef = BuildDisplayReference(v, mappings, useProdValues: true);
                    v["FINAL_RESULT_MESSAGE"] = $"PASS: VALPAC record matched in PRODUCTION. VALPAC: {valpacRef} | PRODUCTION: {prodRef}";
                    v["EXCEPTION_REASON"] = "";
                    v["EXCEPTION_CATEGORY"] = "PASS";
                }
            }
            else if (isPassReview)
            {
                var stNo = ReadValue(v, ValpacDisplayAlias(0)) ?? "";
                var qualVal = mappings.Count > 1 ? ReadValue(v, ValpacDisplayAlias(1)) ?? "" : "";
                var prodQualVal = mappings.Count > 1 ? ReadValue(v, ProdDisplayAlias(1)) ?? "" : "";
                var reviewNote = mappings.Count > 1 && !string.IsNullOrWhiteSpace(qualVal)
                    ? $"Qualification ({mappings[1].ValpacColumn}): VALPAC='{qualVal}' ≠ PROD='{prodQualVal}'. "
                    : "";
                v["FINAL_RESULT_MESSAGE"] = $"PASS (Review): Student No (_007 = '{stNo}') passed on primary qualification in PRODUCTION. {reviewNote}No exception — this additional VALPAC record does not require a match.";
                v["EXCEPTION_REASON"] = $"Student passed on primary qualification. {reviewNote}PRODUCTION stores only the primary qualification record; this additional VALPAC entry is not expected in PRODUCTION.";
                v["EXCEPTION_CATEGORY"] = "PASS_REVIEW";
            }
            else if (isNotInCreg)
            {
                var stNo = ReadValue(v, ValpacDisplayAlias(0)) ?? "";
                var label0 = mappings.Count > 0 ? mappings[0].Label : mappings[0].ValpacColumn;
                v["FINAL_RESULT_MESSAGE"] = $"NOT IN CREG: Student No ({mappings[0].ValpacColumn} = '{stNo}') not found in CREG. The university did not provide a service for this student — absence from PRODUCTION is expected. Not an exception.";
                v["EXCEPTION_REASON"] = $"Student ({label0} = '{stNo}') does not exist in the CREG table. The university never registered a service record for this student, so the student is not expected to appear in PRODUCTION.";
                v["EXCEPTION_CATEGORY"] = "NOT_IN_CREG";
            }
            else if (isCregWithdrawn)
            {
                var stNo = ReadValue(v, ValpacDisplayAlias(0)) ?? "";
                var label0 = mappings.Count > 0 ? mappings[0].Label : mappings[0].ValpacColumn;
                var completionVal = ReadValue(v, "CREG_COMPLETION_STATUS_DISP") ?? "";
                v["FINAL_RESULT_MESSAGE"] = $"WITHDREW (CREG): {label0} ({mappings[0].ValpacColumn} = '{stNo}') not found in PRODUCTION, but is in CREG with completion status = '{completionVal}'. The completion status for this student on CREG is withdrew from the course registration, so absence from PRODUCTION is expected. Not an exception.";
                v["EXCEPTION_REASON"] = $"{label0} ({mappings[0].ValpacColumn} = '{stNo}') does not exist in PRODUCTION table, but was found in CREG with completion status = '{completionVal}', matching the configured withdrawal filter. The student withdrew from the course registration, so the student is not expected to appear in PRODUCTION.";
                v["EXCEPTION_CATEGORY"] = "CREG_WITHDRAWN";
            }
            else
            {
                var partialFound = string.Equals(ReadValue(v, "PARTIAL_MATCH_FOUND"), "1");
                if (!partialFound)
                {
                    // Student (first key) not found in PRODUCTION at all
                    var stNo = ReadValue(v, ValpacDisplayAlias(0)) ?? "";
                    var label0 = mappings.Count > 0 ? mappings[0].Label : mappings[0].ValpacColumn;
                    v["FINAL_RESULT_MESSAGE"] = $"FAIL: {label0} ({mappings[0].ValpacColumn} = '{stNo}') not found in PRODUCTION.";
                    v["EXCEPTION_REASON"] = $"{label0} ({mappings[0].ValpacColumn} = '{stNo}') does not exist in PRODUCTION table. The record cannot be matched because the student is not present.";
                    v["EXCEPTION_CATEGORY"] = $"NOT_FOUND__{label0}";
                }
                else
                {
                    // Student found by first key — find which columns differ
                    var diffParts = new List<string>();
                    var categories = new List<string>();
                    for (var i = 1; i < mappings.Count; i++)
                    {
                        var valpacVal = (ReadValue(v, ValpacDisplayAlias(i)) ?? "").Trim();
                        var prodVal   = (ReadValue(v, ProdDisplayAlias(i)) ?? "").Trim();
                        if (!string.Equals(valpacVal, prodVal, StringComparison.OrdinalIgnoreCase))
                        {
                            var lbl = !string.IsNullOrWhiteSpace(mappings[i].Label) ? mappings[i].Label : mappings[i].ValpacColumn;
                            diffParts.Add($"{lbl} ({mappings[i].ValpacColumn}): VALPAC='{valpacVal}' ≠ PROD='{prodVal}'");
                            categories.Add($"DIFF__{lbl}");
                        }
                    }
                    // Also check first column even though it matched (defensive)
                    var diffSummary = diffParts.Count > 0
                        ? string.Join("; ", diffParts)
                        : "values differ";
                    var categoryKey = categories.Count == 1 ? categories[0]
                        : categories.Count > 1 ? "DIFF__MULTIPLE"
                        : "DIFF__UNKNOWN";
                    v["FINAL_RESULT_MESSAGE"] = $"FAIL: {mappings[0].Label} found in PRODUCTION but record does not match. {diffSummary}.";
                    v["EXCEPTION_REASON"] = $"{mappings[0].Label} ({mappings[0].ValpacColumn} = '{ReadValue(v, ValpacDisplayAlias(0))}') exists in PRODUCTION, but the full record does not match: {diffSummary}.";
                    v["EXCEPTION_CATEGORY"] = categoryKey;
                }
            }

            row.ValidationExplanation = ReadValue(v, "EXCEPTION_REASON") is { Length: > 0 } reason
                ? reason
                : ReadValue(v, "FINAL_RESULT_MESSAGE") ?? "";
        }

        private static List<Rule51ExceptionCategoryViewModel> BuildExceptionCategories(
            IReadOnlyList<Rule51ValidationRowRecord> rows,
            IReadOnlyList<Rule51ColumnMapping> mappings)
        {
            var counts = new Dictionary<string, (string Description, int Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var cat = ReadValue(row.DisplayValues, "EXCEPTION_CATEGORY") ?? "";
                // Skip PASS, PASS_REVIEW, NOT_IN_CREG and CREG_WITHDRAWN (not exceptions); include PASS__FOREIGN_NATIONAL for auditor visibility
                if (string.IsNullOrEmpty(cat)
                    || string.Equals(cat, "PASS", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cat, "PASS_REVIEW", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cat, "NOT_IN_CREG", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(cat, "CREG_WITHDRAWN", StringComparison.OrdinalIgnoreCase))
                    continue;

                string desc;
                if (string.Equals(cat, "PASS__FOREIGN_NATIONAL", StringComparison.OrdinalIgnoreCase))
                {
                    desc = "Exempt — foreign national (not SA/PR): ID is all-Z placeholder, PROD IADIDNO is blank";
                }
                else if (cat.StartsWith("NOT_FOUND__", StringComparison.OrdinalIgnoreCase))
                {
                    var lbl = cat["NOT_FOUND__".Length..];
                    desc = $"{lbl} not found in PRODUCTION";
                }
                else if (cat.StartsWith("DIFF__", StringComparison.OrdinalIgnoreCase))
                {
                    var lbl = cat["DIFF__".Length..];
                    desc = lbl == "MULTIPLE" ? "Multiple columns differ"
                         : lbl == "UNKNOWN"  ? "Record found but mismatch (unknown column)"
                         : $"{lbl} differs";
                }
                else
                {
                    desc = cat;
                }

                if (counts.TryGetValue(cat, out var existing))
                    counts[cat] = (existing.Description, existing.Count + 1);
                else
                    counts[cat] = (desc, 1);
            }

            return counts
                .OrderByDescending(kv => kv.Value.Count)
                .Select(kv => new Rule51ExceptionCategoryViewModel
                {
                    Category    = kv.Key,
                    Description = kv.Value.Description,
                    Count       = kv.Value.Count
                })
                .ToList();
        }

        private static List<Rule51ControlSummaryItemViewModel> BuildControlSummaries(int total, int matched, string valpacTable, string prodTable, int mappingCount)
        {
            var fail = Math.Max(total - matched, 0);
            return new List<Rule51ControlSummaryItemViewModel>
            {
                new()
                {
                    ControlType  = "Control_1",
                    ControlLabel = "Control 1",
                    CriteriaText = $"All {valpacTable} records exist in {prodTable} ({mappingCount} mapped column pair{(mappingCount == 1 ? "" : "s")})",
                    TotalCount   = total,
                    PassCount    = matched,
                    FailCount    = fail,
                    Status       = fail == 0 ? "PASS" : "FAIL"
                }
            };
        }

        private static List<string> BuildProcedureSteps(string valpacTable, string prodTable, IReadOnlyList<Rule51ColumnMapping> mappings) => new()
        {
            $"Select all records from {valpacTable} as the population to test.",
            $"For each VALPAC record, attempt to find a matching row in {prodTable} using {mappings.Count} selected column pair{(mappings.Count == 1 ? "" : "s")}.",
            "Mark PASS when a matching row exists in PRODUCTION; FAIL when no match is found.",
            "All VALPAC data is expected to exist in PRODUCTION."
        };

        // ─── Save / Persist ───────────────────────────────────────────────────

        private async Task<int> SaveValidationRunAsync(Rule51ValidationRequest request, Rule51ValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await using var connection = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(connection, request.ClientId);
            await MarkPreviousRunsHistoricalAsync(connection, request.ClientId, 51);

            var systemUserId = await GetSystemUserIdByEmailAsync(connection, userEmail);
            if (!systemUserId.HasValue) throw new InvalidOperationException("The current analyst could not be resolved.");

            var previousHash = await GetLatestValidationHashAsync(connection, request.ClientId, 51);
            var failRows     = summary.ReviewRows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).ToList();

            await using var cmd = connection.CreateConfiguredCommand();
            cmd.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID,UserID,RuleNumber,RuleName,Status,TotalRecords,PassCount,FailCount,ExceptionRate,RunTimestamp,
 HemisServer,AuditDatabase,StudTable,DeceasedTable,StudColumn,DeceasedColumn,
 ExceptionsJSON,ResultsJSON,RunByUserName,LastEditedByUserName,LastEditedAt,PreviousHash,RecordHash,WorkspaceSavedAt,IsCurrent)
VALUES
(@ClientID,@UserID,51,@RuleName,@Status,@TotalRecords,@PassCount,@FailCount,@ExceptionRate,GETDATE(),
 @HemisServer,@AuditDatabase,@StudTable,@DeceasedTable,NULL,NULL,
 @ExceptionsJSON,@ResultsJSON,@RunByUserName,NULL,NULL,@PreviousHash,NULL,@WorkspaceSavedAt,1);
SELECT CAST(SCOPE_IDENTITY() AS int);";
            cmd.Parameters.AddWithValue("@ClientID", request.ClientId);
            cmd.Parameters.AddWithValue("@UserID", systemUserId.Value);
            cmd.Parameters.AddWithValue("@RuleName", "VALPAC Data in Production");
            cmd.Parameters.AddWithValue("@Status", summary.Status);
            cmd.Parameters.AddWithValue("@TotalRecords", summary.TotalValidated);
            cmd.Parameters.AddWithValue("@PassCount", summary.PassCount);
            cmd.Parameters.AddWithValue("@FailCount", summary.FailCount);
            cmd.Parameters.AddWithValue("@ExceptionRate", summary.ExceptionRate);
            cmd.Parameters.AddWithValue("@HemisServer", request.Server);
            cmd.Parameters.AddWithValue("@AuditDatabase", request.Database);
            cmd.Parameters.AddWithValue("@StudTable", request.ValpacTable);
            cmd.Parameters.AddWithValue("@DeceasedTable", request.ProdTable);
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
            hashCmd.Parameters.AddWithValue("@RecordHash", ComputeHash($"ValidationRun|Rule51|{runId}|{request.ClientId}|{systemUserId.Value}|{summary.Status}|{summary.TotalValidated}|{summary.FailCount}|{summary.Timestamp}|{previousHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            await UpdateStoredSummaryAsync(connection, runId, summary);
            return runId;
        }

        // ─── Preview / Clone ─────────────────────────────────────────────────

        private static void ApplyBrowserPreview(Rule51ValidationSummary summary)
        {
            var rows = summary.ReviewRows;
            if (rows.Count <= BrowserPreviewRowLimit) { summary.DisplayedCount = rows.Count; summary.IsPreviewOnly = false; summary.PreviewLimit = 0; return; }

            // FAIL rows (missing from PRODUCTION) always take priority in the browser preview
            var failRows = rows.Where(r => string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.ValidationNumber).ToList();
            var passRows = rows.Where(r => !string.Equals(r.ValidationResult, "FAIL", StringComparison.OrdinalIgnoreCase)).OrderBy(r => r.ValidationNumber).ToList();

            // Split preview so both PASS and FAIL rows are always visible
            int halfLimit = BrowserPreviewRowLimit / 2;
            int failTake = Math.Min(failRows.Count, passRows.Count > 0 ? halfLimit : BrowserPreviewRowLimit);
            int passTake = Math.Min(passRows.Count, BrowserPreviewRowLimit - failTake);
            int extra    = BrowserPreviewRowLimit - failTake - passTake;
            failTake = Math.Min(failRows.Count, failTake + extra);
            passTake = Math.Min(passRows.Count, BrowserPreviewRowLimit - failTake);

            var selected = new List<Rule51ValidationRowRecord>();
            selected.AddRange(failRows.Take(failTake));
            selected.AddRange(passRows.Take(passTake));

            var preview = selected.Take(BrowserPreviewRowLimit).ToList();
            summary.ReviewRows     = preview;
            summary.DisplayedCount = preview.Count;
            summary.IsPreviewOnly  = summary.TotalValidated > preview.Count;
            summary.PreviewLimit   = preview.Count;
        }

        private static Rule51ValidationSummary CloneSummary(Rule51ValidationSummary s) => new()
        {
            Success = s.Success, ValpacRecordCount = s.ValpacRecordCount, ProdRecordCount = s.ProdRecordCount,
            TotalRequested = s.TotalRequested, TotalValidated = s.TotalValidated, DisplayedCount = s.DisplayedCount,
            IsPreviewOnly = s.IsPreviewOnly, PreviewLimit = s.PreviewLimit, PassCount = s.PassCount, FailCount = s.FailCount,
            ExceptionRate = s.ExceptionRate, Status = s.Status, Timestamp = s.Timestamp, Database = s.Database,
            ValpacTable = s.ValpacTable, ProdTable = s.ProdTable,
            ValpacCol007 = s.ValpacCol007, ValpacCol008 = s.ValpacCol008, ValpacCol001 = s.ValpacCol001, ValpacColYear = s.ValpacColYear,
            ProdColStNo = s.ProdColStNo, ProdColIdNo = s.ProdColIdNo, ProdColQual = s.ProdColQual, ProdColYear = s.ProdColYear,
            ValpacCol049 = s.ValpacCol049, SaNationalValues = s.SaNationalValues,
            ValpacCol008ZPlaceholders = s.ValpacCol008ZPlaceholders,
            ForeignNationalExemptCount = s.ForeignNationalExemptCount,
            PassWithReviewCount = s.PassWithReviewCount, NotInCregCount = s.NotInCregCount, CregWithdrawnCount = s.CregWithdrawnCount,
            CregTable = s.CregTable, CregIdCol = s.CregIdCol,
            CregCompletionStatusCol = s.CregCompletionStatusCol, CregCompletionStatusValues = s.CregCompletionStatusValues,
            ColumnMappings = CloneMappings(s.ColumnMappings),
            TableLinkageText = s.TableLinkageText, RuleModeText = s.RuleModeText, ProcedureSteps = s.ProcedureSteps.ToList(),
            ClientId = s.ClientId, SavedRunId = s.SavedRunId,
            ControlSummaries = s.ControlSummaries.Select(i => new Rule51ControlSummaryItemViewModel
            {
                ControlType = i.ControlType, ControlLabel = i.ControlLabel, CriteriaText = i.CriteriaText,
                TotalCount = i.TotalCount, PassCount = i.PassCount, FailCount = i.FailCount, Status = i.Status
            }).ToList(),
            ExceptionCategories = s.ExceptionCategories.Select(c => new Rule51ExceptionCategoryViewModel
            {
                Category = c.Category, Description = c.Description, Count = c.Count
            }).ToList(),
            ReviewRows = s.ReviewRows.Select(r => new Rule51ValidationRowRecord
            {
                ValidationNumber = r.ValidationNumber, ControlType = r.ControlType, ControlLabel = r.ControlLabel,
                ValidationResult = r.ValidationResult, ValidationExplanation = r.ValidationExplanation,
                DisplayValues = new Dictionary<string, string?>(r.DisplayValues, StringComparer.OrdinalIgnoreCase)
            }).ToList(),
            Warning = s.Warning, Error = s.Error
        };

        private static Rule51ValidationRequest CloneRequest(Rule51ValidationRequest r) => new()
        {
            ClientId = r.ClientId, RunId = r.RunId, Server = r.Server, Database = r.Database, Driver = r.Driver,
            ValpacTable = r.ValpacTable, ProdTable = r.ProdTable,
            ValpacCol007 = r.ValpacCol007, ValpacCol008 = r.ValpacCol008, ValpacCol001 = r.ValpacCol001, ValpacColYear = r.ValpacColYear,
            ProdColStNo = r.ProdColStNo, ProdColIdNo = r.ProdColIdNo, ProdColQual = r.ProdColQual, ProdColYear = r.ProdColYear,
            ValpacCol049 = r.ValpacCol049, SaNationalValues = r.SaNationalValues,
            ValpacCol008ZPlaceholders = r.ValpacCol008ZPlaceholders,
            CregTable = r.CregTable, CregIdCol = r.CregIdCol,
            CregCompletionStatusCol = r.CregCompletionStatusCol, CregCompletionStatusValues = r.CregCompletionStatusValues,
            ColumnMappings = CloneMappings(r.ColumnMappings)
        };

        private static IReadOnlyList<string> ParseSaValues(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new[] { "SA", "PR" };
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(v => v.ToUpperInvariant())
                      .Where(v => v.Length > 0)
                      .Distinct()
                      .ToArray();
        }

        private static IReadOnlyList<string> ParseZPlaceholders(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new[] { "ZZZZZZZZZZZZZ" };
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(v => v.ToUpperInvariant())
                      .Where(v => v.Length > 0)
                      .Distinct()
                      .ToArray();
        }

        private static bool RequestsMatch(Rule51ValidationRequest a, Rule51ValidationRequest b) =>
            a.ClientId == b.ClientId &&
            string.Equals(a.Server?.Trim(), b.Server?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Database?.Trim(), b.Database?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ValpacTable?.Trim(), b.ValpacTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ProdTable?.Trim(), b.ProdTable?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            MappingColumnsMatch(GetMappings(a), GetMappings(b));

        private static List<Rule51ValidationRowRecord> NormalizeRows(IEnumerable<Rule51ValidationRowRecord> rows) =>
            rows.Select((r, i) => { r.ValidationNumber = i + 1; return r; }).ToList();

        // ─── Expand Saved Summary ─────────────────────────────────────────────

        private async Task<Rule51ValidationSummary> ExpandSavedSummaryIfNeededAsync(Rule51ValidationSummary summary, string? server)
        {
            if (!summary.IsPreviewOnly && summary.ReviewRows.Count >= summary.TotalValidated) return summary;
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(summary.Database) || string.IsNullOrWhiteSpace(summary.ValpacTable)) return summary;
            try
            {
                var expanded = await AnalyseAsync(new Rule51ValidationRequest
                {
                    ClientId = summary.ClientId, RunId = summary.SavedRunId, Server = server, Database = summary.Database,
                    Driver = "ODBC Driver 17 for SQL Server", ValpacTable = summary.ValpacTable, ProdTable = summary.ProdTable,
                    ValpacCol007 = summary.ValpacCol007, ValpacCol008 = summary.ValpacCol008, ValpacCol001 = summary.ValpacCol001, ValpacColYear = summary.ValpacColYear,
                    ProdColStNo = summary.ProdColStNo, ProdColIdNo = summary.ProdColIdNo, ProdColQual = summary.ProdColQual, ProdColYear = summary.ProdColYear,
                    ValpacCol049 = summary.ValpacCol049, SaNationalValues = summary.SaNationalValues,
                    ValpacCol008ZPlaceholders = summary.ValpacCol008ZPlaceholders,
                    CregTable = summary.CregTable, CregIdCol = summary.CregIdCol,
                    CregCompletionStatusCol = summary.CregCompletionStatusCol, CregCompletionStatusValues = summary.CregCompletionStatusValues,
                    ColumnMappings = CloneMappings(summary.ColumnMappings)
                }, includeAllReviewRows: true);
                expanded.Timestamp = string.IsNullOrWhiteSpace(summary.Timestamp) ? expanded.Timestamp : summary.Timestamp;
                expanded.ClientId  = summary.ClientId;
                expanded.SavedRunId = summary.SavedRunId;
                return expanded;
            }
            catch { return summary; }
        }

        private async Task<Rule51ValidationSummary> ExpandAndPersistSavedSummaryIfNeededAsync(SqlConnection connection, int runId, Rule51ValidationSummary summary, string? server)
        {
            var expanded = await ExpandSavedSummaryIfNeededAsync(summary, server);
            if (!ReferenceEquals(expanded, summary))
            {
                expanded.SavedRunId = runId;
                await UpdateStoredSummaryAsync(connection, runId, expanded);
            }
            return expanded;
        }

        // ─── DB Helpers ───────────────────────────────────────────────────────

        private const string MatchMarkerAlias = "PROD_MATCH_FOUND";

        private static void ValidateRequest(Rule51ValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server)) throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database)) throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.ValpacTable)) throw new InvalidOperationException("VALPAC table is required.");
            if (string.IsNullOrWhiteSpace(request.ProdTable)) throw new InvalidOperationException("PRODUCTION table is required.");
            ValidateObjectName(request.ValpacTable); ValidateObjectName(request.ProdTable);

            var mappings = GetMappings(request);
            if (mappings.Count == 0) throw new InvalidOperationException("At least one column mapping is required.");
            foreach (var mapping in mappings)
            {
                ValidateObjectName(mapping.ValpacColumn);
                ValidateObjectName(mapping.ProdColumn);
            }

            ApplyMappings(request, mappings);
        }

        private static void ValidateRequest(Rule51VerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server)) throw new InvalidOperationException("Server name is required.");
            if (string.IsNullOrWhiteSpace(request.Database)) throw new InvalidOperationException("Database is required.");
            if (string.IsNullOrWhiteSpace(request.ValpacTable)) throw new InvalidOperationException("VALPAC table is required.");
            if (string.IsNullOrWhiteSpace(request.ProdTable)) throw new InvalidOperationException("PRODUCTION table is required.");
            ValidateObjectName(request.ValpacTable); ValidateObjectName(request.ProdTable);

            var mappings = GetMappings(request);
            if (mappings.Count == 0) throw new InvalidOperationException("At least one column mapping is required.");
            foreach (var mapping in mappings)
            {
                ValidateObjectName(mapping.ValpacColumn);
                ValidateObjectName(mapping.ProdColumn);
            }

            ApplyMappings(request, mappings);
        }

        private static List<Rule51ColumnMapping> BuildDefaultMappings() =>
            DefaultColumnMappings.Select(m => new Rule51ColumnMapping
            {
                ValpacColumn = m.ValpacColumn,
                ProdColumn = m.ProdColumn,
                Label = m.Label
            }).ToList();

        private static List<Rule51ColumnMapping> BuildLegacyMappings(
            string? v007,
            string? v008,
            string? v001,
            string? vYear,
            string? pSt,
            string? pId,
            string? pQual,
            string? pYear)
        {
            var mappings = new List<Rule51ColumnMapping>();
            AddLegacyMapping(mappings, v007, pSt, "Student No");
            AddLegacyMapping(mappings, v008, pId, "ID No");
            AddLegacyMapping(mappings, v001, pQual, "Qualification");
            AddLegacyMapping(mappings, vYear, pYear, "Year");
            return mappings;
        }

        private static void AddLegacyMapping(List<Rule51ColumnMapping> mappings, string? valpacColumn, string? prodColumn, string label)
        {
            if (string.IsNullOrWhiteSpace(valpacColumn) || string.IsNullOrWhiteSpace(prodColumn))
                return;

            mappings.Add(new Rule51ColumnMapping
            {
                ValpacColumn = valpacColumn.Trim(),
                ProdColumn = prodColumn.Trim(),
                Label = label
            });
        }

        private static List<Rule51ColumnMapping> NormalizeMappings(IEnumerable<Rule51ColumnMapping>? mappings, IEnumerable<Rule51ColumnMapping>? fallback)
        {
            var normalized = (mappings ?? Enumerable.Empty<Rule51ColumnMapping>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.ValpacColumn) && !string.IsNullOrWhiteSpace(m.ProdColumn))
                .Select(m => new Rule51ColumnMapping
                {
                    ValpacColumn = m.ValpacColumn.Trim(),
                    ProdColumn = m.ProdColumn.Trim(),
                    Label = string.IsNullOrWhiteSpace(m.Label) ? m.ValpacColumn.Trim() : m.Label.Trim()
                })
                .ToList();

            if (normalized.Count > 0)
                return normalized;

            var fallbackMappings = (fallback ?? BuildDefaultMappings())
                .Where(m => !string.IsNullOrWhiteSpace(m.ValpacColumn) && !string.IsNullOrWhiteSpace(m.ProdColumn))
                .Select(m => new Rule51ColumnMapping
                {
                    ValpacColumn = m.ValpacColumn.Trim(),
                    ProdColumn = m.ProdColumn.Trim(),
                    Label = string.IsNullOrWhiteSpace(m.Label) ? m.ValpacColumn.Trim() : m.Label.Trim()
                })
                .ToList();

            return fallbackMappings.Count > 0 ? fallbackMappings : BuildDefaultMappings();
        }

        private static List<Rule51ColumnMapping> GetMappings(Rule51VerifyRequest request) =>
            NormalizeMappings(request.ColumnMappings, BuildLegacyMappings(
                request.ValpacCol007, request.ValpacCol008, request.ValpacCol001, request.ValpacColYear,
                request.ProdColStNo, request.ProdColIdNo, request.ProdColQual, request.ProdColYear));

        private static List<Rule51ColumnMapping> GetMappings(Rule51ValidationRequest request) =>
            NormalizeMappings(request.ColumnMappings, BuildLegacyMappings(
                request.ValpacCol007, request.ValpacCol008, request.ValpacCol001, request.ValpacColYear,
                request.ProdColStNo, request.ProdColIdNo, request.ProdColQual, request.ProdColYear));

        private static List<Rule51ColumnMapping> GetMappings(Rule51ValidationSummary summary) =>
            NormalizeMappings(summary.ColumnMappings, BuildLegacyMappings(
                summary.ValpacCol007, summary.ValpacCol008, summary.ValpacCol001, summary.ValpacColYear,
                summary.ProdColStNo, summary.ProdColIdNo, summary.ProdColQual, summary.ProdColYear));

        private static List<Rule51ColumnMapping> GetMappings(Rule51WorkspaceStateViewModel workspace) =>
            NormalizeMappings(workspace.ColumnMappings, BuildLegacyMappings(
                workspace.ValpacCol007, workspace.ValpacCol008, workspace.ValpacCol001, workspace.ValpacColYear,
                workspace.ProdColStNo, workspace.ProdColIdNo, workspace.ProdColQual, workspace.ProdColYear));

        private static List<Rule51ColumnMapping> CloneMappings(IEnumerable<Rule51ColumnMapping>? mappings) =>
            (mappings ?? Enumerable.Empty<Rule51ColumnMapping>())
                .Select(m => new Rule51ColumnMapping
                {
                    ValpacColumn = m.ValpacColumn,
                    ProdColumn = m.ProdColumn,
                    Label = m.Label
                })
                .ToList();

        private static List<Rule51ColumnMapping> SanitizeMappings(IEnumerable<Rule51ColumnMapping> mappings) =>
            mappings.Select(m => new Rule51ColumnMapping
            {
                ValpacColumn = Sanitise(m.ValpacColumn),
                ProdColumn = Sanitise(m.ProdColumn),
                Label = string.IsNullOrWhiteSpace(m.Label) ? m.ValpacColumn.Trim() : m.Label.Trim()
            }).ToList();

        private static bool MappingColumnsMatch(IReadOnlyList<Rule51ColumnMapping> left, IReadOnlyList<Rule51ColumnMapping> right)
        {
            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i].ValpacColumn?.Trim(), right[i].ValpacColumn?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(left[i].ProdColumn?.Trim(), right[i].ProdColumn?.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static void ApplyMappings(Rule51VerifyRequest request, IReadOnlyList<Rule51ColumnMapping> mappings)
        {
            request.ColumnMappings = CloneMappings(mappings);
            request.ValpacCol007 = LegacyValpacColumn(mappings, 0, "_007");
            request.ValpacCol008 = LegacyValpacColumn(mappings, 1, "_008");
            request.ValpacCol001 = LegacyValpacColumn(mappings, 2, "_001");
            request.ValpacColYear = LegacyValpacColumn(mappings, 3, "ColYear");
            request.ProdColStNo = LegacyProdColumn(mappings, 0, "IAGSTNO");
            request.ProdColIdNo = LegacyProdColumn(mappings, 1, "IADIDNO");
            request.ProdColQual = LegacyProdColumn(mappings, 2, "IAGQUAL");
            request.ProdColYear = LegacyProdColumn(mappings, 3, "IAGCYR");
        }

        private static void ApplyMappings(Rule51ValidationRequest request, IReadOnlyList<Rule51ColumnMapping> mappings)
        {
            request.ColumnMappings = CloneMappings(mappings);
            request.ValpacCol007 = LegacyValpacColumn(mappings, 0, "_007");
            request.ValpacCol008 = LegacyValpacColumn(mappings, 1, "_008");
            request.ValpacCol001 = LegacyValpacColumn(mappings, 2, "_001");
            request.ValpacColYear = LegacyValpacColumn(mappings, 3, "ColYear");
            request.ProdColStNo = LegacyProdColumn(mappings, 0, "IAGSTNO");
            request.ProdColIdNo = LegacyProdColumn(mappings, 1, "IADIDNO");
            request.ProdColQual = LegacyProdColumn(mappings, 2, "IAGQUAL");
            request.ProdColYear = LegacyProdColumn(mappings, 3, "IAGCYR");
        }

        private static void ApplyMappings(Rule51ValidationSummary summary, IReadOnlyList<Rule51ColumnMapping> mappings)
        {
            summary.ProcedureSteps ??= new List<string>();
            summary.ReviewRows ??= new List<Rule51ValidationRowRecord>();
            summary.ColumnMappings = CloneMappings(mappings);
            summary.ValpacCol007 = LegacyValpacColumn(mappings, 0, "_007");
            summary.ValpacCol008 = LegacyValpacColumn(mappings, 1, "_008");
            summary.ValpacCol001 = LegacyValpacColumn(mappings, 2, "_001");
            summary.ValpacColYear = LegacyValpacColumn(mappings, 3, "ColYear");
            summary.ProdColStNo = LegacyProdColumn(mappings, 0, "IAGSTNO");
            summary.ProdColIdNo = LegacyProdColumn(mappings, 1, "IADIDNO");
            summary.ProdColQual = LegacyProdColumn(mappings, 2, "IAGQUAL");
            summary.ProdColYear = LegacyProdColumn(mappings, 3, "IAGCYR");

            if (!string.IsNullOrWhiteSpace(summary.ValpacTable) && !string.IsNullOrWhiteSpace(summary.ProdTable))
                summary.TableLinkageText = BuildTableLinkageText(summary.ValpacTable, summary.ProdTable, mappings);
            if (string.IsNullOrWhiteSpace(summary.RuleModeText) && !string.IsNullOrWhiteSpace(summary.ValpacTable) && !string.IsNullOrWhiteSpace(summary.ProdTable))
                summary.RuleModeText = $"100% population testing of {summary.ValpacTable} against {summary.ProdTable} on {mappings.Count} mapped column pair{(mappings.Count == 1 ? "" : "s")}";
            if (summary.ProcedureSteps.Count == 0 && !string.IsNullOrWhiteSpace(summary.ValpacTable) && !string.IsNullOrWhiteSpace(summary.ProdTable))
                summary.ProcedureSteps = BuildProcedureSteps(summary.ValpacTable, summary.ProdTable, mappings);

            HydrateLegacyDisplayAliases(summary.ReviewRows, mappings);
        }

        private static void ApplyMappings(Rule51WorkspaceStateViewModel workspace, IReadOnlyList<Rule51ColumnMapping> mappings)
        {
            workspace.ColumnMappings = CloneMappings(mappings);
            workspace.ValpacCol007 = LegacyValpacColumn(mappings, 0, "_007");
            workspace.ValpacCol008 = LegacyValpacColumn(mappings, 1, "_008");
            workspace.ValpacCol001 = LegacyValpacColumn(mappings, 2, "_001");
            workspace.ValpacColYear = LegacyValpacColumn(mappings, 3, "ColYear");
            workspace.ProdColStNo = LegacyProdColumn(mappings, 0, "IAGSTNO");
            workspace.ProdColIdNo = LegacyProdColumn(mappings, 1, "IADIDNO");
            workspace.ProdColQual = LegacyProdColumn(mappings, 2, "IAGQUAL");
            workspace.ProdColYear = LegacyProdColumn(mappings, 3, "IAGCYR");
        }

        private static string LegacyValpacColumn(IReadOnlyList<Rule51ColumnMapping> mappings, int index, string fallback) =>
            mappings.Count > index && !string.IsNullOrWhiteSpace(mappings[index].ValpacColumn)
                ? mappings[index].ValpacColumn
                : fallback;

        private static string LegacyProdColumn(IReadOnlyList<Rule51ColumnMapping> mappings, int index, string fallback) =>
            mappings.Count > index && !string.IsNullOrWhiteSpace(mappings[index].ProdColumn)
                ? mappings[index].ProdColumn
                : fallback;

        private static string BuildTableLinkageText(string valpacTable, string prodTable, IReadOnlyList<Rule51ColumnMapping> mappings) =>
            string.Join(" | ", mappings.Select(m => $"{valpacTable}.{m.ValpacColumn}<>{prodTable}.{m.ProdColumn}"));

        private static string BuildIndentedList(IEnumerable<string> items, string indent)
        {
            var materialized = items.ToList();
            return materialized.Count == 0 ? indent : indent + string.Join("," + Environment.NewLine + indent, materialized);
        }

        private static string BuildOrderByClause(int mappingCount, bool failFirst, bool hasCregCheck = false, bool hasCregCompletionCheck = false)
        {
            var items = new List<string>();
            if (failFirst)
            {
                // FAIL first (0), NOT_IN_CREG (1), CREG_WITHDRAWN (2), PASS_REVIEW (3), PASS last (4)
                var failExpr = hasCregCheck
                    ? (hasCregCompletionCheck
                        ? $"CASE WHEN {MatchMarkerAlias} IS NULL AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NOT NULL AND CREG_WD_STUDENT_KEY IS NULL THEN 0 WHEN {MatchMarkerAlias} IS NULL AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NULL THEN 1 WHEN {MatchMarkerAlias} IS NULL AND SPF_STUDENT_KEY IS NULL AND CREG_WD_STUDENT_KEY IS NOT NULL THEN 2 WHEN {MatchMarkerAlias} IS NULL THEN 3 ELSE 4 END"
                        : $"CASE WHEN {MatchMarkerAlias} IS NULL AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NOT NULL THEN 0 WHEN {MatchMarkerAlias} IS NULL AND SPF_STUDENT_KEY IS NULL AND CREG_STUDENT_KEY IS NULL THEN 1 WHEN {MatchMarkerAlias} IS NULL THEN 2 ELSE 3 END")
                    : $"CASE WHEN {MatchMarkerAlias} IS NULL AND SPF_STUDENT_KEY IS NULL THEN 0 WHEN {MatchMarkerAlias} IS NULL THEN 1 ELSE 2 END";
                items.Add(failExpr);
            }

            for (var i = 0; i < mappingCount; i++)
                items.Add(ValpacDisplayAlias(i));

            IEnumerable<string> orderedItems = items.Count == 0 ? new[] { "1" } : items;
            return string.Join(", ", orderedItems);
        }

        private static string ValpacDisplayAlias(int index) => $"VALPAC_COL_{index + 1}";
        private static string ValpacKeyAlias(int index) => $"VALPAC_KEY_{index + 1}";
        private static string ProdDisplayAlias(int index) => $"PROD_COL_{index + 1}";
        private static string ProdKeyAlias(int index) => $"PROD_KEY_{index + 1}";

        private static string BuildDisplayReference(IReadOnlyDictionary<string, string?> values, IReadOnlyList<Rule51ColumnMapping> mappings, bool useProdValues)
        {
            var parts = new List<string>();
            for (var i = 0; i < mappings.Count; i++)
            {
                var alias = useProdValues ? ProdDisplayAlias(i) : ValpacDisplayAlias(i);
                var value = ReadValue(values, alias);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var mapping = mappings[i];
                var label = !string.IsNullOrWhiteSpace(mapping.Label)
                    ? mapping.Label
                    : useProdValues ? mapping.ProdColumn : mapping.ValpacColumn;
                parts.Add($"{label}: {value}");
            }

            return parts.Count == 0 ? "selected values unavailable" : string.Join(" | ", parts);
        }

        private static void HydrateLegacyDisplayAliases(IEnumerable<Rule51ValidationRowRecord> rows, IReadOnlyList<Rule51ColumnMapping> mappings)
        {
            var legacyValpacAliases = new[] { "VALPAC__007", "VALPAC__008", "VALPAC__001", "VALPAC_COLYEAR" };
            var legacyProdAliases = new[] { "PROD_IAGSTNO", "PROD_IADIDNO", "PROD_IAGQUAL", "PROD_IAGCYR" };

            foreach (var row in rows)
            {
                var values = row.DisplayValues;
                var aliasCount = Math.Min(mappings.Count, legacyValpacAliases.Length);
                for (var i = 0; i < aliasCount; i++)
                {
                    if (!values.ContainsKey(ValpacDisplayAlias(i)) && values.TryGetValue(legacyValpacAliases[i], out var legacyValpac))
                        values[ValpacDisplayAlias(i)] = legacyValpac;
                    if (!values.ContainsKey(ProdDisplayAlias(i)) && values.TryGetValue(legacyProdAliases[i], out var legacyProd))
                        values[ProdDisplayAlias(i)] = legacyProd;
                }

                if (!values.ContainsKey(MatchMarkerAlias))
                {
                    var matched = mappings.Count > 0 && !string.IsNullOrWhiteSpace(ReadValue(values, ProdDisplayAlias(0)));
                    values[MatchMarkerAlias] = matched ? "1" : null;
                }

                EnrichDisplayValues(row, mappings);
            }
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
            {
                signoffs.Add(new RunSignoffViewModel
                {
                    Id = reader.GetInt32(0), SignoffRole = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ReviewerName = reader.IsDBNull(2) ? "" : reader.GetString(2), ReviewerEmail = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Comment = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SignedOffAt = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6)
                });
            }
            return signoffs;
        }

        private static async Task UpdateStoredSummaryAsync(SqlConnection connection, int runId, Rule51ValidationSummary summary)
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

        private static Rule51ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var decoded = ValidationPayloadCodec.Decode(json);
                if (string.IsNullOrWhiteSpace(decoded)) return null;
                var summary = JsonConvert.DeserializeObject<Rule51ValidationSummary>(decoded);
                if (summary == null) return null;
                ApplyMappings(summary, GetMappings(summary));
                return summary;
            }
            catch { return null; }
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)));
        }

        private static int GetInt(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key) => values.TryGetValue(key, out var v) ? v ?? "" : "";
    }
}
