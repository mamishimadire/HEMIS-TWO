using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface INursingService
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<NursingTableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<NursingVerifyResult> VerifyTablesAsync(NursingVerifyRequest request);
        Task<NursingValidationSummary> RunValidationAsync(NursingValidationRequest request, string? userEmail = null, string? userName = null);
        Task<string> GenerateSqlAsync(NursingValidationRequest request);
        Task<NursingWorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? userEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, NursingValidationRequest config, string? userEmail = null);
        Task AddOrUpdateSignoffAsync(int runId, string email, string comment);
        Task RemoveSignoffAsync(int runId, string email);
    }
}
