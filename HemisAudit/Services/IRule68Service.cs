using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule68Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule68TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule68VerifyResult> VerifyTablesAsync(Rule68VerifyRequest request);
        Task<Rule68ValidationSummary> RunValidationAsync(Rule68ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule68ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule68WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule68RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule68WorkspaceSaveResult> SaveWorkspaceAsync(Rule68ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule68WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule68ValidationRequest request);
        Task<Rule68ValidationSummary> GetExportSummaryAsync(Rule68ValidationRequest request);
    }
}
