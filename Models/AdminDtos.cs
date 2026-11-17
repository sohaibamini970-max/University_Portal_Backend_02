namespace UniversityPortal.API.Models
{
    public class CreateAdminRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string EmployeeId { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;   // department_admin | systemadmin | superadmin
        public long? DepartmentId { get; set; }            // required for department_admin
    }

    public class UpdateAdminRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string EmployeeId { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public long? DepartmentId { get; set; }
    }

    public class SetAdminStatusRequest
    {
        public bool IsActive { get; set; }
    }

    public class ResetAdminPasswordRequest
    {
        public string Password { get; set; } = string.Empty;
    }

    // Flat projection used by the controller. Email is locked after creation.
    public class AdminRecord
    {
        public Guid Id { get; set; }
        public Guid ProfileId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? DepartmentType { get; set; }
        public string? Phone { get; set; }
        public string EmployeeId { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public ProfileRole Role { get; set; }
        public bool IsActive { get; set; }
        public long? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}