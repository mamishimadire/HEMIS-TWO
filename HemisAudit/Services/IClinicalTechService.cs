using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IClinicalTechService
    {
        // Database discovery
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<ClinicalTechTableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);

        // Verification
        Task<ClinicalTechVerifyResult> VerifyTablesAsync(ClinicalTechVerifyRequest request);

        // Validation
        Task<ClinicalTechValidationSummary> RunValidationAsync(ClinicalTechValidationRequest request, string? userEmail = null, string? userName = null);
        Task<string> GenerateSqlAsync(ClinicalTechValidationRequest request);
        Task<string> GenerateRScriptAsync(ClinicalTechValidationRequest request);

        // Workspace state
        Task<ClinicalTechWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, ClinicalTechValidationRequest config, string? userEmail = null);

        // Signoff
        Task AddOrUpdateSignoffAsync(int runId, string email, string comment);
        Task RemoveSignoffAsync(int runId, string email);
    }
}
