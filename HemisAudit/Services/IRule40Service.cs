using HemisAudit.ViewModels;

namespace HemisAudit.Services
{
    public interface IRule40Service
    {
        Task<DatabaseListResult> GetDatabasesAsync(string server, string driver);
        Task<Rule40TableDiscoveryResult> GetTablesAsync(string server, string database, string driver);
        Task<List<string>> GetTableColumnsAsync(string server, string database, string driver, string tableName);
        Task<Rule40VerifyResult> VerifyTablesAsync(Rule40VerifyRequest request);
        Task<Rule40ValidationSummary> RunValidationAsync(Rule40ValidationRequest request, string? userEmail = null, string? userName = null);
        Task<Rule40WorkspaceState?> GetCurrentWorkspaceStateAsync(int clientId, string? currentUserEmail = null);
        Task<bool> SaveWorkspaceStateAsync(int clientId, Rule40ValidationRequest request, string? userEmail);
        Task<Rule40ValidationSummary?> GetFullSummaryByRunIdAsync(int runId);
        Task AddOrUpdateSignoffAsync(int runId, string reviewerEmail, string comment);
        Task RemoveSignoffAsync(int runId, string reviewerEmail);
        string GenerateSql(Rule40ValidationRequest request);
    }
}
