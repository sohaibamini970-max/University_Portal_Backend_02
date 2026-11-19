using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;
using System.Security.Claims;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/assessments")]
    public class AssessmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public AssessmentsController(ApplicationDbContext context) => _context = context;

        public class SaveMarksRequest
        {
            public long ProgramCourseId { get; set; }
            public long AssessmentTypeId { get; set; }
            public string SectionName { get; set; } = string.Empty;
            public List<StudentMark> Marks { get; set; } = new();
        }

        public class StudentMark
        {
            public Guid StudentProfileId { get; set; }
            public decimal ObtainedMarks { get; set; }
        }

        // Inside AssessmentsController.cs

        public class SaveSplitsRequest
        {
            public long ProgramCourseId { get; set; }
            public long AssessmentTypeId { get; set; }
            public List<SplitInput> Splits { get; set; } = new();
        }
        public class SplitInput { public string PartName { get; set; } public decimal MaxMarks { get; set; } }

        public class SaveMarksWithSplitsRequest
        {
            public long ProgramCourseId { get; set; }
            public long AssessmentTypeId { get; set; }
            public string SectionName { get; set; } = string.Empty;
            public long? SplitId { get; set; } // Null for labs/unsplit, ID for split parts
            public List<StudentMark> Marks { get; set; } = new();
        }

        // GET: api/assessments/splits
        [HttpGet("splits")]
        [RoleRequirementFilter("teacher,department_admin")]
        public async Task<IActionResult> GetSplits([FromQuery] long programCourseId, [FromQuery] long assessmentTypeId)
        {
            var splits = await _context.AssessmentSplits
                .Where(s => s.ProgramCourseId == programCourseId && s.AssessmentTypeId == assessmentTypeId)
                .ToListAsync();
            return Ok(splits);
        }

        // POST: api/assessments/splits
        [HttpPost("splits")]
        [RoleRequirementFilter("teacher,department_admin")]
        public async Task<IActionResult> SaveSplits([FromBody] SaveSplitsRequest request)
        {
            var existing = await _context.AssessmentSplits
                .Where(s => s.ProgramCourseId == request.ProgramCourseId && s.AssessmentTypeId == request.AssessmentTypeId)
                .ToListAsync();
            _context.AssessmentSplits.RemoveRange(existing);

            foreach (var s in request.Splits)
            {
                _context.AssessmentSplits.Add(new AssessmentSplit
                {
                    ProgramCourseId = request.ProgramCourseId,
                    AssessmentTypeId = request.AssessmentTypeId,
                    PartName = s.PartName,
                    MaxMarks = s.MaxMarks
                });
            }
            await _context.SaveChangesAsync();
            return Ok(new { message = "Splits saved." });
        }

        // GET: api/assessments/teacher/marks-with-splits
        [HttpGet("teacher/marks-with-splits")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> GetMarksWithSplits([FromQuery] long programCourseId, [FromQuery] long assessmentTypeId, [FromQuery] string sectionName)
        {
            var marks = await _context.StudentAssessmentMarks
                .Where(m => m.ProgramCourseId == programCourseId && m.AssessmentTypeId == assessmentTypeId && m.SectionName == sectionName)
                .Select(m => new { m.StudentProfileId, m.ObtainedMarks, m.SplitId })
                .ToListAsync();
            return Ok(marks);
        }

        // POST: api/assessments/teacher/save-with-splits
        [HttpPost("teacher/save-with-splits")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> SaveMarksWithSplits([FromBody] SaveMarksWithSplitsRequest request)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            var existing = await _context.StudentAssessmentMarks
                .Where(m => m.ProgramCourseId == request.ProgramCourseId
                         && m.AssessmentTypeId == request.AssessmentTypeId
                         && m.SectionName == request.SectionName
                         && m.SplitId == request.SplitId)
                .ToListAsync();

            foreach (var item in request.Marks)
            {
                var mark = existing.FirstOrDefault(m => m.StudentProfileId == item.StudentProfileId);
                if (mark != null)
                {
                    mark.ObtainedMarks = item.ObtainedMarks;
                    mark.EnteredByTeacherId = userId;
                    mark.EnteredAt = DateTime.UtcNow;
                }
                else
                {
                    _context.StudentAssessmentMarks.Add(new StudentAssessmentMarks
                    {
                        StudentProfileId = item.StudentProfileId,
                        ProgramCourseId = request.ProgramCourseId,
                        AssessmentTypeId = request.AssessmentTypeId,
                        SectionName = request.SectionName,
                        SplitId = request.SplitId,
                        ObtainedMarks = item.ObtainedMarks,
                        EnteredByTeacherId = userId
                    });
                }
            }
            await _context.SaveChangesAsync();
            return Ok(new { message = "Marks saved." });
        }

        // GET: api/assessments/teacher/courses
        [HttpGet("teacher/courses")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> GetTeacherCourses()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            var assignments = await _context.TeacherAssignments
                .Include(ta => ta.ProgramCourse).ThenInclude(pc => pc.Course)
                .Where(ta => ta.Teacher.ProfileId == userId)
                .Select(ta => new {
                    teacherAssignmentId = ta.Id,
                    programCourseId = ta.ProgramCourseId,
                    courseCode = ta.ProgramCourse.Course.Code,
                    courseName = ta.ProgramCourse.Course.Name,
                    sectionName = ta.SectionName
                }).ToListAsync();

            return Ok(assignments);
        }

        // GET: api/assessments/teacher/students?programCourseId=&sectionName=
        [HttpGet("teacher/students")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> GetTeacherStudents([FromQuery] long programCourseId, [FromQuery] string sectionName)
        {
            var students = await _context.StudentRegistrations
                .Include(r => r.StudentProfile)
                .Where(r => r.ProgramCourseId == programCourseId && r.Section.Name == sectionName)
                .Select(r => new {
                    studentProfileId = r.StudentProfileId,
                    fullName = r.StudentProfile.FullName,
                    rollNo = r.StudentProfile.RollNo
                }).ToListAsync();

            return Ok(students);
        }

        // GET: api/assessments/teacher/marks?programCourseId=&assessmentTypeId=&sectionName=
        [HttpGet("teacher/marks")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> GetExistingMarks([FromQuery] long programCourseId, [FromQuery] long assessmentTypeId, [FromQuery] string sectionName)
        {
            var marks = await _context.StudentAssessmentMarks
                .Where(m => m.ProgramCourseId == programCourseId && m.AssessmentTypeId == assessmentTypeId && m.SectionName == sectionName)
                .ToListAsync();

            return Ok(marks);
        }

        // POST: api/assessments/teacher/save
        [HttpPost("teacher/save")]
        [RoleRequirementFilter("teacher")]
        public async Task<IActionResult> SaveMarks([FromBody] SaveMarksRequest request)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            var existingMarks = await _context.StudentAssessmentMarks
                .Where(m => m.ProgramCourseId == request.ProgramCourseId
                         && m.AssessmentTypeId == request.AssessmentTypeId
                         && m.SectionName == request.SectionName)
                .ToListAsync();

            foreach (var item in request.Marks)
            {
                var mark = existingMarks.FirstOrDefault(m => m.StudentProfileId == item.StudentProfileId);
                if (mark != null)
                {
                    mark.ObtainedMarks = item.ObtainedMarks;
                    mark.EnteredByTeacherId = userId;
                    mark.EnteredAt = DateTime.UtcNow;
                }
                else
                {
                    _context.StudentAssessmentMarks.Add(new StudentAssessmentMarks
                    {
                        StudentProfileId = item.StudentProfileId,
                        ProgramCourseId = request.ProgramCourseId,
                        AssessmentTypeId = request.AssessmentTypeId,
                        SectionName = request.SectionName,
                        ObtainedMarks = item.ObtainedMarks,
                        EnteredByTeacherId = userId
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Marks saved successfully." });
        }

        [HttpGet("student")]
        [RoleRequirementFilter("student")]
        public async Task<IActionResult> GetStudentAssessments()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdStr, out Guid userId)) return Unauthorized();

            var marks = await _context.StudentAssessmentMarks
                .Include(m => m.ProgramCourse).ThenInclude(pc => pc.Course)
                .Include(m => m.AssessmentType)
                .Where(m => m.StudentProfileId == userId)
                .ToListAsync();

            var grouped = marks.GroupBy(m => new { m.ProgramCourseId, m.AssessmentTypeId })
                .Select(g => new
                {
                    id = g.First().Id,
                    courseCode = g.First().ProgramCourse.Course.Code,
                    courseName = g.First().ProgramCourse.Course.Name,
                    assessmentType = g.First().AssessmentType.Name,
                    assessmentCategory = g.First().AssessmentType.Category,
                    section = g.First().SectionName,
                    // Sum of marks for all split parts
                    obtainedMarks = g.Sum(m => m.ObtainedMarks),

                    // Get total marks from config (or fallback to assessment default)
                    totalMarks = _context.CourseAssessmentConfigs
                        .Where(c => c.ProgramCourseId == g.Key.ProgramCourseId && c.AssessmentTypeId == g.Key.AssessmentTypeId)
                        .Select(c => c.CustomTotalMarks)
                        .FirstOrDefault() != 0
                        ? _context.CourseAssessmentConfigs.Where(c => c.ProgramCourseId == g.Key.ProgramCourseId && c.AssessmentTypeId == g.Key.AssessmentTypeId).Select(c => c.CustomTotalMarks).FirstOrDefault()
                        : g.First().AssessmentType.DefaultTotalMarks
                })
                .ToList();

            return Ok(grouped);
        }
    }
}