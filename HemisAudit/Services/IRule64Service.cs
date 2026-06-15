using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule64Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule64TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule64ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule64VerifyResult> VerifyTablesAsync(Rule64VerifyRequest request);
        Task<Rule64ValidationSummary> RunValidationAsync(Rule64ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule64ValidationSummary?> GetStoredSummaryAsync(int runId);
        Task<Rule64WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule64RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule64WorkspaceSaveResult> SaveWorkspaceAsync(Rule64ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule64WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule64ValidationRequest request);
    }
}
