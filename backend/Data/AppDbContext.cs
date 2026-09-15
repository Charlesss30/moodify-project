using Microsoft.EntityFrameworkCore;
using mood_recommendation.Models;

namespace mood_recommendation.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(
            DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<TaiKhoan> TaiKhoans { get; set; }

        public DbSet<TamTrang> TamTrangs { get; set; }

        public DbSet<LichSuTamTrang> LichSuTamTrangs { get; set; }

        public DbSet<NoiDungGiaiTri> NoiDungGiaiTris { get; set; }

        public DbSet<Phim> Phims { get; set; }

        public DbSet<Nhac> Nhacs { get; set; }

        public DbSet<TheLoai> TheLoais { get; set; }

        public DbSet<DanhGia> DanhGias { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TaiKhoan>()
                .HasIndex(x => x.TenDangNhap)
                .IsUnique();

            modelBuilder.Entity<TaiKhoan>()
                .HasIndex(x => x.Email)
                .IsUnique();


            modelBuilder.Entity<TamTrang>()
                .HasIndex(x => x.TenTamTrang)
                .IsUnique();

            modelBuilder.Entity<LichSuTamTrang>()
                .HasOne(x => x.TaiKhoan)
                .WithMany(x => x.LichSuTamTrangs)
                .HasForeignKey(x => x.TaiKhoanID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<LichSuTamTrang>()
                .HasOne(x => x.TamTrang)
                .WithMany(x => x.LichSuTamTrangs)
                .HasForeignKey(x => x.TamTrangID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Phim>()
                .HasOne(x => x.NoiDungGiaiTri)
                .WithOne(x => x.Phim)
                .HasForeignKey<Phim>(x => x.NoiDungID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Nhac>()
                .HasOne(x => x.NoiDungGiaiTri)
                .WithOne(x => x.Nhac)
                .HasForeignKey<Nhac>(x => x.NoiDungID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DanhGia>()
                .HasOne(x => x.TaiKhoan)
                .WithMany(x => x.DanhGias)
                .HasForeignKey(x => x.TaiKhoanID)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DanhGia>()
                .HasOne(x => x.NoiDungGiaiTri)
                .WithMany(x => x.DanhGias)
                .HasForeignKey(x => x.NoiDungID)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
