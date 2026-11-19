using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("fee_structures")]
    public class FeeStructure
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("program_id")]
        public long ProgramId { get; set; }

        [Column("batch_id")]
        public long BatchId { get; set; }

        [Column("credit_hour_fee")]
        public decimal CreditHourFee { get; set; }

        [Column("admission_fee")]
        public decimal AdmissionFee { get; set; }

        [Column("registration_fee")]
        public decimal RegistrationFee { get; set; }

        [Column("lab_fee")]
        public decimal LabFee { get; set; }

        [Column("activity_fee")]
        public decimal ActivityFee { get; set; }

        [Column("exam_fee")]
        public decimal ExamFee { get; set; }

        [Column("fee_due_date")]
        public DateOnly? FeeDueDate { get; set; }

        [Column("other_fee")]
        public decimal OtherFee { get; set; }

        [Column("updated_by")]
        public Guid? UpdatedBy { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [ForeignKey("ProgramId")]
        public DegreeProgram? Program { get; set; }

        [ForeignKey("BatchId")]
        public Batch? Batch { get; set; }
    }
}