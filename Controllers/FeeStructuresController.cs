using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniversityPortal.API.Authorization;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/fee-structures")]
    public class FeeStructuresController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FeeStructuresController(ApplicationDbContext context) => _context = context;

        public class FeeStructureUpsertRequest
        {
            public long ProgramId { get; set; }
            public long BatchId { get; set; }
            public decimal CreditHourFee { get; set; }
            public decimal AdmissionFee { get; set; }
            public decimal RegistrationFee { get; set; }
            public decimal LabFee { get; set; }
            public decimal ActivityFee { get; set; }
            public decimal ExamFee { get; set; }
            public decimal OtherFee { get; set; }
            public string? FeeDueDate { get; set; }
        }

        // GET: api/fee-structures?programId=&batchId=
        [HttpGet]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> GetAll([FromQuery] long? programId, [FromQuery] long? batchId)
        {
            var query = _context.FeeStructures
                .Include(f => f.Program)
                .Include(f => f.Batch)
                .AsQueryable();

            if (programId.HasValue) query = query.Where(f => f.ProgramId == programId.Value);
            if (batchId.HasValue) query = query.Where(f => f.BatchId == batchId.Value);

            var rows = await query
                .OrderBy(f => f.Program!.Name)
                .ThenBy(f => f.Batch!.Name)
                .Select(f => new
                {
                    id = f.Id,
                    programId = f.ProgramId,
                    programCode = f.Program != null ? f.Program.Code : null,
                    programName = f.Program != null ? f.Program.Name : null,
                    batchId = f.BatchId,
                    batchName = f.Batch != null ? f.Batch.Name : null,
                    creditHourFee = f.CreditHourFee,
                    admissionFee = f.AdmissionFee,
                    registrationFee = f.RegistrationFee,
                    labFee = f.LabFee,
                    activityFee = f.ActivityFee,
                    examFee = f.ExamFee,
                    otherFee = f.OtherFee,
                    feeDueDate = f.FeeDueDate != null ? f.FeeDueDate.Value.ToString("yyyy-MM-dd") : null,
                    feeVoucherEnabled = f.Program != null && f.Program.FeeVoucherEnabled,  // ✅ add this
                    updatedAt = f.UpdatedAt
                })
                .ToListAsync();

            return Ok(rows);
        }

        // POST: api/fee-structures  (upsert)
        [HttpPost]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> Upsert([FromBody] FeeStructureUpsertRequest request)
        {
            if (request.CreditHourFee < 0 || request.AdmissionFee < 0 ||
                request.RegistrationFee < 0 || request.LabFee < 0 ||
                request.ActivityFee < 0 || request.ExamFee < 0 || request.OtherFee < 0)
                return BadRequest(new { message = "Fee amounts cannot be negative." });

            var programExists = await _context.Programs.AnyAsync(p => p.Id == request.ProgramId);
            var batchExists = await _context.Batches.AnyAsync(b => b.Id == request.BatchId);

            DateOnly? parsedDueDate = null;
            if (!string.IsNullOrEmpty(request.FeeDueDate) && DateOnly.TryParse(request.FeeDueDate, out var d))
                parsedDueDate = d;

            if (!programExists || !batchExists)
                return BadRequest(new { message = "Invalid program or batch." });

            Guid? userId = null;
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(userIdStr, out var parsed)) userId = parsed;

            var existing = await _context.FeeStructures
                .FirstOrDefaultAsync(f => f.ProgramId == request.ProgramId && f.BatchId == request.BatchId);

            if (existing == null)
            {
                _context.FeeStructures.Add(new FeeStructure
                {
                    ProgramId = request.ProgramId,
                    BatchId = request.BatchId,
                    CreditHourFee = request.CreditHourFee,
                    AdmissionFee = request.AdmissionFee,
                    RegistrationFee = request.RegistrationFee,
                    LabFee = request.LabFee,
                    ActivityFee = request.ActivityFee,
                    ExamFee = request.ExamFee,
                    OtherFee = request.OtherFee,
                    UpdatedBy = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    FeeDueDate = parsedDueDate,
                });
            }
            else
            {
                existing.CreditHourFee = request.CreditHourFee;
                existing.AdmissionFee = request.AdmissionFee;
                existing.RegistrationFee = request.RegistrationFee;
                existing.LabFee = request.LabFee;
                existing.ActivityFee = request.ActivityFee;
                existing.ExamFee = request.ExamFee;
                existing.OtherFee = request.OtherFee;
                existing.UpdatedBy = userId;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.FeeDueDate = parsedDueDate;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Fee structure saved." });
        }

        // DELETE: api/fee-structures/{id}
        [HttpDelete("{id:long}")]
        [RoleRequirementFilter("superadmin,finance_admin")]
        public async Task<IActionResult> Delete(long id)
        {
            var row = await _context.FeeStructures.FindAsync(id);
            if (row == null) return NotFound();

            // Block if any student_fees row already references this program+batch
            var inUse = await _context.StudentFees
                .AnyAsync(f => f.ProgramId == row.ProgramId && f.BatchId == row.BatchId);
            if (inUse)
                return Conflict(new { message = "Cannot delete: students already have fee records for this program/batch." });

            _context.FeeStructures.Remove(row);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Fee structure deleted." });
        }
    }
}