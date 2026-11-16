using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("timetables")]
    public class Timetable
    {
        [Key][Column("id")] public long Id { get; set; }
        [Column("teacher_assignment_id")] public long TeacherAssignmentId { get; set; }
        [Column("room_id")] public long RoomId { get; set; }
        [Column("day_of_week")] public string DayOfWeek { get; set; } = string.Empty;
        [Column("time_slot")] public string TimeSlot { get; set; } = string.Empty;
        [Column("created_at")] public DateTime CreatedAt { get; set; }

        [ForeignKey("TeacherAssignmentId")] public TeacherAssignment? TeacherAssignment { get; set; }
        [ForeignKey("RoomId")] public Room? Room { get; set; }
    }
}