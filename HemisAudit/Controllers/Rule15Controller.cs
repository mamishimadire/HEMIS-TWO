using HemisAudit.Helpers;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class Rule15Controller : Controller
    {
        private readonly IRule15Service _rule15;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule15Controller(
            IRule15Service rule15,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _rule15 = rule15;
            _export = export;
            _audit = audit;
            _users = users;
            _systemDb = systemDb;
        }

        public async Task<IActionResult> Index(int clientId = 0)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "Trainee", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Only assigned engagement members can open audit modules.";
                return RedirectToAction("Index", "Dashboard");
            }

            var clients = await _systemDb.GetClientsAsync(user, role, approvedOnly: true);
            if (clientId > 0 && !await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                TempData["Error"] = "You cannot access this engagement.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Clients = clients
                .Select(c => new Client
                {
                    Id = c.Id,
                    Name = c.EngagementName,
                    FiscalYear = c.MaconomyNumber,
                    Status = c.Status,
                    CreatedAt = c.CreatedAt,
                    CreatedByUserId = "",
                    IsActive = true
                })
                .ToList();
            ViewBag.ClientId = clientId;
            ViewBag.CurrentSystemRole = role;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(15, clientId);
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkspaceState(int clientId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (clientId <= 0)
                return Json(new { success = true, hasWorkspace = false });

            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return Json(new { success = false, error = "You cannot access this engagement." });
            }

            var workspace = await _rule15.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);

            if (workspace != null)
                workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new
            {
                success = true,
                hasWorkspace = workspace != null,
                resultsVisible,
                workspace
            });
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            var review = await _rule15.GetSavedRunAsync(id, user?.Email);
            if (review == null)
                return NotFound();

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to this saved validation run.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!CanViewSavedRun(review, role))
            {
                TempData["Error"] = "Only analyst-signed validation results are available for review.";
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.IsAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
            ViewBag.CanDownloadSavedRun = CanDownloadSavedRun(review, role);
            ViewBag.CanManageEngagement =
                string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase);
            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            var isArchived = clientDetail?.IsArchived == true;
            ViewBag.IsArchived = isArchived;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForSavedRun(
                15,
                review.ClientId,
                clientDetail?.ValidationRuns,
                role,
                review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                !isArchived &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = await _rule15.GenerateSqlAsync(new Rule15ValidationRequest
            {
                ClientId = review.ClientId,
                Server = review.SourceServer,
                Database = review.Summary.Database,
                StudTable = review.Summary.StudTable,
                BridgeTable = review.Summary.BridgeTable,
                CrseTable = review.Summary.CrseTable
            });

            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule15.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _rule15.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTableColumns([FromBody] Rule16ColumnValuesRequest request)
        {
            try
            {
                var cols = await _rule15.GetTableColumnsListAsync(request.Server, request.Database, request.Driver, request.TableName);
                return Json(new { success = true, columns = cols });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message, columns = Array.Empty<string>() });
            }
        }

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule15VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _rule15.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule15ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
            {
                return Json(new Rule15ValidationSummary
                {
                    Success = false,
                    Error = "Select an approved engagement before running validation."
                });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
            {
                return Json(new Rule15ValidationSummary
                {
                    Success = false,
                    Error = "You cannot access this engagement."
                });
            }

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new Rule15ValidationSummary
                {
                    Success = false,
                    Error = "Only the assigned data analyst can run Rule 15."
                });
            }

            async Task<Rule15ValidationSummary> ExecuteValidationAsync(IRule15Service ruleService, IAuditLogService auditService)
            {
                var result = await ruleService.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
                if (result.Success)
                {
                    await auditService.LogAsync(
                        "run_validation",
                        $"Rule 15 on client {request.ClientId}: {result.Status} ({result.FailCount} fail rows), run {result.SavedRunId}",
                        user?.Id,
                        user?.Email);
                }

                return result;
            }

            if (ValidationOperationHttpHelper.IsAsyncRequested(Request))
            {
                return ValidationOperationHttpHelper.Queue(
                    this,
                    HttpContext.RequestServices.GetRequiredService<IValidationOperationService>(),
                    ValidationOperationHttpHelper.ResolveOwnerKey(User),
                    "Rule 15 validation",
                    async (sp, ct) => await ExecuteValidationAsync(
                        sp.GetRequiredService<IRule15Service>(),
                        sp.GetRequiredService<IAuditLogService>()));
            }

            var result = await ExecuteValidationAsync(_rule15, _audit);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule15ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
            {
                return Json(new Rule15WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Only the assigned data analyst can edit a saved workspace."
                });
            }

            if (!request.RunId.HasValue || request.RunId.Value <= 0)
            {
                return Json(new Rule15WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Select a saved run before editing the workspace."
                });
            }

            var result = await _rule15.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync(
                    "workspace_edit_started",
                    $"DataAnalyst started editing Rule 15 run {request.RunId.Value}. Existing signoffs were cleared.",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule15ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!await CanEditWorkspaceAsync(request.ClientId, user, role))
            {
                return Json(new Rule15WorkspaceSaveResult
                {
                    Success = false,
                    Error = "Only the assigned data analyst can save a workspace."
                });
            }

            var result = await _rule15.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success)
            {
                await _audit.LogAsync(
                    "save_validation_workspace",
                    $"DataAnalyst saved Rule 15 workspace for client {request.ClientId}. Signoffs cleared: {result.ClearedSignoffCount ?? 0}",
                    user?.Id,
                    user?.Email);
            }

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule15WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before signing off." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off the workspace." });

            if (!model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Run the validation first so the workspace is saved." });

            var review = await _rule15.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });

            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff is only available on the current run." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only. Signoff is disabled." });

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });
            }

            try
            {
                await _rule15.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync(
                    "signoff_validation_run",
                    $"Rule 15 signoff saved for run {model.RunId.Value} from module workspace",
                    user.Id,
                    user.Email);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }

            var workspace = await _rule15.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email, includeSummary: false);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule15WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0)
                return Json(new { success = false, error = "Select a saved run before removing signoff." });

            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role))
                return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _rule15.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId)
                return Json(new { success = false, error = "The saved validation run could not be found for this engagement." });

            if (!review.IsCurrentRun)
                return Json(new { success = false, error = "History results are read-only. Signoff can only be removed from the current run." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
                return Json(new { success = false, error = "Archived engagements are read-only. Signoff removal is disabled." });

            if (!review.CurrentUserHasSignedOff)
                return Json(new { success = false, error = "There is no signoff for your assigned engagement role to remove." });

            try
            {
                await _rule15.RemoveSignoffAsync(model.RunId.Value, user!.Email!);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }

            var workspace = await _rule15.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email, includeSummary: false);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            var reopenedRunId = workspace?.RunId;
            var preservedHistory = reopenedRunId.HasValue && reopenedRunId.Value != model.RunId.Value;
            var message = preservedHistory
                ? $"Signoff removed. Run #{model.RunId.Value} moved to history and Run #{reopenedRunId.Value} is now the current workspace."
                : "Signoff removed.";
            await _audit.LogAsync(
                "remove_validation_signoff",
                preservedHistory
                    ? $"{review.CurrentUserEngagementRole} removed signoff for Rule 15 run {model.RunId.Value} from module workspace. Historical snapshot preserved; new current run {reopenedRunId.Value} created for continued review."
                    : $"{review.CurrentUserEngagementRole} removed signoff for Rule 15 run {model.RunId.Value} from module workspace",
                user.Id,
                user.Email);
            return Json(new { success = true, message, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule15ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
            {
                return Json(new Rule15SqlResult
                {
                    Success = false,
                    Error = "You cannot access this engagement."
                });
            }

            return Json(await RequireDataAnalystAsync(async () =>
                new Rule15SqlResult
                {
                    Success = true,
                    Sql = await _rule15.GenerateSqlAsync(request)
                }));
        }
        [HttpPost]
        public async Task<IActionResult> GenerateRScript([FromBody] Rule15ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule15SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(await RequireDataAnalystAsync(async () => new Rule15SqlResult
            {
                Success = true,
                Sql = Rule15RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule15")
            }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSignoff(Rule15RunSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule15.GetSavedRunAsync(model.RunId, user?.Email);
            if (review == null)
                return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only. Signoff is disabled.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to sign off this run.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!CanViewSavedRun(review, role))
            {
                TempData["Error"] = "Only analyst-signed validation results are available for review.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!review.IsCurrentRun)
            {
                TempData["Error"] = "History results are read-only. Signoff is only available on the current run.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!review.CanCurrentUserSignOff)
            {
                TempData["Error"] = "Only the assigned data analyst, manager, or director can sign off this run.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            {
                TempData["Error"] = "The assigned data analyst must sign off before this review can be completed.";
                return RedirectToAction(nameof(Run), new { id = model.RunId });
            }

            await _rule15.AddOrUpdateSignoffAsync(model.RunId, user!.Email!, model.Comment);
            await _audit.LogAsync(
                "signoff_validation_run",
                $"{review.CurrentUserEngagementRole} signed off Rule 15 run {model.RunId}",
                user.Id,
                user.Email);

            TempData["Success"] = "Signoff saved.";
            return RedirectToAction(nameof(Run), new { id = model.RunId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveSignoff(int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule15.GetSavedRunAsync(runId, user?.Email);
            if (review == null)
                return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only. Signoff removal is disabled.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to remove this signoff.";
                return RedirectToAction("Index", "Dashboard");
            }

            if (!review.IsCurrentRun)
            {
                TempData["Error"] = "History results are read-only. Signoff cannot be removed from a history run.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            if (!review.CurrentUserHasSignedOff)
            {
                TempData["Error"] = "There is no signoff for your assigned engagement role to remove.";
                return RedirectToAction(nameof(Run), new { id = runId });
            }

            await _rule15.RemoveSignoffAsync(runId, user!.Email!);
            var workspace = await _rule15.GetCurrentWorkspaceStateAsync(review.ClientId, user?.Email, includeSummary: false);
            var redirectRunId = workspace?.RunId ?? runId;
            var preservedHistory = workspace?.RunId.HasValue == true && workspace.RunId.Value != runId;
            await _audit.LogAsync(
                "remove_validation_signoff",
                preservedHistory
                    ? $"{review.CurrentUserEngagementRole} removed signoff for Rule 15 run {runId}. Historical snapshot preserved; new current run {redirectRunId} created for continued review."
                    : $"{review.CurrentUserEngagementRole} removed signoff for Rule 15 run {runId}",
                user.Id,
                user.Email);

            TempData["Success"] = preservedHistory
                ? $"Signoff removed. Run #{runId} moved to history and Run #{redirectRunId} is now current."
                : "Signoff removed.";
            return RedirectToAction(nameof(Run), new { id = redirectRunId });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExcel(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var bytes = _export.ExportExcel(review.Summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule15_Course_Credentials_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedCsv(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var bytes = _export.ExportCsv(review.Summary);
            return File(bytes, "text/csv", $"Rule15_Course_Credentials_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedSql(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null)
                return RedirectToAction(nameof(Run), new { id = runId });

            var request = new Rule15ValidationRequest
            {
                ClientId = review.ClientId,
                Server = review.SourceServer,
                Database = review.Summary.Database,
                StudTable = review.Summary.StudTable,
                BridgeTable = review.Summary.BridgeTable,
                CrseTable = review.Summary.CrseTable
            };

            var bytes = _export.ExportSql(await _rule15.GenerateSqlAsync(request));
            return File(bytes, "application/sql", $"Rule15_Course_Credentials_{runId}.sql");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule15ValidationSummary summary)
        {
            summary = await ResolveExportSummaryAsync(summary);
            var bytes = _export.ExportExcel(summary);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule15_Course_Credentials_{Ts()}.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule15ValidationSummary summary)
        {
            summary = await ResolveExportSummaryAsync(summary);
            var bytes = _export.ExportCsv(summary);
            return File(bytes, "text/csv", $"Rule15_Course_Credentials_{Ts()}.csv");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSql([FromBody] Rule15ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, error = "Only the assigned data analyst can download the SQL script." });

            var bytes = _export.ExportSql(await _rule15.GenerateSqlAsync(request));
            return File(bytes, "application/sql", $"Rule15_Course_Credentials_{Ts()}.sql");
        }

        private async Task<Rule15RunReviewViewModel?> LoadAuthorizedSavedRunAsync(int runId, bool requireDownloadAccess)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _rule15.GetSavedRunAsync(runId, user?.Email, includeFullResults: requireDownloadAccess);
            if (review == null)
            {
                TempData["Error"] = "Saved validation run was not found.";
                return null;
            }

            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role))
            {
                TempData["Error"] = "You do not have access to this saved validation run.";
                return null;
            }

            if (!CanViewSavedRun(review, role))
            {
                TempData["Error"] = "Only analyst-signed validation results are available for review.";
                return null;
            }

            if (requireDownloadAccess && !CanDownloadSavedRun(review, role))
            {
                TempData["Error"] = "The assigned data analyst must sign off before other assigned users can download this run.";
                return null;
            }

            return review;
        }

        private static bool CanDownloadSavedRun(Rule15RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanDownloadSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewSavedRun(Rule15RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanViewSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private async Task<bool> CanEditWorkspaceAsync(int clientId, ApplicationUser? user, string role)
        {
            if (user == null || !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) || clientId <= 0)
                return false;
            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
                return false;
            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return ValidationRunAccessPolicy.IsAssignedDataAnalyst(engagementRole);
        }

        private static bool CanViewWorkspaceResults(string role, Rule15WorkspaceStateViewModel? workspace)
        {
            if (workspace == null)
                return false;

            return ValidationRunAccessPolicy.CanViewSignedResults(role, workspace.CurrentUserEngagementRole, workspace.HasDataAnalystSignoff);
        }

        private async Task<Rule15ValidationSummary> ResolveExportSummaryAsync(Rule15ValidationSummary summary)
        {
            var user = await _users.GetUserAsync(User);

            if (summary.SavedRunId is int savedRunId && savedRunId > 0)
            {
                var review = await _rule15.GetSavedRunAsync(savedRunId, user?.Email, includeFullResults: true);
                if (review?.Summary != null)
                    return review.Summary;
            }

            if (summary.ClientId > 0)
            {
                var workspace = await _rule15.GetCurrentWorkspaceStateAsync(summary.ClientId, user?.Email, includeSummary: false);
                if (workspace?.RunId is int workspaceRunId && workspaceRunId > 0)
                {
                    var review = await _rule15.GetSavedRunAsync(workspaceRunId, user?.Email, includeFullResults: true);
                    if (review?.Summary != null)
                        return review.Summary;
                }
            }

            return summary;
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> action)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                return new { success = false, error = "Only the assigned data analyst can configure or run Rule 15." };
            }

            return await action();
        }

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }
}



