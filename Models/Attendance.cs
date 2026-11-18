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

        /// <summary>
        /// Legacy date column. Kept for existing reports/queries.
        /// In new code, prefer <see cref="AcademicCalendarId"/> (which uniquely
        /// identifies the lecture) and fall back to this only if the calendar
        /// row was deleted.
        /// </summary>
        [Column("date", TypeName = "date")]
        public DateTime Date { get; set; }

        [Column("status")]
        public string Status { get; set; } = string.Empty; // 'Present' or 'Absent'

        [Column("remarks")]
        public string? Remarks { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        // 👇 This is the key field that makes two lectures on the same day
        //    (e.g. Wednesday 08:00 and Wednesday 09:30) independent records.
        [Column("academic_calendar_id")]
        public long? AcademicCalendarId { get; set; }

        // Navigation properties
        [ForeignKey("StudentProfileId")]
        public Profile? StudentProfile { get; set; }

        [ForeignKey("TeacherAssignmentId")]
        public TeacherAssignment? TeacherAssignment { get; set; }

        [ForeignKey("AcademicCalendarId")]
        public AcademicCalendar? AcademicCalendar { get; set; }
    }
}