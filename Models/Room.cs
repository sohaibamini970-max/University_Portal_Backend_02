using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("rooms")]
    public class Room
    {
        [Key][Column("id")] public long Id { get; set; }
        [Column("department_id")] public long DepartmentId { get; set; }
        [Column("room_number")] public string RoomNumber { get; set; } = string.Empty;
        [Column("room_type")] public string RoomType { get; set; } = string.Empty;
        [Column("created_at")] public DateTime CreatedAt { get; set; }

        [ForeignKey("DepartmentId")]
        public Department? Department { get; set; }
    }
}