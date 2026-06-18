using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule67Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule67TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule67VerifyResult> VerifyTablesAsync(Rule67VerifyRequest request);
        Task<Rule67ValidationSummary> RunValidationAsync(Rule67ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule67ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule67WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule67RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule67WorkspaceSaveResult> SaveWorkspaceAsync(Rule67ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule67WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule67ValidationRequest request);
        Task<Rule67ValidationSummary> GetExportSummaryAsync(Rule67ValidationRequest request);
    }
}
