using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/teacher-assignments")]
    public class TeacherAssignmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public TeacherAssignmentsController(ApplicationDbContext context) { _context = context; }

        public class AssignRequest
        {
            public Guid TeacherId { get; set; }
            public long ProgramCourseId { get; set; }
            public string SectionName { get; set; } = string.Empty;
        }

        [HttpPost]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> AssignTeacher([FromBody] AssignRequest request)
        {
            // Check if this section already has a teacher assigned
            var existing = await _context.TeacherAssignments
                .FirstOrDefaultAsync(ta => ta.ProgramCourseId == request.ProgramCourseId && ta.SectionName == request.SectionName);

            if (existing != null)
            {
                existing.TeacherId = request.TeacherId; // Update existing
            }
            else
            {
                _context.TeacherAssignments.Add(new TeacherAssignment
                {
                    TeacherId = request.TeacherId,
                    ProgramCourseId = request.ProgramCourseId,
                    SectionName = request.SectionName,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Teacher assigned successfully." });
        }

        [HttpGet("my-department")]
        [RoleRequirementFilter("department_admin")]
        public async Task<IActionResult> GetDepartmentCourses()
        {
            var deptIdStr = User.FindFirst("departmentId")?.Value;
            if (!long.TryParse(deptIdStr, out long deptId)) return Unauthorized();

            // Fetch ProgramCourses that belong to this department's programs
            var courses = await _context.ProgramCourses
                .Include(pc => pc.Course)
                .Include(pc => pc.Semester)
                .Include(pc => pc.Program)
                .Where(pc => pc.Program.DepartmentId == deptId)
                .Select(pc => new
                {
                    id = pc.Id,
                    code = pc.Course.Code,
                    name = pc.Course.Name,
                    semester = pc.Semester.Name,
                    sections = pc.Sections,
                    assignments = _context.TeacherAssignments
                        .Where(ta => ta.ProgramCourseId == pc.Id)
                        .Select(ta => new {
                            id = ta.Id,
                            sectionName = ta.SectionName,
                            teacherId = ta.TeacherId,
                            teacherName = ta.Teacher.Profile.FullName
                        }).ToList()
                })
                .ToListAsync();

            return Ok(courses);
        }

        // GET: api/teacher-assignments/teacher/{teacherId}
        [HttpGet("teacher/{teacherId}")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> GetTeacherAssignments(Guid teacherId)
        {
            var assignments = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Semester)
                .Where(ta => ta.TeacherId == teacherId)
                .Select(ta => new
                {
                    id = ta.Id, // TeacherAssignment ID
                    courseId = ta.ProgramCourseId,
                    code = ta.ProgramCourse.Course.Code,
                    name = ta.ProgramCourse.Course.Name,
                    semester = ta.ProgramCourse.Semester.Name,
                    section = ta.SectionName
                })
                .ToListAsync();

            return Ok(assignments);
        }

        // DELETE: api/teacher-assignments/{id}
        [HttpDelete("{id}")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> UnassignTeacher(long id)
        {
            var assignment = await _context.TeacherAssignments.FindAsync(id);
            if (assignment == null) return NotFound();

            _context.TeacherAssignments.Remove(assignment);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Teacher unassigned successfully." });
        }
    }
}