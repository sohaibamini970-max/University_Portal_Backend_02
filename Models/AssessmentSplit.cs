using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("assessment_splits")]
    public class AssessmentSplit
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("program_course_id")]
        public long ProgramCourseId { get; set; }

        [ForeignKey("ProgramCourseId")]
        public ProgramCourse? ProgramCourse { get; set; }

        [Column("assessment_type_id")]
        public long AssessmentTypeId { get; set; }

        [ForeignKey("AssessmentTypeId")]
        public AssessmentTypes? AssessmentType { get; set; }

        [Column("part_name")]
        public string PartName { get; set; } = string.Empty;

        [Column("max_marks")]
        public decimal MaxMarks { get; set; }
    }
}