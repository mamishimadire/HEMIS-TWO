using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IBiomedicalService
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<BiomedicalTableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<BiomedicalVerifyResult> VerifyTablesAsync(BiomedicalVerifyRequest request);
        Task<BiomedicalValidationSummary> RunValidationAsync(BiomedicalValidationRequest request, string? userEmail = null, string? userName = null);
        Task<string> GenerateSqlAsync(BiomedicalValidationRequest request);
        Task<BiomedicalWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, BiomedicalValidationRequest config, string? userEmail = null);
        Task AddOrUpdateSignoffAsync(int runId, string email, string comment);
        Task RemoveSignoffAsync(int runId, string email);
    }
}
