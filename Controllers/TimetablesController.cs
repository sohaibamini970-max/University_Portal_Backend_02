using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;
using System.Security.Claims;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/timetables")]
    public class TimetablesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public TimetablesController(ApplicationDbContext context) { _context = context; }

        public class ScheduleRequest
        {
            public long TeacherAssignmentId { get; set; }
            public long RoomId { get; set; }
            public string DayOfWeek { get; set; } = string.Empty;
            public string TimeSlot { get; set; } = string.Empty;
        }

        // GET: api/timetables?...
        [HttpGet]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> GetTimetable(
            [FromQuery] long programId,
            [FromQuery] long batchId,
            [FromQuery] long semesterId,
            [FromQuery] string section)
        {
            var schedule = await _context.Timetables
                .Include(t => t.TeacherAssignment)
                    .ThenInclude(ta => ta.ProgramCourse)
                        .ThenInclude(pc => pc.Course)
                .Include(t => t.TeacherAssignment)
                    .ThenInclude(ta => ta.Teacher)
                        .ThenInclude(tr => tr.Profile)
                .Include(t => t.Room)
                .Where(t => t.TeacherAssignment.ProgramCourse.ProgramId == programId &&
                            t.TeacherAssignment.ProgramCourse.BatchId == batchId &&
                            t.TeacherAssignment.ProgramCourse.SemesterId == semesterId &&
                            t.TeacherAssignment.SectionName == section)
                .Select(t => new
                {
                    id = t.Id,
                    dayOfWeek = t.DayOfWeek,
                    timeSlot = t.TimeSlot,
                    roomName = t.Room.RoomNumber,
                    courseCode = t.TeacherAssignment.ProgramCourse.Course.Code,
                    courseName = t.TeacherAssignment.ProgramCourse.Course.Name,
                    teacherName = t.TeacherAssignment.Teacher.Profile.FullName,
                    teacherAssignmentId = t.TeacherAssignmentId
                })
                .ToListAsync();

            return Ok(schedule);
        }

        // GET: api/timetables/available-courses?...
        [HttpGet("available-courses")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> GetAvailableCourses(
            [FromQuery] long programId,
            [FromQuery] long batchId,
            [FromQuery] long semesterId,
            [FromQuery] string section)
        {
            var courses = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Include(ta => ta.Teacher).ThenInclude(t => t.Profile)
                .Where(ta => ta.ProgramCourse.ProgramId == programId &&
                             ta.ProgramCourse.BatchId == batchId &&
                             ta.ProgramCourse.SemesterId == semesterId &&
                             ta.SectionName == section)
                .Select(ta => new
                {
                    teacherAssignmentId = ta.Id,
                    courseCode = ta.ProgramCourse.Course.Code,
                    courseName = ta.ProgramCourse.Course.Name,
                    teacherName = ta.Teacher.Profile.FullName
                })
                .ToListAsync();

            return Ok(courses);
        }

        // POST: api/timetables
        [HttpPost]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> SaveSlot([FromBody] ScheduleRequest request)
        {
            // 1. Check if Room is already booked
            var roomClash = await _context.Timetables.AnyAsync(t =>
                t.RoomId == request.RoomId &&
                t.DayOfWeek == request.DayOfWeek &&
                t.TimeSlot == request.TimeSlot);

            if (roomClash)
                return BadRequest(new { message = "Room Clash: This room is already booked for this time slot." });

            // 2. Check if the COURSE is already scheduled for this section
            var assignmentClash = await _context.Timetables.AnyAsync(t =>
                t.TeacherAssignmentId == request.TeacherAssignmentId &&
                t.DayOfWeek == request.DayOfWeek &&
                t.TimeSlot == request.TimeSlot);

            if (assignmentClash)
                return BadRequest(new { message = "Course Clash: This course is already scheduled for this time slot." });

            // 3. 👇 TEACHER CLASH CHECK 👇
            // First, get the TeacherId associated with this TeacherAssignment
            var teacherAssignment = await _context.TeacherAssignments
                .FirstOrDefaultAsync(ta => ta.Id == request.TeacherAssignmentId);

            if (teacherAssignment != null)
            {
                // Check if ANY timetable entry exists for this specific TeacherId at the same time
                var teacherClash = await _context.Timetables
                    .Include(t => t.TeacherAssignment)
                    .AnyAsync(t =>
                        t.TeacherAssignment.TeacherId == teacherAssignment.TeacherId &&
                        t.DayOfWeek == request.DayOfWeek &&
                        t.TimeSlot == request.TimeSlot);

                if (teacherClash)
                    return BadRequest(new { message = "Teacher Clash: This teacher is already scheduled for another class at this time." });
            }

            // 4. Save if no clashes
            var slot = new Timetable
            {
                TeacherAssignmentId = request.TeacherAssignmentId,
                RoomId = request.RoomId,
                DayOfWeek = request.DayOfWeek,
                TimeSlot = request.TimeSlot,
                CreatedAt = DateTime.UtcNow
            };

            _context.Timetables.Add(slot);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Class scheduled successfully." });
        }

        // DELETE: api/timetables/{id}
        [HttpDelete("{id}")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> DeleteSlot(long id)
        {
            var slot = await _context.Timetables.FindAsync(id);
            if (slot == null) return NotFound();

            _context.Timetables.Remove(slot);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Class removed from schedule." });
        }

        // GET: api/timetables/my-timetable
        [HttpGet("my-timetable")]
        [RoleRequirementFilter("student")]
        public async Task<IActionResult> GetMyTimetable()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            var registration = await _context.StudentRegistrations
                .Include(r => r.Section)
                .FirstOrDefaultAsync(r => r.StudentProfileId == userId);

            if (registration == null || registration.Section == null)
            {
                Console.WriteLine($"[TIMETABLE] user={userId} → no registration or section");
                return Ok(new List<object>());
            }

            var sectionName = registration.Section.Name;
            var programId = registration.Section.ProgramId;
            var batchId = registration.Section.BatchId;
            var semesterId = registration.Section.SemesterId;

            var rawCount = await _context.Timetables
                .Where(t => t.TeacherAssignment.ProgramCourse.ProgramId == programId
                         && t.TeacherAssignment.ProgramCourse.BatchId == batchId
                         && t.TeacherAssignment.ProgramCourse.SemesterId == semesterId)
                .CountAsync();

            var matchCount = await _context.Timetables
                .Where(t => t.TeacherAssignment.ProgramCourse.ProgramId == programId
                         && t.TeacherAssignment.ProgramCourse.BatchId == batchId
                         && t.TeacherAssignment.ProgramCourse.SemesterId == semesterId
                         && t.TeacherAssignment.SectionName == sectionName)
                .CountAsync();

            Console.WriteLine(
                $"[TIMETABLE] user={userId} section={sectionName} " +
                $"program={programId} batch={batchId} semester={semesterId} " +
                $"raw={rawCount} match={matchCount}");

            var schedule = await _context.Timetables
                .Include(t => t.Room)
                .Include(t => t.TeacherAssignment).ThenInclude(ta => ta.Teacher).ThenInclude(tr => tr.Profile)
                .Include(t => t.TeacherAssignment).ThenInclude(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Where(t => t.TeacherAssignment.SectionName == sectionName
                         && t.TeacherAssignment.ProgramCourse.ProgramId == programId
                         && t.TeacherAssignment.ProgramCourse.BatchId == batchId
                         && t.TeacherAssignment.ProgramCourse.SemesterId == semesterId)
                .Select(t => new
                {
                    id = t.Id,
                    dayOfWeek = t.DayOfWeek,
                    timeSlot = t.TimeSlot,
                    roomName = t.Room != null ? t.Room.RoomNumber : "—",
                    courseCode = t.TeacherAssignment.ProgramCourse.Course.Code,
                    courseName = t.TeacherAssignment.ProgramCourse.Course.Name,
                    teacherName = t.TeacherAssignment.Teacher.Profile != null
                                    ? t.TeacherAssignment.Teacher.Profile.FullName
                                    : "Not Assigned"
                })
                .ToListAsync();

            Console.WriteLine($"[TIMETABLE] → returning {schedule.Count} rows");
            return Ok(schedule);
        }

        // Add this to TimetablesController.cs
        [HttpGet("teacher-schedule-by-date")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> GetTeacherScheduleByDate([FromQuery] string date)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.ProfileId == userId);
            if (teacher == null) return NotFound();

            var parsedDate = DateTime.Parse(date);
            var dayName = parsedDate.DayOfWeek.ToString();

            // Fetch classes for this teacher on this specific day of the week
            var schedule = await _context.Timetables
                .Include(t => t.TeacherAssignment).ThenInclude(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Include(t => t.TeacherAssignment).ThenInclude(ta => ta.ProgramCourse).ThenInclude(pc => pc.Program)
                .Include(t => t.TeacherAssignment).ThenInclude(ta => ta.ProgramCourse).ThenInclude(pc => pc.Batch)
                .Include(t => t.Room)
                .Where(t => t.TeacherAssignment.TeacherId == teacher.Id && t.DayOfWeek == dayName)
                .Select(t => new
                {
                    teacherAssignmentId = t.TeacherAssignmentId,
                    courseCode = t.TeacherAssignment.ProgramCourse.Course.Code,
                    courseName = t.TeacherAssignment.ProgramCourse.Course.Name,
                    section = t.TeacherAssignment.SectionName,
                    batchName = t.TeacherAssignment.ProgramCourse.Batch.Name,
                    roomName = t.Room.RoomNumber,
                    timeSlot = t.TimeSlot
                })
                .OrderBy(t => t.timeSlot)
                .ToListAsync();

            return Ok(schedule);
        }
    }
}