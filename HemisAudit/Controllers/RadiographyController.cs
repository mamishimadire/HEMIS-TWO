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
    public class RadiographyController : Controller
    {
        private readonly IRadiographyService _radiography;
        private readonly IExportService _export;
        private readonly IAuditLogService _audit;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ISystemDatabaseService _systemDb;

        public RadiographyController(
            IRadiographyService radiography,
            IExportService export,
            IAuditLogService audit,
            UserManager<ApplicationUser> users,
            ISystemDatabaseService systemDb)
        {
            _radiography = radiography;
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
            ViewBag.ModuleNavigation = ModuleSequenceNavigationHelper.BuildForWorkspace(71, clientId);
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

            var workspace = await _radiography.GetCurrentWorkspaceStateAsync(clientId, user?.Email);
            var resultsVisible = CanViewWorkspaceResults(role, workspace);
            if (workspace != null) workspace.ResultsVisible = resultsVisible;

            if (workspace != null && !resultsVisible) workspace.Summary = null;

            return Json(new { success = true, hasWorkspace = workspace != null, resultsVisible, workspace });
        }

        [HttpPost]
        public async Task<IActionResult> GetDatabases([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(
                async () => await _radiography.GetDatabasesAsync(model.Server, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GetTables([FromBody] ConnectionViewModel model)
        {
            var result = await RequireDataAnalystAsync(
                async () => await _radiography.GetTablesAsync(model.Server, model.Database, model.Driver));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> GetColumns([FromBody] RadiographyVerifyRequest request)
        {
            var result = await RequireDataAnalystAsync(
                async () => await _radiography.GetColumnsAsync(request.Server, request.Database, request.Driver, request.RadiographyTable));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> VerifyTables([FromBody] RadiographyVerifyRequest request)
        {
            var result = await RequireDataAnalystAsync(
                async () => await _radiography.VerifyTablesAsync(request));
            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> RunValidation([FromBody] RadiographyValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new RadiographyValidationSummary { Success = false, Error = "Select an approved engagement before running validation." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new RadiographyValidationSummary { Success = false, Error = "You cannot access this engagement." });

            var engagementRole = await _systemDb.GetEngagementRoleAsync(request.ClientId, user, role);
            if (!string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
                return Json(new RadiographyValidationSummary { Success = false, Error = "Only the assigned data analyst can run Radiography validation." });

            var result = await _radiography.RunValidationAsync(request, user?.Email, user?.FullName ?? user?.Email);

            if (result.Success)
                await _audit.LogAsync("run_validation", $"Radiography on client {request.ClientId}: {result.Status} ({result.FailCount} fail rows).", user?.Id, user?.Email);

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> SaveWorkspace([FromBody] RadiographyValidationRequest request)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);

            if (request.ClientId <= 0)
                return Json(new { success = false, error = "Select an engagement before saving." });

            if (!await _systemDb.CanAccessClientResultsAsync(request.ClientId, user, role))
                return Json(new { success = false, error = "You cannot access this engagement." });

            var result = await _radiography.SaveWorkspaceStateAsync(request.ClientId, request, user?.Email);

            if (result)
                await _audit.LogAsync("save_validation_workspace", $"DataAnalyst saved Radiography workspace for client {request.ClientId}.", user?.Id, user?.Email);

            return Json(new { success = result, message = result ? "Workspace saved successfully." : "Failed to save workspace." });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateSql([FromBody] RadiographyValidationRequest request)
        {
            var result = await RequireDataAnalystAsync(async () =>
            {
                var sql = await _radiography.GenerateSqlAsync(request);
                return new { success = true, sql } as object;
            });
            return Json(result);
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

        private bool CanViewWorkspaceResults(string role, RadiographyWorkspaceState? workspace)
        {
            if (workspace == null) return false;
            return string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
        }
    }
}
