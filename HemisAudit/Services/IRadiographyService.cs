using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRadiographyService
    {
        // Database discovery
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<RadiographyTableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);

        // Verification
        Task<RadiographyVerifyResult> VerifyTablesAsync(RadiographyVerifyRequest request);

        // Validation
        Task<RadiographyValidationSummary> RunValidationAsync(RadiographyValidationRequest request, string? userEmail = null, string? userName = null);
        Task<string> GenerateSqlAsync(RadiographyValidationRequest request);
        Task<string> GenerateRScriptAsync(RadiographyValidationRequest request);

        // Workspace state
        Task<RadiographyWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, RadiographyValidationRequest config, string? userEmail = null);
    }
}
