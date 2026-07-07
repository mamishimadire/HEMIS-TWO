using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule21Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule21TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<Rule21ColumnDiscoveryResult> GetColumnsAsync(string server, string database, string driver, string tableName, string tableRole);
        Task<Rule21DistinctValuesResult> GetDistinctValuesAsync(string server, string database, string driver, string tableName, string columnName, string? preferredValue);
        Task<Rule21VerifyResult> VerifyTablesAsync(Rule21VerifyRequest request);
        Task<Rule21ValidationSummary> RunValidationAsync(Rule21ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule21WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<Rule21RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null);
        Task<Rule21WorkspaceSaveResult> SaveWorkspaceAsync(Rule21ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule21WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule21ValidationRequest request);
    }
}

