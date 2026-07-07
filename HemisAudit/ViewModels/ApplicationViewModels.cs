using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

using HemisAudit.Helpers;

namespace HemisAudit.ViewModels
{
    // ═══════════════════════════════════════════════════════════════════════════
    // AUTH
    // ═══════════════════════════════════════════════════════════════════════════
    public class LoginViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    public class ChangePasswordViewModel
    {
        [Required, DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = "";

        [Required, MinLength(8), DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";

        public PasswordStatusViewModel PasswordStatus { get; set; } = new();
    }

    public class RenewPasswordViewModel : IValidatableObject
    {
        [EmailAddress]
        public string Email { get; set; } = "";

        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = "";

        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";

        public bool IsPasswordExpiredFlow { get; set; }
        public int Step { get; set; } = 1;
        public bool IsVerificationConfirmed { get; set; }

        public bool ShowVerificationStep => Step <= 1 || !IsVerificationConfirmed;
        public bool ShowNewPasswordStep => Step >= 2 && IsVerificationConfirmed;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Email))
            {
                yield return new ValidationResult("Email address is required.", new[] { nameof(Email) });
            }

            if (Step <= 1)
            {
                if (string.IsNullOrWhiteSpace(CurrentPassword))
                {
                    yield return new ValidationResult("Old password is required.", new[] { nameof(CurrentPassword) });
                }

                yield break;
            }

            if (string.IsNullOrWhiteSpace(NewPassword))
            {
                yield return new ValidationResult("New password is required.", new[] { nameof(NewPassword) });
            }
            else if (NewPassword.Length < 8)
            {
                yield return new ValidationResult("New password must be at least 8 characters.", new[] { nameof(NewPassword) });
            }

