using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Authorization;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/fee-installments")]
    public class FeeInstallmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public FeeInstallmentsController(ApplicationDbContext context) => _context = context;

        public class CreateInstallmentsRequest
        {
            public int Count { get; set; }                       // 2–6
            public decimal? FirstDueDateOffsetDays { get; set; } // optional: days from now for installment 1
        }

        // GET: api/fee-installments/student-fee/{studentFeeId}
        [HttpGet("student-fee/{studentFeeId:long}")]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin,student")]
        public async Task<IActionResult> GetByStudentFee(long studentFeeId)
        {
            var fee = await _context.StudentFees.FindAsync(studentFeeId);
            if (fee == null) return NotFound(new { message = "Fee record not found." });

            var rows = await _context.FeeInstallments
                .Where(i => i.StudentFeeId == studentFeeId)
                .OrderBy(i => i.InstallmentNumber)
                .Select(i => new
                {
                    id = i.Id,
                    studentFeeId = i.StudentFeeId,
                    installmentNumber = i.InstallmentNumber,
                    amount = i.Amount,
                    dueDate = i.DueDate.ToString("yyyy-MM-dd"),
                    status = i.Status,
                    paidDate = i.PaidDate,
                    paidAmount = i.PaidAmount,
                    remarks = i.Remarks
                })
                .ToListAsync();

            return Ok(rows);
        }

        // POST: api/fee-installments/student-fee/{studentFeeId}/generate
        [HttpPost("student-fee/{studentFeeId:long}/generate")]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> Generate(long studentFeeId, [FromBody] CreateInstallmentsRequest request)
        {
            if (request.Count < 2 || request.Count > 6)
                return BadRequest(new { message = "Installment count must be between 2 and 6." });

            var fee = await _context.StudentFees.FindAsync(studentFeeId);
            if (fee == null) return NotFound(new { message = "Fee record not found." });

            if (fee.NetPayable <= 0)
                return BadRequest(new { message = "Net payable is 0. Nothing to split into installments." });

            // Remove any existing installments (re-split)
            var existing = await _context.FeeInstallments
                .Where(i => i.StudentFeeId == studentFeeId)
                .ToListAsync();
            _context.FeeInstallments.RemoveRange(existing);

            // Split net payable into N parts. Round to 2 decimals. The
            // final installment absorbs any rounding remainder.
            var total = fee.NetPayable;
            var baseAmount = Math.Round(total / request.Count, 2, MidpointRounding.ToZero);
            var offset = request.FirstDueDateOffsetDays ?? 30;
            var today = DateTime.UtcNow.Date;

            var newRows = new List<FeeInstallment>();
            decimal runningSum = 0;

            for (int n = 1; n <= request.Count; n++)
            {
                var isLast = n == request.Count;
                var amount = isLast ? total - runningSum : baseAmount;

                newRows.Add(new FeeInstallment
                {
                    StudentFeeId = studentFeeId,
                    InstallmentNumber = n,
                    Amount = amount,
                    DueDate = DateOnly.FromDateTime(today.AddDays((double)offset * n)),
                    Status = "Unpaid",
                    PaidAmount = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                runningSum += amount;
            }

            _context.FeeInstallments.AddRange(newRows);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Created {request.Count} installments for fee record {studentFeeId}.",
                total,
                perInstallment = baseAmount,
                installments = newRows.Select(r => new
                {
                    r.InstallmentNumber,
                    r.Amount,
                    dueDate = r.DueDate.ToString("yyyy-MM-dd")
                })
            });
        }

        // PUT: api/fee-installments/{id}/mark-paid
        [HttpPut("{id:long}/mark-paid")]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> MarkPaid(long id, [FromBody] MarkPaidRequest request)
        {
            var row = await _context.FeeInstallments.FindAsync(id);
            if (row == null) return NotFound(new { message = "Installment not found." });

            if (request.Paid)
            {
                row.Status = "Paid";
                row.PaidDate = DateTime.UtcNow;
                row.PaidAmount = row.Amount;
            }
            else
            {
                row.Status = "Unpaid";
                row.PaidDate = null;
                row.PaidAmount = 0;
            }

            row.Remarks = request.Remarks?.Trim();
            row.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Auto-update parent student_fee's payment_status based on installment state
            var fee = await _context.StudentFees.FindAsync(row.StudentFeeId);
            if (fee != null)
            {
                var allInstallments = await _context.FeeInstallments
                    .Where(i => i.StudentFeeId == fee.Id)
                    .ToListAsync();

                if (allInstallments.Count > 0)
                {
                    var paidCount = allInstallments.Count(i => i.Status == "Paid");
                    fee.PaymentStatus = paidCount == allInstallments.Count ? "Paid"
                                       : paidCount == 0 ? "Unpaid"
                                       : "Partial";
                    fee.PaidAmount = allInstallments.Sum(i => i.PaidAmount);
                    fee.PaymentDate = fee.PaymentStatus == "Paid" ? DateTime.UtcNow : fee.PaymentDate;
                    fee.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new { message = request.Paid ? "Marked as paid." : "Marked as unpaid." });
        }

        public class MarkPaidRequest
        {
            public bool Paid { get; set; }
            public string? Remarks { get; set; }
        }

        // DELETE: api/fee-installments/student-fee/{studentFeeId}
        [HttpDelete("student-fee/{studentFeeId:long}")]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> DeleteAll(long studentFeeId)
        {
            var rows = await _context.FeeInstallments
                .Where(i => i.StudentFeeId == studentFeeId)
                .ToListAsync();
            if (rows.Count == 0) return NotFound(new { message = "No installments found." });

            _context.FeeInstallments.RemoveRange(rows);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Installments deleted." });
        }
    }
}