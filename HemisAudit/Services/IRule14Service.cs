using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule14Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule14TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule14VerifyResult> VerifyTablesAsync(Rule14VerifyRequest request);
        Task<Rule14ValidationSummary> RunValidationAsync(Rule14ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule14ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule14WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule14RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule14WorkspaceSaveResult> SaveWorkspaceAsync(Rule14ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule14WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule14ValidationRequest request);
        Task<Rule14ValidationSummary> GetExportSummaryAsync(Rule14ValidationRequest request);
        Task<List<string>> GetTableColumnsListAsync(string server, string database, string driver, string tableName);
    }
}



