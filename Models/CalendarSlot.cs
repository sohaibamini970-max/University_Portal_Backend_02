using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("calendar_slots")]
    public class CalendarSlot
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("academic_calendar_id")]
        public long AcademicCalendarId { get; set; }

        [Column("teacher_assignment_id")]
        public long TeacherAssignmentId { get; set; }

        // Navigation Properties
        [ForeignKey("AcademicCalendarId")]
        public AcademicCalendar? AcademicCalendar { get; set; }

        [ForeignKey("TeacherAssignmentId")]
        public TeacherAssignment? TeacherAssignment { get; set; }
    }
}