using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Supabase;
using Supabase.Gotrue;
using UniversityPortal.API.Authorization;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/admins")]
    public class AdminsController : ControllerBase
    {
        private static readonly ProfileRole[] AdminRoles =
        {
            ProfileRole.department_admin,
            ProfileRole.systemadmin,
            ProfileRole.superadmin
        };

        private static readonly Expression<Func<Admin, AdminRecord>> RecordSelector = a => new AdminRecord
        {
            Id = a.Id,
            ProfileId = a.ProfileId,
            FullName = a.Profile.FullName,
            Email = a.Profile.Email,
            Phone = a.Profile.Phone,
            EmployeeId = a.EmployeeId,
            Designation = a.Designation,
            Role = a.Profile.Role,
            IsActive = a.Profile.IsActive,
            DepartmentId = a.DepartmentId,
            DepartmentName = a.Department != null ? a.Department.Name : null,
            DepartmentType = a.Department != null ? a.Department.Type : null,
            CreatedAt = a.CreatedAt
        };

        private readonly ApplicationDbContext _context;

        public AdminsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ===================== HELPERS =====================

        private async Task<Profile?> GetCallerAsync()
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(idStr, out var callerId)) return null;
            return await _context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == callerId);
        }

        private static bool TryParseAdminRole(string? value, out ProfileRole role)
        {
            role = default;
            return Enum.TryParse(value, ignoreCase: true, out role) && AdminRoles.Contains(role);
        }

        // Who can manage whom. Self-checks are handled in each endpoint.
        private static bool CanManage(ProfileRole caller, ProfileRole target) => caller switch
        {
            ProfileRole.superadmin => AdminRoles.Contains(target),
            ProfileRole.systemadmin => target == ProfileRole.department_admin,
            _ => false
        };

        private async Task<string?> GetDepartmentAdminError(long departmentId, Guid? exceptProfileId)
        {
            var department = await _context.Departments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == departmentId);

            if (department == null) return "Department not found.";

            var taken = await _context.Admins.AnyAsync(a =>
                a.DepartmentId == departmentId &&
                a.Profile.Role == ProfileRole.department_admin &&
                (exceptProfileId == null || a.ProfileId != exceptProfileId));

            return taken ? $"{department.Name} already has a department admin." : null;
        }

        private Task<bool> AnotherActiveSuperadminExistsAsync(Guid excludeProfileId) =>
            _context.Profiles.AnyAsync(p =>
                p.Role == ProfileRole.superadmin && p.IsActive && p.Id != excludeProfileId);

        private async Task<Profile?> WaitForProfileAsync(Guid profileId)
        {
            // The Supabase trigger creates the profile row shortly after the auth user exists
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == profileId);
                if (profile != null) return profile;
                await Task.Delay(300);
            }
            return null;
        }

        private AdminClient CreateAdminClient(IConfiguration config)
        {
            var serviceRoleKey = config["Supabase:ServiceRoleKey"];
            var supabaseUrl = config["Supabase:Url"];

            return new Supabase.Gotrue.AdminClient(serviceRoleKey, new ClientOptions
            {
                Url = $"{supabaseUrl}/auth/v1",
                Headers = new Dictionary<string, string> { { "apikey", serviceRoleKey } }
            });
        }

        private static string? NullIfBlank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static object ToResponse(AdminRecord r) => new
        {
            id = r.Id,
            profileId = r.ProfileId,
            fullName = r.FullName,
            email = r.Email,
            phone = r.Phone,
            employeeId = r.EmployeeId,
            designation = r.Designation,
            role = r.Role.ToString(),
            isActive = r.IsActive,
            departmentId = r.DepartmentId,
            departmentName = r.DepartmentName,
            departmentType = r.DepartmentType,
            createdAt = r.CreatedAt
        };

        // ===================== READ =====================

        // GET: api/admins?page=1&pageSize=10&search=ali&role=department_admin&departmentType=education
        // GET: api/admins
        [HttpGet]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            [FromQuery] string? role = null,
            [FromQuery] string? departmentType = null)
        {
            var caller = await GetCallerAsync();
            if (caller == null) return Unauthorized();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            // 👇 1. Handle Department Admins (Fetch from Departments/Teachers table)
            if (role == "department_admin")
            {
                var deptQuery = _context.Departments
                    .Include(d => d.Admin)
                    .Where(d => d.AdminId != null)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(departmentType))
                {
                    var type = departmentType.Trim().ToLower();
                    deptQuery = deptQuery.Where(d => d.Type.ToLower() == type);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Trim().ToLower();
                    deptQuery = deptQuery.Where(d =>
                        d.Name.ToLower().Contains(term) ||
                        d.Admin.FullName.ToLower().Contains(term) ||
                        d.Email.ToLower().Contains(term));
                }

                var totalDept = await deptQuery.CountAsync();

                var deptAdmins = await deptQuery
                    .OrderBy(d => d.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(d => new
                    {
                        id = d.AdminId, // Use Profile ID as the row ID
                        profileId = d.AdminId,
                        fullName = d.Admin.FullName,
                        email = d.Email, // Use Department Email
                        phone = d.Admin.Phone,
                        employeeId = "DEPT-ADMIN", // Static or fetch from teacher table if needed
                        designation = "Department Admin",
                        role = "department_admin",
                        isActive = d.IsActive,
                        departmentId = d.Id,
                        departmentName = d.Name,
                        departmentType = d.Type,
                        createdAt = d.CreatedAt
                    })
                    .ToListAsync();

                return Ok(new
                {
                    items = deptAdmins,
                    total = totalDept,
                    page,
                    pageSize,
                    totalPages = (int)Math.Ceiling(totalDept / (double)pageSize)
                });
            }

            // 👇 2. Handle System Admins and Super Admins (Fetch from Admins table)
            var query = _context.Admins.AsNoTracking().AsQueryable();

            // If the caller is a System Admin, only show System/Super Admins. 
            // (Remove this filter if a System Admin should see everyone)
            if (caller.Role == ProfileRole.systemadmin)
            {
                query = query.Where(a => a.Profile.Role == ProfileRole.systemadmin || a.Profile.Role == ProfileRole.superadmin);
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                if (TryParseAdminRole(role, out var roleFilter))
                    query = query.Where(a => a.Profile.Role == roleFilter);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(a =>
                    a.Profile.FullName.ToLower().Contains(term) ||
                    a.Profile.Email.ToLower().Contains(term) ||
                    a.EmployeeId.ToLower().Contains(term));
            }

            var total = await query.CountAsync();

            var records = await query
                .OrderBy(a => a.Profile.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(RecordSelector)
                .ToListAsync();

            return Ok(new
            {
                items = records.Select(ToResponse).ToList(),
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }

        // GET: api/admins/me
        [HttpGet("me")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetMe()
        {
            var caller = await GetCallerAsync();
            if (caller == null) return Unauthorized();

            var record = await _context.Admins
                .AsNoTracking()
                .Where(a => a.ProfileId == caller.Id)
                .Select(RecordSelector)
                .FirstOrDefaultAsync();

            if (record == null) return NotFound(new { message = "Admin record not found for this account." });

            return Ok(ToResponse(record));
        }

        // GET: api/admins/{id}
        [HttpGet("{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> GetById(Guid id)
        {
            // First, try to find them in the Admins table
            var adminRecord = await _context.Admins
                .AsNoTracking()
                .Where(a => a.Id == id)
                .Select(RecordSelector)
                .FirstOrDefaultAsync();

            if (adminRecord != null) return Ok(ToResponse(adminRecord));

            // If not found, check if they are a Department Admin
            var deptRecord = await _context.Departments
                .Include(d => d.Admin)
                .Where(d => d.AdminId == id)
                .Select(d => new
                {
                    id = d.AdminId,
                    profileId = d.AdminId,
                    fullName = d.Admin.FullName,
                    email = d.Email,
                    phone = d.Admin.Phone,
                    employeeId = "DEPT-ADMIN",
                    designation = "Department Admin",
                    role = "department_admin",
                    isActive = d.IsActive,
                    departmentId = d.Id,
                    departmentName = d.Name,
                    departmentType = d.Type,
                    createdAt = d.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (deptRecord != null) return Ok(deptRecord);

            return NotFound(new { message = "Admin not found." });
        }

        // ===================== CREATE =====================

        // POST: api/admins
        [HttpPost]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Create([FromBody] CreateAdminRequest request, [FromServices] IConfiguration config)
        {
            var caller = await GetCallerAsync();
            if (caller == null) return Unauthorized();

            var fullName = request.FullName.Trim();
            var email = request.Email.Trim().ToLowerInvariant();
            var employeeId = request.EmployeeId.Trim();

            if (fullName.Length == 0) return BadRequest(new { message = "Full name is required." });
            if (email.Length == 0) return BadRequest(new { message = "Email is required." });
            if (employeeId.Length == 0) return BadRequest(new { message = "Employee ID is required." });
            if (request.Password.Length < 6) return BadRequest(new { message = "Password must be at least 6 characters." });

            if (!TryParseAdminRole(request.Role, out var role))
                return BadRequest(new { message = "Role must be department_admin, systemadmin or superadmin." });

            if (!CanManage(caller.Role, role))
                return StatusCode(403, new { message = "You are not allowed to create this admin role." });

            long? departmentId = null;
            if (role == ProfileRole.department_admin)
            {
                if (request.DepartmentId is not > 0)
                    return BadRequest(new { message = "A department is required for department admins." });

                departmentId = request.DepartmentId;

                var deptError = await GetDepartmentAdminError(departmentId.Value, null);
                if (deptError != null) return Conflict(new { message = deptError });
            }

            if (await _context.Profiles.AnyAsync(p => p.Email == email))
                return Conflict(new { message = "A user with this email already exists." });

            if (await _context.Admins.AnyAsync(a => a.EmployeeId == employeeId))
                return Conflict(new { message = $"Employee ID '{employeeId}' is already in use." });

            // 1. Create the login account
            var adminClient = CreateAdminClient(config);
            Guid profileId;
            try
            {
                var authUser = await adminClient.CreateUser(new Supabase.Gotrue.AdminUserAttributes
                {
                    Email = email,
                    Password = request.Password,
                    EmailConfirm = true,
                    UserMetadata = new Dictionary<string, object>
                    {
                        { "full_name", fullName },
                        { "role", role.ToString() }
                    }
                });

                if (authUser == null) return BadRequest(new { message = "Failed to create login account." });
                profileId = Guid.Parse(authUser.Id);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Failed to create login account.", details = ex.Message });
            }

            // 2. Write the profile and admin rows. If this fails, remove the login account.
            var adminId = Guid.NewGuid();
            var profile = await WaitForProfileAsync(profileId);

            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();

                if (profile == null)
                {
                    profile = new Profile
                    {
                        Id = profileId,
                        Email = email,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Profiles.Add(profile);
                }

                profile.FullName = fullName;
                profile.Phone = NullIfBlank(request.Phone);
                profile.Role = role;
                profile.IsActive = true;
                profile.DepartmentId = departmentId;
                profile.UpdatedAt = DateTime.UtcNow;

                _context.Admins.Add(new Admin
                {
                    Id = adminId,
                    ProfileId = profileId,
                    DepartmentId = departmentId,
                    EmployeeId = employeeId,
                    Designation = request.Designation.Trim(),
                    CreatedBy = caller.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                if (departmentId.HasValue)
                {
                    var dept = await _context.Departments.FirstAsync(d => d.Id == departmentId.Value);
                    dept.AdminId = profileId;
                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                try { await adminClient.DeleteUser(profileId.ToString()); }
                catch (Exception cleanupEx) { Console.WriteLine($"[CREATE ADMIN CLEANUP ERROR]: {cleanupEx.Message}"); }

                var message = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"[CREATE ADMIN ERROR]: {message}");
                return StatusCode(500, new { message = "Failed to create admin.", details = message });
            }

            return Ok(new { message = "Admin created successfully.", id = adminId });
        }

        // ===================== UPDATE =====================

        // PUT: api/admins/{id}
        [HttpPut("{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAdminRequest request)
        {
            var caller = await GetCallerAsync();
            if (caller == null) return Unauthorized();

            var admin = await _context.Admins
                .Include(a => a.Profile)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (admin == null) return NotFound(new { message = "Admin not found." });

            var currentRole = admin.Profile.Role;
            var isSelf = admin.ProfileId == caller.Id;

            if (!CanManage(caller.Role, currentRole))
                return StatusCode(403, new { message = "You are not allowed to edit this admin." });

            if (!TryParseAdminRole(request.Role, out var newRole))
                return BadRequest(new { message = "Role must be department_admin, systemadmin or superadmin." });

            if (isSelf && newRole != currentRole)
                return BadRequest(new { message = "You can't change your own role." });

            if (!CanManage(caller.Role, newRole))
                return StatusCode(403, new { message = "You are not allowed to assign this role." });

            var fullName = request.FullName.Trim();
            var employeeId = request.EmployeeId.Trim();

            if (fullName.Length == 0) return BadRequest(new { message = "Full name is required." });
            if (employeeId.Length == 0) return BadRequest(new { message = "Employee ID is required." });

            if (await _context.Admins.AnyAsync(a => a.EmployeeId == employeeId && a.Id != id))
                return Conflict(new { message = $"Employee ID '{employeeId}' is already in use." });

            long? departmentId = null;
            if (newRole == ProfileRole.department_admin)
            {
                if (request.DepartmentId is not > 0)
                    return BadRequest(new { message = "A department is required for department admins." });

                departmentId = request.DepartmentId;

                var deptError = await GetDepartmentAdminError(departmentId.Value, admin.ProfileId);
                if (deptError != null) return Conflict(new { message = deptError });
            }

            if (currentRole == ProfileRole.superadmin &&
                newRole != ProfileRole.superadmin &&
                !await AnotherActiveSuperadminExistsAsync(admin.ProfileId))
            {
                return BadRequest(new { message = "At least one active superadmin must remain." });
            }

            var previousDepartmentId = admin.DepartmentId;

            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();

                // Release the old department's admin slot if this admin held it
                if (previousDepartmentId.HasValue)
                {
                    var previousDept = await _context.Departments
                        .FirstOrDefaultAsync(d => d.Id == previousDepartmentId.Value);

                    if (previousDept != null && previousDept.AdminId == admin.ProfileId)
                        previousDept.AdminId = null;
                }

                admin.Profile.FullName = fullName;
                admin.Profile.Phone = NullIfBlank(request.Phone);
                admin.Profile.Role = newRole;
                admin.Profile.DepartmentId = departmentId;
                admin.Profile.UpdatedAt = DateTime.UtcNow;

                admin.EmployeeId = employeeId;
                admin.Designation = request.Designation.Trim();
                admin.DepartmentId = departmentId;
                admin.UpdatedAt = DateTime.UtcNow;

                if (departmentId.HasValue)
                {
                    var dept = await _context.Departments.FirstAsync(d => d.Id == departmentId.Value);
                    dept.AdminId = admin.ProfileId;
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"[UPDATE ADMIN ERROR]: {message}");
                return StatusCode(500, new { message = "Failed to update admin.", details = message });
            }

            return Ok(new { message = "Admin updated successfully." });
        }

        // PUT: api/admins/{id}/status
        [HttpPut("{id:guid}/status")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetAdminStatusRequest request)
        {
            var caller = await GetCallerAsync();
            if (caller == null) return Unauthorized();

            var admin = await _context.Admins
                .Include(a => a.Profile)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (admin == null) return NotFound(new { message = "Admin not found." });

            if (!CanManage(caller.Role, admin.Profile.Role))
                return StatusCode(403, new { message = "You are not allowed to change this admin's status." });

            if (admin.ProfileId == caller.Id)
                return BadRequest(new { message = "You can't change your own status." });

            if (!request.IsActive &&
                admin.Profile.Role == ProfileRole.superadmin &&
                !await AnotherActiveSuperadminExistsAsync(admin.ProfileId))
            {
                return BadRequest(new { message = "At least one active superadmin must remain." });
            }

            admin.Profile.IsActive = request.IsActive;
            admin.Profile.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = request.IsActive ? "Admin activated." : "Admin deactivated." });
        }

        // PUT: api/admins/{id}/reset-password
        [HttpPut("{id:guid}/reset-password")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetAdminPasswordRequest request, [FromServices] IConfiguration config)
        {
            var caller = await GetCallerAsync();
            if (caller == null) return Unauthorized();

            var admin = await _context.Admins
                .Include(a => a.Profile)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (admin == null) return NotFound(new { message = "Admin not found." });

            if (!CanManage(caller.Role, admin.Profile.Role))
                return StatusCode(403, new { message = "You are not allowed to reset this admin's password." });

            if (admin.ProfileId == caller.Id)
                return BadRequest(new { message = "Use account settings to change your own password." });

            if (request.Password.Length < 6)
                return BadRequest(new { message = "Password must be at least 6 characters." });

            try
            {
                await CreateAdminClient(config).UpdateUserById(
                    admin.ProfileId.ToString(),
                    new Supabase.Gotrue.AdminUserAttributes { Password = request.Password });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RESET ADMIN PASSWORD ERROR]: {ex.Message}");
                return StatusCode(500, new { message = "Failed to update password.", details = ex.Message });
            }

            return Ok(new { message = "Password updated successfully." });
        }

        // ===================== DELETE =====================

        // DELETE: api/admins/{id}
        [HttpDelete("{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Delete(Guid id, [FromServices] IConfiguration config)
        {
            var caller = await GetCallerAsync();
            if (caller == null) return Unauthorized();

            var admin = await _context.Admins
                .Include(a => a.Profile)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (admin == null) return NotFound(new { message = "Admin not found." });

            if (!CanManage(caller.Role, admin.Profile.Role))
                return StatusCode(403, new { message = "You are not allowed to delete this admin." });

            if (admin.ProfileId == caller.Id)
                return BadRequest(new { message = "You can't delete your own account." });

            if (admin.Profile.Role == ProfileRole.superadmin &&
                !await AnotherActiveSuperadminExistsAsync(admin.ProfileId))
            {
                return BadRequest(new { message = "At least one active superadmin must remain." });
            }

            var profileId = admin.ProfileId;

            // A profile that is also a teacher or student is handled through those records, not here
            var linkedToAcademicRecord =
                await _context.Teachers.AnyAsync(t => t.ProfileId == profileId) ||
                await _context.Students.AnyAsync(s => s.ProfileId == profileId);

            if (linkedToAcademicRecord)
                return Conflict(new { message = "This account is linked to a teacher or student record. Remove that record first." });

            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();

                // Clear references to this profile so the foreign keys don't block the delete
                var departments = await _context.Departments
                    .Where(d => d.AdminId == profileId || d.HeadId == profileId || d.CreatedBy == profileId)
                    .ToListAsync();

                foreach (var d in departments)
                {
                    if (d.AdminId == profileId) d.AdminId = null;
                    if (d.HeadId == profileId) d.HeadId = null;
                    if (d.CreatedBy == profileId) d.CreatedBy = null;
                }

                var adminsCreatedHere = await _context.Admins
                    .Where(a => a.CreatedBy == profileId)
                    .ToListAsync();

                foreach (var a in adminsCreatedHere) a.CreatedBy = null;

                _context.Admins.Remove(admin);
                await _context.SaveChangesAsync();

                var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Id == profileId);
                if (profile != null)
                {
                    _context.Profiles.Remove(profile);
                    await _context.SaveChangesAsync();
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"[DELETE ADMIN ERROR]: {message}");
                return StatusCode(500, new { message = "Failed to delete admin.", details = message });
            }

            // The database rows are already gone. A failure here only leaves a login account, so log it.
            try
            {
                await CreateAdminClient(config).DeleteUser(profileId.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE ADMIN AUTH WARNING]: {ex.Message}");
            }

            return Ok(new { message = "Admin deleted successfully." });
        }
    }
}