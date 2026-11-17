using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("attendance")]
    public class Attendance
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("student_profile_id")]
        public Guid StudentProfileId { get; set; }

        [Column("teacher_assignment_id")]
        public long TeacherAssignmentId { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("status")]
        public string Status { get; set; } = string.Empty; // 'Present' or 'Absent'

        [Column("remarks")]
        public string? Remarks { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        // Navigation Properties
        [ForeignKey("StudentProfileId")]
        public Profile? StudentProfile { get; set; }

        [ForeignKey("TeacherAssignmentId")]
        public TeacherAssignment? TeacherAssignment { get; set; }
    }
}