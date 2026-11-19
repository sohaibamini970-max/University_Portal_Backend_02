using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;
using Supabase;
using Supabase.Gotrue;
using System.Security.Claims;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/students")]  
    public class StudentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public StudentsController(ApplicationDbContext context) { _context = context; }

        public class RegisterStudentRequest
        {
            public string SequenceNo { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string FatherName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Phone { get; set; } = string.Empty;
            public long DepartmentId { get; set; }
            public long ProgramId { get; set; }
            public long BatchId { get; set; }
            public string Section { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        // Inside StudentsController.cs

        [HttpGet("dashboard-stats")]
        [RoleRequirementFilter("student")]
        public async Task<IActionResult> GetDashboardStats()
        {
            // 1. Get the logged-in user's Profile ID from the Token
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            // 2. Fetch Student + Profile + Program + Batch
            var student = await _context.Students
                .Include(s => s.Profile)
                .Include(s => s.Program)
                .Include(s => s.Batch)
                .Include(s => s.Department)
                .FirstOrDefaultAsync(s => s.ProfileId == userId);

            if (student == null) return NotFound(new { message = "Student profile not found." });

            // 3. Fetch Enrolled Courses
            var enrolledCourses = await _context.StudentRegistrations
                .Include(r => r.ProgramCourse).ThenInclude(pc => pc.Course)
                .Where(r => r.StudentProfileId == userId)
                .Select(r => new
                {
                    code = r.ProgramCourse.Course.Code,
                    name = r.ProgramCourse.Course.Name,
                    creditHours = r.ProgramCourse.Course.CreditHours
                })
                .ToListAsync();

            return Ok(new
            {
                studentInfo = new
                {
                    name = student.Profile.FullName,
                    regNo = student.Profile.RollNo,
                    section = student.Section,
                    program = student.Program.Name,
                    session = student.Batch.Name, // e.g., "2024-2028"
                    department = student.Department.Name
                },
                courses = enrolledCourses
            });
        }

        [HttpPost("register-admission")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> RegisterStudent(
            [FromBody] RegisterStudentRequest request,
            [FromServices] Supabase.Client supabase,
            [FromServices] IConfiguration config)
        {
            try
            {
                // 👇 1. GENERATE ROLL NO 👇
                var program = await _context.Programs.FindAsync(request.ProgramId);
                var batch = await _context.Batches.FindAsync(request.BatchId);

                if (program == null || batch == null)
                    return BadRequest(new { message = "Invalid Program or Batch." });

                var paddedSeq = request.SequenceNo.PadLeft(3, '0');
                var generatedRollNo = $"{batch.StartYear}F-{program.Code}-{paddedSeq}";

                // 👇 2. CREATE AUTH USER IN SUPABASE 👇
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
                        { "role", "student" }
                    }
                });

                if (authUser == null) return BadRequest(new { message = "Failed to create Auth user." });

                await Task.Delay(500); // Wait for DB trigger to create profile

                // 👇 3. CREATE PROFILE 👇
                var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Email == request.Email);
                if (profile == null)
                {
                    profile = new Profile
                    {
                        Id = Guid.Parse(authUser.Id),
                        Email = request.Email,
                        FullName = request.FullName,
                        Role = ProfileRole.student,
                        IsActive = false,
                        RollNo = generatedRollNo,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Profiles.Add(profile);
                }
                else
                {
                    profile.RollNo = generatedRollNo;
                    profile.IsActive = false;
                }

                // 👇 4. CREATE STUDENT 👇
                var student = new Student
                {
                    ProfileId = profile.Id,
                    DepartmentId = request.DepartmentId,
                    ProgramId = request.ProgramId,
                    BatchId = request.BatchId,
                    Section = request.Section,
                    FatherName = request.FatherName,
                    IsActive = false
                };
                _context.Students.Add(student);

                await _context.SaveChangesAsync();

                // 👇 5. AUTO-ASSIGN 1st SEMESTER COURSES 👇
                var firstSem = await _context.Semesters.FirstOrDefaultAsync(s => s.Name.Contains("1"));
                if (firstSem == null) return BadRequest(new { message = "1st Semester not found in system." });

                var sectionRecord = await _context.Sections
                    .FirstOrDefaultAsync(s => s.ProgramId == request.ProgramId &&
                                              s.BatchId == request.BatchId &&
                                              s.SemesterId == firstSem.Id &&
                                              s.Name == request.Section);

                if (sectionRecord == null)
                {
                    return BadRequest(new { message = $"Section '{request.Section}' is not configured for this Program and Batch. Please configure it in System Admin -> 1st Semester." });
                }

                var assignedCourses = await _context.ProgramCourses
                    .Where(pc => pc.ProgramId == request.ProgramId &&
                                 pc.BatchId == request.BatchId &&
                                 pc.SemesterId == firstSem.Id &&
                                 pc.Sections.Contains(request.Section))
                    .ToListAsync();

                if (assignedCourses.Count == 0)
                {
                    return BadRequest(new { message = $"No courses found for Section '{request.Section}'. Did you assign courses in Super Admin -> Assign Courses?" });
                }

                foreach (var course in assignedCourses)
                {
                    _context.StudentRegistrations.Add(new StudentRegistration
                    {
                        StudentProfileId = profile.Id,
                        ProgramCourseId = course.Id,
                        SectionId = sectionRecord.Id,
                        Status = "Enrolled",
                        RegisteredAt = DateTime.UtcNow
                    });
                }
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Student registered successfully. Roll No: {generatedRollNo}" });
            }
            catch (Exception ex)
            {
                // Unwrap the inner exception to get the Postgres error
                var errorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;

                // Print to terminal
                Console.WriteLine($"[REGISTRATION ERROR]: {errorMessage}");

                // Return to frontend so you can see it in the browser alert
                return StatusCode(500, new { message = "Database error", details = errorMessage });
            }
        }

        public class BulkStudentRow
        {
            public string? SequenceNo { get; set; }
            public string? FullName { get; set; }
            public string? FatherName { get; set; }
            public string? Email { get; set; }
            public string? Phone { get; set; }
            public string? CNIC { get; set; }
            public string? DepartmentName { get; set; }
            public string? ProgramCode { get; set; }
            public string? BatchName { get; set; }
            public string? Section { get; set; }
        }

        public class BulkRegisterRequest
        {
            public List<BulkStudentRow>? Students { get; set; }
        }

        public class BulkActivateRequest
        {
            public string? Search { get; set; }   // optional — mirrors the admin list filter
        }

        [HttpPost("admin/bulk-activate")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> BulkActivateStudents([FromBody] BulkActivateRequest request)
        {
            var search = request?.Search?.Trim();

            // Build the query for INACTIVE students only
            var query = _context.Students
                .Include(s => s.Profile)
                .Where(s => !s.IsActive || (s.Profile != null && !s.Profile.IsActive));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                query = query.Where(s =>
                    s.Profile.FullName.ToLower().Contains(term) ||
                    (s.Profile.RollNo != null && s.Profile.RollNo.ToLower().Contains(term)) ||
                    s.Profile.Email.ToLower().Contains(term));
            }

            var students = await query.ToListAsync();

            if (students.Count == 0)
                return Ok(new { message = "No inactive students to activate.", activatedCount = 0 });

            foreach (var s in students)
            {
                s.IsActive = true;
                if (s.Profile != null)
                {
                    s.Profile.IsActive = true;
                    s.Profile.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"{students.Count} student{(students.Count == 1 ? "" : "s")} activated successfully.",
                activatedCount = students.Count
            });
        }

        [HttpPost("bulk-register")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        [Consumes("application/json")]
        public async Task<IActionResult> BulkRegister(
            [FromBody] BulkRegisterRequest request,
            [FromServices] Supabase.Client supabase,
            [FromServices] IConfiguration config)
        {
            if (request.Students == null || request.Students.Count == 0)
                return BadRequest(new { message = "No students provided." });

            const string DEFAULT_PASSWORD = "Student123!";
            var successCount = 0;
            var errors = new List<object>();
            var adminClient = CreateAdminClient(config);

            // Pre-fetch reference data to avoid N+1 queries
            var departments = await _context.Departments.AsNoTracking().ToListAsync();
            var programs = await _context.Programs.AsNoTracking().ToListAsync();
            var batches = await _context.Batches.AsNoTracking().ToListAsync();
            var firstSem = await _context.Semesters.FirstOrDefaultAsync(s => s.Name.Contains("1"));
            if (firstSem == null)
                return BadRequest(new { message = "1st Semester not configured in system." });

            for (int i = 0; i < request.Students.Count; i++)
            {
                var row = request.Students[i];
                var rowNum = i + 2; // Excel row (header is row 1)

                try
                {
                    // --- Validate references ---
                    var dept = departments.FirstOrDefault(d =>
                        d.Name.Equals(row.DepartmentName?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (dept == null) { errors.Add(new { row = rowNum, message = $"Department '{row.DepartmentName}' not found." }); continue; }

                    var program = programs.FirstOrDefault(p =>
                        p.Code.Equals(row.ProgramCode?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (program == null) { errors.Add(new { row = rowNum, message = $"Program code '{row.ProgramCode}' not found." }); continue; }

                    var batch = batches.FirstOrDefault(b =>
                        b.Name.Equals(row.BatchName?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (batch == null) { errors.Add(new { row = rowNum, message = $"Batch '{row.BatchName}' not found." }); continue; }

                    if (string.IsNullOrWhiteSpace(row.Email) || !row.Email.Contains('@'))
                    { errors.Add(new { row = rowNum, message = "Invalid email." }); continue; }

                    // Duplicate email check (in DB)
                    if (await _context.Profiles.AnyAsync(p => p.Email.ToLower() == row.Email.ToLower()))
                    { errors.Add(new { row = rowNum, message = $"Email '{row.Email}' already registered." }); continue; }

                    // --- Generate Roll No ---
                    var paddedSeq = (row.SequenceNo ?? "").PadLeft(3, '0');
                    var rollNo = $"{batch.StartYear}F-{program.Code}-{paddedSeq}";

                    // --- Create Supabase Auth user ---
                    Supabase.Gotrue.User? authUser = null;
                    try
                    {
                        authUser = await adminClient.CreateUser(new Supabase.Gotrue.AdminUserAttributes
                        {
                            Email = row.Email,
                            Password = DEFAULT_PASSWORD,
                            EmailConfirm = true,
                            UserMetadata = new Dictionary<string, object>
                    {
                        { "full_name", row.FullName },
                        { "role", "student" }
                    }
                        });
                    }
                    catch (Exception authEx)
                    {
                        errors.Add(new { row = rowNum, message = $"Auth creation failed: {authEx.Message}" });
                        continue;
                    }

                    if (authUser == null)
                    { errors.Add(new { row = rowNum, message = "Auth user creation returned null." }); continue; }

                    await Task.Delay(300); // let trigger create profile if configured

                    // --- Create/Update Profile ---
                    var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Email == row.Email);
                    if (profile == null)
                    {
                        profile = new Profile
                        {
                            Id = Guid.Parse(authUser.Id),
                            Email = row.Email,
                            FullName = row.FullName,
                            Role = ProfileRole.student,
                            IsActive = false,
                            RollNo = rollNo,
                            Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim(),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.Profiles.Add(profile);
                    }
                    else
                    {
                        profile.RollNo = rollNo;
                        profile.FullName = row.FullName;
                        profile.IsActive = false;
                        profile.Phone = string.IsNullOrWhiteSpace(row.Phone) ? null : row.Phone.Trim();
                        profile.UpdatedAt = DateTime.UtcNow;
                    }

                    // --- Create Student ---
                    var student = new Student
                    {
                        ProfileId = profile.Id,
                        DepartmentId = dept.Id,
                        ProgramId = program.Id,
                        BatchId = batch.Id,
                        Section = row.Section?.Trim() ?? "",
                        FatherName = row.FatherName?.Trim() ?? "",
                        IsActive = false
                    };
                    _context.Students.Add(student);
                    await _context.SaveChangesAsync();

                    // --- Validate Section config ---
                    var sectionRecord = await _context.Sections.FirstOrDefaultAsync(s =>
                        s.ProgramId == program.Id &&
                        s.BatchId == batch.Id &&
                        s.SemesterId == firstSem.Id &&
                        s.Name == student.Section);

                    if (sectionRecord == null)
                    {
                        errors.Add(new { row = rowNum, message = $"Section '{student.Section}' not configured for this Program/Batch." });
                        continue;
                    }

                    // --- Auto-assign 1st semester courses ---
                    var assignedCourses = await _context.ProgramCourses
                        .Where(pc => pc.ProgramId == program.Id &&
                                     pc.BatchId == batch.Id &&
                                     pc.SemesterId == firstSem.Id &&
                                     pc.Sections.Contains(student.Section))
                        .ToListAsync();

                    if (assignedCourses.Count == 0)
                    {
                        errors.Add(new { row = rowNum, message = $"No courses assigned for Section '{student.Section}'." });
                        continue;
                    }

                    foreach (var course in assignedCourses)
                    {
                        _context.StudentRegistrations.Add(new StudentRegistration
                        {
                            StudentProfileId = profile.Id,
                            ProgramCourseId = course.Id,
                            SectionId = sectionRecord.Id,
                            Status = "Enrolled",
                            RegisteredAt = DateTime.UtcNow
                        });
                    }
                    await _context.SaveChangesAsync();
                    successCount++;
                }
                catch (Exception ex)
                {
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    Console.WriteLine($"[BULK REGISTER ROW {rowNum}]: {msg}");
                    errors.Add(new { row = rowNum, message = msg });
                }
            }

            return Ok(new
            {
                message = $"{successCount} of {request.Students.Count} student(s) registered successfully. Default password: {DEFAULT_PASSWORD}",
                successCount,
                failedCount = errors.Count,
                errors
            });
        }

        // GET: api/students/registrations
        [HttpGet("registrations")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> GetStudentRegistrations()
        {
            var registrations = await _context.StudentRegistrations
                .Include(r => r.StudentProfile)
                .Include(r => r.ProgramCourse).ThenInclude(pc => pc.Course)
                .Include(r => r.Section)
                .GroupBy(r => r.StudentProfileId)
                .Select(g => new
                {
                    id = g.Key,
                    name = g.First().StudentProfile.FullName,
                    rollNo = g.First().StudentProfile.RollNo,
                    registeredCourses = g.Select(r => new
                    {
                        code = r.ProgramCourse.Course.Code,
                        name = r.ProgramCourse.Course.Name,
                        credits = r.ProgramCourse.Course.CreditHours,
                        section = r.Section.Name
                    }).ToList()
                })
                .ToListAsync();

            return Ok(registrations);
        }

        // GET: api/students/all
        [HttpGet("all")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetAllStudents()
        {
            var students = await _context.Students
                .Include(s => s.Profile)
                .Include(s => s.Program)
                .Include(s => s.Batch)
                .Select(s => new
                {
                    id = s.Id,
                    profileId = s.ProfileId,
                    section = s.Section,
                    isActive = s.IsActive,
                    profile = new
                    {
                        fullName = s.Profile.FullName,
                        rollNo = s.Profile.RollNo,
                        email = s.Profile.Email
                    },
                    program = new { code = s.Program.Code },
                    batch = new { name = s.Batch.Name }
                })
                .ToListAsync();

            return Ok(students);
        }

        // Inside StudentsController.cs

        [HttpGet("my-courses")]
        [RoleRequirementFilter("student")]
        public async Task<IActionResult> GetMyCourses()
        {
            // 1. Get the logged-in user's Profile ID from the Token
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            // 2. Fetch the student's registrations, joining all necessary tables
            var courses = await _context.StudentRegistrations
                .Include(r => r.ProgramCourse)
                    .ThenInclude(pc => pc.Course)
                .Include(r => r.ProgramCourse)
                    .ThenInclude(pc => pc.Semester)
                .Include(r => r.ProgramCourse)
                    .ThenInclude(pc => pc.Batch)
                .Where(r => r.StudentProfileId == userId)
                .Select(r => new
                {
                    id = r.ProgramCourse.Id,
                    code = r.ProgramCourse.Course.Code,
                    name = r.ProgramCourse.Course.Name,
                    creditHours = r.ProgramCourse.Course.CreditHours,
                    type = r.ProgramCourse.Course.Type, // Core or Elective
                    semester = r.ProgramCourse.Semester.Name,
                    session = r.ProgramCourse.Batch.Name,
                    section = r.Section.Name,
                    // 👇 Fetch the teacher assigned to this specific section
                    teacher = _context.TeacherAssignments
                        .Where(ta => ta.ProgramCourseId == r.ProgramCourse.Id && ta.SectionName == r.Section.Name)
                        .Select(ta => ta.Teacher.Profile.FullName)
                        .FirstOrDefault() ?? "Not Assigned"
                })
                .ToListAsync();

            return Ok(courses);
        }

        public class ToggleStatusRequest { public bool IsActive { get; set; } }

        // PUT: api/students/{id}/toggle-status
        [HttpPut("{id}/toggle-status")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> ToggleStatus(Guid id, [FromBody] ToggleStatusRequest request)
        {
            var student = await _context.Students.FindAsync(id);
            if (student == null) return NotFound(new { message = "Student not found." });

            student.IsActive = request.IsActive;
            var profile = await _context.Profiles.FindAsync(student.ProfileId);
            if (profile != null) profile.IsActive = request.IsActive;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Status updated successfully." });
        }

        [HttpPut("{profileId}/reset-password")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> ResetPassword(Guid profileId, [FromBody] ResetPasswordRequest request, [FromServices] IConfiguration config)
        {
            var profile = await _context.Profiles.FindAsync(profileId);
            if (profile == null) return NotFound(new { message = "Profile not found." });

            try
            {
                var serviceRoleKey = config["Supabase:ServiceRoleKey"];
                var supabaseUrl = config["Supabase:Url"];

                var adminClient = new Supabase.Gotrue.AdminClient(serviceRoleKey, new ClientOptions
                {
                    Url = $"{supabaseUrl}/auth/v1",
                    Headers = new Dictionary<string, string> { { "apikey", serviceRoleKey } }
                });

                try
                {
                    // First, try to update the user
                    var attributes = new Supabase.Gotrue.AdminUserAttributes { Password = request.Password };
                    await adminClient.UpdateUserById(profileId.ToString(), attributes);
                }
                catch (Exception ex) when (ex.Message.Contains("User not found"))
                {
                    // 👇 If the user doesn't exist, CREATE them in Supabase Auth
                    var authUser = await adminClient.CreateUser(new Supabase.Gotrue.AdminUserAttributes
                    {
                        Email = profile.Email,
                        Password = request.Password,
                        EmailConfirm = true,
                        UserMetadata = new Dictionary<string, object>
                {
                    { "full_name", profile.FullName },
                    { "role", "student" }
                }
                    });

                    if (authUser == null)
                        return BadRequest(new { message = "Failed to create Auth user." });
                }

                return Ok(new { message = "Password updated successfully." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RESET PASSWORD ERROR]: {ex.Message}");
                return BadRequest(new { message = "Failed to update password.", details = ex.Message });
            }
        }

        public class ResetPasswordRequest { public string Password { get; set; } = string.Empty; }

        // Inside StudentsController.cs

        [HttpGet("my-timetable")]
        [RoleRequirementFilter("student")]
        public async Task<IActionResult> GetMyTimetable()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            // 1. Get the student's registered section
            var registration = await _context.StudentRegistrations
                .Include(r => r.Section)
                .FirstOrDefaultAsync(r => r.StudentProfileId == userId);

            if (registration == null || registration.Section == null)
                return Ok(new List<object>()); // Return empty if not registered

            var sectionName = registration.Section.Name; // e.g., "A"
            var programId = registration.Section.ProgramId;
            var batchId = registration.Section.BatchId;
            var semesterId = registration.Section.SemesterId;

            // 2. Fetch the schedule for this section
            var schedule = await _context.Timetables
                .Include(t => t.Room)
                .Include(t => t.TeacherAssignment).ThenInclude(ta => ta.Teacher).ThenInclude(tr => tr.Profile)
                .Include(t => t.TeacherAssignment).ThenInclude(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Where(t => t.TeacherAssignment.SectionName == sectionName &&
                            t.TeacherAssignment.ProgramCourse.ProgramId == programId &&
                            t.TeacherAssignment.ProgramCourse.BatchId == batchId &&
                            t.TeacherAssignment.ProgramCourse.SemesterId == semesterId)
                .Select(t => new
                {
                    id = t.Id,
                    dayOfWeek = t.DayOfWeek,
                    timeSlot = t.TimeSlot,
                    roomName = t.Room.RoomNumber,
                    courseCode = t.TeacherAssignment.ProgramCourse.Course.Code,
                    courseName = t.TeacherAssignment.ProgramCourse.Course.Name,
                    teacherName = t.TeacherAssignment.Teacher.Profile.FullName
                })
                .ToListAsync();

            return Ok(schedule);
        }

        // ===================== ADMIN STUDENT MANAGEMENT =====================

        public class UpdateStudentRequest
        {
            public string FullName { get; set; } = string.Empty;
            public string FatherName { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public string Section { get; set; } = string.Empty;
        }

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

        // GET: api/students/admin?page=1&pageSize=10&search=ali
        [HttpGet("admin")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetStudentsPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.Students.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(s =>
                    s.Profile.FullName.ToLower().Contains(term) ||
                    (s.Profile.RollNo != null && s.Profile.RollNo.ToLower().Contains(term)) ||
                    s.Profile.Email.ToLower().Contains(term));
            }

            var total = await query.CountAsync();

            var items = await query
                .OrderBy(s => s.Profile.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    id = s.Id,
                    profileId = s.ProfileId,
                    fullName = s.Profile.FullName,
                    rollNo = s.Profile.RollNo,
                    email = s.Profile.Email,
                    phone = s.Profile.Phone,
                    fatherName = s.FatherName,
                    section = s.Section,
                    isActive = s.IsActive,
                    programName = s.Program.Name,
                    programCode = s.Program.Code,
                    batchName = s.Batch.Name,
                    departmentName = s.Department.Name
                })
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

        // GET: api/students/admin/{id}
        [HttpGet("admin/{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetStudentDetail(Guid id)
        {
            var student = await _context.Students
                .AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new
                {
                    id = s.Id,
                    profileId = s.ProfileId,
                    fullName = s.Profile.FullName,
                    rollNo = s.Profile.RollNo,
                    email = s.Profile.Email,
                    phone = s.Profile.Phone,
                    fatherName = s.FatherName,
                    section = s.Section,
                    isActive = s.IsActive,
                    programName = s.Program.Name,
                    programCode = s.Program.Code,
                    batchName = s.Batch.Name,
                    departmentName = s.Department.Name
                })
                .FirstOrDefaultAsync();

            if (student == null) return NotFound(new { message = "Student not found." });

            var registeredCourses = await _context.StudentRegistrations
                .AsNoTracking()
                .Where(r => r.StudentProfileId == student.profileId)
                .Select(r => new
                {
                    code = r.ProgramCourse.Course.Code,
                    name = r.ProgramCourse.Course.Name,
                    creditHours = r.ProgramCourse.Course.CreditHours,
                    section = r.Section.Name
                })
                .ToListAsync();

            return Ok(new { student, registeredCourses });
        }

        // PUT: api/students/admin/{id}
        [HttpPut("admin/{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> UpdateStudent(Guid id, [FromBody] UpdateStudentRequest request)
        {
            var student = await _context.Students
                .Include(s => s.Profile)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (student == null) return NotFound(new { message = "Student not found." });

            if (string.IsNullOrWhiteSpace(request.FullName))
                return BadRequest(new { message = "Full name is required." });

            var newSection = request.Section?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(newSection))
                return BadRequest(new { message = "Section is required." });

            student.Profile.FullName = request.FullName.Trim();
            student.Profile.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            student.Profile.UpdatedAt = DateTime.UtcNow;
            student.FatherName = request.FatherName?.Trim() ?? string.Empty;

            // If the section changed, move the registrations to the matching section record
            if (!string.Equals(student.Section, newSection, StringComparison.Ordinal))
            {
                var registrations = await _context.StudentRegistrations
                    .Include(r => r.Section)
                    .Where(r => r.StudentProfileId == student.ProfileId)
                    .ToListAsync();

                foreach (var reg in registrations)
                {
                    var target = await _context.Sections.FirstOrDefaultAsync(s =>
                        s.ProgramId == student.ProgramId &&
                        s.BatchId == student.BatchId &&
                        s.SemesterId == reg.Section.SemesterId &&
                        s.Name == newSection);

                    if (target == null)
                        return BadRequest(new { message = $"Section '{newSection}' is not configured for this Program and Batch." });

                    reg.SectionId = target.Id;
                }

                student.Section = newSection;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Student updated successfully." });
        }

        // DELETE: api/students/admin/{id}
        [HttpDelete("admin/{id:guid}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> DeleteStudent(Guid id, [FromServices] IConfiguration config)
        {
            var student = await _context.Students.FirstOrDefaultAsync(s => s.Id == id);
            if (student == null) return NotFound(new { message = "Student not found." });

            var profileId = student.ProfileId;

            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();

                await _context.Attendances
                    .Where(a => a.StudentProfileId == profileId)
                    .ExecuteDeleteAsync();

                await _context.StudentRegistrations
                    .Where(r => r.StudentProfileId == profileId)
                    .ExecuteDeleteAsync();

                _context.Students.Remove(student);
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
                Console.WriteLine($"[DELETE STUDENT ERROR]: {message}");
                return StatusCode(500, new { message = "Failed to delete student.", details = message });
            }

            // Remove the login account. Failure here shouldn't undo the DB delete, so only log it.
            try
            {
                var adminClient = CreateAdminClient(config);
                await adminClient.DeleteUser(profileId.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE AUTH USER WARNING]: {ex.Message}");
            }

            return Ok(new { message = "Student deleted successfully." });
        }
    }
}