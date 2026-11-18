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
        public async Task<IActionResult> GetConfiguration(
    [FromQuery] long programId, [FromQuery] long batchId,
    [FromQuery] long semesterId, [FromQuery] string section)
        {
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

                    // 👇 Full slots (day + period)
                    scheduleSlots = _context.Timetables
                        .Where(t => t.TeacherAssignmentId == ta.Id)
                        .OrderBy(t => t.DayOfWeek)
                        .ThenBy(t => t.TimeSlot)
                        .Select(t => new { dayOfWeek = t.DayOfWeek, timeSlot = t.TimeSlot })
                        .ToList(),

                    // 👇 Days only, for backward compatibility with current frontend
                    scheduleDays = _context.Timetables
                        .Where(t => t.TeacherAssignmentId == ta.Id)
                        .Select(t => t.DayOfWeek)
                        .Distinct()
                        .ToList(),

                    // 👇 Total classes per week (counts back-to-back periods separately)
                    classesPerWeek = _context.Timetables.Count(t => t.TeacherAssignmentId == ta.Id)
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
            var assignment = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .FirstOrDefaultAsync(ta => ta.Id == request.TeacherAssignmentId);

            if (assignment == null) return NotFound();

            // ✅ Pull the actual timetable rows — one per class/period.
            //    Do NOT Distinct() on DayOfWeek, or back-to-back periods collapse.
            var timetableSlots = await _context.Timetables
                .Where(t => t.TeacherAssignmentId == request.TeacherAssignmentId)
                .Select(t => new { t.DayOfWeek, t.TimeSlot })
                .OrderBy(t => t.DayOfWeek)
                .ThenBy(t => t.TimeSlot)
                .ToListAsync();

            if (timetableSlots.Count == 0)
                return BadRequest(new { message = "No timetable schedule found for this course." });

            int classesPerWeek = timetableSlots.Count;   // 3 for your example

            // Remove existing dates for this course
            var existing = await _context.AcademicCalendars
                .Where(c => c.TeacherAssignmentId == request.TeacherAssignmentId)
                .ToListAsync();
            _context.AcademicCalendars.RemoveRange(existing);

            var newDates = new List<AcademicCalendar>();

            // Find the Monday of the week containing StartDate
            var start = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
            int daysToMonday = ((int)start.DayOfWeek + 6) % 7;   // Monday=0, Tue=1, ..., Sun=6
            var firstMonday = start.AddDays(-daysToMonday);

            // For each week, for each timetable slot, create exactly one academic_calendar row
            for (int w = 0; w < request.TotalWeeks; w++)
            {
                var weekStart = firstMonday.AddDays(7 * w);
                int classIndexInWeek = 0;

                foreach (var slot in timetableSlots)
                {
                    var date = ResolveDateForDay(weekStart, slot.DayOfWeek);

                    // Skip slots that fall before the semester start date
                    if (date < start) continue;

                    classIndexInWeek++;
                    newDates.Add(new AcademicCalendar
                    {
                        TeacherAssignmentId = request.TeacherAssignmentId,
                        WeekNumber = w + 1,
                        ClassNumber = classIndexInWeek,     // 1, 2, 3 within the week
                        Date = DateTime.SpecifyKind(date, DateTimeKind.Utc),
                        DayName = slot.DayOfWeek,
                        TimeSlot = slot.TimeSlot,
                        IsAttendanceAllowed = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _context.AcademicCalendars.AddRange(newDates);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Generated {newDates.Count} dates across {request.TotalWeeks} weeks.",
                classesPerWeek,
                weeks = request.TotalWeeks
            });
        }

        private static DateTime ResolveDateForDay(DateTime weekMonday, string dayOfWeek)
        {
            // weekMonday is a Monday; find the offset for the requested day name
            return dayOfWeek switch
            {
                "Monday" => weekMonday,
                "Tuesday" => weekMonday.AddDays(1),
                "Wednesday" => weekMonday.AddDays(2),
                "Thursday" => weekMonday.AddDays(3),
                "Friday" => weekMonday.AddDays(4),
                "Saturday" => weekMonday.AddDays(5),
                "Sunday" => weekMonday.AddDays(6),
                _ => weekMonday
            };
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
                    dayName = c.DayName,
                    timeSlot = c.TimeSlot,
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