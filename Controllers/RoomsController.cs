using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Data;
using UniversityPortal.API.Models;
using UniversityPortal.API.Authorization;
using System.Security.Claims;

namespace UniversityPortal.API.Controllers
{
    [ApiController]
    [Route("api/rooms")]
    public class RoomsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public RoomsController(ApplicationDbContext context) { _context = context; }

        public class RoomRequest
        {
            public string RoomName { get; set; } = string.Empty;
            public string RoomType { get; set; } = string.Empty;
        }

        // 👇 ADDED GET ENDPOINT HERE 👇
        [HttpGet]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> GetRooms()
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var deptIdStr = User.FindFirst("departmentId")?.Value;

            var query = _context.Rooms.Include(r => r.Department).AsQueryable();

            // Filter by department if the requester is a department_admin
            if (userRole == "department_admin" && long.TryParse(deptIdStr, out long deptId))
            {
                query = query.Where(r => r.DepartmentId == deptId);
            }

            var rooms = await query.Select(r => new {
                id = r.Id,
                roomNumber = r.RoomNumber,
                roomType = r.RoomType,
                building = r.Department.Building,
                departmentName = r.Department.Name
            }).ToListAsync();

            return Ok(rooms);
        }

        // POST: api/rooms
        [HttpPost]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> CreateRoom([FromBody] RoomRequest request)
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var deptIdStr = User.FindFirst("departmentId")?.Value;

            long departmentId;
            if (userRole == "department_admin" && long.TryParse(deptIdStr, out long adminDeptId))
            {
                departmentId = adminDeptId;
            }
            else
            {
                return BadRequest(new { message = "Only Department Admins can create rooms for their department." });
            }

            var dept = await _context.Departments.FindAsync(departmentId);
            if (dept == null) return NotFound(new { message = "Department not found." });

            if (string.IsNullOrEmpty(request.RoomName) || !request.RoomName.StartsWith(dept.Building, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = $"Room name must start with your department's building code: '{dept.Building}'" });
            }

            var exists = await _context.Rooms.AnyAsync(r => r.RoomNumber.ToLower() == request.RoomName.ToLower() && r.DepartmentId == departmentId);
            if (exists)
            {
                return BadRequest(new { message = $"Room '{request.RoomName}' already exists in your department." });
            }

            var room = new Room
            {
                DepartmentId = departmentId,
                RoomNumber = request.RoomName.ToUpper(),
                RoomType = request.RoomType,
                CreatedAt = DateTime.UtcNow
            };

            _context.Rooms.Add(room);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Room created successfully.", name = room.RoomNumber });
        }

        // PUT: api/rooms/{id}
        [HttpPut("{id}")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> UpdateRoom(long id, [FromBody] RoomRequest request)
        {
            var room = await _context.Rooms.Include(r => r.Department).FirstOrDefaultAsync(r => r.Id == id);
            if (room == null) return NotFound(new { message = "Room not found." });

            if (!request.RoomName.StartsWith(room.Department.Building, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = $"Room name must start with building code: '{room.Department.Building}'" });
            }

            var exists = await _context.Rooms.AnyAsync(r => r.Id != id && r.RoomNumber.ToLower() == request.RoomName.ToLower() && r.DepartmentId == room.DepartmentId);
            if (exists)
            {
                return BadRequest(new { message = $"Room '{request.RoomName}' already exists." });
            }

            room.RoomNumber = request.RoomName.ToUpper();
            room.RoomType = request.RoomType;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Room updated successfully." });
        }

        // 👇 ADDED DELETE ENDPOINT HERE 👇
        [HttpDelete("{id}")]
        [RoleRequirementFilter("department_admin,superadmin,systemadmin")]
        public async Task<IActionResult> DeleteRoom(long id)
        {
            var room = await _context.Rooms.FindAsync(id);
            if (room == null) return NotFound(new { message = "Room not found." });

            _context.Rooms.Remove(room);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Room deleted successfully." });
        }
    }
}