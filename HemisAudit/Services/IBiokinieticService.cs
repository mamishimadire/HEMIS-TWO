using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IBiokinieticService
    {
        // Database discovery
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<BiokinieticTableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);

        // Verification
        Task<BiokinieticVerifyResult> VerifyTablesAsync(BiokinieticVerifyRequest request);

        // Validation
        Task<BiokinieticValidationSummary> RunValidationAsync(BiokinieticValidationRequest request, string? userEmail = null, string? userName = null);
        Task<string> GenerateSqlAsync(BiokinieticValidationRequest request);
        Task<string> GenerateRScriptAsync(BiokinieticValidationRequest request);

        // Workspace state
        Task<BiokinieticWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, BiokinieticValidationRequest config, string? userEmail = null);

        // Signoff
        Task AddOrUpdateSignoffAsync(int runId, string email, string comment);
        Task RemoveSignoffAsync(int runId, string email);
    }
}
