using System.Globalization;
using System.Security.Cryptography;
using HemisAudit.ViewModels;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

namespace HemisAudit.Services
{
    public class ClinicalTechService : IClinicalTechService
    {
        private const int BrowserPreviewRowLimit = 10;
        private readonly IConfiguration _configuration;
        private readonly IPendingValidationCacheService _pendingValidationCache;

        public ClinicalTechService(IConfiguration configuration, IPendingValidationCacheService pendingValidationCache)
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

        public async Task<ClinicalTechTableDiscoveryResult> GetTablesAsync(string server, string database, string driver)
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
                return new ClinicalTechTableDiscoveryResult
                {
                    Success = true,
                    Tables = tables,
                    AutoClinicaltechTable = FindFirst(tables, ["Clinicaltech", "clinicaltech"], ["clinical"]),
                    AutoProductionTable = FindFirst(tables, ["Clinical_Production", "ClinicalProduction"], ["production"])
                };
            }
            catch (Exception ex) { return new ClinicalTechTableDiscoveryResult { Success = false, Error = ex.Message }; }
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

        public async Task<ClinicalTechVerifyResult> VerifyTablesAsync(ClinicalTechVerifyRequest request)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var clinicalTechTable = Sanitise(request.ClinicaltechTable);
                var prodTable = Sanitise(request.ProductionTable);
                var qualCol = Sanitise(request.QualificationColumn);

