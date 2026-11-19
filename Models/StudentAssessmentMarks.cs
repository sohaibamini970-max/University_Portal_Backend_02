using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("student_assessment_marks")]
    public class StudentAssessmentMarks
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("student_profile_id")]
        public Guid StudentProfileId { get; set; }

        [ForeignKey("StudentProfileId")]
        public Profile? StudentProfile { get; set; }

        [Column("program_course_id")]
        public long ProgramCourseId { get; set; }

        [ForeignKey("ProgramCourseId")]
        public ProgramCourse? ProgramCourse { get; set; }

        [Column("assessment_type_id")]
        public long AssessmentTypeId { get; set; }

        [ForeignKey("AssessmentTypeId")]
        public AssessmentTypes? AssessmentType { get; set; }

        [Column("section_name")]
        public string SectionName { get; set; } = string.Empty;

        [Column("obtained_marks")]
        public decimal ObtainedMarks { get; set; }

        [Column("entered_by_teacher_id")]
        public Guid EnteredByTeacherId { get; set; }

        [Column("entered_at")]
        public DateTime EnteredAt { get; set; } = DateTime.UtcNow;

        // 👇 ADD THIS NEW PROPERTY 👇
        [Column("split_id")]
        public long? SplitId { get; set; }

        [ForeignKey("SplitId")]
        public AssessmentSplit? Split { get; set; }
    }
}