using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UniversityPortal.API.Authorization;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProgramsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ProgramsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/programs
        [HttpGet]
        [RoleRequirementFilter("superadmin,systemadmin,department_admin")]
        public async Task<IActionResult> GetAll()
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var deptIdStr = User.FindFirst("departmentId")?.Value;

            var query = _context.Programs.AsQueryable();

            // 👇 Filter by department if the requester is a department_admin
            if (userRole == "department_admin" && long.TryParse(deptIdStr, out long deptId))
            {
                query = query.Where(p => p.DepartmentId == deptId);
            }

            var programs = await query.ToListAsync();
            return Ok(programs);
        }

        // GET: api/programs/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            var program = await _context.Programs
                .Include(p => p.Department)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (program == null) return NotFound();
            return Ok(program);
        }

        // POST: api/programs
        [HttpPost]
        [RoleRequirementFilter("superadmin")]
        // 👇 Use DegreeProgram here
        public async Task<IActionResult> Create([FromBody] DegreeProgram program)
        {
            if (string.IsNullOrWhiteSpace(program.Name) || string.IsNullOrWhiteSpace(program.Code))
                return BadRequest(new { message = "Name and Code are required." });

            var exists = await _context.Programs.AnyAsync(p => p.Code == program.Code);
            if (exists)
                return Conflict(new { message = $"Program with code '{program.Code}' already exists." });

            program.CreatedAt = DateTime.UtcNow;
            program.UpdatedAt = DateTime.UtcNow;
            program.IsActive = true;

            _context.Programs.Add(program);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = program.Id }, program);
        }

        // PUT: api/programs/5
        [HttpPut("{id}")]
        [RoleRequirementFilter("superadmin")]
        // 👇 Use DegreeProgram here
        public async Task<IActionResult> Update(long id, [FromBody] DegreeProgram updated)
        {
            var program = await _context.Programs.FindAsync(id);
            if (program == null) return NotFound();

            program.Name = updated.Name;
            program.DepartmentId = updated.DepartmentId;
            program.DegreeLevel = updated.DegreeLevel;
            program.DurationYears = updated.DurationYears;
            program.TotalCreditHours = updated.TotalCreditHours;
            program.Description = updated.Description;
            program.IsActive = updated.IsActive;
            program.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(program);
        }

        // DELETE: api/programs/5
        [HttpDelete("{id}")]
        [RoleRequirementFilter("superadmin")]
        public async Task<IActionResult> Delete(long id)
        {
            var program = await _context.Programs.FindAsync(id);
            if (program == null) return NotFound();

            _context.Programs.Remove(program);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        public class ToggleFeeVoucherRequest { public bool Enabled { get; set; } }

        [HttpPut("{id:long}/fee-voucher")]
        [RoleRequirementFilter("superadmin,systemadmin,finance_admin")]
        public async Task<IActionResult> ToggleFeeVoucher(long id, [FromBody] ToggleFeeVoucherRequest request)
        {
            var program = await _context.Programs.FindAsync(id);
            if (program == null) return NotFound(new { message = "Program not found." });

            program.FeeVoucherEnabled = request.Enabled;
            program.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Fee voucher {(request.Enabled ? "enabled" : "disabled")} for {program.Code}.",
                feeVoucherEnabled = program.FeeVoucherEnabled
            });
        }
    }
}