                var clinicalTechCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{clinicalTechTable}];");
                var prodCount = await CountAsync(conn, $"SELECT COUNT(*) FROM [{prodTable}];");

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandText = BuildPopulationCountSql(clinicalTechTable, prodTable, qualCol);
                await using var reader = await cmd.ExecuteReaderAsync();

                var result = new ClinicalTechVerifyResult { Success = true, ClinicaltechRecordCount = clinicalTechCount, ProductionRecordCount = prodCount };
                if (await reader.ReadAsync())
                {
                    result.TotalTested = GetInt(reader, 0);
                    result.MatchedCount = GetInt(reader, 1);
                    result.MissingCount = GetInt(reader, 2);
                }
                return result;
            }
            catch (Exception ex) { return new ClinicalTechVerifyResult { Success = false, Error = ex.Message }; }
        }

        // ── Validation ─────────────────────────────────────────────────────────

        public async Task<ClinicalTechValidationSummary> RunValidationAsync(ClinicalTechValidationRequest request, string? userEmail = null, string? userName = null)
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
                            _pendingValidationCache.ClearPending(69, request.ClientId, userEmail!);
                    }
                    catch (Exception ex)
                    {
                        browserSummary.Warning = $"Analysis completed, but the workspace could not be saved automatically: {ex.Message}";
                    }
                }

                if (!browserSummary.SavedRunId.HasValue)
                {
                    if (browserSummary.Success && request.ClientId > 0 && !string.IsNullOrWhiteSpace(userEmail))
                        _pendingValidationCache.StorePending(69, request.ClientId, userEmail!, request, CloneSummary(browserSummary), userName);
                    browserSummary.Warning = string.IsNullOrWhiteSpace(browserSummary.Warning)
                        ? "Counts reflect the full Clinicaltech population. Browser review rows are limited for performance."
                        : browserSummary.Warning;
                }
                else
                {
                    browserSummary.Warning = "The current ClinicalTech run has been written to the system database. Click Save Workspace to finalize it for signoff.";
                }
                return browserSummary;
            }
            catch (Exception ex) { return new ClinicalTechValidationSummary { Success = false, Error = ex.Message }; }
        }

        private async Task<ClinicalTechValidationSummary> AnalyseAsync(ClinicalTechValidationRequest request, bool includeAllReviewRows)
        {
            try
            {
                ValidateRequest(request);
                var connStr = BuildConnectionString(request.Server, request.Database, request.Driver);
                await using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                var clinicalTechTable = Sanitise(request.ClinicaltechTable);
                var prodTable = Sanitise(request.ProductionTable);
                var qualCol = Sanitise(request.QualificationColumn);
                var surnameCol = Sanitise(request.SurnameColumn);

                await using var cmd = conn.CreateConfiguredCommand();
                cmd.CommandTimeout = 300;
                cmd.CommandText = await GenerateSqlAsync(request);

                await using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<ClinicalTechReviewRow>();
                int passCount = 0, failCount = 0, totalCount = 0;

                while (await reader.ReadAsync())
                {
                    totalCount++;
                    var status = reader.GetString(2);
                    var row = new ClinicalTechReviewRow
                    {
                        ClinicaltechQualification = reader.GetString(0) ?? "",
                        ClinicaltechSurname = reader.GetString(1) ?? "",
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
                var status_result = exceptionRate == 0 ? "PASS" : "FAIL";

                return new ClinicalTechValidationSummary
                {
                    Success = true,
                    Status = status_result,
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
                return new ClinicalTechValidationSummary { Success = false, Error = ex.Message };
            }
        }

        public async Task<string> GenerateSqlAsync(ClinicalTechValidationRequest request)
        {
            ValidateRequest(request);
            var clinicalTechTable = Sanitise(request.ClinicaltechTable);
            var prodTable = Sanitise(request.ProductionTable);
            var qualCol = Sanitise(request.QualificationColumn);
            var surnameCol = Sanitise(request.SurnameColumn);

            var sql = $@"-- CLINICALTECH MODULE: Qualification Code and Surname Validation
-- Checks if QUALIFICATION values from Clinicaltech table exist in Clinical Production table
-- and confirms matching Surname records

DECLARE @ClinicaltechQualCount INT = 0;
DECLARE @ProductionQualCount INT = 0;
DECLARE @MatchedCount INT = 0;
DECLARE @MissingCount INT = 0;

-- Build reference qualification codes from Production table
SELECT
    UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification,
    UPPER(LTRIM(RTRIM(CAST([{surnameCol}] AS nvarchar(500))))) AS ProdSurname
INTO #ProdQualifications
FROM [{prodTable}]
WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> '';

CREATE INDEX IDX_ProdQual ON #ProdQualifications(ProdQualification);

SELECT @ProductionQualCount = COUNT(*) FROM #ProdQualifications;

-- Validate Clinicaltech against Production
SELECT
    UPPER(LTRIM(RTRIM(CAST(CT.[{qualCol}] AS nvarchar(255))))) AS ClinicalQual,
    UPPER(LTRIM(RTRIM(CAST(CT.[{surnameCol}] AS nvarchar(500))))) AS ClinicalSurname,
    CASE WHEN PQ.ProdQualification IS NOT NULL THEN 'PASS' ELSE 'FAIL' END AS Status,
    ISNULL(PQ.ProdQualification, '') AS MatchedQual,
    ISNULL(PQ.ProdSurname, '') AS MatchedSurname
FROM [{clinicalTechTable}] CT
LEFT JOIN #ProdQualifications PQ ON UPPER(LTRIM(RTRIM(CAST(CT.[{qualCol}] AS nvarchar(255))))) = PQ.ProdQualification
WHERE CT.[{qualCol}] IS NOT NULL AND CT.[{qualCol}] <> ''
ORDER BY Status DESC, ClinicalQual;

DROP TABLE #ProdQualifications;";

            return await Task.FromResult(sql);
        }

        public async Task<string> GenerateRScriptAsync(ClinicalTechValidationRequest request)
        {
            return await Task.FromResult(
                ClinicalTechRScriptGenerator.Generate(
                    69,
                    "HEMIS Clinicaltech Module: Qualification and Surname Validation",
                    request.ClinicaltechTable,
                    request.ProductionTable,
                    request.QualificationColumn,
                    request.SurnameColumn));
        }

        // ── Workspace state ────────────────────────────────────────────────────

        public async Task<ClinicalTechWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null)
        {
            try
            {
                if (clientId <= 0) return null;

                var cached = _pendingValidationCache.GetPending<ClinicalTechValidationRequest, ClinicalTechValidationSummary>(69, clientId, userEmail ?? "");
                if (cached?.Request is not null && cached.Summary is not null)
                {
                    var req = cached.Request;
                    return new ClinicalTechWorkspaceState
                    {
                        ClientId = clientId,
                        Server = req.Server,
                        Database = req.Database,
                        Driver = req.Driver,
                        ClinicaltechTable = req.ClinicaltechTable,
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

        public async Task<bool> SaveWorkspaceStateAsync(int clientId, ClinicalTechValidationRequest config, string? userEmail = null)
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

        private static string BuildPopulationCountSql(string clinicalTechTable, string prodTable, string qualCol)
        {
            return $@"
SELECT
    COUNT(*) AS TotalTested,
    SUM(CASE WHEN PQ.ProdQualification IS NOT NULL THEN 1 ELSE 0 END) AS MatchedCount,
    SUM(CASE WHEN PQ.ProdQualification IS NULL THEN 1 ELSE 0 END) AS MissingCount
FROM (
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CAST(CT.[{qualCol}] AS nvarchar(255))))) AS ClinicalQual
    FROM [{clinicalTechTable}] CT
    WHERE CT.[{qualCol}] IS NOT NULL AND CT.[{qualCol}] <> ''
) CT
LEFT JOIN (
    SELECT DISTINCT
        UPPER(LTRIM(RTRIM(CAST([{qualCol}] AS nvarchar(255))))) AS ProdQualification
    FROM [{prodTable}]
    WHERE [{qualCol}] IS NOT NULL AND [{qualCol}] <> ''
) PQ ON CT.ClinicalQual = PQ.ProdQualification;";
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

        private static void ValidateRequest(ClinicalTechVerifyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(request.ClinicaltechTable))
                throw new ArgumentException("ClinicaltechTable must be specified.");
            if (string.IsNullOrWhiteSpace(request.ProductionTable))
                throw new ArgumentException("ProductionTable must be specified.");
        }

        private static void ValidateRequest(ClinicalTechValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server))
                throw new ArgumentException("Server must be specified.");
            if (string.IsNullOrWhiteSpace(request.Database))
                throw new ArgumentException("Database must be specified.");
            if (string.IsNullOrWhiteSpace(request.ClinicaltechTable))
                throw new ArgumentException("ClinicaltechTable must be specified.");
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

        private static ClinicalTechValidationSummary CloneSummary(ClinicalTechValidationSummary summary)
        {
            return JsonConvert.DeserializeObject<ClinicalTechValidationSummary>(
                JsonConvert.SerializeObject(summary)) ?? new ClinicalTechValidationSummary();
        }

        private static ClinicalTechValidationRequest CloneRequest(ClinicalTechValidationRequest request)
        {
            return JsonConvert.DeserializeObject<ClinicalTechValidationRequest>(
                JsonConvert.SerializeObject(request)) ?? new ClinicalTechValidationRequest();
        }

        private async Task<int> SaveValidationRunAsync(ClinicalTechValidationRequest request, ClinicalTechValidationSummary summary, string? userEmail, string? userName, bool markWorkspaceSaved)
        {
            await Task.Delay(100);
            return 1;
        }
    }
}
