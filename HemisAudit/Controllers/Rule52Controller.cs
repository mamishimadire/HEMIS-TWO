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
    public class Rule52Controller : Controller
    {
        private readonly IRule52Service _Rule52;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public Rule52Controller(IRule52Service Rule52, IExportService export, IAuditLogService audit, UserManager<ApplicationUser> users, ISystemDatabaseService systemDb)
        {
            _Rule52 = Rule52; _export = export; _audit = audit; _users = users; _systemDb = systemDb;
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

            ViewBag.Clients = clients.Select(c => new Client
            {
                Id = c.Id, Name = c.EngagementName, FiscalYear = c.MaconomyNumber,
                Status = c.Status, CreatedAt = c.CreatedAt, CreatedByUserId = "", IsActive = true
            }).ToList();
            ViewBag.ClientId = clientId;
            ViewBag.CurrentSystemRole = role;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(52, clientId);
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetWorkspaceState(int clientId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            if (clientId <= 0) return Json(new { success = true, hasWorkspace = false });

            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role))
            {
                Response.StatusCode = StatusCodes.Status403Forbidden;
                return Json(new { success = false, error = "You cannot access this engagement." });
            }

            var workspace = await _Rule52.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpGet]
        public async Task<IActionResult> Run(int id)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();

            var review = await _Rule52.GetSavedRunAsync(id, user?.Email);
            if (review == null) return NotFound();

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
            ViewBag.IsArchived = clientDetail?.IsArchived == true;
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForSavedRun(51, review.ClientId, clientDetail?.ValidationRuns, role, review.CurrentUserEngagementRole);
            ViewBag.CanOpenWorkspace =
                clientDetail?.IsArchived != true &&
                await _systemDb.CanAccessClientModuleAsync(review.ClientId, user, role) &&
                (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(review.CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase));

            review.GeneratedSql = await _Rule52.GenerateSqlAsync(new Rule52ValidationRequest
            {
                ClientId = review.ClientId, Server = review.SourceServer, Database = review.Summary.Database,
                ValpacTable = review.Summary.ValpacTable, ProdTable = review.Summary.ProdTable,
                ValpacSubjCol = review.Summary.ValpacSubjCol, ProdSubjCol = review.Summary.ProdSubjCol,
                ApprovalStatusCol = review.Summary.ApprovalStatusCol, ApprovalStatusValues = review.Summary.ApprovalStatusValues
            });

            return View(review);
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _Rule52.GetDatabasesAsync(model.Server, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model) =>
            Json(await RequireDataAnalystAsync(async () => await _Rule52.GetTablesAsync(model.Server, model.Database, model.Driver)));

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] Rule52VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () =>
                await _Rule52.GetColumnsAsync(request.Server, request.Database, request.Driver, request.ValpacTable)));

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] Rule52VerifyRequest request) =>
            Json(await RequireDataAnalystAsync(async () => await _Rule52.VerifyTablesAsync(request)));

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] Rule52ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0) return Json(new Rule52ValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });
            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role)) return Json(new Rule52ValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) || !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new Rule52ValidationSummary { Success = false, Error = "Only the assigned data analyst can run Rule 52." });

            async Task<Rule52ValidationSummary> Execute(IRule52Service svc, IAuditLogService auditSvc)
            {
                var result = await svc.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);
                if (result.Success)
                    await auditSvc.LogAsync("run_validation", $"Rule 52 on client {request.ClientId}: {result.Status} ({result.FailCount} fail rows).", user?.Id, user?.Email);
                return result;
            }

            if (ValidationOperationHttpHelper.IsAsyncRequested(Request))
            {
                return ValidationOperationHttpHelper.Queue(this, HttpContext.RequestServices.GetRequiredService<IValidationOperationService>(),
                    ValidationOperationHttpHelper.ResolveOwnerKey(User), "Rule 52 validation",
                    async (sp, ct) => await Execute(sp.GetRequiredService<IRule52Service>(), sp.GetRequiredService<IAuditLogService>()));
            }

            return Json(await Execute(_Rule52, _audit));
        }

        [HttpPost]
        public async Task<IActionResult> BeginWorkspaceEdit([FromBody] Rule52ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditWorkspaceAsync(request.ClientId, user, role)) return Json(new Rule52WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can edit a saved workspace." });
            if (!request.RunId.HasValue || request.RunId.Value <= 0) return Json(new Rule52WorkspaceSaveResult { Success = false, Error = "Select a saved run before editing." });

            var result = await _Rule52.BeginWorkspaceEditAsync(request.RunId.Value, user!.Email!, user.FullName);
            if (result.Success) await _audit.LogAsync("workspace_edit_started", $"DataAnalyst started editing Rule 52 run {request.RunId.Value}.", user?.Id, user?.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] Rule52ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!await CanEditWorkspaceAsync(request.ClientId, user, role)) return Json(new Rule52WorkspaceSaveResult { Success = false, Error = "Only the assigned data analyst can save a workspace." });

            var result = await _Rule52.SaveWorkspaceAsync(request, user!.Email!, user.FullName);
            if (result.Success) await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Rule 52 workspace for client {request.ClientId}. Run: {result.Workspace?.RunId}", user?.Id, user?.Email);
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SignOffWorkspace([FromBody] Rule52WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0) return Json(new { success = false, error = "Select an engagement before signing off." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role)) return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can sign off." });
            if (!model.RunId.HasValue || model.RunId.Value <= 0) return Json(new { success = false, error = "Run the validation first." });

            var review = await _Rule52.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId) return Json(new { success = false, error = "The saved validation run could not be found." });
            if (!review.IsCurrentRun) return Json(new { success = false, error = "History results are read-only." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true) return Json(new { success = false, error = "Archived engagements are read-only." });
            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
                return Json(new { success = false, error = "The assigned data analyst must sign off before this review can be completed." });

            try
            {
                await _Rule52.AddOrUpdateSignoffAsync(model.RunId.Value, user!.Email!, model.Comment);
                await _audit.LogAsync("signoff_validation_run", $"Rule 52 signoff saved for run {model.RunId.Value}", user.Id, user.Email);
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace = await _Rule52.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email, includeSummary: false);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            return Json(new { success = true, message = "Signoff saved.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveWorkspaceSignoff([FromBody] Rule52WorkspaceSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (model.ClientId <= 0 || !model.RunId.HasValue || model.RunId.Value <= 0) return Json(new { success = false, error = "Select a saved run before removing signoff." });
            if (!await ValidationRunAccessPolicy.CanAssignedUserRemoveOwnSignoffAsync(_systemDb, model.ClientId, user, role)) return Json(new { success = false, error = "Only the assigned data analyst, manager, or director can remove signoff." });

            var review = await _Rule52.GetSavedRunAsync(model.RunId.Value, user?.Email);
            if (review == null || review.ClientId != model.ClientId) return Json(new { success = false, error = "The saved validation run could not be found." });
            if (!review.IsCurrentRun) return Json(new { success = false, error = "History results are read-only." });

            var clientDetail = await _systemDb.GetClientDetailAsync(model.ClientId, user, role);
            if (clientDetail?.IsArchived == true) return Json(new { success = false, error = "Archived engagements are read-only." });
            if (!review.CurrentUserHasSignedOff) return Json(new { success = false, error = "There is no signoff to remove." });

            try { await _Rule52.RemoveSignoffAsync(model.RunId.Value, user!.Email!); }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }

            var workspace = await _Rule52.GetCurrentWorkspaceStateAsync(model.ClientId, user?.Email, includeSummary: false);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;
            await _audit.LogAsync("remove_validation_signoff", $"{review.CurrentUserEngagementRole} removed signoff for Rule 52 run {model.RunId.Value}", user?.Id, user?.Email);
            return Json(new { success = true, message = "Signoff removed.", resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] Rule52ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule52SqlResult { Success = false, Error = "You cannot access this engagement." });
            return Json(await RequireDataAnalystAsync(async () => new Rule52SqlResult { Success = true, Sql = await _Rule52.GenerateSqlAsync(request) }));
        }
        [HttpPost]
        public async Task<IActionResult> GenerateRScript([FromBody] Rule52ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId > 0 && !await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new Rule52SqlResult { Success = false, Error = "You cannot access this engagement." });

            return Json(await RequireDataAnalystAsync(async () => new Rule52SqlResult
            {
                Success = true,
                Sql = Rule52RScriptGenerator.Generate(request) + RScriptScaffold.BuildAutoExportFooter("Rule52")
            }));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSignoff(Rule52RunSignoffInputModel model)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _Rule52.GetSavedRunAsync(model.RunId, user?.Email);
            if (review == null) return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true) { TempData["Error"] = "Archived engagements are read-only."; return RedirectToAction(nameof(Run), new { id = model.RunId }); }
            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role)) { TempData["Error"] = "You do not have access."; return RedirectToAction("Index", "Dashboard"); }
            if (!CanViewSavedRun(review, role)) { TempData["Error"] = "Only analyst-signed results are available."; return RedirectToAction("Index", "Dashboard"); }
            if (!review.IsCurrentRun) { TempData["Error"] = "History results are read-only."; return RedirectToAction(nameof(Run), new { id = model.RunId }); }
            if (!review.CanCurrentUserSignOff) { TempData["Error"] = "Only the assigned data analyst, manager, or director can sign off."; return RedirectToAction(nameof(Run), new { id = model.RunId }); }
            if (!ValidationRunAccessPolicy.CanCompleteReviewSignoff(role, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff))
            { TempData["Error"] = "The data analyst must sign off first."; return RedirectToAction(nameof(Run), new { id = model.RunId }); }

            await _Rule52.AddOrUpdateSignoffAsync(model.RunId, user!.Email!, model.Comment);
            await _audit.LogAsync("signoff_validation_run", $"{review.CurrentUserEngagementRole} signed off Rule 52 run {model.RunId}", user.Id, user.Email);
            TempData["Success"] = "Signoff saved.";
            return RedirectToAction(nameof(Run), new { id = model.RunId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveSignoff(int runId)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _Rule52.GetSavedRunAsync(runId, user?.Email);
            if (review == null) return NotFound();

            var clientDetail = await _systemDb.GetClientDetailAsync(review.ClientId, user, role);
            if (clientDetail?.IsArchived == true) { TempData["Error"] = "Archived engagements are read-only."; return RedirectToAction(nameof(Run), new { id = runId }); }
            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role)) { TempData["Error"] = "You do not have access."; return RedirectToAction("Index", "Dashboard"); }
            if (!review.IsCurrentRun) { TempData["Error"] = "History results are read-only."; return RedirectToAction(nameof(Run), new { id = runId }); }
            if (!review.CurrentUserHasSignedOff) { TempData["Error"] = "No signoff to remove."; return RedirectToAction(nameof(Run), new { id = runId }); }

            await _Rule52.RemoveSignoffAsync(runId, user!.Email!);
            await _audit.LogAsync("remove_validation_signoff", $"{review.CurrentUserEngagementRole} removed signoff for Rule 52 run {runId}", user?.Id, user?.Email);
            TempData["Success"] = "Signoff removed.";
            return RedirectToAction(nameof(Run), new { id = runId });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedExcel(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            return File(_export.ExportRule52Excel(review.Summary), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule52_VALPAC_Production_Run_{runId}.xlsx");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedCsv(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            return File(_export.ExportRule52Csv(review.Summary), "text/csv", $"Rule52_VALPAC_Production_Run_{runId}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSavedSql(int runId)
        {
            var review = await LoadAuthorizedSavedRunAsync(runId, requireDownloadAccess: true);
            if (review == null) return RedirectToAction(nameof(Run), new { id = runId });
            var sql = await _Rule52.GenerateSqlAsync(new Rule52ValidationRequest
            {
                ClientId = review.ClientId, Server = review.SourceServer, Database = review.Summary.Database,
                ValpacTable = review.Summary.ValpacTable, ProdTable = review.Summary.ProdTable,
                ValpacSubjCol = review.Summary.ValpacSubjCol, ProdSubjCol = review.Summary.ProdSubjCol,
                ApprovalStatusCol = review.Summary.ApprovalStatusCol, ApprovalStatusValues = review.Summary.ApprovalStatusValues
            });
            return File(_export.ExportSql(sql), "application/sql", $"Rule52_dbo_QUAL_{runId}.sql");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadExcel([FromBody] Rule52ValidationSummary summary)
        {
            var resolved = await ResolveExportSummaryAsync(summary);
            return File(_export.ExportRule52Excel(resolved), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Rule52_VALPAC_Production_{Ts()}.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadCsv([FromBody] Rule52ValidationSummary summary)
        {
            var resolved = await ResolveExportSummaryAsync(summary);
            return File(_export.ExportRule52Csv(resolved), "text/csv", $"Rule52_VALPAC_Production_{Ts()}.csv");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSql([FromBody] Rule52ValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase)) return Json(new { success = false, error = "Only the assigned data analyst can download the SQL script." });
            return File(_export.ExportSql(await _Rule52.GenerateSqlAsync(request)), "application/sql", $"Rule52_VALPAC_Production_{Ts()}.sql");
        }

        // â”€â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private async Task<Rule52RunReviewViewModel?> LoadAuthorizedSavedRunAsync(int runId, bool requireDownloadAccess)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            var review = await _Rule52.GetSavedRunAsync(runId, user?.Email, includeFullResults: requireDownloadAccess);
            if (review == null) { TempData["Error"] = "Saved validation run was not found."; return null; }
            if (!await _systemDb.CanAccessClientResultsAsync(review.ClientId, user, role)) { TempData["Error"] = "You do not have access."; return null; }
            if (!CanViewSavedRun(review, role)) { TempData["Error"] = "Only analyst-signed results are available."; return null; }
            if (requireDownloadAccess && !CanDownloadSavedRun(review, role)) { TempData["Error"] = "The data analyst must sign off first."; return null; }
            return review;
        }

        private async Task<Rule52ValidationSummary> ResolveExportSummaryAsync(Rule52ValidationSummary summary)
        {
            var user = await _users.GetUserAsync(User);
            if (summary.SavedRunId is int savedRunId && savedRunId > 0)
            {
                var review = await _Rule52.GetSavedRunAsync(savedRunId, user?.Email, includeFullResults: true);
                if (review?.Summary != null) return review.Summary;
            }
            if (summary.ClientId > 0)
            {
                var workspace = await _Rule52.GetCurrentWorkspaceStateAsync(summary.ClientId, user?.Email, includeSummary: false);
                if (workspace?.RunId is int workspaceRunId && workspaceRunId > 0)
                {
                    var review = await _Rule52.GetSavedRunAsync(workspaceRunId, user?.Email, includeFullResults: true);
                    if (review?.Summary != null) return review.Summary;
                }
            }
            return summary;
        }

        private static bool CanDownloadSavedRun(Rule52RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanDownloadSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewSavedRun(Rule52RunReviewViewModel review, string systemRole)
            => ValidationRunAccessPolicy.CanViewSignedResults(systemRole, review.CurrentUserEngagementRole, review.HasDataAnalystSignoff);

        private static bool CanViewWorkspaceResults(string role, Rule52WorkspaceStateViewModel? workspace)
        {
            if (workspace == null) return false;
            return ValidationRunAccessPolicy.CanViewSignedResults(role, workspace.CurrentUserEngagementRole, workspace.HasDataAnalystSignoff);
        }

        private async Task<bool> CanEditWorkspaceAsync(int clientId, ApplicationUser? user, string role)
        {
            if (user == null || !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) || clientId <= 0) return false;
            if (!await _systemDb.CanAccessClientResultsAsync(clientId, user, role)) return false;
            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, role);
            return ValidationRunAccessPolicy.IsAssignedDataAnalyst(engagementRole);
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole)) return systemRole!;
            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private async Task<object> RequireDataAnalystAsync<T>(Func<Task<T>> action) where T : class
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only the assigned data analyst can configure or run Rule 52." };
            var result = await action();
            return result ?? (object)new { success = false, error = "Action returned no result." };
        }

        private static string Ts() => DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }
}

