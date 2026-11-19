using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("student_fees")]
    public class StudentFee
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("student_profile_id")]
        public Guid StudentProfileId { get; set; }

        [Column("fee_due_date")]
        public DateOnly? FeeDueDate { get; set; }

        [Column("program_id")]
        public long ProgramId { get; set; }

        [Column("batch_id")]
        public long BatchId { get; set; }

        [Column("semester_id")]
        public long SemesterId { get; set; }

        [Column("credit_hours_taken")]
        public int CreditHoursTaken { get; set; }

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

        [Column("other_fee")]
        public decimal OtherFee { get; set; }

        [Column("gross_total")]
        public decimal GrossTotal { get; set; }

        [Column("discount_amount")]
        public decimal DiscountAmount { get; set; }

        [Column("discount_reason")]
        public string? DiscountReason { get; set; }

        [Column("net_payable")]
        public decimal NetPayable { get; set; }

        [Column("payment_status")]
        public string PaymentStatus { get; set; } = "Unpaid";

        [Column("paid_amount")]
        public decimal PaidAmount { get; set; }

        [Column("payment_date")]
        public DateTime? PaymentDate { get; set; }

        [Column("payment_reference")]
        public string? PaymentReference { get; set; }

        [Column("admit_card_enabled")]
        public bool AdmitCardEnabled { get; set; }

        [Column("generated_at")]
        public DateTime GeneratedAt { get; set; }

        [Column("updated_by")]
        public Guid? UpdatedBy { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        [ForeignKey("StudentProfileId")]
        public Profile? StudentProfile { get; set; }

        [ForeignKey("ProgramId")]
        public DegreeProgram? Program { get; set; }

        [ForeignKey("SemesterId")]
        public Semester? Semester { get; set; }

        [ForeignKey("BatchId")]
        public Batch? Batch { get; set; }

        // In StudentFee.cs
        public ICollection<FeeInstallment> Installments { get; set; } = new List<FeeInstallment>();
    }
}