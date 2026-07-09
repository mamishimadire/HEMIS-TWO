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
    public class BiomedicalController : Controller
    {
        private readonly IBiomedicalService _biomedical;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public BiomedicalController(
            IBiomedicalService biomedical,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _biomedical = biomedical;
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
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(74, clientId);
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

            var workspace = await _biomedical.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(
                async () => await _biomedical.GetDatabasesAsync(model.Server, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(
                async () => await _biomedical.GetTablesAsync(model.Server, model.Database, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] BiomedicalVerifyRequest request)
        {
            var tableName = !string.IsNullOrWhiteSpace(request.TableName) ? request.TableName : request.BiomedicalTable;
            var result = await RequireDataAnalystAsync(
                async () => await _biomedical.GetColumnsAsync(request.Server, request.Database, request.Driver, tableName));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] BiomedicalVerifyRequest request)
        {
            var result = await RequireDataAnalystAsync(
                async () => await _biomedical.VerifyTablesAsync(request));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] BiomedicalValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new BiomedicalValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new BiomedicalValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new BiomedicalValidationSummary { Success = false, Error = "Only the assigned data analyst can run Biomedical validation." });

            var result = await _biomedical.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);

            if (result.Success)
                await _audit.LogAsync("run_validation", $"Biomedical on client {request.ClientId}: {result.Status} ({result.FailCount} fail rows).", user?.Id, user?.Email);

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] BiomedicalValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before saving." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new { success = false, error = "You cannot access this engagement." });

            var result = await _biomedical.SaveWorkspaceStateAsync(request.ClientId, request, user?.Email);

            if (result)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Biomedical workspace for client {request.ClientId}.", user?.Id, user?.Email);

            return Json(new { success = result, message = result ? "Workspace saved successfully." : "Failed to save workspace." });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] BiomedicalValidationRequest request)
        {
            var result = await RequireDataAnalystAsync(async () =>
            {
                var sql = await _biomedical.GenerateSqlAsync(request);
                return new { success = true, sql } as object;
            });
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> AddSignoff([FromBody] QualSurnameSignoffInput model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return Json(new { success = false, error = "Not authenticated." });
            try
            {
                await _biomedical.AddOrUpdateSignoffAsync(model.RunId, user.Email!, model.Comment);
                await _audit.LogAsync("add_signoff", $"Biomedical signoff added for run {model.RunId}.", user.Id, user.Email);
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveSignoff([FromBody] QualSurnameSignoffInput model)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return Json(new { success = false, error = "Not authenticated." });
            try
            {
                await _biomedical.RemoveSignoffAsync(model.RunId, user.Email!);
                await _audit.LogAsync("remove_signoff", $"Biomedical signoff removed for run {model.RunId}.", user.Id, user.Email);
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
        }

        [HttpPost]
        public IActionResult DownloadExcel([FromBody] BiomedicalValidationSummary summary)
        {
            var rows = (summary.ReviewRows ?? new()).Select(r => (r.BiomedicalQualification, r.BiomedicalSurname, r.Status, r.ProductionQualification, r.ProductionSurname));
            var bytes = _export.ExportQualSurnameExcel("Biomedical", 74, summary.TotalValidated, summary.PassCount, summary.FailCount, summary.ExceptionRate, summary.Status ?? "", "Biomedical", "Clinical_Production", "QUALIFICATION", "Surname", rows);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Rule74_Biomedical.xlsx");
        }

        [HttpPost]
        public async Task<IActionResult> DownloadSql([FromBody] BiomedicalValidationRequest request)
        {
            var sql = await _biomedical.GenerateSqlAsync(request);
            return Json(new { success = true, sql });
        }

        // ── Helper methods ─────────────────────────────────────────────────────

        private async Task<T> RequireDataAnalystAsync<T>(Func<Task<T>> operation) where T : class
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return new { success = false, error = "Only data analysts can perform this operation." } as T ?? (T)(object)new { };

            return await operation();
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole)) return systemRole!;
            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }

        private static bool CanViewWorkspaceResults(string role, BiomedicalWorkspaceState? workspace)
        {
            if (workspace == null) return false;
            if (string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
            return workspace.IsWorkspaceSaved && workspace.HasDataAnalystSignoff;
        }
    }
}
