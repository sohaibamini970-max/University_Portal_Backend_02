using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("assessment_types")]
    public class AssessmentTypes // Has the 's' as requested
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("category")]
        public string Category { get; set; } = string.Empty;
        [Column("is_split_allowed")]
        public bool IsSplitAllowed { get; set; }

        [Column("default_total_marks")]
        public decimal DefaultTotalMarks { get; set; } // 👈 This is the correct property name

        [Column("is_fixed")]
        public bool IsFixed { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}