using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using HemisAudit.Data;
using HemisAudit.Helpers;
using HemisAudit.Models;
using HemisAudit.Services;
using HemisAudit.ViewModels;

namespace HemisAudit.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private static readonly string[] IndustryOptions =
        {
            "TVET College",
            "Higher Institution - Public",
            "Higher Institution - Private",
            "Private College",
            "Public Entity",
            "Other"
        };

        private readonly UserManager<ApplicationUser>  _users;
        private readonly RoleManager<IdentityRole>     _roles;
        private readonly ApplicationDbContext          _db;
        private readonly IAuditLogService              _audit;
        private readonly IPasswordPolicyService        _passwordPolicy;
        private readonly ISystemDatabaseService        _systemDb;

        public AdminController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles,
            ApplicationDbContext db, IAuditLogService audit, IPasswordPolicyService passwordPolicy,
            ISystemDatabaseService systemDb)
        {
            _users = users; _roles = roles; _db = db; _audit = audit; _passwordPolicy = passwordPolicy; _systemDb = systemDb;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // USERS
        // ═══════════════════════════════════════════════════════════════════════

        // ── User List ──────────────────────────────────────────────────────────
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Users()
        {
            var users = await _users.Users.ToListAsync();
            var list  = new List<UserListViewModel>();

            foreach (var u in users.OrderBy(u => u.LastName))
            {
                var roles           = await _users.GetRolesAsync(u);
                var assignedClients = await _systemDb.GetAssignedClientCountAsync(u, roles.FirstOrDefault() ?? "");
                var isLockedOut     = await _users.IsLockedOutAsync(u);
                var lockoutEnd      = await _users.GetLockoutEndDateAsync(u);
                list.Add(new UserListViewModel
                {
                    Id                   = u.Id,
                    FirstName            = u.FirstName,
                    LastName             = u.LastName,
                    FullName             = u.FullName,
                    Email                = u.Email ?? "",
                    EmployeeCode         = u.EmployeeCode,
                    ProfilePicturePath   = u.ProfilePicturePath,
                    IsActive             = u.IsActive,
                    IsLockedOut          = isLockedOut,
                    LockoutEnd           = lockoutEnd,
                    CreatedAt            = u.CreatedAt,
                    LastLoginAt          = u.LastLoginAt,
                    Roles                = roles.ToList(),
                    AssignedClientsCount = assignedClients,
                    PasswordStatus       = BuildPasswordStatus(u)
                });
            }

            return View(list);
        }

        // ── Create User GET ────────────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult CreateUser()
        {
            ViewBag.Roles = new[] { "Admin","Director","Manager","DataAnalyst","Trainee" };
            return View(new CreateUserViewModel());
        }

        // ── Create User POST ───────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateUser(CreateUserViewModel model)
        {
            ViewBag.Roles = new[] { "Admin","Director","Manager","DataAnalyst","Trainee" };
            if (!ModelState.IsValid) return View(model);

            if (await _users.FindByEmailAsync(model.Email) != null)
            {
                ModelState.AddModelError("Email", "A user with this email already exists.");
                return View(model);
            }

            if (!string.IsNullOrWhiteSpace(model.EmployeeCode))
            {
                var employeeExists = await _users.Users.AnyAsync(u =>
                    u.EmployeeCode == model.EmployeeCode);

                if (employeeExists)
                {
                    ModelState.AddModelError("EmployeeCode", "A user with this employee code already exists.");
                    return View(model);
                }
            }

            var user = new ApplicationUser
            {
                UserName       = model.Email,
                Email          = model.Email,
                FirstName      = model.FirstName,
                LastName       = model.LastName,
                EmployeeCode   = model.EmployeeCode,
                EmailConfirmed = true,
                IsActive       = true,
                PasswordSetDate = DateTime.UtcNow
            };

            var policyErrors = _passwordPolicy.ValidatePassword(user, model.Password);
            foreach (var error in policyErrors)
                ModelState.AddModelError("", error);

            if (!ModelState.IsValid)
                return View(model);

            var identityUserCreated = false;

            try
            {
                var result = await _users.CreateAsync(user, model.Password);
                if (!result.Succeeded)
                {
                    foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
                    return View(model);
                }

                identityUserCreated = true;

                var addRoleResult = await _users.AddToRoleAsync(user, model.Role);
                if (!addRoleResult.Succeeded)
                {
                    foreach (var e in addRoleResult.Errors) ModelState.AddModelError("", e.Description);
                    await _users.DeleteAsync(user);
                    return View(model);
                }

                user.PasswordHistory = _passwordPolicy.BuildPasswordHistory(null, user.PasswordHash ?? string.Empty);
                var updateResult = await _users.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    foreach (var e in updateResult.Errors) ModelState.AddModelError("", e.Description);
                    await _users.DeleteAsync(user);
                    return View(model);
                }

                await _systemDb.EnsureUserMirrorAsync(user, model.Role);
            }
            catch (Exception ex)
            {
                if (identityUserCreated)
                    await _users.DeleteAsync(user);

                ModelState.AddModelError("", $"The user could not be saved to the system database: {ex.Message}");
                return View(model);
            }

            var admin = await _users.GetUserAsync(User);
            await _audit.LogAsync("create_user", $"Created user {user.Email} with role {model.Role}",
                admin?.Id, admin?.Email);

            TempData["Success"] = $"Team member {user.FullName} added successfully.";
            return RedirectToAction(nameof(Users));
        }

        // ── Edit User GET ──────────────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditUser(string id)
        {
            var user = await _users.FindByIdAsync(id);
            if (user == null) return NotFound();

            var roles = await _users.GetRolesAsync(user);
            ViewBag.Roles = new[] { "Admin","Director","Manager","DataAnalyst","Trainee" };

            return View(new EditUserViewModel
            {
                Id           = user.Id,
                FirstName    = user.FirstName,
                LastName     = user.LastName,
                Email        = user.Email ?? "",
                EmployeeCode = user.EmployeeCode,
                PhoneNumber = user.PhoneNumber,
                Gender = user.Gender,
                Department = user.Department,
                OfficeAddress = user.OfficeAddress,
                CurrentProfilePicturePath = user.ProfilePicturePath,
                Role         = roles.FirstOrDefault() ?? "DataAnalyst",
                IsActive     = user.IsActive
            });
        }

        // ── Edit User POST ─────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditUser(EditUserViewModel model)
        {
            ViewBag.Roles = new[] { "Admin","Director","Manager","DataAnalyst","Trainee" };
            var user = await _users.FindByIdAsync(model.Id);
            if (user == null) return NotFound();

            model.Email = user.Email ?? "";
            model.CurrentProfilePicturePath = user.ProfilePicturePath;

            if (!ModelState.IsValid) return View(model);

            if (!string.IsNullOrWhiteSpace(model.EmployeeCode))
            {
                var employeeCodeExists = await _users.Users.AnyAsync(u =>
                    u.Id != model.Id &&
                    u.EmployeeCode == model.EmployeeCode);

                if (employeeCodeExists)
                {
                    ModelState.AddModelError(nameof(model.EmployeeCode), "A user with this employee code already exists.");
                    return View(model);
                }
            }

            var changes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            TrackAdminChange(changes, "FirstName", user.FirstName, model.FirstName);
            TrackAdminChange(changes, "LastName", user.LastName, model.LastName);
            TrackAdminChange(changes, "EmployeeCode", user.EmployeeCode, model.EmployeeCode);
            TrackAdminChange(changes, "PhoneNumber", user.PhoneNumber, model.PhoneNumber);
            TrackAdminChange(changes, "Gender", user.Gender, model.Gender);
            TrackAdminChange(changes, "Department", user.Department, model.Department);
            TrackAdminChange(changes, "OfficeAddress", user.OfficeAddress, model.OfficeAddress);
            if (user.IsActive != model.IsActive)
                changes["IsActive"] = model.IsActive.ToString();

            user.FirstName = model.FirstName.Trim();
            user.LastName = model.LastName.Trim();
            user.EmployeeCode = (model.EmployeeCode ?? string.Empty).Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
            user.Gender = string.IsNullOrWhiteSpace(model.Gender) ? null : model.Gender.Trim();
            user.Department = string.IsNullOrWhiteSpace(model.Department) ? null : model.Department.Trim();
            user.OfficeAddress = string.IsNullOrWhiteSpace(model.OfficeAddress) ? null : model.OfficeAddress.Trim();
            user.IsActive = model.IsActive;

            await _users.UpdateAsync(user);

            // Update role
            var currentRoles = await _users.GetRolesAsync(user);
            var currentRole = currentRoles.FirstOrDefault() ?? "";
            await _users.RemoveFromRolesAsync(user, currentRoles);
            await _users.AddToRoleAsync(user, model.Role);
            if (!string.Equals(currentRole, model.Role, StringComparison.OrdinalIgnoreCase))
                changes["SystemRole"] = model.Role;
            await _systemDb.EnsureUserMirrorAsync(user, model.Role);

            var admin = await _users.GetUserAsync(User);
            await _audit.LogAsync("USER_PROFILE_UPDATED_BY_ADMIN", JsonSerializer.Serialize(changes), admin?.Id, admin?.Email);

            TempData["Success"] = $"User {user.FullName} updated.";
            return RedirectToAction(nameof(Users));
        }

        // ── Toggle Active ──────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ToggleUserActive(string id)
        {
            var user = await _users.FindByIdAsync(id);
            if (user == null) return NotFound();

            // Cannot deactivate yourself
            var self = await _users.GetUserAsync(User);
            if (user.Id == self?.Id)
            {
                TempData["Error"] = "You cannot deactivate your own account.";
                return RedirectToAction(nameof(Users));
            }

            user.IsActive = !user.IsActive;
            await _users.UpdateAsync(user);
            var userRoles = await _users.GetRolesAsync(user);
            await _systemDb.EnsureUserMirrorAsync(user, userRoles.FirstOrDefault() ?? "");

            var action = user.IsActive ? "activate_user" : "deactivate_user";
            await _audit.LogAsync(action, $"User {user.Email} {(user.IsActive ? "activated" : "deactivated")}",
                self?.Id, self?.Email);

            TempData["Success"] = $"User {user.FullName} {(user.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Users));
        }

        // ── Reset Password ─────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _users.FindByIdAsync(id);
            if (user == null) return NotFound();

            var admin = await _users.GetUserAsync(User);
            if (admin == null)
                return RedirectToAction("Login", "Account");

            if (string.Equals(user.Id, admin.Id, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "You cannot delete your own account.";
                return RedirectToAction(nameof(Users));
            }

            if (user.IsActive)
            {
                TempData["Error"] = "Deactivate the user before deleting the profile.";
                return RedirectToAction(nameof(Users));
            }

            var adminRoles = await _users.GetRolesAsync(admin);
            var adminRole = adminRoles.FirstOrDefault() ?? "Admin";

            await _systemDb.DeleteUserMirrorAsync(user, admin, adminRole);

            var sqliteAssignments = await _db.ClientUsers.Where(cu => cu.UserId == user.Id).ToListAsync();
            if (sqliteAssignments.Count > 0)
                _db.ClientUsers.RemoveRange(sqliteAssignments);

            await _db.SaveChangesAsync();

            var deleteResult = await _users.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                TempData["Error"] = string.Join(", ", deleteResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Users));
            }

            await _audit.LogAsync("delete_user", $"Deleted user profile {user.Email}", admin.Id, admin.Email);
            TempData["Success"] = $"User {user.FullName} deleted.";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ResetPassword(string id, string newPassword)
        {
            var user = await _users.FindByIdAsync(id);
            if (user == null) return NotFound();

            var policyErrors = _passwordPolicy.ValidatePassword(user, newPassword);
            foreach (var error in policyErrors)
                ModelState.AddModelError("", error);

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Users));
            }

            var token  = await _users.GeneratePasswordResetTokenAsync(user);
            var result = await _users.ResetPasswordAsync(user, token, newPassword);

            var admin = await _users.GetUserAsync(User);
            if (result.Succeeded)
            {
                var refreshed = await _users.FindByIdAsync(user.Id);
                if (refreshed != null)
                {
                    refreshed.PasswordSetDate = DateTime.UtcNow;
                    refreshed.PasswordChangedAt = DateTime.UtcNow;
                    refreshed.PasswordHistory = _passwordPolicy.BuildPasswordHistory(refreshed.PasswordHistory, refreshed.PasswordHash ?? string.Empty);
                    await _users.UpdateAsync(refreshed);
                    await _users.SetLockoutEndDateAsync(refreshed, null);
                    await _users.ResetAccessFailedCountAsync(refreshed);
                    var refreshedRoles = await _users.GetRolesAsync(refreshed);
                    await _systemDb.EnsureUserMirrorAsync(refreshed, refreshedRoles.FirstOrDefault() ?? "");
                }
                await _audit.LogAsync("reset_password", $"Reset password for {user.Email}", admin?.Id, admin?.Email);
                TempData["Success"] = $"Password reset for {user.FullName}.";
            }
            else
            {
                TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
            }
            return RedirectToAction(nameof(Users));
        }

        // ── Unlock Account ─────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UnlockUser(string id)
        {
            var user = await _users.FindByIdAsync(id);
            if (user == null) return NotFound();

            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);

            var admin = await _users.GetUserAsync(User);
            await _audit.LogAsync("unlock_user", $"Unlocked account for {user.Email}", admin?.Id, admin?.Email);

            TempData["Success"] = $"Account for {user.FullName} unlocked.";
            return RedirectToAction(nameof(Users));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CLIENTS (ENGAGEMENTS)
        // ═══════════════════════════════════════════════════════════════════════

        // ── Client List ────────────────────────────────────────────────────────
        public async Task<IActionResult> Clients(string? q)
        {
            var user = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(user);
            await _systemDb.NormalizeCompletedRunStatusesAsync();
            ViewBag.ClientSearchQuery = q ?? string.Empty;
            return View(await _systemDb.GetClientsAsync(user, role, search: q));
        }

        // ── Create Client GET ──────────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "Director")]
        public IActionResult CreateClient()
        {
            ViewBag.IndustryOptions = IndustryOptions;
            return View(new CreateClientViewModel());
        }

        // ── Create Client POST ─────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Director")]
        public async Task<IActionResult> CreateClient(CreateClientViewModel model)
        {
            ViewBag.IndustryOptions = IndustryOptions;
            if (!ModelState.IsValid) return View(model);

            var creator = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(creator);

            try
            {
                var clientId = await _systemDb.CreateClientAsync(model, creator!, role);
                await _audit.LogAsync("create_client", $"Created client: {model.EngagementName} ({model.MaconomyNumber})",
                    creator?.Id, creator?.Email);
                TempData["Success"] = role == "Admin"
                    ? $"Engagement '{model.EngagementName}' created and activated."
                    : $"Engagement '{model.EngagementName}' created and is awaiting admin approval.";
                return RedirectToAction(nameof(ClientDetail), new { id = clientId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View(model);
            }
        }

        // ── Client Detail ──────────────────────────────────────────────────────
        public async Task<IActionResult> ClientDetail(int id)
        {
            var currentUser = await _users.GetUserAsync(User);
            var role = await GetCurrentSystemRoleAsync(currentUser);
            await _systemDb.NormalizeCompletedRunStatusesAsync();
            var detail = await _systemDb.GetClientDetailAsync(id, currentUser, role);
            if (detail == null)
            {
                TempData["Error"] = "The engagement was not found or you do not have access to it.";
                return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                    ? RedirectToAction(nameof(Clients))
                    : RedirectToAction("Index", "Dashboard");
            }
            var canAccessModule = await _systemDb.CanAccessClientModuleAsync(id, currentUser, role);
            var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
            var isDataAnalyst = string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase);
            ViewBag.CanOpenModule = canAccessModule;
            ViewBag.CanRunModules =
                canAccessModule &&
                isDataAnalyst;
            ViewBag.CanManageAssignments =
                isAdmin &&
                !detail.IsArchived;
            ViewBag.ShowModulesWorkspaceUi = canAccessModule || isAdmin;

            detail.CanArchive = false;
            detail.ArchiveEligibilityMessage = detail.IsArchived
                ? "This engagement is archived."
                : "Only the director can archive an engagement after all signoffs are complete.";

            if (string.Equals(role, "Director", StringComparison.OrdinalIgnoreCase))
            {
                var archiveEligibility = await _systemDb.GetArchiveEligibilityAsync(id);
                detail.CanArchive = archiveEligibility.CanArchive;
                detail.CurrentRunId = archiveEligibility.CurrentRunId;
                detail.CurrentRunRuleNumber = archiveEligibility.CurrentRunRuleNumber;
                detail.ArchiveEligibilityMessage = archiveEligibility.Message;
            }
            else
            {
                var currentRun = detail.ValidationRuns.FirstOrDefault(run => run.IsCurrent)
                    ?? detail.ValidationRuns.FirstOrDefault(run => run.HasAllRequiredSignoffs);
                detail.CurrentRunId = currentRun?.Id;
                detail.CurrentRunRuleNumber = currentRun?.RuleNumber;
            }

            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                detail.ValidationRuns = detail.ValidationRuns
                    .Where(run => ValidationRunAccessPolicy.CanViewSignedResults(role, detail.CurrentUserEngagementRole, run.HasDataAnalystSignoff))
                    .ToList();
            }

            ViewBag.CurrentSystemRole = role;

            if (isAdmin && !detail.IsArchived)
            {
                var allUsers = await _users.Users.ToListAsync();
                var assignedIds = detail.AssignedUsers.Select(cu => cu.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                detail.AvailableUsers = allUsers
                    .Where(u => u.IsActive && !assignedIds.Contains(u.Id))
                    .Select(u => new UserListViewModel { Id = u.Id, FullName = u.FullName, Email = u.Email ?? "" })
                    .OrderBy(u => u.FullName)
                    .ToList();
            }

            return View(detail);
        }

        // ── Assign User ────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignUser(AssignUserViewModel model)
        {
            var admin = await _users.GetUserAsync(User);
            var adminRole = await GetCurrentSystemRoleAsync(admin);
            var detail = await _systemDb.GetClientDetailAsync(model.ClientId, admin, adminRole);
            if (detail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only.";
                return RedirectToAction(nameof(ClientDetail), new { id = model.ClientId });
            }

            var assignedUser = await _users.FindByIdAsync(model.UserId);
            if (assignedUser == null)
            {
                TempData["Error"] = "Selected user could not be found.";
                return RedirectToAction(nameof(ClientDetail), new { id = model.ClientId });
            }

            await _systemDb.AssignUserAsync(model.ClientId, assignedUser, model.EngagementRole, admin!, adminRole);

            await _audit.LogAsync("assign_user",
                $"Assigned {assignedUser.Email} to client {model.ClientId} as {model.EngagementRole}",
                admin?.Id, admin?.Email);

            TempData["Success"] = $"{assignedUser.FullName} added to the engagement team.";

            return RedirectToAction(nameof(ClientDetail), new { id = model.ClientId });
        }

        // ── Remove User ────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RemoveUser(int clientUserId, int clientId)
        {
            var admin = await _users.GetUserAsync(User);
            var adminRole = await GetCurrentSystemRoleAsync(admin);
            var detail = await _systemDb.GetClientDetailAsync(clientId, admin, adminRole);
            if (detail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only.";
                return RedirectToAction(nameof(ClientDetail), new { id = clientId });
            }

            await _systemDb.RemoveAssignmentAsync(clientUserId);

            await _audit.LogAsync("remove_user",
                $"Removed assignment {clientUserId} from client {clientId}",
                admin?.Id, admin?.Email);

            TempData["Success"] = $"Removed assignment from engagement.";
            return RedirectToAction(nameof(ClientDetail), new { id = clientId });
        }

        // ── Audit Log ──────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveClient(int id, string? returnUrl = null)
        {
            var admin = await _users.GetUserAsync(User);
            if (admin == null)
                return RedirectToAction("Login", "Account");

            var adminRole = await GetCurrentSystemRoleAsync(admin);
            var detail = await _systemDb.GetClientDetailAsync(id, admin, adminRole);
            if (detail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only.";
                return RedirectToAction(nameof(ClientDetail), new { id });
            }

            await _systemDb.ApproveClientAsync(id, admin, "Admin");
            await _audit.LogAsync("approve_client", $"Approved engagement {id}", admin.Id, admin.Email);
            TempData["Success"] = "Engagement approved.";

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(ClientDetail), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "DataAnalyst")]
        public async Task<IActionResult> SaveRuleScope(int clientId, List<int>? selectedRules)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToAction("Login", "Account");

            var currentSystemRole = await GetCurrentSystemRoleAsync(user);
            var detail = await _systemDb.GetClientDetailAsync(clientId, user, currentSystemRole);
            if (detail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only.";
                return RedirectToAction(nameof(ClientDetail), new { id = clientId });
            }

            // Ensure only the assigned engagement DataAnalyst may change the rule scope
            var engagementRole = await _systemDb.GetEngagementRoleAsync(clientId, user, currentSystemRole);
            if (!string.Equals(engagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Only the assigned Data Analyst may change the rule selection for this engagement.";
                return RedirectToAction(nameof(ClientDetail), new { id = clientId });
            }

            await _systemDb.SaveEngagementScopeAsync(clientId, selectedRules ?? new List<int>(), user);
            var count = (selectedRules?.Count ?? 0);
            TempData["Success"] = count == 0
                ? "Rule scope cleared — all rules are now required for archiving."
                : $"Rule scope saved: {count} rule{(count == 1 ? "" : "s")} selected for this engagement.";

            return RedirectToAction(nameof(ClientDetail), new { id = clientId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Director")]
        public async Task<IActionResult> ArchiveClient(int id, string? returnUrl = null)
        {
            var director = await _users.GetUserAsync(User);
            if (director == null)
                return RedirectToAction("Login", "Account");

            var directorRole = await GetCurrentSystemRoleAsync(director);
            var detail = await _systemDb.GetClientDetailAsync(id, director, directorRole);
            if (detail?.IsArchived == true)
            {
                TempData["Error"] = "This engagement is already archived.";
                return RedirectToAction(nameof(ClientDetail), new { id });
            }

            try
            {
                await _systemDb.ArchiveClientAsync(id, director, "Director");
                await _audit.LogAsync("archive_client", $"Archived engagement {id}", director.Id, director.Email);
                TempData["Success"] = "Engagement archived.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(ClientDetail), new { id });
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(ClientDetail), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteClient(int id)
        {
            var admin = await _users.GetUserAsync(User);
            if (admin == null)
                return RedirectToAction("Login", "Account");

            var adminRole = await GetCurrentSystemRoleAsync(admin);
            var detail = await _systemDb.GetClientDetailAsync(id, admin, adminRole);
            if (detail?.IsArchived == true)
            {
                TempData["Error"] = "Archived engagements are read-only.";
                return RedirectToAction(nameof(ClientDetail), new { id });
            }

            await _systemDb.DeleteClientAsync(id);

            var sqliteRuns = await _db.ValidationRuns.Where(v => v.ClientId == id).ToListAsync();
            if (sqliteRuns.Count > 0)
                _db.ValidationRuns.RemoveRange(sqliteRuns);

            var sqliteAssignments = await _db.ClientUsers.Where(cu => cu.ClientId == id).ToListAsync();
            if (sqliteAssignments.Count > 0)
                _db.ClientUsers.RemoveRange(sqliteAssignments);

            var sqliteClient = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id);
            if (sqliteClient != null)
                _db.Clients.Remove(sqliteClient);

            await _db.SaveChangesAsync();
            await _audit.LogAsync("delete_client", $"Deleted engagement {id}", admin.Id, admin.Email);
            TempData["Success"] = "Engagement deleted.";
            return RedirectToAction(nameof(Clients));
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AuditLog()
        {
            var logs = await _systemDb.GetAuditLogsAsync(500);
            return View(logs);
        }

        private static void TrackAdminChange(IDictionary<string, string?> changes, string field, string? currentValue, string? newValue)
        {
            var current = string.IsNullOrWhiteSpace(currentValue) ? null : currentValue.Trim();
            var updated = string.IsNullOrWhiteSpace(newValue) ? null : newValue.Trim();

            if (!string.Equals(current, updated, StringComparison.Ordinal))
                changes[field] = updated;
        }

        private PasswordStatusViewModel BuildPasswordStatus(ApplicationUser user)
        {
            var now = DateTime.UtcNow;
            var ageDays = _passwordPolicy.GetPasswordAgeDays(user, now);
            var daysRemaining = _passwordPolicy.GetPasswordDaysRemaining(user, now);
            var isExpired = _passwordPolicy.IsPasswordExpired(user, now);

            return new PasswordStatusViewModel
            {
                ReferenceDateUtc = _passwordPolicy.GetPasswordReferenceDate(user),
                AgeDays = ageDays,
                DaysRemaining = daysRemaining,
                MaxAgeDays = _passwordPolicy.MaxPasswordAgeDays,
                WarningWindowDays = _passwordPolicy.WarningWindowDays,
                IsExpired = isExpired,
                IsExpiringSoon = !isExpired && daysRemaining > 0 && daysRemaining <= _passwordPolicy.WarningWindowDays
            };
        }

        private async Task<string> GetCurrentSystemRoleAsync(ApplicationUser? user)
        {
            var systemRole = await _systemDb.GetSystemRoleAsync(user);
            if (!string.IsNullOrWhiteSpace(systemRole))
                return systemRole!;

            var roles = user != null ? await _users.GetRolesAsync(user) : new List<string>();
            return roles.FirstOrDefault() ?? "";
        }
    }
}
