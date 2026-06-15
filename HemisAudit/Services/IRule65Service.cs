using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule65Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule65TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule65ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule65VerifyResult> VerifyTablesAsync(Rule65VerifyRequest request);
        Task<Rule65ValidationSummary> RunValidationAsync(Rule65ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule65ValidationSummary?> GetStoredSummaryAsync(int runId);
        Task<Rule65WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule65RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule65WorkspaceSaveResult> SaveWorkspaceAsync(Rule65ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule65WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule65ValidationRequest request);
    }
}
