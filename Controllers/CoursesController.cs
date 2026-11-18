using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoursesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CoursesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/courses
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // Now .Include() will work perfectly
            var courses = await _context.Courses
                .Include(c => c.Prerequisite)
                .OrderBy(c => c.Code)
                .ToListAsync();

            return Ok(courses);
        }

        // POST: api/courses
        [HttpPost]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Create([FromBody] Course course)
        {
            course.CreatedAt = DateTime.UtcNow;
            _context.Courses.Add(course);
            await _context.SaveChangesAsync();

            return Ok(course);
        }

        [HttpPut("{id}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Update(long id, [FromBody] Course updated)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course == null) return NotFound();

            course.Code = updated.Code;
            course.Name = updated.Name;
            course.CreditHours = updated.CreditHours;
            course.Type = updated.Type;
            course.PrerequisiteId = updated.PrerequisiteId;
            course.IsActive = updated.IsActive;

            await _context.SaveChangesAsync();
            return Ok(course);
        }

        // DELETE: api/courses/{id}
        [HttpDelete("{id}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> Delete(long id)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course == null) return NotFound();

            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/courses/program-course/{programCourseId}
        [HttpDelete("program-course/{programCourseId:long}")]
        [RoleRequirementFilter("superadmin,systemadmin")]
        public async Task<IActionResult> RemoveProgramCourse(long programCourseId)
        {
            // 1. Find the assignment
            var pc = await _context.ProgramCourses
                .Include(x => x.Program)
                .Include(x => x.Course)
                .Include(x => x.Batch)
                .Include(x => x.Semester)
                .FirstOrDefaultAsync(x => x.Id == programCourseId);

            if (pc == null)
                return NotFound(new { message = "Program course assignment not found." });

            // 2. Block deletion if any student is currently enrolled in it
            var enrolledCount = await _context.StudentRegistrations
                .CountAsync(r => r.ProgramCourseId == programCourseId);

            if (enrolledCount > 0)
            {
                return BadRequest(new
                {
                    message = $"{enrolledCount} student(s) are currently enrolled in '{pc.Course.Code} - {pc.Course.Name}' " +
                              $"({pc.Program.Code} / {pc.Batch.Name} / {pc.Semester?.Name}). " +
                              $"Remove their registrations first, or contact system admin.",
                    enrolledCount
                });
            }

            // 3. Block deletion if a teacher is assigned to it
            var teacherAssigned = await _context.TeacherAssignments
                .AnyAsync(ta => ta.ProgramCourseId == programCourseId);

            if (teacherAssigned)
            {
                return BadRequest(new
                {
                    message = $"A teacher is still assigned to teach '{pc.Course.Code}' for this section. " +
                              $"Remove the teacher assignment first."
                });
            }

            // 4. Safe to remove
            _context.ProgramCourses.Remove(pc);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Removed '{pc.Course.Code} - {pc.Course.Name}' from {pc.Program.Code} / {pc.Batch.Name}.",
                removed = new
                {
                    courseCode = pc.Course.Code,
                    courseName = pc.Course.Name,
                    program = pc.Program.Code,
                    batch = pc.Batch.Name,
                    semester = pc.Semester?.Name
                }
            });
        }
    }
}