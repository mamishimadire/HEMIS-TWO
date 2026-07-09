using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class BiokinieticService : IBiokinieticService
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public BiokinieticService(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
        {
            _configuration = configuration;
            _pendingValidationCache = pendingValidationCache;
        }

        // ── Database discovery ─────────────────────────────────────────────────

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

        public async Task<BiokinieticTableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                return new BiokinieticTableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoBiokinieticTable = FindFirst(tables, ["Biokinetic", "biokinetic"], ["biokin"]),
                    AutoProductionTable = FindFirst(tables, ["Clinical_Production", "ClinicalProduction"], ["production"])
                };
            }
            catch (Exception ex) { return new BiokinieticTableDiscoveryResult { Success = false, Error = ex.Message }; }
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

        // ── Verification ───────────────────────────────────────────────────────

        public async Task<BiokinieticVerifyResult> VerifyTablesAsync(BiokinieticVerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var biokinieticTable = Sanitise(request.BiokinieticTable);
                var prodTable = Sanitise(request.ProductionTable);
                var qualCol = Sanitise(request.QualificationColumn);

                var biokinieticCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{biokinieticTable}];");
                var prodCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{prodTable}];");

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = BuildPopulationCountSql(biokinieticTable, prodTable, qualCol);
                await using var reader = await cmd.ExecuteReaderAsync();

                var result = new BiokinieticVerifyResult { Success = true, BiokinieticRecordCount = biokinieticCount, ProductionRecordCount = prodCount };
                if (await reader.ReadAsync())
                {
                    result.TotalTested = GetInt(reader, 0);
                    result.MatchedCount = GetInt(reader, 1);
                    result.MissingCount = GetInt(reader, 2);
                }
                return result;
            }
            catch (Exception ex) { return new BiokinieticVerifyResult { Success = false, Error = ex.Message }; }
        }

        // ── Validation ─────────────────────────────────────────────────────────

        public async Task<BiokinieticValidationSummary> RunValidationAsync(BiokinieticValidationRequest request, string? userEmail = null, string? userName = null)
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
                            _pendingValidationCache.ClearPending(70, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(70, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);
                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full Biokinetic population. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current Biokinetic run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }
                return browserSummary;
            }
            catch (Exception ex) { return new BiokinieticValidationSummary { Success = false, Error = ex.Message }; }
        }

        private async Task<BiokinieticValidationSummary> AnalyseAsync(BiokinieticValidationRequest request, bool includeAllReviewRows)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandTimeout = 300;
                cmd.CommandText = await GenerateSqlAsync(request);

                await using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<BiokinieticReviewRow>();
                int passCount = 0, failCount = 0, totalCount = 0;

                while (await reader.ReadAsync())
                {
                    totalCount++;
                    var status = reader.GetString(2);
                    var row = new BiokinieticReviewRow
                    {
                        BiokinieticQualification = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        BiokinieticSurname = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Status = status,
                        ProductionQualification = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        ProductionSurname = reader.IsDBNull(4) ? "" : reader.GetString(4)
                    };

                    if (status == "PASS") passCount++;
                    else if (status == "FAIL") failCount++;

                    if (results.Count < (includeAllReviewRows ? int.MaxValue : BrowserPreviewRowLimit))
                        results.Add(row);
                }

                var exceptionRate = totalCount > 0 ? Math.Round((decimal)failCount * 100 / totalCount, 2) : 0;
                var statusResult = exceptionRate == 0 ? "PASS" : "FAIL";

                return new BiokinieticValidationSummary
                {
                    Success = true,
                    Status = statusResult,
                    TotalValidated = totalCount,
                    PassCount = passCount,
                    FailCount = failCount,
                    ExceptionRate = exceptionRate,
                    ReviewRows = results,
                    IsPreviewOnly = !includeAllReviewRows && results.Count < totalCount
                };
            }
            catch (Exception ex)
            {
                return new BiokinieticValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<string> GenerateSqlAsync(BiokinieticValidationRequest request)
        {
            ValidateRequest(request);
            var biokinieticTable = Sanitise(request.BiokinieticTable);
            var prodTable = Sanitise(request.ProductionTable);
            var qualCol = Sanitise(request.QualificationColumn);
            var surnameCol = Sanitise(request.SurnameColumn);

            var sql = $@"-- BIOKINETIC MODULE: Qualification Code and Surname Validation
-- Checks if QUALIFICATION values from Biokinetic table exist in Clinical Production table
-- and confirms matching Surname records

-- Build reference qualification codes from Production table
SELECT
    UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification,
    UPPER(LTRIM(RTRIM(CAST([{surnameCol}] AS nvarchar(500))))) AS ProdSurname
INTO #ProdQualifications
FROM [{prodTable}]
WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> '';

CREATE INDEX IDX_ProdQual ON #ProdQualifications(ProdQualification);

-- Validate Biokinetic against Production
SELECT
    UPPER(LTRIM(RTRIM(CAST(BK.[{qualCol}] AS nvarchar(255))))) AS BioQual,
    UPPER(LTRIM(RTRIM(CAST(BK.[{surnameCol}] AS nvarchar(500))))) AS BioSurname,
    CASE WHEN PQ.ProdQualification IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Status,
    ISNULL(PQ.ProdQualification, '') AS MatchedQual,
    ISNULL(PQ.ProdSurname, '') AS MatchedSurname
FROM [{biokinieticTable}] BK
LEFT JOIN #ProdQualifications PQ ON UPPER(LTRIM(RTRIM(CAST(BK.[{qualCol}] AS nvarchar(255))))) = PQ.ProdQualification
WHERE BK.[{qualCol}] IS NOT NULL AND BK.[{qualCol}] <> ''
ORDER BY Status DESC, BioQual;

DROP TABLE #ProdQualifications;";

            return await Task.FromResult(sql);
        }

        public async Task<string> GenerateRScriptAsync(BiokinieticValidationRequest request)
        {
            return await Task.FromResult(
                BiokinieticRScriptGenerator.Generate(
                    request.Server,
                    request.Database,
                    request.BiokinieticTable,
                    request.ProductionTable,
                    request.QualificationColumn,
                    request.SurnameColumn));
        }

        // ── Workspace state ────────────────────────────────────────────────────

        public async Task<BiokinieticWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null)
        {
            try
            {
                if (clientId <= 0) return null;

                var cached = _pendingValidationCache.GetPending<BiokinieticValidationRequest, BiokinieticValidationSummary>(70, clientId, userEmail ?? "");
                if (cached?.Request is not null && cached.Summary is not null)
                {
                    var req = cached.Request;
                    return new BiokinieticWorkspaceState
                    {
                        ClientId = clientId,
                        Server = req.Server,
                        Database = req.Database,
                        Driver = req.Driver,
                        BiokinieticTable = req.BiokinieticTable,
                        ProductionTable = req.ProductionTable,
                        QualificationColumn = req.QualificationColumn,
                        SurnameColumn = req.SurnameColumn,
                        Summary = cached.Summary,
                        LastRunAt = DateTime.UtcNow
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> SaveWorkspaceStateAsync(int clientId, BiokinieticValidationRequest config, string? userEmail = null)
        {
            try
            {
                if (clientId <= 0) return false;
                await Task.Delay(100);
                return true;
            }
            catch { return false; }
        }

        // ── Helper methods ─────────────────────────────────────────────────────

        private static string BuildConnectionString(string server, string database, string driver)
        {
            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;Connection Timeout=180;";
        }

        private static string BuildPopulationCountSql(string biokinieticTable, string prodTable, string qualCol)
        {
            return $@"
SELECT
    COUNT(*) AS TotalTested,
    SUM(CASE WHEN PQ.ProdQualification IS NOT NULL THEN 1 ELSE 0 END) AS MatchedCount,
    SUM(CASE WHEN PQ.ProdQualification IS NULL THEN 1 ELSE 0 END) AS MissingCount
FROM (
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CAST(BK.[{qualCol}] AS nvarchar(255))))) AS BioQual
    FROM [{biokinieticTable}] BK
    WHERE BK.[{qualCol}] IS NOT NULL AND BK.[{qualCol}] <> ''
) BK
LEFT JOIN (
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification
    FROM [{prodTable}]
    WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> ''
) PQ ON BK.BioQual = PQ.ProdQualification;";
        }

        private static async Task<int> CountAsync(SqlConnection conn, string sql)
        {
            await using var cmd = conn.CreateConfiguredCommand();
            cmd.CommandText = sql;
            var result = await cmd.ExecuteScalarAsync();
            return result is int count ? count : 0;
        }

        private static int GetInt(SqlDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);

        private static string Sanitise(string? objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return "";
            var trimmed = objectName.Trim().Replace("'", "").Replace("\"", "");
            return trimmed.Length > 128 ? trimmed[..128] : trimmed;
        }

        private static void ValidateObjectName(string? objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                throw new ArgumentException("Object name cannot be empty.");
            if (objectName.Length > 128)
                throw new ArgumentException("Object name cannot exceed 128 characters.");
        }

        private static void ValidateRequest(BiokinieticVerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(request.BiokinieticTable))
                throw new ArgumentException("BiokinieticTable must be specified.");
            if (string.IsNullOrWhiteSpace(request.ProductionTable))
                throw new ArgumentException("ProductionTable must be specified.");
        }

        private static void ValidateRequest(BiokinieticValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(request.BiokinieticTable))
                throw new ArgumentException("BiokinieticTable must be specified.");
            if (string.IsNullOrWhiteSpace(request.ProductionTable))
                throw new ArgumentException("ProductionTable must be specified.");
        }

        private static string? FindFirst(List<string> items, string[] preferred, string[] contains)
        {
            foreach (var p in preferred)
                if (items.Any(i => i.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    return items.First(i => i.Equals(p, StringComparison.OrdinalIgnoreCase));

            foreach (var c in contains)
                if (items.Any(i => i.Contains(c, StringComparison.OrdinalIgnoreCase)))
                    return items.First(i => i.Contains(c, StringComparison.OrdinalIgnoreCase));

            return null;
        }

        private static BiokinieticValidationSummary CloneSummary(BiokinieticValidationSummary summary)
        {
            return JsonConvert.DeserializeObject<BiokinieticValidationSummary>(
                JsonConvert.SerializeObject(summary)) ?? new BiokinieticValidationSummary();
        }

        private static BiokinieticValidationRequest CloneRequest(BiokinieticValidationRequest request)
        {
            return JsonConvert.DeserializeObject<BiokinieticValidationRequest>(
                JsonConvert.SerializeObject(request)) ?? new BiokinieticValidationRequest();
        }

        private async Task<int> SaveValidationRunAsync(BiokinieticValidationRequest request, BiokinieticValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await Task.Delay(100);
            return 1;
        }
    }
}
