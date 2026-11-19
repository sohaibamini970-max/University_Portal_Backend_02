using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniversityPortal.API.Authorization;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/student-fees")]
    public class StudentFeesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public StudentFeesController(ApplicationDbContext context) => _context = context;

        public class GenerateFeesRequest
        {
            public long ProgramId { get; set; }
            public long BatchId { get; set; }
            public long SemesterId { get; set; }
        }

        public class UpdateStudentFeeRequest
        {
            public decimal DiscountAmount { get; set; }
            public string? DiscountReason { get; set; }
            public string PaymentStatus { get; set; } = "Unpaid";
            public bool AdmitCardEnabled { get; set; }
        }

        // GET: api/student-fees/my
        // Returns the logged-in student's current/latest fee record with full breakdown.
        [HttpGet("my")]
        [RoleRequirementFilter("student")]
        public async Task<IActionResult> GetMyFee([FromQuery] long? semesterId = null)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

            var student = await _context.Students
                .Include(s => s.Profile)
                .Include(s => s.Program)
                .Include(s => s.Batch)
                .FirstOrDefaultAsync(s => s.ProfileId == userId);

            if (student.Program == null || !student.Program.FeeVoucherEnabled)
                return NotFound(new { message = "Fee vouchers are not enabled for your program. Please contact the finance office." });

            if (student == null) return NotFound(new { message = "Student record not found." });

            // Pick the specific semester if requested, otherwise the latest fee record
            var query = _context.StudentFees
                .Include(f => f.Semester)
                .Include(f => f.Program)
                .Where(f => f.StudentProfileId == userId);

            if (semesterId.HasValue)
                query = query.Where(f => f.SemesterId == semesterId.Value);

            var fee = await query
                .OrderByDescending(f => f.SemesterId)
                .FirstOrDefaultAsync();

            if (fee == null)
                return NotFound(new { message = "No fee record generated for this student yet." });

            // In GetMyFee, after fetching `fee`, before building the response:
            var installments = await _context.FeeInstallments
                .Where(i => i.StudentFeeId == fee.Id)
                .OrderBy(i => i.InstallmentNumber)
                .Select(i => new
                {
                    id = i.Id,
                    installmentNumber = i.InstallmentNumber,
                    amount = i.Amount,
                    dueDate = i.DueDate.ToString("yyyy-MM-dd"),
                    status = i.Status,
                    paidDate = i.PaidDate,
                    paidAmount = i.PaidAmount
                })
                .ToListAsync();

            // Full breakdown for the voucher
            var response = new
            {
                student = new
                {
                    fullName = student.Profile!.FullName,
                    rollNo = student.Profile.RollNo,
                    email = student.Profile.Email,
                    programName = student.Program != null ? student.Program.Name : null,
                    batchName = student.Batch != null ? student.Batch.Name : null,
                    section = student.Section
                },
                fee = new
                {
                    id = fee.Id,
                    semesterId = fee.SemesterId,
                    semesterName = fee.Semester != null ? fee.Semester.Name : null,
                    feeDueDate = fee.FeeDueDate != null ? fee.FeeDueDate.Value.ToString("yyyy-MM-dd") : null,
                    creditHoursTaken = fee.CreditHoursTaken,
                    creditHourFee = fee.CreditHourFee,
                    creditHoursTotal = fee.CreditHoursTaken * fee.CreditHourFee,

                    admissionFee = fee.AdmissionFee,
                    registrationFee = fee.RegistrationFee,
                    labFee = fee.LabFee,
                    activityFee = fee.ActivityFee,
                    examFee = fee.ExamFee,
                    otherFee = fee.OtherFee,

                    grossTotal = fee.GrossTotal,
                    discountAmount = fee.DiscountAmount,
                    discountReason = fee.DiscountReason,
                    netPayable = fee.NetPayable,

                    pendingAmount = Math.Max(0, fee.NetPayable - fee.PaidAmount),

                    paymentStatus = fee.PaymentStatus,
                    paidAmount = fee.PaidAmount,
                    paymentDate = fee.PaymentDate,
                    admitCardEnabled = fee.AdmitCardEnabled,

                    generatedAt = fee.GeneratedAt
                },
                installments
            };

            return Ok(response);
        }

        // GET: api/student-fees?page=1&pageSize=10&search=ADM-2025-001&status=Unpaid&programId=1&batchId=1&semesterId=1
        [HttpGet]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null,
            [FromQuery] long? programId = null,
            [FromQuery] long? batchId = null,
            [FromQuery] long? semesterId = null)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.StudentFees
                .Include(f => f.StudentProfile)
                .Include(f => f.Program)
                .Include(f => f.Semester)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(f =>
                    f.StudentProfile!.FullName.ToLower().Contains(term) ||
                    (f.StudentProfile.RollNo != null && f.StudentProfile.RollNo.ToLower().Contains(term)) ||
                    f.StudentProfile.Email.ToLower().Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(f => f.PaymentStatus == status);
            if (programId.HasValue) query = query.Where(f => f.ProgramId == programId.Value);
            if (batchId.HasValue) query = query.Where(f => f.BatchId == batchId.Value);
            if (semesterId.HasValue) query = query.Where(f => f.SemesterId == semesterId.Value);

            var total = await query.CountAsync();

            var items = await query
                .OrderBy(f => f.StudentProfile!.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new
                {
                    id = f.Id,
                    studentProfileId = f.StudentProfileId,
                    fullName = f.StudentProfile!.FullName,
                    rollNo = f.StudentProfile.RollNo,
                    programName = f.Program != null ? f.Program.Name : null,
                    semesterName = f.Semester != null ? f.Semester.Name : null,
                    creditHoursTaken = f.CreditHoursTaken,
                    creditHourFee = f.CreditHourFee,
                    admissionFee = f.AdmissionFee,
                    registrationFee = f.RegistrationFee,
                    labFee = f.LabFee,
                    activityFee = f.ActivityFee,
                    examFee = f.ExamFee,
                    otherFee = f.OtherFee,
                    grossTotal = f.GrossTotal,
                    discountAmount = f.DiscountAmount,
                    discountReason = f.DiscountReason,
                    netPayable = f.NetPayable,
                    paymentStatus = f.PaymentStatus,
                    paidAmount = f.PaidAmount,
                    admitCardEnabled = f.AdmitCardEnabled
                })
                .ToListAsync();

            return Ok(new
            {
                items,
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }

        // GET: api/student-fees/{id}
        [HttpGet("{id:long}")]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> GetById(long id)
        {
            var fee = await _context.StudentFees
                .Include(f => f.StudentProfile)
                .Include(f => f.Program)
                .Include(f => f.Semester)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (fee == null) return NotFound();
            return Ok(fee);
        }

        // POST: api/student-fees/generate
        // Generates fee records for all students in a program/batch/semester
        [HttpPost("generate")]
        [RoleRequirementFilter("superadmin,finance_admin")]
        public async Task<IActionResult> Generate([FromBody] GenerateFeesRequest request)
        {
            var structure = await _context.FeeStructures
                .FirstOrDefaultAsync(f => f.ProgramId == request.ProgramId && f.BatchId == request.BatchId);

            if (structure == null)
                return BadRequest(new { message = "No fee structure configured for this program/batch. Set rates first." });

            var isFirstSemester = request.SemesterId == 1;

            // Students in that program/batch/semester
            var students = await _context.Students
                .Include(s => s.Profile)
                .Where(s => s.ProgramId == request.ProgramId && s.BatchId == request.BatchId)
                .ToListAsync();

            if (students.Count == 0)
                return BadRequest(new { message = "No students found for this program/batch." });

            var semIsFirst = (await _context.Semesters.FindAsync(request.SemesterId))?.Id == 1;

            int created = 0, updated = 0;

            foreach (var student in students)
            {
                // Sum credit hours of courses the student is registered in this semester
                var creditHours = await _context.StudentRegistrations
                    .Where(r => r.StudentProfileId == student.ProfileId
                             && r.ProgramCourse!.SemesterId == request.SemesterId)
                    .SumAsync(r => (int?)r.ProgramCourse!.Course!.CreditHours) ?? 0;

                // Skip if no courses -> cannot compute fee
                if (creditHours == 0) continue;

                // Discount is preserved from existing record if there is one
                var existing = await _context.StudentFees
                    .FirstOrDefaultAsync(f => f.StudentProfileId == student.ProfileId
                                           && f.SemesterId == request.SemesterId);

                var discount = existing?.DiscountAmount ?? 0;
                var discountReason = existing?.DiscountReason;
                var status = existing?.PaymentStatus ?? "Unpaid";
                var paidAmount = existing?.PaidAmount ?? 0;
                var admitCard = existing?.AdmitCardEnabled ?? false;
                var paymentDate = existing?.PaymentDate;
                var paymentRef = existing?.PaymentReference;

                var admissionFee = semIsFirst ? structure.AdmissionFee : 0;
                var gross = (creditHours * structure.CreditHourFee)
                          + admissionFee
                          + structure.RegistrationFee
                          + structure.LabFee
                          + structure.ActivityFee
                          + structure.ExamFee
                          + structure.OtherFee;

                var net = Math.Max(0, gross - discount);

                if (existing == null)
                {
                    _context.StudentFees.Add(new StudentFee
                    {
                        StudentProfileId = student.ProfileId,
                        ProgramId = request.ProgramId,
                        BatchId = request.BatchId,
                        SemesterId = request.SemesterId,
                        CreditHoursTaken = creditHours,
                        CreditHourFee = structure.CreditHourFee,
                        AdmissionFee = admissionFee,
                        RegistrationFee = structure.RegistrationFee,
                        LabFee = structure.LabFee,
                        ActivityFee = structure.ActivityFee,
                        ExamFee = structure.ExamFee,
                        OtherFee = structure.OtherFee,
                        GrossTotal = gross,
                        DiscountAmount = 0,
                        NetPayable = gross,
                        PaymentStatus = "Unpaid",
                        AdmitCardEnabled = false,
                        GeneratedAt = DateTime.UtcNow,
                        FeeDueDate = structure.FeeDueDate,
                        UpdatedAt = DateTime.UtcNow
                    });
                    created++;
                }
                else
                {
                    existing.CreditHoursTaken = creditHours;
                    existing.CreditHourFee = structure.CreditHourFee;
                    existing.AdmissionFee = admissionFee;
                    existing.RegistrationFee = structure.RegistrationFee;
                    existing.LabFee = structure.LabFee;
                    existing.ActivityFee = structure.ActivityFee;
                    existing.ExamFee = structure.ExamFee;
                    existing.OtherFee = structure.OtherFee;
                    existing.GrossTotal = gross;
                    existing.DiscountAmount = discount;
                    existing.DiscountReason = discountReason;
                    existing.NetPayable = net;
                    existing.PaymentStatus = status;
                    existing.PaidAmount = paidAmount;
                    existing.PaymentDate = paymentDate;
                    existing.PaymentReference = paymentRef;
                    existing.AdmitCardEnabled = admitCard;
                    existing.FeeDueDate = structure.FeeDueDate;
                    existing.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Generated {created} new, updated {updated} existing fee records." });
        }

        // PUT: api/student-fees/{id}
        [HttpPut("{id:long}")]
        [RoleRequirementFilter("superadmin,finance_admin")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateStudentFeeRequest request)
        {
            var fee = await _context.StudentFees.FindAsync(id);
            if (fee == null) return NotFound();

            if (request.DiscountAmount < 0 || request.DiscountAmount > fee.GrossTotal)
                return BadRequest(new { message = "Discount must be between 0 and gross total." });

            if (request.PaymentStatus != "Paid" && request.PaymentStatus != "Unpaid" && request.PaymentStatus != "Partial")
                return BadRequest(new { message = "Invalid payment status." });

            // Business rule: cannot enable admit card if unpaid
            var admitCard = request.AdmitCardEnabled && request.PaymentStatus == "Paid";

            Guid? userId = null;
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(userIdStr, out var parsed)) userId = parsed;

            fee.DiscountAmount = request.DiscountAmount;
            fee.DiscountReason = request.DiscountReason?.Trim();
            fee.NetPayable = Math.Max(0, fee.GrossTotal - request.DiscountAmount);
            fee.PaymentStatus = request.PaymentStatus;
            fee.AdmitCardEnabled = admitCard;
            fee.PaidAmount = request.PaymentStatus == "Paid" ? fee.NetPayable : (request.PaymentStatus == "Partial" ? fee.PaidAmount : 0);
            fee.PaymentDate = request.PaymentStatus == "Paid" ? DateTime.UtcNow : fee.PaymentDate;
            fee.UpdatedBy = userId;
            fee.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Fee record updated.", netPayable = fee.NetPayable });
        }
    }
}