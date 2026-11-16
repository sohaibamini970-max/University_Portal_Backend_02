using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("time_slots")]
    public class TimeSlot
    {
        [Key][Column("id")] public long Id { get; set; }
        [Column("department_id")] public long DepartmentId { get; set; }
        [Column("start_time")] public string StartTime { get; set; } = string.Empty;
        [Column("end_time")] public string EndTime { get; set; } = string.Empty;
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}