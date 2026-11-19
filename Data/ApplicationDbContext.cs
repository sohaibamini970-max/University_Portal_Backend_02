using Microsoft.EntityFrameworkCore;
using UniversityPortal.API.Models;

namespace UniversityPortal.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        // Existing DbSets
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<DegreeProgram> Programs { get; set; }
        public DbSet<Teacher> Teachers { get; set; }
        public DbSet<Batch> Batches { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<ProgramCourse> ProgramCourses { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<Semester> Semesters { get; set; }
        public DbSet<ProgramSessionSemester> ProgramSessionSemesters { get; set; }
        public DbSet<TeacherAssignment> TeacherAssignments { get; set; }
        public DbSet<Room> Rooms { get; set; }
        public DbSet<TimeSlot> TimeSlots { get; set; }

        public DbSet<Student> Students { get; set; }
        public DbSet<Section> Sections { get; set; }
        public DbSet<StudentRegistration> StudentRegistrations { get; set; }
        public DbSet<Timetable> Timetables { get; set; }
        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<AcademicCalendar> AcademicCalendars { get; set; }

        public DbSet<CalendarSlot> CalendarSlots { get; set; }
        public DbSet<Admin> Admins => Set<Admin>();
        public DbSet<FeeStructure> FeeStructures { get; set; }
        public DbSet<StudentFee> StudentFees { get; set; }
        public DbSet<AssessmentSplit> AssessmentSplits { get; set; }
        public DbSet<FeeInstallment> FeeInstallments { get; set; }

        // 👇 KEEPING THE 'S' AS REQUESTED 👇
        public DbSet<AssessmentTypes> AssessmentTypes { get; set; }
        public DbSet<StudentAssessmentMarks> StudentAssessmentMarks { get; set; }
        public DbSet<CourseAssessmentConfigs> CourseAssessmentConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==========================================
            // 1. UNIQUE CONSTRAINTS
            // ==========================================
            modelBuilder.Entity<Profile>().HasIndex(p => p.Email).IsUnique();
            modelBuilder.Entity<Profile>().HasIndex(p => p.RollNo).IsUnique();
            modelBuilder.Entity<Department>().HasIndex(d => d.Code).IsUnique();
            modelBuilder.Entity<DegreeProgram>().HasIndex(p => p.Code).IsUnique();
            modelBuilder.Entity<Batch>().HasIndex(b => b.Name).IsUnique();
            modelBuilder.Entity<Course>().HasIndex(c => c.Code).IsUnique();
            modelBuilder.Entity<Session>().HasIndex(s => s.Name).IsUnique();
            modelBuilder.Entity<Semester>().HasIndex(s => s.Name).IsUnique();

            // ==========================================
            // 2. POSTGRESQL ENUM MAPPINGS
            // ==========================================
            modelBuilder.Entity<Profile>()
             .Property(p => p.Role)
             .HasColumnType("user_role");

            modelBuilder.Entity<Department>()
                .Property(d => d.Type)
                .HasColumnType("department_type");

            // ==========================================
            // 3. ADMIN RELATIONSHIPS
            // ==========================================
            modelBuilder.Entity<Admin>(e =>
            {
                e.ToTable("admins");
                e.HasKey(a => a.Id);

                e.Property(a => a.Id).HasColumnName("id");
                e.Property(a => a.ProfileId).HasColumnName("profile_id");
                e.Property(a => a.DepartmentId).HasColumnName("department_id");
                e.Property(a => a.EmployeeId).HasColumnName("employee_id");
                e.Property(a => a.Designation).HasColumnName("designation");
                e.Property(a => a.CreatedBy).HasColumnName("created_by");
                e.Property(a => a.CreatedAt).HasColumnName("created_at");
                e.Property(a => a.UpdatedAt).HasColumnName("updated_at");

                e.HasIndex(a => a.ProfileId).IsUnique();
                e.HasIndex(a => a.EmployeeId).IsUnique();

                e.HasOne(a => a.Profile)
                 .WithOne()
                 .HasForeignKey<Admin>(a => a.ProfileId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(a => a.Department)
                 .WithMany()
                 .HasForeignKey(a => a.DepartmentId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            // ==========================================
            // 4. PROFILE & DEPARTMENT RELATIONSHIPS
            // ==========================================
            modelBuilder.Entity<Department>()
                .HasOne(d => d.Admin)
                .WithMany()
                .HasForeignKey(d => d.AdminId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Department>()
                .HasOne(d => d.Head)
                .WithMany()
                .HasForeignKey(d => d.HeadId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Profile>()
                .HasOne(p => p.Department)
                .WithMany()
                .HasForeignKey(p => p.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Teacher>()
                .HasOne(t => t.Profile)
                .WithOne()
                .HasForeignKey<Teacher>(t => t.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Student>()
                .HasOne(s => s.Profile)
                .WithOne(p => p.StudentRecord)
                .HasForeignKey<Student>(s => s.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================
            // 5. PROGRAM COURSE RELATIONSHIPS
            // ==========================================
            modelBuilder.Entity<ProgramCourse>()
                .HasOne(pc => pc.Program)
                .WithMany()
                .HasForeignKey(pc => pc.ProgramId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgramCourse>()
                .HasOne(pc => pc.Course)
                .WithMany()
                .HasForeignKey(pc => pc.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgramCourse>()
                .HasOne(pc => pc.Batch)
                .WithMany()
                .HasForeignKey(pc => pc.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgramCourse>()
                .HasOne(pc => pc.Session)
                .WithMany()
                .HasForeignKey(pc => pc.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgramCourse>()
                .HasOne(pc => pc.Semester)
                .WithMany()
                .HasForeignKey(pc => pc.SemesterId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgramCourse>()
                .Property(pc => pc.Sections)
                .HasColumnType("text[]")
                .HasDefaultValue(Array.Empty<string>());

            modelBuilder.Entity<ProgramCourse>()
                .HasIndex(pc => new
                {
                    pc.ProgramId,
                    pc.CourseId,
                    pc.BatchId,
                    pc.SessionId,
                    pc.SemesterId
                })
                .IsUnique()
                .HasDatabaseName("ux_program_courses_unique_assignment");

            // ==========================================
            // 6. COURSE SELF-REFERENCING (PREREQUISITE)
            // ==========================================
            modelBuilder.Entity<Course>()
                .HasOne(c => c.Prerequisite)
                .WithMany()
                .HasForeignKey(c => c.PrerequisiteId)
                .OnDelete(DeleteBehavior.SetNull);

            // ==========================================
            // 7. PROGRAM SESSION SEMESTER RELATIONSHIPS
            // ==========================================
            modelBuilder.Entity<ProgramSessionSemester>()
                .HasIndex(x => new { x.ProgramId, x.BatchId, x.SessionId, x.SemesterId })
                .IsUnique();

            modelBuilder.Entity<ProgramSessionSemester>()
                .HasOne(x => x.Program)
                .WithMany()
                .HasForeignKey(x => x.ProgramId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProgramSessionSemester>()
                .HasOne(x => x.Batch)
                .WithMany()
                .HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProgramSessionSemester>()
                .HasOne(x => x.Session)
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgramSessionSemester>()
                .HasOne(x => x.Semester)
                .WithMany()
                .HasForeignKey(x => x.SemesterId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // 8. SESSION DATE COLUMNS 
            // ==========================================
            modelBuilder.Entity<Session>()
                .Property(s => s.StartDate)
                .HasColumnType("date");

            modelBuilder.Entity<Session>()
                .Property(s => s.EndDate)
                .HasColumnType("date");

            // ==========================================
            // 9. STUDENT & SECTION RELATIONSHIPS
            // ==========================================
            modelBuilder.Entity<Student>()
                .HasOne(s => s.Department)
                .WithMany()
                .HasForeignKey(s => s.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Student>()
                .HasOne(s => s.Program)
                .WithMany()
                .HasForeignKey(s => s.ProgramId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Student>()
                .HasOne(s => s.Batch)
                .WithMany()
                .HasForeignKey(s => s.BatchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Section>()
                .HasIndex(s => new { s.ProgramId, s.BatchId, s.SemesterId, s.Name })
                .IsUnique();

            modelBuilder.Entity<Section>()
                .HasOne(s => s.Program)
                .WithMany()
                .HasForeignKey(s => s.ProgramId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Section>()
                .HasOne(s => s.Batch)
                .WithMany()
                .HasForeignKey(s => s.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Section>()
                .HasOne(s => s.Semester)
                .WithMany()
                .HasForeignKey(s => s.SemesterId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StudentRegistration>()
                .HasOne(r => r.StudentProfile)
                .WithMany()
                .HasForeignKey(r => r.StudentProfileId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StudentRegistration>()
                .HasOne(r => r.ProgramCourse)
                .WithMany()
                .HasForeignKey(r => r.ProgramCourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StudentRegistration>()
                .HasOne(r => r.Section)
                .WithMany()
                .HasForeignKey(r => r.SectionId)
                .OnDelete(DeleteBehavior.Restrict);

            // ==========================================
            // 10. TEACHER ASSIGNMENTS RELATIONSHIPS
            // ==========================================
            modelBuilder.Entity<TeacherAssignment>()
                .HasOne(ta => ta.Teacher)
                .WithMany()
                .HasForeignKey(ta => ta.TeacherId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TeacherAssignment>()
                .HasOne(ta => ta.ProgramCourse)
                .WithMany()
                .HasForeignKey(ta => ta.ProgramCourseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Room>()
                .HasOne(r => r.Department)
                .WithMany()
                .HasForeignKey(r => r.DepartmentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Attendance>()
              .HasOne(a => a.StudentProfile)
              .WithMany()
              .HasForeignKey(a => a.StudentProfileId)
              .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.TeacherAssignment)
                .WithMany()
                .HasForeignKey(a => a.TeacherAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AcademicCalendar>()
                .HasOne(c => c.TeacherAssignment)
                .WithMany()
                .HasForeignKey(c => c.TeacherAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CalendarSlot>()
                .HasOne(cs => cs.AcademicCalendar)
                .WithMany()
                .HasForeignKey(cs => cs.AcademicCalendarId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CalendarSlot>()
                .HasOne(cs => cs.TeacherAssignment)
                .WithMany()
                .HasForeignKey(cs => cs.TeacherAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // ==========================================
            // 11. FEE MODULE RELATIONSHIPS
            // ==========================================
            modelBuilder.Entity<FeeStructure>()
               .HasIndex(f => new { f.ProgramId, f.BatchId })
               .IsUnique();

            modelBuilder.Entity<StudentFee>()
                .HasIndex(f => new { f.StudentProfileId, f.SemesterId })
                .IsUnique();

            modelBuilder.Entity<FeeInstallment>(entity =>
            {
                entity.ToTable("fee_installments");

                entity.HasIndex(i => new { i.StudentFeeId, i.InstallmentNumber })
                      .IsUnique()
                      .HasDatabaseName("uq_installment_student_fee_number");

                entity.HasOne(i => i.StudentFee)
                      .WithMany(sf => sf.Installments)
                      .HasForeignKey(i => i.StudentFeeId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.Property(i => i.Amount).HasColumnType("numeric(12,2)");
                entity.Property(i => i.PaidAmount).HasColumnType("numeric(12,2)");
                entity.Property(i => i.DueDate).HasColumnType("date");
            });

            // ==========================================
            // 12. ASSESSMENT MODULE EXPLICIT MAPPINGS
            // ==========================================
            modelBuilder.Entity<AssessmentTypes>(entity =>
            {
                entity.ToTable("assessment_types");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Name).HasColumnName("name");
                entity.Property(e => e.Category).HasColumnName("category");
                entity.Property(e => e.DefaultTotalMarks).HasColumnName("default_total_marks");
                entity.Property(e => e.IsFixed).HasColumnName("is_fixed");
                entity.Property(e => e.IsActive).HasColumnName("is_active");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            });

            modelBuilder.Entity<CourseAssessmentConfigs>(entity =>
            {
                entity.ToTable("course_assessment_configs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ProgramCourseId).HasColumnName("program_course_id");
                entity.Property(e => e.AssessmentTypeId).HasColumnName("assessment_type_id");
                entity.Property(e => e.CustomTotalMarks).HasColumnName("custom_total_marks");
                entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.HasOne(c => c.ProgramCourse)
                      .WithMany()
                      .HasForeignKey(c => c.ProgramCourseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(c => c.AssessmentType)
                      .WithMany()
                      .HasForeignKey(c => c.AssessmentTypeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StudentAssessmentMarks>(entity =>
            {
                entity.ToTable("student_assessment_marks");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.StudentProfileId).HasColumnName("student_profile_id");
                entity.Property(e => e.ProgramCourseId).HasColumnName("program_course_id");
                entity.Property(e => e.AssessmentTypeId).HasColumnName("assessment_type_id");
                entity.Property(e => e.SectionName).HasColumnName("section_name");
                entity.Property(e => e.ObtainedMarks).HasColumnName("obtained_marks");
                entity.Property(e => e.EnteredByTeacherId).HasColumnName("entered_by_teacher_id");
                entity.Property(e => e.EnteredAt).HasColumnName("entered_at");

                entity.HasOne(m => m.StudentProfile)
                      .WithMany()
                      .HasForeignKey(m => m.StudentProfileId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(m => m.ProgramCourse)
                      .WithMany()
                      .HasForeignKey(m => m.ProgramCourseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(m => m.AssessmentType)
                      .WithMany()
                      .HasForeignKey(m => m.AssessmentTypeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<AssessmentSplit>(entity =>
            {
                entity.ToTable("assessment_splits");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ProgramCourseId).HasColumnName("program_course_id");
                entity.Property(e => e.AssessmentTypeId).HasColumnName("assessment_type_id");
                entity.Property(e => e.PartName).HasColumnName("part_name");
                entity.Property(e => e.MaxMarks).HasColumnName("max_marks");

                entity.HasOne(s => s.ProgramCourse)
                      .WithMany()
                      .HasForeignKey(s => s.ProgramCourseId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(s => s.AssessmentType)
                      .WithMany()
                      .HasForeignKey(s => s.AssessmentTypeId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}