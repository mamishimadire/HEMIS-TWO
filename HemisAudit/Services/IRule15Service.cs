using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule15Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule15TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule15VerifyResult> VerifyTablesAsync(Rule15VerifyRequest request);
        Task<Rule15ValidationSummary> RunValidationAsync(Rule15ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule15ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule15WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule15RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule15WorkspaceSaveResult> SaveWorkspaceAsync(Rule15ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule15WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule15ValidationRequest request);
        Task<Rule15ValidationSummary> GetExportSummaryAsync(Rule15ValidationRequest request);
        Task<List<string>> GetTableColumnsListAsync(string server, string database, string driver, string tableName);
    }
}


