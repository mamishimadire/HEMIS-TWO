using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule18Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule18TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<ColumnValuesResult> GetColumnValuesAsync(string server, string database, string driver, string tableName, string columnName);
        Task<ColumnValuesResult> GetTableColumnsListAsync(string server, string database, string driver, string tableName);
        Task<Rule18VerifyResult> VerifyTablesAsync(Rule18VerifyRequest request);
        Task<Rule18ValidationSummary> RunValidationAsync(Rule18ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule18ValidationSummary?> GetPendingValidationPreviewAsync(int clientId, string reviewerEmail);
        Task<bool> HasPendingValidationAsync(int clientId, string reviewerEmail);
        Task<int?> GetClientIdForRunAsync(int runId);
        Task<Rule18WorkspaceStateViewModel?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null, bool includeSummary = true);
        Task<Rule18RunReviewViewModel?> GetSavedRunAsync(int runId, string? currentUserEmail = null, bool includeFullResults = false);
        Task<Rule18WorkspaceSaveResult> SaveWorkspaceAsync(Rule18ValidationRequest request, string reviewerEmail, string? reviewerName = null);
        Task<Rule18WorkspaceSaveResult> BeginWorkspaceEditAsync(int runId, string reviewerEmail, string? reviewerName = null);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        Task<string> GenerateSqlAsync(Rule18ValidationRequest request);
        Task<Rule18ValidationSummary> GetExportSummaryAsync(Rule18ValidationRequest request);
        Task<byte[]> ExportFullExcelAsync(Rule18ValidationSummary summary, string? overrideServer = null);
        Task<byte[]> ExportFullCsvAsync(Rule18ValidationSummary summary, string? overrideServer = null);
    }
}
