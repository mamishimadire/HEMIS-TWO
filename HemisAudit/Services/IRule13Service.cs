using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule13Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule13TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnListResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole);
        Task<Rule13VerifyResult> VerifyTablesAsync(Rule13VerifyRequest request);
        Task<Rule13ValidationSummary> RunValidationAsync(Rule13ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule13WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule13RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule13WorkspaceSaveResult> SaveWorkspaceAsync(Rule13ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule13WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule13ValidationRequest request);
        Task<Rule13ValidationSummary> GetExportSummaryAsync(Rule13ValidationRequest request);
    }
}

