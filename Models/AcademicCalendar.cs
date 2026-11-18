using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("academic_calendar")]
    public class AcademicCalendar
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("teacher_assignment_id")]
        public long TeacherAssignmentId { get; set; }

        [Column("week_number")]
        public int WeekNumber { get; set; }

        [Column("class_number")]
        public int ClassNumber { get; set; }

        [Column("date")]
        public DateTime Date { get; set; }

        [Column("day_name")]
        public string DayName { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        // 👇 This must be TeacherAssignment, not Section
        [ForeignKey("TeacherAssignmentId")]
        public TeacherAssignment? TeacherAssignment { get; set; }

        [Column("is_attendance_allowed")]
        public bool IsAttendanceAllowed { get; set; } = true;

        [Column("time_slot")]
        public string? TimeSlot { get; set; }
    }
}