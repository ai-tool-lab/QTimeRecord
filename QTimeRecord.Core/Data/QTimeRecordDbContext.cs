
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QTimeRecord.Core.Domain;

namespace QTimeRecord.Core.Data;

public sealed class QTimeRecordDbContext(DbContextOptions<QTimeRecordDbContext> options)
    : DbContext(options)
{
    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<Staff> Staff => Set<Staff>();

    public DbSet<StaffQrToken> StaffQrTokens => Set<StaffQrToken>();

    public DbSet<TimeRecord> TimeRecords => Set<TimeRecord>();

    public DbSet<DeviceSettings> DeviceSettings => Set<DeviceSettings>();

    public DbSet<AdminCredential> AdminCredentials => Set<AdminCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureStore(modelBuilder);
        ConfigureStaff(modelBuilder);
        ConfigureTimeRecord(modelBuilder);
        ConfigureSettings(modelBuilder);

        UseSnakeCaseNames(modelBuilder);
    }

    private static void ConfigureStore(ModelBuilder b)
    {
        b.Entity<Store>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StoreCode).HasMaxLength(32);
            e.Property(x => x.StoreName).HasMaxLength(128).IsRequired();
            e.Property(x => x.CompanyName).HasMaxLength(128).IsRequired();
            e.Property(x => x.AnnouncementTitle).HasMaxLength(40);
        });

        b.Entity<Announcement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Heading).HasMaxLength(30).IsRequired();
            e.Property(x => x.Body).HasMaxLength(200).IsRequired();

            e.HasOne(x => x.Store)
                .WithMany(x => x.Announcements)
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.StoreId, x.DisplayOrder });
        });
    }

    private static void ConfigureStaff(ModelBuilder b)
    {
        b.Entity<Staff>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StaffNo).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.NameKana).HasMaxLength(64);
            e.Property(x => x.EmploymentType).HasMaxLength(32);
            e.Property(x => x.Status).HasConversion(EnumConverter<StaffStatus>()).HasMaxLength(16);

            e.HasOne(x => x.Store)
                .WithMany()
                .HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.StoreId, x.Status });

            // 社員番号は任意項目。入力されている場合だけ店舗内で一意にする。
            e.HasIndex(x => new { x.StoreId, x.StaffNo })
                .IsUnique()
                .HasFilter("staff_no IS NOT NULL");
        });

        b.Entity<StaffQrToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();

            e.HasOne(x => x.Staff)
                .WithMany(x => x.QrTokens)
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.Token).IsUnique();

            // 「このスタッフの有効なトークン」を引くための索引。
            e.HasIndex(x => new { x.StaffId, x.RevokedAt });
        });
    }

    private static void ConfigureTimeRecord(ModelBuilder b)
    {
        b.Entity<TimeRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RecordType).HasConversion(EnumConverter<TimeRecordType>()).HasMaxLength(16);
            e.Property(x => x.EntryMethod).HasConversion(EnumConverter<EntryMethod>()).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(200);

            // スタッフを物理削除すると打刻が道連れで消えるため、DB 側で拒否する。
            // 退職は Status で表す運用だが、誤った DELETE を最後に止めるのはここ。
            e.HasOne(x => x.Staff)
                .WithMany()
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);

            // 月次一覧（店舗の営業日で絞る）
            e.HasIndex(x => new { x.StoreId, x.WorkDate });

            // スタッフ別の勤務日抽出、および直近の打刻の取得
            e.HasIndex(x => new { x.StaffId, x.WorkDate });
            e.HasIndex(x => new { x.StaffId, x.RecordedAt });
        });

        // 打刻の重複は DB 制約で防がない。
        // 「同一スタッフ・同一種別・同時刻」を一意にすると、正当な再出勤まで弾いてしまう。
        // 連打対策はアプリ側の 60 秒ルールで行う（→ plan.md Q7）。
    }

    private static void ConfigureSettings(ModelBuilder b)
    {
        b.Entity<DeviceSettings>(e =>
        {
            e.HasKey(x => x.StoreId);
            e.Property(x => x.ComPort).HasMaxLength(16);
            e.Property(x => x.Parity).HasMaxLength(16).IsRequired();
            e.Property(x => x.StopBits).HasMaxLength(16).IsRequired();

            e.HasOne(x => x.Store).WithOne().HasForeignKey<DeviceSettings>(x => x.StoreId);
        });

        b.Entity<AdminCredential>(e =>
        {
            e.HasKey(x => x.StoreId);
            e.Property(x => x.PasswordHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.Salt).HasMaxLength(64).IsRequired();

            e.HasOne(x => x.Store).WithOne().HasForeignKey<AdminCredential>(x => x.StoreId);
        });
    }

    /// <summary>
    /// 列挙型を大文字スネークケースの文字列として保存する。
    /// 数値だと DB を直接開いたときに意味が読めない。
    /// </summary>
    private static ValueConverter<TEnum, string> EnumConverter<TEnum>() where TEnum : struct, Enum
        => new(v => EnumDbValue.ToDbValue(v), v => EnumDbValue.Parse<TEnum>(v));

    /// <summary>
    /// テーブル名・列名・索引名をまとめてスネークケースにする。
    ///
    /// 1つずつ HasColumnName を書くと、追加したプロパティで書き忘れが起きる。
    /// 命名を機械的に決めてしまえば、その事故は起こらない。
    /// </summary>
    private static void UseSnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
            }
        }
    }

    private static string ToSnakeCase(string name) => NamingConventions.ToSnakeCase(name);
}
