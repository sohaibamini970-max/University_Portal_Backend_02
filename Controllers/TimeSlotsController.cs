using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;
using System.Security.Claims;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/time-slots")]
    public class TimeSlotsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public TimeSlotsController(ApplicationDbContext context) { _context = context; }

        public class TimeSlotRequest
        {
            public string StartTime { get; set; } = string.Empty;
            public string EndTime { get; set; } = string.Empty;
        }

        // 👇 ADD THIS GET ENDPOINT 👇
        [HttpGet]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin,student,teacher")]
        public async Task<IActionResult> GetSlots()
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var deptIdStr = User.FindFirst("departmentId")?.Value;

            var query = _context.TimeSlots.AsQueryable();

            // If it's a department_admin, filter by their department.
            // Students/Teachers/Admins will see all time slots (or we could filter by their department too).
            if (userRole == "department_admin" && long.TryParse(deptIdStr, out long deptId))
            {
                query = query.Where(s => s.DepartmentId == deptId);
            }

            var slots = await query
                .OrderBy(s => s.StartTime)
                .Select(s => new { id = s.Id, startTime = s.StartTime, endTime = s.EndTime })
                .ToListAsync();

            return Ok(slots);
        }

        // POST: api/time-slots
        [HttpPost]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> AddSlot([FromBody] TimeSlotRequest request)
        {
            var deptIdStr = User.FindFirst("departmentId")?.Value;
            if (!long.TryParse(deptIdStr, out long deptId)) return Unauthorized();

            var exists = await _context.TimeSlots.AnyAsync(s =>
                s.DepartmentId == deptId &&
                s.StartTime == request.StartTime &&
                s.EndTime == request.EndTime);

            if (exists) return BadRequest(new { message = "This time slot already exists." });

            var slot = new TimeSlot
            {
                DepartmentId = deptId,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                CreatedAt = DateTime.UtcNow
            };

            _context.TimeSlots.Add(slot);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Slot added successfully." });
        }

        // DELETE: api/time-slots/{id}
        [HttpDelete("{id}")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> DeleteSlot(long id)
        {
            var slot = await _context.TimeSlots.FindAsync(id);
            if (slot == null) return NotFound();

            _context.TimeSlots.Remove(slot);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Slot removed." });
        }
    }
}