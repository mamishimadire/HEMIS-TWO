using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HemisAudit.Helpers;
using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public class Rule40Service : IRule40Service
    {
        private const int BrowserPreviewRowLimit = 10;
        private const int ExceptionSaveLimit     = 5000;
        private const int AgreeSaveLimit         = 200;

        private static readonly Rule40ColumnPair[] DefaultPairs =
        [
            new() { ValpacCol="_011", AsciiCol="_011", Label="Date of Birth" },
            new() { ValpacCol="_012", AsciiCol="_012", Label="Gender" },
            new() { ValpacCol="_013", AsciiCol="_013", Label="Race" },
            new() { ValpacCol="_014", AsciiCol="_014", Label="Nationality" },
            new() { ValpacCol="_038", AsciiCol="_038", Label="Empl. Commencement" },
            new() { ValpacCol="_042", AsciiCol="_042", Label="Full/Part-time" },
        ];

        private static readonly Dictionary<string, string> ColumnLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["_011"] = "Date of Birth",
            ["_012"] = "Gender",
            ["_013"] = "Race",
            ["_014"] = "Nationality",
            ["_038"] = "Empl. Commencement",
            ["_042"] = "Full/Part-time",
        };

        private readonly IConfiguration _configuration;

        public Rule40Service(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ── Connection helpers ────────────────────────────────────────────────

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
                using (var startP = Process.Start(new ProcessStartInfo("sqllocaldb", $"start \"{instance}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
                { startP.WaitForExit(8000); }
                using var infoP = Process.Start(new ProcessStartInfo("sqllocaldb", $"info \"{instance}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                var output = infoP.StandardOutput.ReadToEnd();
                infoP.WaitForExit(3000);
                var m = System.Text.RegularExpressions.Regex.Match(output, @"Instance pipe name:\s*(np:[^\r\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }

        private async Task<SqlConnection> OpenSystemConnectionAsync()
        {
            var server   = _configuration["SystemDatabase:Server"] ?? @"(localdb)\MSSQLLocalDB";
            var database = _configuration["SystemDatabase:Name"] ?? "HEMISBaseSystem";
            var trust    = _configuration.GetValue("SystemDatabase:TrustServerCertificate", true);
            var builder  = new SqlConnectionStringBuilder
            {
                DataSource             = server,
                InitialCatalog         = database,
                IntegratedSecurity     = true,
                TrustServerCertificate = trust,
                Encrypt                = false,
                ConnectTimeout         = 180
            };
            var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            return conn;
        }

        // ── Discovery ─────────────────────────────────────────────────────────

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
            catch (Exception ex) { return new DatabaseListResult { Success = false, Error = ex.Message }; }
        }

        public async Task<Rule40TableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                return new Rule40TableDiscoveryResult
                {
                    Success         = true,
                    Tables          = tables,
                    AutoValpacTable = FindFirst(tables, ["dbo_PROF", "H16PROF"], ["PROF", "VALPAC"]),
                    AutoAsciiTable  = FindFirst(tables, [], ["ASCII", "ascii"])
                };
            }
            catch (Exception ex) { return new Rule40TableDiscoveryResult { Success = false, Error = ex.Message }; }
        }

        public async Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName)
        {
            ValidateObjectName(tableName);
            try
            {
                await using var conn = new SqlConnection(BuildConnectionString(server, database, driver));
                await conn.OpenAsync();
                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tbl ORDER BY ORDINAL_POSITION;";
                cmd.Parameters.AddWithValue("@tbl", tableName);
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = new List<string>();
                while (await reader.ReadAsync()) cols.Add(reader.GetString(0));
                return cols;
            }
            catch { return new List<string>(); }
        }

        public async Task<Rule40VerifyResult> VerifyTablesAsync(Rule40VerifyRequest request)
        {
            try
            {
                ValidateObjectName(request.ValpacTable);
                ValidateObjectName(request.AsciiTable);
                await using var conn = new SqlConnection(BuildConnectionString(request.Server, request.Database, request.Driver));
                await conn.OpenAsync();
                var vt = Sanitise(request.ValpacTable);
                var at = Sanitise(request.AsciiTable);
                return new Rule40VerifyResult
                {
                    Success     = true,
                    ValpacCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{vt}];"),
                    AsciiCount  = await CountAsync(conn, $"SELECT COUNT(*) FROM [{at}];")
                };
            }
            catch (Exception ex) { return new Rule40VerifyResult { Success = false, Error = ex.Message }; }
        }

        // ── Validation ────────────────────────────────────────────────────────

        public async Task<Rule40ValidationSummary> RunValidationAsync(Rule40ValidationRequest request, string? userEmail = null, string? userName = null)
        {
            try
            {
                ValidateObjectName(request.ValpacTable);
                ValidateObjectName(request.AsciiTable);

                var summary = await AnalyseAsync(request);

                if (summary.Success && request.ClientId > 0)
                    summary.SavedRunId = await SaveValidationRunAsync(request, summary, userEmail, userName);

                ApplyBrowserPreview(summary);
                return summary;
            }
            catch (Exception ex) { return new Rule40ValidationSummary { Success = false, Error = ex.Message }; }
        }

        private async Task<Rule40ValidationSummary> AnalyseAsync(Rule40ValidationRequest req)
        {
            var pairs   = (req.Pairs?.Count > 0 ? req.Pairs : null) ?? DefaultPairs.ToList();
            var allCols = pairs.SelectMany(p => new[] { p.ValpacCol, p.AsciiCol }).Append("_037").Distinct().ToList();

            await using var conn = new SqlConnection(BuildConnectionString(req.Server, req.Database, req.Driver));
            await conn.OpenAsync();

            var valpacMap = await LoadTableAsync(conn, req.ValpacTable, "_037", allCols);
            var asciiMap  = await LoadTableAsync(conn, req.AsciiTable,  "_037", allCols);

            var allKeys = valpacMap.Keys
                .Union(asciiMap.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k)
                .ToList();

            var exceptionRows = new List<Rule40ReconcRow>();
            var agreeRows     = new List<Rule40ReconcRow>();
            int rowNo = 0;

            foreach (var normKey in allKeys)
            {
                rowNo++;
                var vRow    = valpacMap.GetValueOrDefault(normKey);
                var aRow    = asciiMap.GetValueOrDefault(normKey);
                var staffNo = vRow?.GetValueOrDefault("_037")?.Trim()
                           ?? aRow?.GetValueOrDefault("_037")?.Trim()
                           ?? normKey;

                var row   = new Rule40ReconcRow { RowNumber = rowNo, StaffNumber = staffNo };
                var diffs = new List<string>();

                if (vRow == null)
                {
                    row.OverallResult  = "MISSING-VALPAC";
                    row.DisagreeDetail = $"Staff {staffNo}: in ASCII but not in VALPAC";
                    foreach (var p in pairs)
                        row.Fields[p.Label] = new Rule40FieldValue { AsciiValue = Disp(aRow?.GetValueOrDefault(p.AsciiCol)), Match = "MISSING" };
                }
                else if (aRow == null)
                {
                    row.OverallResult  = "MISSING-ASCII";
                    row.DisagreeDetail = $"Staff {staffNo}: in VALPAC but not in ASCII";
                    foreach (var p in pairs)
                        row.Fields[p.Label] = new Rule40FieldValue { ValpacValue = Disp(vRow.GetValueOrDefault(p.ValpacCol)), Match = "MISSING" };
                }
                else
                {
                    foreach (var p in pairs)
                    {
                        var vv    = Disp(vRow.GetValueOrDefault(p.ValpacCol));
                        var av    = Disp(aRow.GetValueOrDefault(p.AsciiCol));
                        var match = Norm(vv) == Norm(av) ? "AGREE" : "DISAGREE";
                        row.Fields[p.Label] = new Rule40FieldValue { ValpacValue = vv, AsciiValue = av, Match = match };
                        if (match == "DISAGREE") diffs.Add(p.Label);
                    }
                    row.OverallResult  = diffs.Count == 0 ? "AGREE" : "DISAGREE";
                    row.DisagreeDetail = string.Join(", ", diffs);
                }

                if (row.OverallResult == "AGREE") agreeRows.Add(row);
                else                              exceptionRows.Add(row);
            }

            var exCount = exceptionRows.Count;
            return new Rule40ValidationSummary
            {
                Success              = true,
                Timestamp            = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Server               = req.Server,
                Database             = req.Database,
                ValpacTable          = req.ValpacTable,
                AsciiTable           = req.AsciiTable,
                ClientId             = req.ClientId,
                TotalCount           = rowNo,
                AgreeCount           = agreeRows.Count,
                DisagreeCount        = exceptionRows.Count(r => r.OverallResult == "DISAGREE"),
                MissingInAsciiCount  = exceptionRows.Count(r => r.OverallResult == "MISSING-ASCII"),
                MissingInValpacCount = exceptionRows.Count(r => r.OverallResult == "MISSING-VALPAC"),
                ExceptionRate        = rowNo == 0 ? 0m : Math.Round(exCount * 100m / rowNo, 2),
                Status               = exCount == 0 ? "PASS" : "FAIL",
                Pairs                = pairs,
                ReviewRows           = exceptionRows.Take(ExceptionSaveLimit).ToList(),
                AgreeSample          = agreeRows.Take(AgreeSaveLimit).ToList()
            };
        }

        private static void ApplyBrowserPreview(Rule40ValidationSummary? summary)
        {
            if (summary == null) return;
            summary.ReviewRows  = summary.ReviewRows.Take(BrowserPreviewRowLimit).ToList();
            summary.AgreeSample = summary.AgreeSample.Take(BrowserPreviewRowLimit).ToList();
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        private async Task<int> SaveValidationRunAsync(Rule40ValidationRequest req, Rule40ValidationSummary summary, string? userEmail, string? userName)
        {
            await using var conn = await OpenSystemConnectionAsync();
            await EnsureClientNotArchivedAsync(conn, req.ClientId);
            await MarkPreviousRunsHistoricalAsync(conn, req.ClientId, 40);

            var userId = await GetUserIdByEmailAsync(conn, userEmail)
                ?? throw new InvalidOperationException("Analyst not found in system database.");

            var prevHash = await GetLatestHashAsync(conn, req.ClientId, 40);
            var json     = ValidationPayloadCodec.Encode(JsonConvert.SerializeObject(summary));

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
INSERT INTO dbo.ValidationRuns
(ClientID,UserID,RuleNumber,RuleName,Status,TotalRecords,PassCount,FailCount,ExceptionRate,RunTimestamp,
 HemisServer,AuditDatabase,StudTable,DeceasedTable,StudColumn,DeceasedColumn,
 ExceptionsJSON,ResultsJSON,RunByUserName,LastEditedByUserName,LastEditedAt,PreviousHash,RecordHash,IsCurrent)
OUTPUT INSERTED.RunID
VALUES
(@ClientID,@UserID,40,'PROF ASCII Staff Agreement',@Status,@Total,@Pass,@Fail,@Rate,GETDATE(),
 @Server,@Database,@ValpacTable,@AsciiTable,'_037','_037',
 NULL,@JSON,@RunBy,NULL,NULL,@PrevHash,NULL,1);";
            cmd.Parameters.AddWithValue("@ClientID",    req.ClientId);
            cmd.Parameters.AddWithValue("@UserID",      userId);
            cmd.Parameters.AddWithValue("@Status",      summary.Status);
            cmd.Parameters.AddWithValue("@Total",       summary.TotalCount);
            cmd.Parameters.AddWithValue("@Pass",        summary.AgreeCount);
            cmd.Parameters.AddWithValue("@Fail",        summary.DisagreeCount + summary.MissingInAsciiCount + summary.MissingInValpacCount);
            cmd.Parameters.AddWithValue("@Rate",        summary.ExceptionRate);
            cmd.Parameters.AddWithValue("@Server",      req.Server);
            cmd.Parameters.AddWithValue("@Database",    req.Database);
            cmd.Parameters.AddWithValue("@ValpacTable", req.ValpacTable);
            cmd.Parameters.AddWithValue("@AsciiTable",  req.AsciiTable);
            cmd.Parameters.AddWithValue("@JSON",        json);
            cmd.Parameters.AddWithValue("@RunBy",       (object?)userName ?? (object?)userEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PrevHash",    (object?)prevHash ?? DBNull.Value);

            var runId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            summary.SavedRunId = runId;

            await using var hashCmd = conn.CreateConfiguredCommand();
            hashCmd.CommandText = "UPDATE dbo.ValidationRuns SET RecordHash=@Hash WHERE RunID=@RunID;";
            hashCmd.Parameters.AddWithValue("@RunID", runId);
            hashCmd.Parameters.AddWithValue("@Hash",  ComputeHash($"Rule40|{runId}|{req.ClientId}|{summary.Status}|{summary.TotalCount}|{summary.Timestamp}|{prevHash}"));
            await hashCmd.ExecuteNonQueryAsync();

            return runId;
        }

        public async Task<Rule40WorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null)
        {
            await using var conn      = await OpenSystemConnectionAsync();
            var currentUserId = await GetUserIdByEmailAsync(conn, currentUserEmail);

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT TOP 1
    vr.RunID, vr.ClientID,
    ISNULL(vr.HemisServer,'')    AS Server,
    ISNULL(vr.AuditDatabase,'') AS [Database],
    ISNULL(vr.StudTable,'')      AS ValpacTable,
    ISNULL(vr.DeceasedTable,'') AS AsciiTable,
    ISNULL(vr.Status,'')         AS Status,
    vr.ResultsJSON,
    vr.WorkspaceSavedAt,
    vr.LastEditedByUserName,
    vr.LastEditedAt
FROM dbo.ValidationRuns vr
WHERE vr.ClientID = @ClientID AND vr.RuleNumber = 40 AND vr.IsCurrent = 1
ORDER BY vr.RunTimestamp DESC, vr.RunID DESC;";
            cmd.Parameters.AddWithValue("@ClientID", clientId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var runId   = reader.GetInt32(0);
            var summary = DeserializeSummary(reader.IsDBNull(7) ? null : reader.GetString(7));
            var workspace = new Rule40WorkspaceState
            {
                ClientId             = reader.GetInt32(1),
                RunId                = runId,
                Server               = reader.GetString(2),
                Database             = reader.GetString(3),
                ValpacTable          = reader.GetString(4),
                AsciiTable           = reader.GetString(5),
                CurrentStatus        = reader.GetString(6),
                IsWorkspaceSaved     = !reader.IsDBNull(8),
                LastEditedByUserName = reader.IsDBNull(9)  ? null : reader.GetString(9),
                LastEditedAt         = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                Driver               = "ODBC Driver 17 for SQL Server",
                Summary              = summary
            };
            await reader.CloseAsync();

            ApplyBrowserPreview(summary);

            workspace.CurrentUserEngagementRole = currentUserId.HasValue
                ? await GetEngagementRoleAsync(conn, clientId, currentUserId.Value) ?? ""
                : "";

            var signoffs = await GetSignoffsAsync(conn, runId, currentUserId);
            workspace.HasDataAnalystSignoff     = signoffs.Any(s => string.Equals(s.SignoffRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));
            var mySignoff = signoffs.FirstOrDefault(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, workspace.CurrentUserEngagementRole));
            workspace.CurrentUserHasSignedOff   = mySignoff != null;
            workspace.CurrentUserSignoffComment = mySignoff?.Comment ?? "";

            return workspace;
        }

        public async Task<bool> SaveWorkspaceStateAsync(int clientId, Rule40ValidationRequest request, string? userEmail)
        {
            try
            {
                if (!request.RunId.HasValue || request.RunId.Value <= 0) return false;

                await using var conn = await OpenSystemConnectionAsync();
                await EnsureClientNotArchivedAsync(conn, clientId);

                var prevHash = await GetRunHashAsync(conn, request.RunId.Value);

                await using var delCmd = conn.CreateConfiguredCommand();
                delCmd.CommandText = "DELETE FROM dbo.ReviewSignoffs WHERE RunID = @RunID;";
                delCmd.Parameters.AddWithValue("@RunID", request.RunId.Value);
                await delCmd.ExecuteNonQueryAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = @"
UPDATE dbo.ValidationRuns
SET StudTable = @ValpacTable, DeceasedTable = @AsciiTable,
    WorkspaceSavedAt = GETDATE(), LastEditedByUserName = @EditedBy, LastEditedAt = GETDATE(),
    Status = 'Needs Review', IsCurrent = 1,
    PreviousHash = @PrevHash, RecordHash = @Hash
WHERE RunID = @RunID AND ClientID = @ClientID;";
                cmd.Parameters.AddWithValue("@RunID",       request.RunId.Value);
                cmd.Parameters.AddWithValue("@ClientID",    clientId);
                cmd.Parameters.AddWithValue("@ValpacTable", request.ValpacTable);
                cmd.Parameters.AddWithValue("@AsciiTable",  request.AsciiTable);
                cmd.Parameters.AddWithValue("@EditedBy",    (object?)userEmail ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PrevHash",    (object?)prevHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Hash",        ComputeHash($"WorkspaceSave|Rule40|{request.RunId.Value}|{clientId}|{userEmail}|{DateTime.UtcNow:o}|{prevHash}"));
                await cmd.ExecuteNonQueryAsync();

                return true;
            }
            catch { return false; }
        }

        public async Task<Rule40ValidationSummary?> GetFullSummaryByRunIdAsync(int runId)
        {
            await using var conn = await OpenSystemConnectionAsync();
            await using var cmd  = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 ResultsJSON FROM dbo.ValidationRuns WHERE RunID = @RunID AND RuleNumber = 40;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var json = Convert.ToString(await cmd.ExecuteScalarAsync());
            return string.IsNullOrWhiteSpace(json) ? null : DeserializeSummary(json);
        }

        public async Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment)
        {
            await using var conn = await OpenSystemConnectionAsync();
            var reviewerId       = await GetUserIdByEmailAsync(conn, reviewerEmail)
                                   ?? throw new InvalidOperationException("Reviewer not found.");
            var clientId         = await GetClientIdAsync(conn, runId)
                                   ?? throw new InvalidOperationException("Run not found.");

            await EnsureClientNotArchivedAsync(conn, clientId);

            if (!await IsWorkspaceSavedAsync(conn, runId))
                throw new InvalidOperationException("Save the workspace before signing off.");

            var engRole = await GetEngagementRoleAsync(conn, clientId, reviewerId);
            if (!IsSignoffRole(engRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can sign off.");

            if (!string.Equals(engRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) &&
                !await HasSignoffAsync(conn, runId, "DataAnalyst"))
                throw new InvalidOperationException("The data analyst must sign off first.");

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND ReviewerID=@ReviewerID)
    UPDATE dbo.ReviewSignoffs SET SignoffRole=@Role,ReviewType='Final',Comment=@Comment,SignedOffAt=GETDATE()
    WHERE RunID=@RunID AND ReviewerID=@ReviewerID;
ELSE
    INSERT INTO dbo.ReviewSignoffs (ClientID,RunID,ReviewerID,SignoffRole,ReviewType,Comment,SignedOffAt)
    VALUES (@ClientID,@RunID,@ReviewerID,@Role,'Final',@Comment,GETDATE());";
            cmd.Parameters.AddWithValue("@ClientID",   clientId);
            cmd.Parameters.AddWithValue("@RunID",      runId);
            cmd.Parameters.AddWithValue("@ReviewerID", reviewerId);
            cmd.Parameters.AddWithValue("@Role",       engRole!);
            cmd.Parameters.AddWithValue("@Comment",    string.IsNullOrWhiteSpace(comment) ? DBNull.Value : (object)comment.Trim());
            await cmd.ExecuteNonQueryAsync();
            await UpdateStatusFromSignoffsAsync(conn, runId);
        }

        public async Task RemoveSignoffAsync(int runId, string reviewerEmail)
        {
            await using var conn = await OpenSystemConnectionAsync();
            var reviewerId       = await GetUserIdByEmailAsync(conn, reviewerEmail)
                                   ?? throw new InvalidOperationException("Reviewer not found.");
            var clientId         = await GetClientIdAsync(conn, runId)
                                   ?? throw new InvalidOperationException("Run not found.");

            await EnsureClientNotArchivedAsync(conn, clientId);
            var engRole = await GetEngagementRoleAsync(conn, clientId, reviewerId);

            await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(conn, runId, engRole!, reviewerEmail);
        }

        // ── SQL generation ────────────────────────────────────────────────────

        public string GenerateSql(Rule40ValidationRequest request)
        {
            var vt    = Sanitise(request.ValpacTable);
            var at    = Sanitise(request.AsciiTable);
            var pairs = (request.Pairs?.Count > 0 ? request.Pairs : null) ?? DefaultPairs.ToList();

            var colLines = string.Join(",\n", pairs.Select(p =>
            {
                var vc = Sanitise(p.ValpacCol); var ac = Sanitise(p.AsciiCol);
                return $"    v.[{vc}] AS [VALPAC_{vc}],\n    a.[{ac}] AS [ASCII_{ac}],\n" +
                       $"    CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(CAST(v.[{vc}] AS NVARCHAR(500)),''))))=UPPER(LTRIM(RTRIM(ISNULL(CAST(a.[{ac}] AS NVARCHAR(500)),'')))) THEN 'AGREE' ELSE 'DISAGREE' END AS [MATCH_{vc}]  -- {p.Label}";
            }));

            var agreeWhen = string.Join("\n        AND ", pairs.Select(p =>
            {
                var vc = Sanitise(p.ValpacCol); var ac = Sanitise(p.AsciiCol);
                return $"UPPER(LTRIM(RTRIM(ISNULL(CAST(v.[{vc}] AS NVARCHAR(500)),''))))=UPPER(LTRIM(RTRIM(ISNULL(CAST(a.[{ac}] AS NVARCHAR(500)),''))))";
            }));

            return $@"-- ============================================================
-- HEMIS RULE 40 – PROF VALPAC vs ASCII Staff Agreement
-- Generated : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
-- Database  : {request.Database}
-- VALPAC    : [{vt}]
-- ASCII     : [{at}]
-- Key       : _037 (Staff Number)
-- Compared  : {string.Join(", ", pairs.Select(p => p.Label))}
-- ============================================================

SELECT
    COALESCE(v.[_037], a.[_037]) AS Staff_Number,
{colLines},
    CASE
        WHEN v.[_037] IS NULL THEN 'MISSING-VALPAC'
        WHEN a.[_037] IS NULL THEN 'MISSING-ASCII'
        WHEN {agreeWhen} THEN 'AGREE'
        ELSE 'DISAGREE'
    END AS Overall_Result
FROM [{vt}] v
FULL OUTER JOIN [{at}] a
    ON UPPER(LTRIM(RTRIM(CAST(v.[_037] AS NVARCHAR(200))))) = UPPER(LTRIM(RTRIM(CAST(a.[_037] AS NVARCHAR(200)))))
ORDER BY Staff_Number;

-- ── Summary ─────────────────────────────────────────────────────
SELECT
    COUNT(*)                                                                                          AS Total,
    SUM(CASE WHEN v.[_037] IS NOT NULL AND a.[_037] IS NOT NULL AND ({agreeWhen}) THEN 1 ELSE 0 END) AS Agree,
    SUM(CASE WHEN v.[_037] IS NOT NULL AND a.[_037] IS NOT NULL AND NOT ({agreeWhen}) THEN 1 ELSE 0 END) AS Disagree,
    SUM(CASE WHEN a.[_037] IS NULL THEN 1 ELSE 0 END) AS Missing_In_ASCII,
    SUM(CASE WHEN v.[_037] IS NULL THEN 1 ELSE 0 END) AS Missing_In_VALPAC
FROM [{vt}] v
FULL OUTER JOIN [{at}] a
    ON UPPER(LTRIM(RTRIM(CAST(v.[_037] AS NVARCHAR(200))))) = UPPER(LTRIM(RTRIM(CAST(a.[_037] AS NVARCHAR(200)))));
-- ============================================================".Trim();
        }

        // ── Internal DB helpers ───────────────────────────────────────────────

        private static async Task<Dictionary<string, Dictionary<string, string?>>> LoadTableAsync(
            SqlConnection conn, string tableName, string keyCol, IEnumerable<string> columns)
        {
            ValidateObjectName(tableName);
            var tbl     = Sanitise(tableName);
            var selCols = columns.Select(c => $"[{Sanitise(c)}]").Distinct().ToList();
            var keyExpr = $"[{Sanitise(keyCol)}]";
            if (!selCols.Contains(keyExpr)) selCols.Insert(0, keyExpr);

            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandTimeout = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
            cmd.CommandText = $"SELECT {string.Join(", ", selCols)} FROM [{tbl}];";
            await using var reader = await cmd.ExecuteReaderAsync();
            var fieldNames = Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetName(i)).ToList();
            var map        = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[fieldNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString()?.Trim();

                var rawKey  = row.GetValueOrDefault(keyCol);
                var normKey = Norm(rawKey);
                if (!string.IsNullOrEmpty(normKey) && !map.ContainsKey(normKey))
                    map[normKey] = row;
            }
            return map;
        }

        private static async Task<int?> GetUserIdByEmailAsync(SqlConnection conn, string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 UserID FROM dbo.Users WHERE Email=@Email;";
            cmd.Parameters.AddWithValue("@Email", email);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
        }

        private static async Task<int?> GetClientIdAsync(SqlConnection conn, int runId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToInt32(v);
        }

        private static async Task<string?> GetEngagementRoleAsync(SqlConnection conn, int clientId, int userId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 EngagementRole FROM dbo.UserClientAssignments WHERE ClientID=@CID AND UserID=@UID;";
            cmd.Parameters.AddWithValue("@CID", clientId);
            cmd.Parameters.AddWithValue("@UID", userId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private static async Task<List<RunSignoffViewModel>> GetSignoffsAsync(SqlConnection conn, int runId, int? currentUserId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT rs.SignoffID, ISNULL(rs.SignoffRole,''),
       LTRIM(RTRIM(ISNULL(u.FirstName,'')+' '+ISNULL(u.LastName,''))) AS Name,
       ISNULL(u.Email,''), ISNULL(rs.Comment,''), rs.SignedOffAt,
       CASE WHEN @CurUID IS NOT NULL AND rs.ReviewerID=@CurUID THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
FROM dbo.ReviewSignoffs rs
INNER JOIN dbo.Users u ON u.UserID=rs.ReviewerID
WHERE rs.RunID=@RunID
ORDER BY CASE ISNULL(rs.SignoffRole,'') WHEN 'DataAnalyst' THEN 1 WHEN 'Manager' THEN 2 WHEN 'Director' THEN 3 ELSE 4 END, rs.SignedOffAt DESC;";
            cmd.Parameters.AddWithValue("@RunID",  runId);
            cmd.Parameters.AddWithValue("@CurUID", currentUserId.HasValue ? (object)currentUserId.Value : DBNull.Value);
            await using var reader = await cmd.ExecuteReaderAsync();
            var list = new List<RunSignoffViewModel>();
            while (await reader.ReadAsync())
                list.Add(new RunSignoffViewModel
                {
                    Id            = reader.GetInt32(0),
                    SignoffRole   = reader.GetString(1),
                    ReviewerName  = reader.GetString(2),
                    ReviewerEmail = reader.GetString(3),
                    Comment       = reader.GetString(4),
                    SignedOffAt   = reader.IsDBNull(5) ? DateTime.UtcNow : reader.GetDateTime(5),
                    IsCurrentUser = !reader.IsDBNull(6) && reader.GetBoolean(6)
                });
            return list;
        }

        private static async Task<bool> IsWorkspaceSavedAsync(SqlConnection conn, int runId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT CASE WHEN WorkspaceSavedAt IS NOT NULL THEN 1 ELSE 0 END FROM dbo.ValidationRuns WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<bool> HasSignoffAsync(SqlConnection conn, int runId, string role)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole=@Role) THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            cmd.Parameters.AddWithValue("@Role",  role);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
        }

        private static async Task<string?> GetLatestHashAsync(SqlConnection conn, int clientId, int ruleNumber)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE ClientID=@CID AND RuleNumber=@RN AND RecordHash IS NOT NULL ORDER BY RunTimestamp DESC, RunID DESC;";
            cmd.Parameters.AddWithValue("@CID", clientId);
            cmd.Parameters.AddWithValue("@RN",  ruleNumber);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private static async Task<string?> GetRunHashAsync(SqlConnection conn, int runId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 RecordHash FROM dbo.ValidationRuns WHERE RunID=@RunID;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? null : Convert.ToString(v);
        }

        private static async Task EnsureClientNotArchivedAsync(SqlConnection conn, int clientId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "SELECT TOP 1 Status FROM dbo.Clients WHERE ClientID=@CID;";
            cmd.Parameters.AddWithValue("@CID", clientId);
            if (string.Equals(Convert.ToString(await cmd.ExecuteScalarAsync()), "Archived", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Archived engagements are read-only.");
        }

        private static async Task MarkPreviousRunsHistoricalAsync(SqlConnection conn, int clientId, int ruleNumber)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = "UPDATE dbo.ValidationRuns SET IsCurrent=0 WHERE ClientID=@CID AND RuleNumber=@RN AND IsCurrent=1;";
            cmd.Parameters.AddWithValue("@CID", clientId);
            cmd.Parameters.AddWithValue("@RN",  ruleNumber);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task UpdateStatusFromSignoffsAsync(SqlConnection conn, int runId)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = @"
SELECT
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='DataAnalyst') THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='Manager')     THEN 1 ELSE 0 END,
    CASE WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID=@RunID AND SignoffRole='Director')    THEN 1 ELSE 0 END;";
            cmd.Parameters.AddWithValue("@RunID", runId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var allSigned = await reader.ReadAsync() && reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1 && reader.GetInt32(2) == 1;
            await reader.CloseAsync();
            await using var upd = conn.CreateConfiguredCommand();
            upd.CommandText = "UPDATE dbo.ValidationRuns SET Status=@Status WHERE RunID=@RunID;";
            upd.Parameters.AddWithValue("@Status", allSigned ? "Reviewed and Completed" : "Needs Review");
            upd.Parameters.AddWithValue("@RunID",  runId);
            await upd.ExecuteNonQueryAsync();
        }

        private static async Task<int> CountAsync(SqlConnection conn, string sql)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandTimeout = SqlLargeDataExtensions.LargeDataCommandTimeoutSeconds;
            cmd.CommandText = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static string Norm(string? v) =>
            string.IsNullOrWhiteSpace(v) ? "" : System.Text.RegularExpressions.Regex.Replace(v.Trim().ToUpperInvariant(), @"\s+", " ");

        private static string Disp(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v.Trim();

        private static string Sanitise(string name) =>
            name.Replace("]", "").Replace("[", "").Replace("'", "").Replace(";", "").Trim();

        private static void ValidateObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Table name is required.");
            foreach (var bad in new[] { ";", "'", "\"", "--", "/*", "*/" })
                if (value.Contains(bad, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe table name was provided.");
        }

        private static string? FindFirst(IEnumerable<string> values, string[] exactMatches, string[] containsMatches)
        {
            foreach (var exact in exactMatches)
            {
                var m = values.FirstOrDefault(v => v.Equals(exact, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(m)) return m;
            }
            foreach (var fragment in containsMatches)
            {
                var m = values.FirstOrDefault(v => v.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(m)) return m;
            }
            return null;
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }

        private static Rule40ValidationSummary? DeserializeSummary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<Rule40ValidationSummary>(ValidationPayloadCodec.Decode(json)); }
            catch { return null; }
        }

        private static bool IsSignoffRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager",     StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director",    StringComparison.OrdinalIgnoreCase);
    }
}