            if (string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                yield return new ValidationResult("Confirm new password is required.", new[] { nameof(ConfirmPassword) });
            }
            else if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
            {
                yield return new ValidationResult("The new password and confirmation password do not match.", new[] { nameof(ConfirmPassword) });
            }
        }
    }

    public class ForgotPasswordViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }

    public class ResetPasswordViewModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Token { get; set; } = "";

        [Required, MinLength(8), DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }

    public class PasswordExpiredViewModel
    {
        public string Email { get; set; } = "";
        public string? Message { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ADMIN – USER MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════════
    public class UserListViewModel
    {
        public string Id { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string? ProfilePicturePath { get; set; }
        public bool IsActive { get; set; }
        public bool IsLockedOut { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public List<string> Roles { get; set; } = new();
        public int AssignedClientsCount { get; set; }
        public PasswordStatusViewModel PasswordStatus { get; set; } = new();
    }

    public class CreateUserViewModel
    {
        [Required, MaxLength(100)]
        public string FirstName { get; set; } = "";

        [Required, MaxLength(100)]
        public string LastName { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [MaxLength(20)]
        public string EmployeeCode { get; set; } = "";

        [Required]
        public string Role { get; set; } = "DataAnalyst";

        [Required, MinLength(8), DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password)), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }

    public class EditUserViewModel
    {
        public string Id { get; set; } = "";

        [Required, MaxLength(100)]
        public string FirstName { get; set; } = "";

        [Required, MaxLength(100)]
        public string LastName { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [MaxLength(20)]
        public string EmployeeCode { get; set; } = "";

        [Phone]
        public string? PhoneNumber { get; set; }

        [MaxLength(50)]
        public string? Gender { get; set; }

        [MaxLength(150)]
        public string? Department { get; set; }

        [MaxLength(500)]
        public string? OfficeAddress { get; set; }

        public string? CurrentProfilePicturePath { get; set; }

        [Required]
        public string Role { get; set; } = "DataAnalyst";

        public bool IsActive { get; set; } = true;
    }

    public class ProfileEditViewModel
    {
        [Required, MaxLength(100)]
        public string FirstName { get; set; } = "";

        [Required, MaxLength(100)]
        public string LastName { get; set; } = "";

        [MaxLength(20)]
        public string EmployeeCode { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        public string SystemRole { get; set; } = "";
        public string? CurrentProfilePicturePath { get; set; }
        public IFormFile? ProfilePicture { get; set; }

        [Phone]
        public string? PhoneNumber { get; set; }

        [MaxLength(50)]
        public string? Gender { get; set; }

        [MaxLength(150)]
        public string? Department { get; set; }

        [MaxLength(500)]
        public string? OfficeAddress { get; set; }
    }

    public class ProfilePasswordChangeViewModel
    {
        [Required, DataType(DataType.Password)]
        public string OldPassword { get; set; } = "";

        [Required, MinLength(8), DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Required, Compare(nameof(NewPassword)), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }

    public class PasswordStatusViewModel
    {
        public DateTime? ReferenceDateUtc { get; set; }
        public int AgeDays { get; set; }
        public int DaysRemaining { get; set; }
        public int MaxAgeDays { get; set; }
        public int WarningWindowDays { get; set; }
        public bool IsExpired { get; set; }
        public bool IsExpiringSoon { get; set; }
    }

    public class ProfilePageViewModel
    {
        public string UserId { get; set; } = "";
        public ProfileEditViewModel Edit { get; set; } = new();
        public ProfilePasswordChangeViewModel PasswordChange { get; set; } = new();
        public PasswordStatusViewModel PasswordStatus { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ADMIN – CLIENT MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════════
    public class ClientListViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string FiscalYear { get; set; } = "";
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string Industry { get; set; } = "";
        public string DirectorName { get; set; } = "";
        public string DirectorEmail { get; set; } = "";
        public string DirectorEmpCode { get; set; } = "";
        public string ManagerName { get; set; } = "";
        public string ManagerEmail { get; set; } = "";
        public string ManagerEmpCode { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string CreatedByName { get; set; } = "";
        public int AssignedUsersCount { get; set; }
        public int ValidationRunsCount { get; set; }
        public int? LatestRunId { get; set; }
        public int? LatestRunRuleNumber { get; set; }
        public int? LatestSignedOffRunId { get; set; }
        public int? LatestSignedOffRunRuleNumber { get; set; }
        public string? LastRunStatus { get; set; }
        public DateTime? LastRunAt { get; set; }
        public string? LatestSignedOffStatus { get; set; }
        public DateTime? LatestSignedOffAt { get; set; }
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool IsFavorite { get; set; }
        public bool IsApproved =>
            string.Equals(Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);
        public bool IsArchived =>
            string.Equals(Status, "Archived", StringComparison.OrdinalIgnoreCase);
        public bool IsActiveEngagement => IsApproved && !IsArchived;
        public bool CanRunModules => IsActiveEngagement;
    }

    public class CreateClientViewModel
    {
        [Required, MaxLength(255)]
        public string EngagementName { get; set; } = "";

        [Required, MaxLength(100)]
        public string MaconomyNumber { get; set; } = "";

        [Required, MaxLength(100)]
        public string Industry { get; set; } = "";

        [Required, MaxLength(200)]
        public string DirectorName { get; set; } = "";

        [Required, EmailAddress, MaxLength(255)]
        public string DirectorEmail { get; set; } = "";

        [Required, MaxLength(50)]
        public string DirectorEmpCode { get; set; } = "";

        [Required, MaxLength(200)]
        public string ManagerName { get; set; } = "";

        [Required, EmailAddress, MaxLength(255)]
        public string ManagerEmail { get; set; } = "";

        [Required, MaxLength(50)]
        public string ManagerEmpCode { get; set; } = "";
    }

    public class ClientDetailViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string FiscalYear { get; set; } = "";
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string Industry { get; set; } = "";
        public string DirectorName { get; set; } = "";
        public string DirectorEmail { get; set; } = "";
        public string DirectorEmpCode { get; set; } = "";
        public string ManagerName { get; set; } = "";
        public string ManagerEmail { get; set; } = "";
        public string ManagerEmpCode { get; set; } = "";
        public string? Description { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string CreatedByName { get; set; } = "";
        public bool IsApproved =>
            string.Equals(Status, "Approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);
        public bool IsArchived =>
            string.Equals(Status, "Archived", StringComparison.OrdinalIgnoreCase);
        public List<ClientUserRow> AssignedUsers { get; set; } = new();
        public List<ValidationRunRow> ValidationRuns { get; set; } = new();
        public List<UserListViewModel> AvailableUsers { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool CanArchive { get; set; }
        public string? ArchiveEligibilityMessage { get; set; }
        public int? CurrentRunId { get; set; }
        public int? CurrentRunRuleNumber { get; set; }
        public HashSet<int> ScopeRuleNumbers { get; set; } = new();
    }

    public class ClientUserRow
    {
        public int ClientUserId { get; set; }
        public string UserId { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string EngagementRole { get; set; } = "";
        public DateTime AssignedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class ValidationRunRow
    {
        public int Id { get; set; }
        public int ClientId { get; set; }
        public string ClientName { get; set; } = "";
        public int RuleNumber { get; set; }
        public string RuleName { get; set; } = "";
        public string Status { get; set; } = "";
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public int GhostStudentCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public DateTime RunAt { get; set; }
        public string RunByUserName { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public bool IsCurrent { get; set; }
        public int SignoffCount { get; set; }
        public bool HasDataAnalystSignoff { get; set; }
        public bool HasManagerSignoff { get; set; }
        public bool HasDirectorSignoff { get; set; }
        public bool IsReviewedAndCompleted =>
            Status.Contains("Reviewed and Completed", StringComparison.OrdinalIgnoreCase) ||
            HasAllRequiredSignoffs;
        public bool HasAllRequiredSignoffs =>
            HasDataAnalystSignoff &&
            HasManagerSignoff &&
            HasDirectorSignoff;
    }

    public class ModuleSequenceLinkViewModel
    {
        public int RuleNumber { get; set; }
        public string RuleLabel { get; set; } = "";
        public string RuleTitle { get; set; } = "";
        public string Url { get; set; } = "";
        public bool OpensSavedRun { get; set; }
    }

    public class ModuleSequenceNavigationViewModel
    {
        public int CurrentRuleNumber { get; set; }
        public string CurrentRuleLabel { get; set; } = "";
        public string CurrentRuleTitle { get; set; } = "";
        public string? BackToValidationRunsUrl { get; set; }
        public string? BackToEngagementUrl { get; set; }
        public ModuleSequenceLinkViewModel? Previous { get; set; }
        public ModuleSequenceLinkViewModel? Next { get; set; }
    }

    public class RunSignoffViewModel
    {
        public int Id { get; set; }
        public string SignoffRole { get; set; } = "";
        public string ReviewerName { get; set; } = "";
        public string ReviewerEmail { get; set; } = "";
        public string Comment { get; set; } = "";
        public DateTime SignedOffAt { get; set; }
        public bool IsCurrentUser { get; set; }
    }

    public class Rule36RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public bool IsCurrentRun { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string GeneratedSql { get; set; } = "";
        public ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            Signoffs.Any(s => ValidationRunAccessPolicy.IsSignoffOwnedByEngagementRole(s.SignoffRole, CurrentUserEngagementRole));
        public bool CanCurrentUserSignOff => IsCurrentRun && ValidationRunAccessPolicy.CanAssignedUserSignOff(CurrentUserEngagementRole);
        public bool CanCurrentUserRemoveSignoff => IsCurrentRun && CurrentUserHasSignedOff;
        public bool CanCurrentUserDownload => ValidationRunAccessPolicy.CanAssignedUserDownload(CurrentUserEngagementRole);
    }

    public class Rule36WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string StudTable { get; set; } = "";
        public string DeceasedTable { get; set; } = "";
        public string StudColumn { get; set; } = "";
        public string DeceasedColumn { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public bool IsWorkspaceSaved { get; set; }
        public ValidationSummary? Summary { get; set; }
    }

    public class Rule36RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule36WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class AssignUserViewModel
    {
        public int ClientId { get; set; }
        public string UserId { get; set; } = "";
        public string EngagementRole { get; set; } = "DataAnalyst";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DASHBOARD
    // ═══════════════════════════════════════════════════════════════════════════
    public class DashboardViewModel
    {
        public int TotalClients { get; set; }
        public int PendingApprovalClients { get; set; }
        public int FavoriteClients { get; set; }
        public int TotalUsers { get; set; }
        public int TotalValidationRuns { get; set; }
        public int TotalExceptions { get; set; }
        public int ApprovedClients { get; set; }
        public int ArchivedClients { get; set; }
        public int DisplayedClientCount { get; set; }
        public int ReviewedAndCompletedRuns { get; set; }
        public int NeedsReviewRuns { get; set; }
        public int AwaitingReviewRuns { get; set; }
        public int PassedRuleRuns { get; set; }
        public int FailedRuleRuns { get; set; }
        public int PassedRuleRecords { get; set; }
        public int FailedRuleRecords { get; set; }
        public decimal PassedRuleRecordRate { get; set; }
        public decimal FailedRuleRecordRate { get; set; }
        public int AnalystSignedRuns { get; set; }
        public int UnsignedAnalystRuns { get; set; }
        public int ManagerSignedRuns { get; set; }
        public int DirectorSignedRuns { get; set; }
        public int ArchiveReadyEngagements { get; set; }
        public int HistorySignedRuns { get; set; }
        public string CurrentScope { get; set; } = "active";
        public string? CurrentSearch { get; set; }
        public List<ClientListViewModel> PortfolioClients { get; set; } = new();
        public List<ClientListViewModel> ArchivedPortfolioClients { get; set; } = new();
        public List<ClientListViewModel> PendingApprovalQueue { get; set; } = new();
        public List<DashboardIndustryMetric> IndustryBreakdown { get; set; } = new();
        public List<DashboardRuleOutcomeMetric> RuleOutcomeBreakdown { get; set; } = new();
        public List<DashboardEngagementInsightViewModel> EngagementInsights { get; set; } = new();
        public List<DashboardRuleOutcomeMetric> HistoryRuleOutcomeBreakdown { get; set; } = new();
        public List<ValidationRunRow> CurrentRuns { get; set; } = new();
        public List<ValidationRunRow> HistoryRuns { get; set; } = new();
        public List<ValidationRunRow> RecentRuns { get; set; } = new();
        public string CurrentUserName { get; set; } = "";
        public string CurrentUserRole { get; set; } = "";
    }

    public class ArchiveEligibilityViewModel
    {
        public bool CanArchive { get; set; }
        public int? CurrentRunId { get; set; }
        public int? CurrentRunRuleNumber { get; set; }
        public string Message { get; set; } = "";
        public List<string> MissingSignoffRoles { get; set; } = new();
    }

    public class DashboardIndustryMetric
    {
        public string Industry { get; set; } = "";
        public int Count { get; set; }
    }

    public class DashboardRuleOutcomeMetric
    {
        public int RuleNumber { get; set; }
        public string RuleLabel { get; set; } = "";
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
    }

    public class DashboardEngagementInsightViewModel
    {
        public int ClientId { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public string Industry { get; set; } = "";
        public string Status { get; set; } = "";
        public string CurrentUserEngagementRole { get; set; } = "";
        public int ReviewedAndCompletedRuns { get; set; }
        public int NeedsReviewRuns { get; set; }
        public int PassedRuleRuns { get; set; }
        public int FailedRuleRuns { get; set; }
        public int PassedRuleRecords { get; set; }
        public int FailedRuleRecords { get; set; }
        public decimal PassedRuleRecordRate { get; set; }
        public decimal FailedRuleRecordRate { get; set; }
        public int AnalystSignedRuns { get; set; }
        public int ManagerSignedRuns { get; set; }
        public int DirectorSignedRuns { get; set; }
        public List<DashboardRuleOutcomeMetric> RuleOutcomeBreakdown { get; set; } = new();
        public List<ValidationRunRow> RecentRuns { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RULE 36 – CONNECTION / VALIDATION
    // ═══════════════════════════════════════════════════════════════════════════
    public class ConnectionViewModel
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
    }

    public class DatabaseListResult
    {
        public bool Success { get; set; }
        public List<string> Databases { get; set; } = new();
        public string? Error { get; set; }
    }

    public class TableListResult
    {
        public bool Success { get; set; }
        public List<string> Tables { get; set; } = new();
        public string? AutoStudTable { get; set; }
        public string? AutoDeceasedTable { get; set; }
        public string? AutoQualTable { get; set; }
        public string? Error { get; set; }
    }

    public class ColumnListResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoSelected { get; set; }
        public string? Error { get; set; }
    }

    public class VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string DeceasedTable { get; set; } = "";
        public string StudColumn { get; set; } = "";
        public string DeceasedColumn { get; set; } = "";
    }

    public class VerifyResult
    {
        public bool Success { get; set; }
        public int StudTotal { get; set; }
        public int DeceasedTotal { get; set; }
        public int MatchingRecords { get; set; }
        public string? Error { get; set; }
    }

    public class ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string DeceasedTable { get; set; } = "";
        public string StudColumn { get; set; } = "";
        public string DeceasedColumn { get; set; } = "";
    }

    public class ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string StudTable { get; set; } = "";
        public string DeceasedTable { get; set; } = "";
        public string StudColumn { get; set; } = "";
        public string DeceasedColumn { get; set; } = "";
        public string Database { get; set; } = "";
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<ValidationRowRecord> ValidationRows { get; set; } = new();
        public List<ExceptionRecord> Exceptions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule36WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class AuditLogRowViewModel
    {
        public int LogId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? UserName { get; set; }
        public string Action { get; set; } = "";
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }
        public string? IpAddress { get; set; }
        public string? PreviousHash { get; set; }
        public string? RecordHash { get; set; }
    }

    public class MessageRecipientOptionViewModel
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public bool Selected { get; set; }
    }

    public class MessageSummaryViewModel
    {
        public int ThreadId { get; set; }
        public string Subject { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string Preview { get; set; } = "";
        public DateTime LastMessageAt { get; set; }
        public string LastSenderName { get; set; } = "";
        public int UnreadCount { get; set; }
        public bool HasUnread => UnreadCount > 0;
        public bool IsActive { get; set; }
    }

    public class MessageItemViewModel
    {
        public int MessageId { get; set; }
        public int ThreadId { get; set; }
        public string SenderName { get; set; } = "";
        public string SenderEmail { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime SentAt { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public int RecipientCount { get; set; }
        public int ReadCount { get; set; }
        public DateTime? FirstReadAt { get; set; }
        public DateTime? LastReadAt { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public bool IsEdited { get; set; }
        public DateTime? EditedAt { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public List<MessageAttachmentViewModel> Attachments { get; set; } = new();
    }

    public class MessageAttachmentInput
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long FileSize { get; set; }
        public string AttachmentKind { get; set; } = "file";
    }

    public class MessageAttachmentViewModel
    {
        public int AttachmentId { get; set; }
        public int MessageId { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long FileSize { get; set; }
        public string AttachmentKind { get; set; } = "file";
        public bool IsImage => string.Equals(AttachmentKind, "image", StringComparison.OrdinalIgnoreCase);
        public bool IsAudio => string.Equals(AttachmentKind, "audio", StringComparison.OrdinalIgnoreCase);
        public bool IsVideo => string.Equals(AttachmentKind, "video", StringComparison.OrdinalIgnoreCase);
    }

    public class MessageThreadViewModel
    {
        public int ThreadId { get; set; }
        public int? ClientId { get; set; }
        public string Subject { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string CreatedByName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastMessageAt { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
        public List<string> Participants { get; set; } = new();
        public List<MessageItemViewModel> Messages { get; set; } = new();
    }

    public class MessageThreadEditViewModel
    {
        public int ThreadId { get; set; }
        public int? ClientId { get; set; }
        [Required, MaxLength(255)]
        public string Subject { get; set; } = "";
    }

    public class MessageSendViewModel
    {
        public int? ClientId { get; set; }
        [MaxLength(255)]
        public string Subject { get; set; } = "";
        public string Body { get; set; } = "";
        public List<int> RecipientIds { get; set; } = new();
        public string RecipientIdsCsv { get; set; } = "";
        public int? ReplyToMessageId { get; set; }
        public List<IFormFile> Attachments { get; set; } = new();
    }

    public class MessageReplyViewModel
    {
        public int? ClientId { get; set; }
        public int? ThreadId { get; set; }
        public string Body { get; set; } = "";
        public List<IFormFile> Attachments { get; set; } = new();
    }

    public class MessageEditViewModel
    {
        public int MessageId { get; set; }
        public int ThreadId { get; set; }
        public int? ClientId { get; set; }
        public string Body { get; set; } = "";
    }

    public class MessagePollViewModel
    {
        public int UnreadCount { get; set; }
        public List<MessageSummaryViewModel> Inbox { get; set; } = new();
        public MessageThreadViewModel? ActiveThread { get; set; }
    }

    public class MessagePageViewModel
    {
        public List<MessageSummaryViewModel> Inbox { get; set; } = new();
        public List<MessageRecipientOptionViewModel> RecipientOptions { get; set; } = new();
        public MessageThreadViewModel? ActiveThread { get; set; }
        public MessageSendViewModel Compose { get; set; } = new();
        public MessageThreadEditViewModel EditThread { get; set; } = new();
        public MessageEditViewModel EditMessage { get; set; } = new();
        public int UnreadCount { get; set; }
        public string CurrentRole { get; set; } = "";
        public bool ShowComposeModal { get; set; }
        public bool ShowEditModal { get; set; }
        public bool ShowEditMessageModal { get; set; }
        public int? SelectedThreadId { get; set; }
        public int? EditingMessageId { get; set; }
    }

    public class ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string ValidationResult { get; set; } = "";
        public string? ExceptionReason { get; set; }
        public string StudentId { get; set; } = "";
        public Dictionary<string, string?> AdditionalColumns { get; set; } = new();
    }

    public class ExceptionRecord
    {
        public int ValidationNumber { get; set; }
        public string StudentId { get; set; } = "";
        public string ExceptionReason { get; set; } = "";
        public string ValidationResult { get; set; } = "";
        public Dictionary<string, string?> AdditionalColumns { get; set; } = new();
    }

    public class ExportRequest
    {
        public ValidationSummary Summary { get; set; } = new();
        public string Format { get; set; } = "excel";
    }

    public class SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
