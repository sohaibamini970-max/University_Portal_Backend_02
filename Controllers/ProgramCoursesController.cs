using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/program-courses")]
    public class ProgramCoursesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ProgramCoursesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==============================================================
        // GET: api/program-courses?batchId=&programId=&semesterId=&sessionId=
        // Returns FULL entity rows (kept for any existing consumer).
        // ==============================================================
        [HttpGet]
        public async Task<IActionResult> GetAssignments(
            [FromQuery] long? batchId,
            [FromQuery] long? programId,
            [FromQuery] long? semesterId,
            [FromQuery] long? sessionId)
        {
            var query = _context.ProgramCourses
                .Include(pc => pc.Course)
                .Include(pc => pc.Program)
                .Include(pc => pc.Batch)
                .Include(pc => pc.Session)
                .Include(pc => pc.Semester)
                .AsQueryable();

            if (batchId.HasValue) query = query.Where(pc => pc.BatchId == batchId.Value);
            if (programId.HasValue) query = query.Where(pc => pc.ProgramId == programId.Value);
            if (semesterId.HasValue) query = query.Where(pc => pc.SemesterId == semesterId.Value);
            if (sessionId.HasValue) query = query.Where(pc => pc.SessionId == sessionId.Value);

            var result = await query
                .OrderBy(pc => pc.SemesterId)
                .ThenBy(pc => pc.Course!.Code)
                .ToListAsync();

            return Ok(result);
        }

        // ==============================================================
        // 👇 NEW: GET: api/program-courses/assignments
        // Flat shape for the ManageCourses "Program Assignments" panel.
        // Filter by programId and/or semesterId (the two dropdowns on the page).
        // ==============================================================
        [HttpGet("assignments")]
        public async Task<IActionResult> GetAssignmentsFlat(
            [FromQuery] long? programId,
            [FromQuery] long? semesterId,
            [FromQuery] long? batchId)
        {
            var query = _context.ProgramCourses
                .Include(pc => pc.Course)
                .Include(pc => pc.Program)
                .Include(pc => pc.Batch)
                .Include(pc => pc.Semester)
                .AsQueryable();

            if (programId.HasValue) query = query.Where(pc => pc.ProgramId == programId.Value);
            if (semesterId.HasValue) query = query.Where(pc => pc.SemesterId == semesterId.Value);
            if (batchId.HasValue) query = query.Where(pc => pc.BatchId == batchId.Value);

            var rows = await query
                .OrderBy(pc => pc.Program!.Code)
                .ThenBy(pc => pc.SemesterId)
                .ThenBy(pc => pc.Course!.Code)
                .Select(pc => new
                {
                    id = pc.Id,
                    courseId = pc.CourseId,
                    courseCode = pc.Course != null ? pc.Course.Code : "N/A",
                    courseName = pc.Course != null ? pc.Course.Name : "Unknown",
                    programId = pc.ProgramId,
                    programCode = pc.Program != null ? pc.Program.Code : null,
                    programName = pc.Program != null ? pc.Program.Name : null,
                    batchId = pc.BatchId,
                    batchName = pc.Batch != null ? pc.Batch.Name : null,
                    semesterId = pc.SemesterId,
                    semesterName = pc.Semester != null ? pc.Semester.Name : null,
                    sections = pc.Sections,
                    enrolledCount = _context.StudentRegistrations
                                        .Count(r => r.ProgramCourseId == pc.Id),
                    hasTeacher = _context.TeacherAssignments
                                        .Any(ta => ta.ProgramCourseId == pc.Id)
                })
                .ToListAsync();

            return Ok(rows);
        }

        // ==============================================================
        // GET: api/program-courses/filter?programId=&batchId=&semesterId=
        // (kept as-is for backward compatibility)
        // ==============================================================
        [HttpGet("filter")]
        public async Task<IActionResult> GetFilteredCourses(
            [FromQuery] long programId,
            [FromQuery] long batchId,
            [FromQuery] long semesterId)
        {
            var courses = await _context.ProgramCourses
                .Include(pc => pc.Course)
                .Where(pc => pc.ProgramId == programId &&
                             pc.BatchId == batchId &&
                             pc.SemesterId == semesterId)
                .Select(pc => new
                {
                    id = pc.Id,
                    courseId = pc.CourseId,
                    courseCode = pc.Course != null ? pc.Course.Code : "N/A",
                    courseName = pc.Course != null ? pc.Course.Name : "Unknown Course",
                    creditHours = pc.Course != null ? pc.Course.CreditHours : 0,
                    sections = pc.Sections
                })
                .ToListAsync();

            return Ok(courses);
        }

        // ==============================================================
        // GET: api/program-courses/{id}
        // ==============================================================
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var pc = await _context.ProgramCourses
                .Include(x => x.Course)
                .Include(x => x.Program)
                .Include(x => x.Batch)
                .Include(x => x.Session)
                .Include(x => x.Semester)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (pc == null) return NotFound();
            return Ok(pc);
        }

        // ==============================================================
        // POST: Assign a course to program/batch/session/semester
        // ==============================================================
        [HttpPost]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> AssignCourse([FromBody] ProgramCourse assignment)
        {
            var programOk = await _context.Programs.AnyAsync(p => p.Id == assignment.ProgramId);
            var courseOk = await _context.Courses.AnyAsync(c => c.Id == assignment.CourseId);
            var batchOk = await _context.Batches.AnyAsync(b => b.Id == assignment.BatchId);
            var sessionOk = await _context.Sessions.AnyAsync(s => s.Id == assignment.SessionId);
            var semesterOk = await _context.Semesters.AnyAsync(s => s.Id == assignment.SemesterId);

            if (!programOk || !courseOk || !batchOk || !sessionOk || !semesterOk)
                return BadRequest(new { message = "Invalid program/course/batch/session/semester reference." });

            var exists = await _context.ProgramCourses.AnyAsync(pc =>
                pc.ProgramId == assignment.ProgramId &&
                pc.CourseId == assignment.CourseId &&
                pc.BatchId == assignment.BatchId &&
                pc.SessionId == assignment.SessionId &&
                pc.SemesterId == assignment.SemesterId);

            if (exists)
                return Conflict(new { message = "Course is already assigned for this program, batch, session, and semester." });

            assignment.CreatedAt = DateTime.UtcNow;
            _context.ProgramCourses.Add(assignment);
            await _context.SaveChangesAsync();

            await _context.Entry(assignment).Reference(x => x.Course).LoadAsync();
            await _context.Entry(assignment).Reference(x => x.Program).LoadAsync();
            await _context.Entry(assignment).Reference(x => x.Batch).LoadAsync();
            await _context.Entry(assignment).Reference(x => x.Session).LoadAsync();
            await _context.Entry(assignment).Reference(x => x.Semester).LoadAsync();

            return Ok(assignment);
        }

        // ==============================================================
        // PUT: Update an existing assignment
        // ==============================================================
        [HttpPut("{id:long}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Update(long id, [FromBody] ProgramCourse updated)
        {
            var existing = await _context.ProgramCourses.FindAsync(id);
            if (existing == null) return NotFound();

            var programOk = await _context.Programs.AnyAsync(p => p.Id == updated.ProgramId);
            var courseOk = await _context.Courses.AnyAsync(c => c.Id == updated.CourseId);
            var batchOk = await _context.Batches.AnyAsync(b => b.Id == updated.BatchId);
            var sessionOk = await _context.Sessions.AnyAsync(s => s.Id == updated.SessionId);
            var semesterOk = await _context.Semesters.AnyAsync(s => s.Id == updated.SemesterId);

            if (!programOk || !courseOk || !batchOk || !sessionOk || !semesterOk)
                return BadRequest(new { message = "Invalid program/course/batch/session/semester reference." });

            var duplicate = await _context.ProgramCourses.AnyAsync(pc =>
                pc.Id != id &&
                pc.ProgramId == updated.ProgramId &&
                pc.CourseId == updated.CourseId &&
                pc.BatchId == updated.BatchId &&
                pc.SessionId == updated.SessionId &&
                pc.SemesterId == updated.SemesterId);

            if (duplicate)
                return Conflict(new { message = "Another assignment already exists with these exact parameters." });

            existing.ProgramId = updated.ProgramId;
            existing.CourseId = updated.CourseId;
            existing.BatchId = updated.BatchId;
            existing.SessionId = updated.SessionId;
            existing.SemesterId = updated.SemesterId;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }

        // ==============================================================
        // DELETE: api/program-courses/{id}
        // Removes only the ASSIGNMENT row — the course itself stays.
        // ==============================================================
        [HttpDelete("{id:long}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Delete(long id)
        {
            var pc = await _context.ProgramCourses
                .Include(x => x.Course)
                .Include(x => x.Program)
                .Include(x => x.Batch)
                .Include(x => x.Semester)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (pc == null)
                return NotFound(new { message = "Assignment not found." });

            // Guard 1: block if students are still enrolled
            var enrolledCount = await _context.StudentRegistrations
                .CountAsync(r => r.ProgramCourseId == id);

            if (enrolledCount > 0)
            {
                return BadRequest(new
                {
                    message = $"{enrolledCount} student(s) are currently enrolled in " +
                              $"'{pc.Course?.Code} - {pc.Course?.Name}'. " +
                              $"Un-enroll them before removing this assignment."
                });
            }

            // Guard 2: block if a teacher is still assigned
            var hasTeacher = await _context.TeacherAssignments
                .AnyAsync(ta => ta.ProgramCourseId == id);

            if (hasTeacher)
            {
                return BadRequest(new
                {
                    message = $"A teacher is still assigned to teach '{pc.Course?.Code}' for this section. " +
                              $"Remove the teacher assignment first."
                });
            }

            _context.ProgramCourses.Remove(pc);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Removed '{pc.Course?.Code} - {pc.Course?.Name}' from " +
                          $"{pc.Program?.Code} / {pc.Batch?.Name} / {pc.Semester?.Name}.",
                removed = new
                {
                    id = pc.Id,
                    courseCode = pc.Course?.Code,
                    courseName = pc.Course?.Name,
                    programCode = pc.Program?.Code,
                    batchName = pc.Batch?.Name,
                    semesterName = pc.Semester?.Name
                }
            });
        }
    }
}