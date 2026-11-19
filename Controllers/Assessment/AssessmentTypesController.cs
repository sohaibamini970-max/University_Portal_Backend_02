using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/assessment-types")]
    public class AssessmentTypesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public AssessmentTypesController(ApplicationDbContext context) => _context = context;

        public class ConfigRequest
        {
            public long ProgramCourseId { get; set; }
            public List<TypeConfig> Configs { get; set; } = new();
        }

        public class TypeConfig
        {
            public long AssessmentTypeId { get; set; }
            public decimal CustomTotalMarks { get; set; }
            public bool IsEnabled { get; set; }
        }

        // GET: api/assessment-types/course/{programCourseId}
        // GET: api/assessment-types/course/{programCourseId}
        [HttpGet("course/{programCourseId}")]
        [RoleRequirementFilter("department_admin,systemadmin,teacher")]
        public async Task<IActionResult> GetCourseConfig(long programCourseId)
        {
            // 1. Get the ProgramCourse to check the Course's credit hours
            var programCourse = await _context.ProgramCourses
                .Include(pc => pc.Course)
                .FirstOrDefaultAsync(pc => pc.Id == programCourseId);

            if (programCourse == null || programCourse.Course == null)
                return NotFound(new { message = "Course not found." });

            var creditHours = programCourse.Course.CreditHours;

            // 2. Fetch all assessment types
            var types = await _context.AssessmentTypes.ToListAsync();

            // 3. Filter based on Theory vs Lab
            if (creditHours <= 1)
            {
                // It's a Lab course: Only show Lab assessments
                types = types.Where(t => t.Category == "Lab").ToList();
            }
            else
            {
                // It's a Theory course: Show everything EXCEPT Lab assessments
                types = types.Where(t => t.Category != "Lab").ToList();
            }

            var configs = await _context.CourseAssessmentConfigs
                .Where(c => c.ProgramCourseId == programCourseId)
                .ToListAsync();

            var result = types.Select(t => {
                var config = configs.FirstOrDefault(c => c.AssessmentTypeId == t.Id);
                return new
                {
                    id = t.Id,
                    name = t.Name,
                    category = t.Category,
                    defaultMarks = t.DefaultTotalMarks,
                    isFixed = t.IsFixed,
                    isSplitAllowed = t.IsSplitAllowed, // Needed for Mid/Final splitting
                    customTotalMarks = config?.CustomTotalMarks ?? t.DefaultTotalMarks,
                    isEnabled = config?.IsEnabled ?? true
                };
            });

            return Ok(result);
        }

        // POST: api/assessment-types/course/config
        [HttpPost("course/config")]
        [RoleRequirementFilter("department_admin")]
        public async Task<IActionResult> SaveCourseConfig([FromBody] ConfigRequest request)
        {
            var existingConfigs = await _context.CourseAssessmentConfigs
                .Where(c => c.ProgramCourseId == request.ProgramCourseId)
                .ToListAsync();

            foreach (var item in request.Configs)
            {
                var config = existingConfigs.FirstOrDefault(c => c.AssessmentTypeId == item.AssessmentTypeId);
                if (config != null)
                {
                    config.CustomTotalMarks = item.CustomTotalMarks;
                    config.IsEnabled = item.IsEnabled;
                }
                else
                {
                    _context.CourseAssessmentConfigs.Add(new CourseAssessmentConfigs
                    {
                        ProgramCourseId = request.ProgramCourseId,
                        AssessmentTypeId = item.AssessmentTypeId,
                        CustomTotalMarks = item.CustomTotalMarks,
                        IsEnabled = item.IsEnabled
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Assessment settings saved." });
        }
    }
}