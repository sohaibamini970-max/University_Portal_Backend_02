using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UniversityPortal.API.Models
{
    [Table("fee_installments")]
    public class FeeInstallment
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // 👇 Explicitly tell EF this is the Foreign Key
        [Column("student_fee_id")]
        public long StudentFeeId { get; set; }

        [Column("installment_number")]
        public int InstallmentNumber { get; set; }

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("due_date")]
        public DateOnly DueDate { get; set; }

        [Column("status")]
        public string Status { get; set; } = "Unpaid";

        [Column("paid_date")]
        public DateTime? PaidDate { get; set; }

        [Column("paid_amount")]
        public decimal PaidAmount { get; set; }

        [Column("remarks")]
        public string? Remarks { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        // 👇 Link the navigation back to StudentFee
        [ForeignKey(nameof(StudentFeeId))]
        public StudentFee? StudentFee { get; set; }
    }
}