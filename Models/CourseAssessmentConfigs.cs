using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("course_assessment_configs")]
    public class CourseAssessmentConfigs // 👈 Has 's'
    {
        [Key]
        public long Id { get; set; }
        public long ProgramCourseId { get; set; }
        public ProgramCourse? ProgramCourse { get; set; }
        public long AssessmentTypeId { get; set; }
        public AssessmentTypes? AssessmentType { get; set; }
        public decimal CustomTotalMarks { get; set; }
        public bool IsEnabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}