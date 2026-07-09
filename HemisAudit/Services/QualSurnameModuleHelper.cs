using Microsoft.Data.SqlClient;

namespace HemisAudit.Services
{
    /// <summary>
    /// Shared signoff and workspace helpers for qualification/surname modules (Rules 69-75).
    /// All methods accept an already-open SqlConnection so the caller controls the connection.
    /// </summary>
    internal static class QualSurnameModuleHelper
    {
        // ── Signoff ─────────────────────────────────────────────────────────────

        internal static async Task AddOrUpdateSignoffAsync(
            SqlConnection connection,
            int runId,
            int clientId,
            int reviewerId,
            string engagementRole,
            string comment)
        {
            if (!CanSignOffAsRole(engagementRole))
                throw new InvalidOperationException("Only assigned data analysts, managers, and directors can sign off.");

            if (!await IsWorkspaceSavedAsync(connection, runId))
                throw new InvalidOperationException("The data analyst must save the workspace before signoff is available.");

            if (!string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase)
                && !await HasSignoffRoleAsync(connection, runId, "DataAnalyst"))
            {
                throw new InvalidOperationException("The assigned data analyst must sign off before this review can be completed.");
            }

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND ReviewerID = @ReviewerID)
BEGIN
    UPDATE dbo.ReviewSignoffs
    SET SignoffRole = @SignoffRole, ReviewType = 'Final', Comment = @Comment, SignedOffAt = GETDATE()
    WHERE RunID = @RunID AND ReviewerID = @ReviewerID;
END
ELSE
BEGIN
    INSERT INTO dbo.ReviewSignoffs (ClientID, RunID, ReviewerID, SignoffRole, ReviewType, Comment, SignedOffAt)
    VALUES (@ClientID, @RunID, @ReviewerID, @SignoffRole, 'Final', @Comment, GETDATE());
END";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@ReviewerID", reviewerId);
            command.Parameters.AddWithValue("@SignoffRole", engagementRole);
            command.Parameters.AddWithValue("@Comment", string.IsNullOrWhiteSpace(comment) ? DBNull.Value : (object)comment.Trim());
            await command.ExecuteNonQueryAsync();

            await UpdateRunStatusAsync(connection, runId);
        }

        internal static async Task RemoveSignoffAsync(
            SqlConnection connection,
            int runId,
            string engagementRole)
        {
            if (!HemisAudit.Helpers.ValidationRunAccessPolicy.CanAssignedUserRemoveSignoff(engagementRole))
                throw new InvalidOperationException("Only the assigned data analyst, manager, or director can remove signoff.");

            var removal = await ReviewSignoffSqlHelper.RemoveRoleSignoffWithVersioningAsync(
                connection, runId, engagementRole, null);
            _ = removal;
        }

        internal static async Task<bool> MarkWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "UPDATE dbo.ValidationRuns SET WorkspaceSavedAt = GETDATE(), Status = 'Needs Review' WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            return await command.ExecuteNonQueryAsync() > 0;
        }

        // ── Signoff state helpers ────────────────────────────────────────────────

        internal static async Task<(bool HasDataAnalystSignoff, bool CurrentUserHasSignedOff, string CurrentUserSignoffComment)>
            GetSignoffStateAsync(SqlConnection connection, int runId, int? currentUserId, string currentUserEngagementRole)
        {
            bool hasDA = false, currentSigned = false;
            string currentComment = "";

            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT rs.SignoffRole,
       rs.ReviewerID,
       ISNULL(rs.Comment, '') AS Comment
FROM dbo.ReviewSignoffs rs
WHERE rs.RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var role = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var rId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var comment = reader.IsDBNull(2) ? "" : reader.GetString(2);

                if (string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                    hasDA = true;

                if (currentUserId.HasValue && rId == currentUserId.Value)
                {
                    currentSigned = true;
                    currentComment = comment;
                }
                else if (!string.IsNullOrEmpty(currentUserEngagementRole) &&
                         string.Equals(role, currentUserEngagementRole, StringComparison.OrdinalIgnoreCase))
                {
                    currentSigned = true;
                    currentComment = comment;
                }
            }

            return (hasDA, currentSigned, currentComment);
        }

        internal static async Task<int?> GetClientIdForRunAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT TOP 1 ClientID FROM dbo.ValidationRuns WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
        }

        internal static async Task<string?> GetEngagementRoleAsync(SqlConnection connection, int clientId, int userId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT TOP 1 EngagementRole
FROM dbo.UserClientAssignments
WHERE ClientID = @ClientID AND UserID = @UserID;";
            command.Parameters.AddWithValue("@ClientID", clientId);
            command.Parameters.AddWithValue("@UserID", userId);
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        internal static async Task<bool> IsWorkspaceSavedAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.ValidationRuns
    WHERE RunID = @RunID
      AND (WorkspaceSavedAt IS NOT NULL
           OR EXISTS (SELECT 1 FROM dbo.ReviewSignoffs rs WHERE rs.RunID = ValidationRuns.RunID AND rs.SignoffRole = 'DataAnalyst'))
) THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@RunID", runId);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static bool CanSignOffAsRole(string? role) =>
            string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);

        private static async Task<bool> HasSignoffRoleAsync(SqlConnection connection, int runId, string signoffRole)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = "SELECT COUNT(1) FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = @SignoffRole;";
            command.Parameters.AddWithValue("@RunID", runId);
            command.Parameters.AddWithValue("@SignoffRole", signoffRole);
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }

        private static async Task UpdateRunStatusAsync(SqlConnection connection, int runId)
        {
            await using var command = connection.CreateConfiguredCommand();
            command.CommandText = @"
UPDATE dbo.ValidationRuns
SET Status = CASE
    WHEN EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'DataAnalyst')
     AND EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Manager')
     AND EXISTS (SELECT 1 FROM dbo.ReviewSignoffs WHERE RunID = @RunID AND SignoffRole = 'Director')
    THEN 'Reviewed and Completed'
    ELSE 'Needs Review'
END
WHERE RunID = @RunID;";
            command.Parameters.AddWithValue("@RunID", runId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
