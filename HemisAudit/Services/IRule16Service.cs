using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule16Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule16TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule16VerifyResult> VerifyTablesAsync(Rule16VerifyRequest request);
        Task<Rule16ValidationSummary> RunValidationAsync(Rule16ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule16ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule16WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule16RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule16WorkspaceSaveResult> SaveWorkspaceAsync(Rule16ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule16WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule16ValidationRequest request);
        Task<Rule16ValidationSummary> GetExportSummaryAsync(Rule16ValidationRequest request);
        Task<List<string>> GetTableColumnsListAsync(string server, string database, string driver, string tableName);
    }
}

