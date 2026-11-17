using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/admin-attendance")]
    public class AdminAttendanceController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public AdminAttendanceController(ApplicationDbContext context) { _context = context; }

        // GET: api/admin-attendance/config?programId=1&batchId=1&semesterId=1
        [HttpGet("config")]
        [RoleRequirementFilter("department_admin,systemadmin,superadmin")]
        public async Task<IActionResult> GetCourseConfig([FromQuery] long programId, [FromQuery] long batchId, [FromQuery] long semesterId)
        {
            // Fetch all courses for this program/batch/semester regardless of section
            var courses = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Where(ta => ta.ProgramCourse.ProgramId == programId &&
                             ta.ProgramCourse.BatchId == batchId &&
                             ta.ProgramCourse.SemesterId == semesterId)
                .GroupBy(ta => new { ta.ProgramCourseId, ta.ProgramCourse.Course.Code, ta.ProgramCourse.Course.Name, ta.IsAttendanceLocked })
                .Select(g => new
                {
                    programCourseId = g.Key.ProgramCourseId,
                    courseCode = g.Key.Code,
                    courseName = g.Key.Name,
                    isGlobalLocked = g.Key.IsAttendanceLocked,
                    sections = g.Select(ta => new {
                        teacherAssignmentId = ta.Id,
                        section = ta.SectionName,
                        isRestricted = !ta.IsAttendanceLocked // If globally locked, it's already restricted
                    }).ToList()
                })
                .ToListAsync();

            return Ok(courses);
        }

        // GET: api/admin-attendance/weeks?programCourseId=1
        [HttpGet("weeks")]
        [RoleRequirementFilter("department_admin,systemadmin,superadmin")]
        public async Task<IActionResult> GetWeeks([FromQuery] long programCourseId)
        {
            // Get unique weeks and their restriction status for this course
            var weeks = await _context.AcademicCalendars
                .Where(c => c.TeacherAssignment.ProgramCourseId == programCourseId)
                .GroupBy(c => c.WeekNumber)
                .Select(g => new
                {
                    weekNumber = g.Key,
                    isAllowed = g.All(c => c.IsAttendanceAllowed) // If all sections allow, the week is allowed
                })
                .OrderBy(w => w.weekNumber)
                .ToListAsync();

            return Ok(weeks);
        }

        // PUT: api/admin-attendance/toggle-week
        [HttpPut("toggle-week")]
        [RoleRequirementFilter("department_admin,systemadmin,superadmin")]
        public async Task<IActionResult> ToggleWeek([FromBody] ToggleWeekRequest request)
        {
            // Update ALL sections for this course for the selected week
            var entries = await _context.AcademicCalendars
                .Where(c => c.TeacherAssignment.ProgramCourseId == request.ProgramCourseId && c.WeekNumber == request.WeekNumber)
                .ToListAsync();

            foreach (var entry in entries)
            {
                entry.IsAttendanceAllowed = request.IsAllowed;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Week {request.WeekNumber} updated." });
        }

        public class ToggleWeekRequest { public long ProgramCourseId { get; set; } public int WeekNumber { get; set; } public bool IsAllowed { get; set; } }

        // PUT: api/admin-attendance/toggle-global-lock
        [HttpPut("toggle-global-lock")]
        [RoleRequirementFilter("systemadmin,superadmin")]
        public async Task<IActionResult> ToggleGlobalLock([FromBody] ToggleGlobalLockRequest request)
        {
            var assignments = await _context.TeacherAssignments
                .Where(ta => ta.ProgramCourseId == request.ProgramCourseId)
                .ToListAsync();

            foreach (var a in assignments)
            {
                a.IsAttendanceLocked = request.IsLocked;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Global lock updated." });
        }

        public class ToggleGlobalLockRequest { public long ProgramCourseId { get; set; } public bool IsLocked { get; set; } }

        // GET: api/admin-attendance/records?programCourseId=1&week=1
        [HttpGet("records")]
        [RoleRequirementFilter("department_admin,systemadmin,superadmin")]
        public async Task<IActionResult> GetRecords([FromQuery] long programCourseId, [FromQuery] int? week)
        {
            var query = _context.Attendances
                .Include(a => a.StudentProfile)
                .Include(a => a.TeacherAssignment)
                .Where(a => a.TeacherAssignment.ProgramCourseId == programCourseId);

            if (week.HasValue)
            {
                // Filter by week
                var targetDates = await _context.AcademicCalendars
                    .Where(c => c.TeacherAssignment.ProgramCourseId == programCourseId && c.WeekNumber == week.Value)
                    .Select(c => c.Date)
                    .ToListAsync();

                query = query.Where(a => targetDates.Contains(a.Date));
            }

            var records = await query
                .Select(a => new
                {
                    id = a.Id,
                    studentName = a.StudentProfile.FullName,
                    rollNo = a.StudentProfile.RollNo,
                    section = a.TeacherAssignment.SectionName,
                    date = a.Date.ToString("yyyy-MM-dd"),
                    status = a.Status,
                    remarks = a.Remarks
                })
                .OrderByDescending(a => a.date)
                .ToListAsync();

            return Ok(records);
        }
    }
}