using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IPharmacyService
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<PharmacyTableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<PharmacyVerifyResult> VerifyTablesAsync(PharmacyVerifyRequest request);
        Task<PharmacyValidationSummary> RunValidationAsync(PharmacyValidationRequest request, string? userEmail = null, string? userName = null);
        Task<string> GenerateSqlAsync(PharmacyValidationRequest request);
        Task<PharmacyWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, PharmacyValidationRequest config, string? userEmail = null);

        // Signoff
        Task AddOrUpdateSignoffAsync(int runId, string email, string comment);
        Task RemoveSignoffAsync(int runId, string email);
    }
}
