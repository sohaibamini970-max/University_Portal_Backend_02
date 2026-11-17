using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;
using Supabase.Gotrue;
using System.Security.Claims;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TeachersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TeachersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // In TeachersController.cs
        [HttpGet]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetAll()
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var deptIdStr = User.FindFirst("departmentId")?.Value;

            var query = _context.Teachers
                .Include(t => t.Profile)
                .Include(t => t.Department)
                .AsQueryable();

            // 👇 Filter by department if the requester is a department_admin
            if (userRole == "department_admin" && long.TryParse(deptIdStr, out long deptId))
            {
                query = query.Where(t => t.DepartmentId == deptId);
            }

            var teachers = await query.ToListAsync();
            return Ok(teachers);
        }

        // POST: api/Teachers
        [HttpPost]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> Create([FromBody] CreateTeacherRequest request, [FromServices] Supabase.Client supabase, [FromServices] IConfiguration config)
        {
            try
            {
                // 1. Automatically Assign Department for Department Admins
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var deptIdStr = User.FindFirst("departmentId")?.Value;

                if (userRole == "department_admin" && long.TryParse(deptIdStr, out long adminDeptId))
                {
                    request.DepartmentId = adminDeptId;
                }

                if (request.DepartmentId <= 0)
                {
                    return BadRequest(new { message = "A valid Department ID is required." });
                }

                // 2. Create the Auth User using Supabase Admin API
                var serviceRoleKey = config["Supabase:ServiceRoleKey"];
                var supabaseUrl = config["Supabase:Url"];

                var adminClient = new Supabase.Gotrue.AdminClient(serviceRoleKey, new ClientOptions
                {
                    Url = $"{supabaseUrl}/auth/v1",
                    Headers = new Dictionary<string, string> { { "apikey", serviceRoleKey } }
                });

                var authUser = await adminClient.CreateUser(new Supabase.Gotrue.AdminUserAttributes
                {
                    Email = request.Email,
                    Password = request.Password,
                    EmailConfirm = true,
                    UserMetadata = new Dictionary<string, object>
                    {
                        { "full_name", request.FullName },
                        { "role", "teacher" }
                    }
                });

                if (authUser == null) return BadRequest(new { message = "Failed to create Auth user." });

                await Task.Delay(500); // Wait for the Supabase trigger to create the profile

                // 3. Update Profile Role
                var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Email == request.Email);
                if (profile == null) return BadRequest(new { message = "Profile not created by trigger." });

                profile.Role = ProfileRole.teacher;
                profile.FullName = request.FullName;

                // 4. Create Teacher Record
                var teacher = new Teacher
                {
                    Id = Guid.NewGuid(),
                    ProfileId = profile.Id,
                    DepartmentId = request.DepartmentId,
                    EmployeeId = request.EmployeeId,
                    Designation = request.Designation,
                    Qualification = request.Qualification,
                    IsHod = request.IsHod,
                    IsChairman = request.IsChairman,
                    IsDean = request.IsDean
                };

                _context.Teachers.Add(teacher);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Teacher created successfully." });
            }
            catch (Exception ex)
            {
                var errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return BadRequest(new { message = errorMessage });
            }
        }

        // 👇 NEW: PUT endpoint to update an existing teacher 👇
        [HttpPut("{id}")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeacherRequest request)
        {
            try
            {
                var teacher = await _context.Teachers
                    .Include(t => t.Profile)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (teacher == null) return NotFound(new { message = "Teacher not found." });

                // 1. Prevent Department Admins from moving teachers to other departments
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var deptIdStr = User.FindFirst("departmentId")?.Value;

                if (userRole == "department_admin" && long.TryParse(deptIdStr, out long adminDeptId))
                {
                    request.DepartmentId = adminDeptId;
                }

                // 2. Update Profile
                if (teacher.Profile != null)
                {
                    teacher.Profile.FullName = request.FullName;
                    // Note: Updating email in Supabase Auth requires a separate API call. 
                    // This updates the local DB, but not the login email.
                }

                // 3. Update Teacher Details
                teacher.DepartmentId = request.DepartmentId;
                teacher.EmployeeId = request.EmployeeId;
                teacher.Designation = request.Designation;
                teacher.Qualification = request.Qualification;
                teacher.IsHod = request.IsHod;
                teacher.IsChairman = request.IsChairman;
                teacher.IsDean = request.IsDean;

                await _context.SaveChangesAsync();

                return Ok(new { message = "Teacher updated successfully." });
            }
            catch (Exception ex)
            {
                var errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return BadRequest(new { message = errorMessage });
            }
        }

        [HttpGet("my-department")]
        [RoleRequirementFilter("department_admin")]
        public async Task<IActionResult> GetMyDepartmentTeachers()
        {
            var deptIdStr = User.FindFirst("departmentId")?.Value;
            if (!long.TryParse(deptIdStr, out long departmentId))
                return Unauthorized(new { message = "No department context found." });

            var teachers = await _context.Teachers
                .Include(t => t.Profile)
                .Include(t => t.Department)
                .Where(t => t.DepartmentId == departmentId)
                .Select(t => new
                {
                    id = t.Id,
                    employeeId = t.EmployeeId,
                    designation = t.Designation,
                    // 👇 Return the nested Profile object exactly as the frontend expects
                    profile = new
                    {
                        fullName = t.Profile.FullName, // Returns the Name (e.g., "Dr. Smith")
                        email = t.Profile.Email,
                        isActive = t.Profile.IsActive
                    },
                    department = new
                    {
                        name = t.Department != null ? t.Department.Name : "N/A"
                    }
                })
                .ToListAsync();

            return Ok(teachers);
        }

        // In TeachersController.cs

        [HttpGet("dashboard-stats")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> GetDashboardStats()
        {
            // 1. Get Teacher ID from Token
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            // 2. Fetch Teacher + Profile + Department
            var teacher = await _context.Teachers
                .Include(t => t.Profile)
                .Include(t => t.Department)
                .FirstOrDefaultAsync(t => t.ProfileId == userId);

            if (teacher == null) return NotFound(new { message = "Teacher profile not found." });

            // 3. Calculate Stats
            // Count distinct ProgramCourses assigned to this teacher
            var assignedCourses = await _context.TeacherAssignments
                .Where(ta => ta.TeacherId == teacher.Id)
                .Select(ta => ta.ProgramCourseId)
                .Distinct()
                .CountAsync();

            // Count total sections assigned to this teacher
            var assignedSections = await _context.TeacherAssignments
                .CountAsync(ta => ta.TeacherId == teacher.Id);

            // 4. Fetch Schedule (Assigned Courses)
            var schedule = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Where(ta => ta.TeacherId == teacher.Id)
                .Select(ta => new
                {
                    courseName = ta.ProgramCourse.Course.Name,
                    section = ta.SectionName,
                    // Note: Since we don't have a Timetable table yet, we will pass dummy times
                    time = "09:00 AM",
                    end = "10:30 AM",
                    room = "TBA"
                })
                .ToListAsync();

            return Ok(new
            {
                teacherInfo = new
                {
                    name = teacher.Profile.FullName,
                    empId = teacher.EmployeeId,
                    designation = teacher.Designation,
                    department = teacher.Department != null ? teacher.Department.Name : "N/A",
                    email = teacher.Profile.Email
                },
                stats = new
                {
                    totalCourses = assignedCourses,
                    totalSections = assignedSections,
                    // Attendance and package will be added later
                },
                schedule = schedule
            });
        }

        // Inside TeachersController.cs

        [HttpGet("my-courses")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> GetMyCourses()
        {
            // 1. Get the logged-in user's Profile ID from the Token
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            // 2. Find the Teacher record
            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.ProfileId == userId);
            if (teacher == null) return NotFound(new { message = "Teacher profile not found." });

            // 3. Fetch assigned courses with full details (Semester, Batch, Program, Section)
            var courses = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse)
                    .ThenInclude(pc => pc.Course)
                .Include(ta => ta.ProgramCourse)
                    .ThenInclude(pc => pc.Batch)
                .Include(ta => ta.ProgramCourse)
                    .ThenInclude(pc => pc.Semester)
                .Include(ta => ta.ProgramCourse)
                    .ThenInclude(pc => pc.Program)
                .Where(ta => ta.TeacherId == teacher.Id)
                .Select(ta => new
                {
                    id = ta.Id,
                    code = ta.ProgramCourse.Course.Code,
                    name = ta.ProgramCourse.Course.Name,
                    creditHours = ta.ProgramCourse.Course.CreditHours,
                    program = ta.ProgramCourse.Program.Name,
                    batch = ta.ProgramCourse.Batch.Name,
                    semester = ta.ProgramCourse.Semester.Name,
                    section = ta.SectionName
                })
                .ToListAsync();

            return Ok(courses);
        }

        // ===================== ADMIN TEACHER MANAGEMENT =====================

        public class AdminTeacherRequest
        {
            public string FullName { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public string EmployeeId { get; set; } = string.Empty;
            public string Designation { get; set; } = string.Empty;
            public string Qualification { get; set; } = string.Empty;
            public long DepartmentId { get; set; }
            public bool IsHod { get; set; }
            public bool IsChairman { get; set; }
            public bool IsDean { get; set; }
        }

        public class TeacherRow
        {
            public Guid Id { get; set; }
            public Guid ProfileId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public string EmployeeId { get; set; } = string.Empty;
            public string Designation { get; set; } = string.Empty;
            public string Qualification { get; set; } = string.Empty;
            public long? DepartmentId { get; set; }
            public string? DepartmentName { get; set; }
            public bool IsHod { get; set; }
            public bool IsChairman { get; set; }
            public bool IsDean { get; set; }
            public bool IsActive { get; set; }
        }

        private static readonly System.Linq.Expressions.Expression<Func<Teacher, TeacherRow>> RowSelector =
            t => new TeacherRow
            {
                Id = t.Id,
                ProfileId = t.ProfileId,
                FullName = t.Profile.FullName,
                Email = t.Profile.Email,
                Phone = t.Profile.Phone,
                EmployeeId = t.EmployeeId,
                Designation = t.Designation,
                Qualification = t.Qualification,
                DepartmentId = t.DepartmentId,
                DepartmentName = t.Department != null ? t.Department.Name : null,
                IsHod = t.IsHod,
                IsChairman = t.IsChairman,
                IsDean = t.IsDean,
                IsActive = t.Profile.IsActive
            };

        private async Task<Profile?> GetCallerProfileAsync()
        {
            var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(idStr, out var callerId)) return null;
            return await _context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == callerId);
        }

        private static bool IsDepartmentAdmin(Profile caller) => caller.Role.ToString() == "department_admin";

        private Supabase.Gotrue.AdminClient CreateAdminClient(IConfiguration config)
        {
            var serviceRoleKey = config["Supabase:ServiceRoleKey"];
            var supabaseUrl = config["Supabase:Url"];

            return new Supabase.Gotrue.AdminClient(serviceRoleKey, new ClientOptions
            {
                Url = $"{supabaseUrl}/auth/v1",
                Headers = new Dictionary<string, string> { { "apikey", serviceRoleKey } }
            });
        }

        // GET: api/teachers/admin?page=1&pageSize=10&search=smith
        [HttpGet("admin")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetTeachersPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            var caller = await GetCallerProfileAsync();
            if (caller == null) return Unauthorized();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.Teachers.AsNoTracking().AsQueryable();

            if (IsDepartmentAdmin(caller))
                query = query.Where(t => t.DepartmentId == caller.DepartmentId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(t =>
                    t.Profile.FullName.ToLower().Contains(term) ||
                    t.Profile.Email.ToLower().Contains(term) ||
                    t.EmployeeId.ToLower().Contains(term));
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderBy(t => t.Profile.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(RowSelector)
                .ToListAsync();

            return Ok(new
            {
                items,
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }

        // GET: api/teachers/admin/{id}
        [HttpGet("admin/{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetTeacherDetail(Guid id)
        {
            var caller = await GetCallerProfileAsync();
            if (caller == null) return Unauthorized();

            var teacher = await _context.Teachers
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(RowSelector)
                .FirstOrDefaultAsync();

            if (teacher == null) return NotFound(new { message = "Teacher not found." });

            if (IsDepartmentAdmin(caller) && teacher.DepartmentId != caller.DepartmentId)
                return StatusCode(403, new { message = "You can only view teachers in your own department." });

            var assignments = await _context.TeacherAssignments
                .AsNoTracking()
                .Where(ta => ta.TeacherId == id)
                .Select(ta => new
                {
                    code = ta.ProgramCourse.Course.Code,
                    name = ta.ProgramCourse.Course.Name,
                    program = ta.ProgramCourse.Program.Name,
                    batch = ta.ProgramCourse.Batch.Name,
                    semester = ta.ProgramCourse.Semester.Name,
                    section = ta.SectionName
                })
                .ToListAsync();

            return Ok(new { teacher, assignments });
        }

        // PUT: api/teachers/admin/{id}
        [HttpPut("admin/{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> UpdateTeacherAdmin(Guid id, [FromBody] AdminTeacherRequest request)
        {
            var caller = await GetCallerProfileAsync();
            if (caller == null) return Unauthorized();

            var teacher = await _context.Teachers
                .Include(t => t.Profile)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (teacher == null) return NotFound(new { message = "Teacher not found." });

            var isDeptAdmin = IsDepartmentAdmin(caller);
            if (isDeptAdmin && teacher.DepartmentId != caller.DepartmentId)
                return StatusCode(403, new { message = "You can only edit teachers in your own department." });

            if (string.IsNullOrWhiteSpace(request.FullName))
                return BadRequest(new { message = "Full name is required." });

            if (string.IsNullOrWhiteSpace(request.EmployeeId))
                return BadRequest(new { message = "Employee ID is required." });

            var employeeId = request.EmployeeId.Trim();
            var duplicate = await _context.Teachers
                .AnyAsync(t => t.EmployeeId == employeeId && t.Id != id);
            if (duplicate)
                return BadRequest(new { message = $"Employee ID '{employeeId}' is already used by another teacher." });

            // Department admins can't move teachers out of their department
            if (!isDeptAdmin)
            {
                if (request.DepartmentId <= 0)
                    return BadRequest(new { message = "A valid department is required." });

                teacher.DepartmentId = request.DepartmentId;
            }

            teacher.Profile.FullName = request.FullName.Trim();
            teacher.Profile.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            teacher.Profile.UpdatedAt = DateTime.UtcNow;

            teacher.EmployeeId = employeeId;
            teacher.Designation = request.Designation?.Trim() ?? string.Empty;
            teacher.Qualification = request.Qualification?.Trim() ?? string.Empty;
            teacher.IsHod = request.IsHod;
            teacher.IsChairman = request.IsChairman;
            teacher.IsDean = request.IsDean;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Teacher updated successfully." });
        }

        // DELETE: api/teachers/admin/{id}
        [HttpDelete("admin/{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> DeleteTeacherAdmin(Guid id, [FromServices] IConfiguration config)
        {
            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.Id == id);
            if (teacher == null) return NotFound(new { message = "Teacher not found." });

            // teacher_assignments has no cascade, and attendance and timetables hang off it.
            // Block the delete rather than silently wiping academic records.
            var hasAssignments = await _context.TeacherAssignments.AnyAsync(ta => ta.TeacherId == id);
            if (hasAssignments)
                return Conflict(new { message = "This teacher still has course assignments. Remove them before deleting." });

            var profileId = teacher.ProfileId;

            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();

                _context.Teachers.Remove(teacher);
                await _context.SaveChangesAsync();

                var profile = await _context.Profiles.FindAsync(profileId);
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
                Console.WriteLine($"[DELETE TEACHER ERROR]: {message}");
                return StatusCode(500, new { message = "Failed to delete teacher. If this person is a department head or admin, reassign that role first.", details = message });
            }

            // Login account removal. A failure here shouldn't undo the DB delete, so only log it.
            try
            {
                var adminClient = CreateAdminClient(config);
                await adminClient.DeleteUser(profileId.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE TEACHER AUTH WARNING]: {ex.Message}");
            }

            return Ok(new { message = "Teacher deleted successfully." });
        }



    }

    // Request Models
    public class CreateTeacherRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string EmployeeId { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Qualification { get; set; } = string.Empty;
        public long DepartmentId { get; set; }
        public bool IsHod { get; set; }
        public bool IsChairman { get; set; }
        public bool IsDean { get; set; }
    }

    public class UpdateTeacherRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public long DepartmentId { get; set; }
        public string EmployeeId { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Qualification { get; set; } = string.Empty;
        public bool IsHod { get; set; }
        public bool IsChairman { get; set; }
        public bool IsDean { get; set; }
    }
}