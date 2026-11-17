using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;
using System.Security.Claims;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/academic-calendar")]
    public class AcademicCalendarController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public AcademicCalendarController(ApplicationDbContext context) { _context = context; }

        // GET: api/academic-calendar/configure?programId=1&batchId=1&semesterId=1&section=A
        [HttpGet("configure")]
        [RoleRequirementFilter("department_admin")]
        public async Task<IActionResult> GetConfiguration([FromQuery] long programId, [FromQuery] long batchId, [FromQuery] long semesterId, [FromQuery] string section)
        {
            // Fetch courses assigned to this section
            var courses = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Where(ta => ta.ProgramCourse.ProgramId == programId &&
                             ta.ProgramCourse.BatchId == batchId &&
                             ta.ProgramCourse.SemesterId == semesterId &&
                             ta.SectionName == section)
                .Select(ta => new
                {
                    teacherAssignmentId = ta.Id,
                    courseCode = ta.ProgramCourse.Course.Code,
                    courseName = ta.ProgramCourse.Course.Name,
                    creditHours = ta.ProgramCourse.Course.CreditHours,
                    // Get the days this course is scheduled on
                    scheduleDays = _context.Timetables
                        .Where(t => t.TeacherAssignmentId == ta.Id)
                        .Select(t => t.DayOfWeek)
                        .Distinct()
                        .ToList()
                })
                .ToListAsync();

            return Ok(courses);
        }

        public class GenerateRequest
        {
            public long TeacherAssignmentId { get; set; }
            public int TotalWeeks { get; set; } = 14;
            public DateTime StartDate { get; set; }
        }

        // POST: api/academic-calendar/generate
        [HttpPost("generate")]
        [RoleRequirementFilter("department_admin")]
        public async Task<IActionResult> GenerateDates([FromBody] GenerateRequest request)
        {
            // Get the course details
            var assignment = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .FirstOrDefaultAsync(ta => ta.Id == request.TeacherAssignmentId);

            if (assignment == null) return NotFound();

            int totalClassesNeeded = assignment.ProgramCourse.Course.CreditHours * request.TotalWeeks;

            // Get the timetable days for this course (e.g., Monday, Wednesday, Friday)
            var scheduledDays = await _context.Timetables
                .Where(t => t.TeacherAssignmentId == request.TeacherAssignmentId)
                .Select(t => t.DayOfWeek)
                .Distinct()
                .ToListAsync();

            if (scheduledDays.Count == 0) return BadRequest(new { message = "No timetable schedule found for this course." });

            // Remove existing dates for this course
            var existing = await _context.AcademicCalendars.Where(c => c.TeacherAssignmentId == request.TeacherAssignmentId).ToListAsync();
            _context.AcademicCalendars.RemoveRange(existing);

            var newDates = new List<AcademicCalendar>();
            var currentDate = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
            int classCount = 1;
            int weekNum = 1;

            // Loop until we generate the required number of classes
            while (classCount <= totalClassesNeeded)
            {
                // Check if today is a scheduled day for this course
                if (scheduledDays.Contains(currentDate.DayOfWeek.ToString()))
                {
                    newDates.Add(new AcademicCalendar
                    {
                        TeacherAssignmentId = request.TeacherAssignmentId,
                        WeekNumber = weekNum,
                        ClassNumber = classCount,
                        Date = currentDate,
                        DayName = currentDate.DayOfWeek.ToString(),
                        CreatedAt = DateTime.UtcNow
                    });
                    classCount++;
                }

                // Move to next day
                currentDate = currentDate.AddDays(1);

                // Increment week number when we hit Monday
                if (currentDate.DayOfWeek == DayOfWeek.Monday && newDates.Count > 0)
                {
                    weekNum++;
                }
            }

            _context.AcademicCalendars.AddRange(newDates);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Generated {newDates.Count} dates across {weekNum} weeks." });
        }

        // GET: api/academic-calendar/course?teacherAssignmentId=1
        [HttpGet("course")]
        [RoleRequirementFilter("department_admin,teacher")]
        public async Task<IActionResult> GetCourseDates([FromQuery] long teacherAssignmentId)
        {
            var dates = await _context.AcademicCalendars
                .Where(c => c.TeacherAssignmentId == teacherAssignmentId)
                .OrderBy(c => c.WeekNumber)
                .ThenBy(c => c.Date)
                .Select(c => new {
                    id = c.Id,
                    weekNumber = c.WeekNumber,
                    classNumber = c.ClassNumber,
                    date = c.Date.ToString("yyyy-MM-dd"),
                    dayName = c.DayName
                })
                .ToListAsync();

            return Ok(dates);
        }

        // PUT: api/academic-calendar/{id}/date
        [HttpPut("{id}/date")]
        [RoleRequirementFilter("department_admin")]
        public async Task<IActionResult> UpdateDate(long id, [FromBody] UpdateDateRequest request)
        {
            var entry = await _context.AcademicCalendars.FindAsync(id);
            if (entry == null) return NotFound();

            var newDate = DateTime.SpecifyKind(DateTime.Parse(request.NewDate).Date, DateTimeKind.Utc);
            entry.Date = newDate;
            entry.DayName = newDate.DayOfWeek.ToString();

            await _context.SaveChangesAsync();
            return Ok(new { message = "Date updated." });
        }

        public class UpdateDateRequest { public string NewDate { get; set; } = string.Empty; }

        public class ToggleAttendanceRequest { public bool IsAllowed { get; set; } }

        [HttpPut("{id}/toggle-attendance")]
        [RoleRequirementFilter("department_admin,systemadmin,superadmin")]
        public async Task<IActionResult> ToggleAttendance(long id, [FromBody] ToggleAttendanceRequest request)
        {
            var entry = await _context.AcademicCalendars.FindAsync(id);
            if (entry == null) return NotFound();

            entry.IsAttendanceAllowed = request.IsAllowed;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Attendance restriction updated." });
        }
    }
}