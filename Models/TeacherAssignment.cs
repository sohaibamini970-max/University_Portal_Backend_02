using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("teacher_assignments")]
    public class TeacherAssignment
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("teacher_id")]
        public Guid TeacherId { get; set; }

        [Column("program_course_id")]
        public long ProgramCourseId { get; set; }

        [Column("section_name")]
        public string SectionName { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        // Navigation Properties
        [ForeignKey("TeacherId")]
        public Teacher? Teacher { get; set; }

        [ForeignKey("ProgramCourseId")]
        public ProgramCourse? ProgramCourse { get; set; }

        [Column("is_attendance_locked")]
        public bool IsAttendanceLocked { get; set; } = false;
    }
}