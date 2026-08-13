using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using OpenShock.RepositoryServer.Enums;
using OpenShock.RepositoryServer.RepoServerDb.Models;
using Version = OpenShock.RepositoryServer.RepoServerDb.Models.Version;

namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// This is meant for use in migrations only.
/// </summary>
public sealed class MigrationOpenShockContext : RepoServerContext
{
    private readonly string? _connectionString;
    private readonly bool _debug;
    private readonly bool _migrationTool;
    private readonly ILoggerFactory? _loggerFactory;

    public MigrationOpenShockContext()
    {
        _migrationTool = true;
    }

    public MigrationOpenShockContext(string connectionString, bool debug, ILoggerFactory loggerFactory)
    {
        _connectionString = connectionString;
        _debug = debug;
        _loggerFactory = loggerFactory;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_migrationTool)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=repo-server;Username=openshock;Password=openshock", MapEnums);
            optionsBuilder.UseExceptionProcessor();
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
            return;
        }
        if(string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("Connection string is not set.");
        ConfigureOptionsBuilder(optionsBuilder, _connectionString, _debug);

        if (_loggerFactory != null)
            optionsBuilder.UseLoggerFactory(_loggerFactory);
    }
}

public class RepoServerContext : DbContext
{
    public RepoServerContext()
    {
    }

    public RepoServerContext(DbContextOptions<RepoServerContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Single source of truth for PostgreSQL enum mappings. Since Npgsql 9, MapEnum also
    /// injects the enum definitions into the EF model, so no HasPostgresEnum calls may
    /// coexist with these — that duplicates the definitions and breaks type mapping.
    /// Names are pinned explicitly because they don't all match the default translation
    /// (firmware_release_note_type vs. ReleaseNoteSectionType).
    /// </summary>
    protected static void MapEnums(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsqlBuilder)
    {
        npgsqlBuilder.MapEnum<ReleaseChannel>("release_channel");
        npgsqlBuilder.MapEnum<FirmwareArtifactType>("firmware_artifact_type");
        npgsqlBuilder.MapEnum<ReleaseNoteSectionType>("firmware_release_note_type");
        npgsqlBuilder.MapEnum<FirmwareChipArchitecture>("firmware_chip_architecture");
        npgsqlBuilder.MapEnum<ReleaseStatus>("release_status");
        npgsqlBuilder.MapEnum<RepositoryProvider>("repository_provider");
        npgsqlBuilder.MapEnum<RepositoryScope>("repository_scope");
        npgsqlBuilder.MapEnum<AdvisorySeverity>("advisory_severity");
        npgsqlBuilder.MapEnum<DiscordNotificationEvent>("discord_notification_event");
    }

    public static void ConfigureOptionsBuilder(DbContextOptionsBuilder optionsBuilder, string connectionString,
        bool debug)
    {
        optionsBuilder.UseNpgsql(connectionString, MapEnums);

        optionsBuilder.UseExceptionProcessor();

        // Suppress false positive: EF Core 10 sees enum schema differences from EF Core 9 snapshots
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        if (debug)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
        }
    }

    // Shared source-code repository registry (used by firmware + desktop)
    public virtual DbSet<SourceRepository> Repositories { get; set; }

    // Desktop module tables
    public virtual DbSet<Module> Modules { get; set; }
    public virtual DbSet<Version> Versions { get; set; }

    // Firmware tables
    public virtual DbSet<FirmwareChip> FirmwareChips { get; set; }
    public virtual DbSet<FirmwareBoard> FirmwareBoards { get; set; }
    public virtual DbSet<FirmwareVersion> FirmwareVersions { get; set; }
    public virtual DbSet<FirmwareRelease> FirmwareReleases { get; set; }
    public virtual DbSet<FirmwareArtifact> FirmwareArtifacts { get; set; }
    public virtual DbSet<FirmwareReleaseNote> FirmwareReleaseNotes { get; set; }
    public virtual DbSet<FirmwareStagedArtifact> FirmwareStagedArtifacts { get; set; }
    public virtual DbSet<FirmwareStagedReleaseNote> FirmwareStagedReleaseNotes { get; set; }

    // USB catalog (flashtool support)
    public virtual DbSet<UsbDevice> UsbDevices { get; set; }
    public virtual DbSet<UsbSerialFilter> UsbSerialFilters { get; set; }
    public virtual DbSet<FirmwareChipUsbDevice> FirmwareChipUsbDevices { get; set; }
    public virtual DbSet<FirmwareBoardUsbDevice> FirmwareBoardUsbDevices { get; set; }

    // Firmware advisories shown on the manifest endpoint
    public virtual DbSet<FirmwareAdvisory> FirmwareAdvisories { get; set; }

    // Discord notification targets
    public virtual DbSet<DiscordWebhook> DiscordWebhooks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Shared source-code repository registry
        modelBuilder.Entity<SourceRepository>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("repositories_pkey");
            entity.ToTable("repositories");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.Owner).HasMaxLength(128).HasColumnName("owner");
            entity.Property(e => e.Repo).HasMaxLength(128).HasColumnName("repo");
            entity.Property(e => e.Scopes).HasColumnName("scopes");

            // Uniqueness is enforced case-insensitively by a unique index on
            // (provider, lower(owner), lower(repo)), created in the migration — expression indexes
            // cannot be declared in the EF model. GitHub treats owner and repo case-insensitively, so
            // a case-sensitive constraint would let "OpenShock/Firmware" and "openshock/firmware"
            // exist as separate grants while the OIDC lookup only ever finds one of them.
            entity.HasIndex(e => new { e.Provider, e.Owner, e.Repo })
                .HasDatabaseName("ix_repositories_provider_owner_repo");
        });

        modelBuilder.Entity<DiscordWebhook>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("discord_webhooks_pkey");
            entity.ToTable("discord_webhooks");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasMaxLength(128).HasColumnName("name");
            entity.Property(e => e.Url).HasMaxLength(512).HasColumnName("url");
            entity.Property(e => e.Events).HasColumnName("events");
            entity.Property(e => e.Enabled).HasDefaultValue(true).HasColumnName("enabled");
        });

        // Desktop module entities (unchanged — source traceability is not yet wired here)
        modelBuilder.Entity<Module>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("modules_pkey");

            entity.ToTable("modules");

            entity.Property(e => e.Id)
                .HasMaxLength(128)
                .HasColumnName("id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IconUrl)
                .HasMaxLength(256)
                .HasColumnName("icon_url");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.SourceUrl)
                .HasMaxLength(256)
                .HasColumnName("source_url");
            entity.Property(e => e.RepositoryId).HasColumnName("repository_id");

            entity.HasOne(d => d.RepositoryNavigation).WithMany()
                .HasForeignKey(d => d.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_modules_repository");
        });

        modelBuilder.Entity<Version>(entity =>
        {
            entity.HasKey(e => new { Version1 = e.VersionName, e.Module }).HasName("versions_pkey");

            entity.ToTable("versions");

            entity.Property(e => e.VersionName)
                .HasMaxLength(64)
                .HasColumnName("version");
            entity.Property(e => e.Module)
                .HasMaxLength(128)
                .HasColumnName("module");
            entity.Property(e => e.ChangelogUrl)
                .HasMaxLength(256)
                .HasColumnName("changelog_url");
            entity.Property(e => e.HashSha256).HasColumnName("hash_sha256");
            entity.Property(e => e.ReleaseUrl)
                .HasMaxLength(256)
                .HasColumnName("release_url");
            entity.Property(e => e.ZipUrl)
                .HasMaxLength(256)
                .HasColumnName("zip_url");

            entity.HasOne(d => d.ModuleNavigation).WithMany(p => p.Versions)
                .HasForeignKey(d => d.Module)
                .HasConstraintName("fk_versions_module");
        });

        // Firmware entities
        modelBuilder.Entity<FirmwareChip>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("firmware_chips_pkey");
            entity.ToTable("firmware_chips");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(64)
                .HasColumnName("name");
            entity.Property(e => e.Architecture)
                .HasColumnName("architecture");

            // Uniqueness is enforced case-insensitively by a unique index on lower(name), created in
            // the AddCaseInsensitiveNameIndexes migration. Expression indexes cannot be declared in the
            // EF model, so this is intentionally absent here rather than duplicated as a weaker
            // case-sensitive index. Chip names must match esptool-js identifiers exactly.
            entity.HasIndex(e => e.Name)
                .HasDatabaseName("ix_firmware_chips_name");
        });

        modelBuilder.Entity<FirmwareBoard>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("firmware_boards_pkey");
            entity.ToTable("firmware_boards");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChipId).HasColumnName("chip_id");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Discontinued)
                .HasDefaultValue(false)
                .HasColumnName("discontinued");
            entity.Property(e => e.RequiredArtifactTypes)
                .HasColumnName("required_artifact_types");

            entity.HasOne(d => d.ChipNavigation).WithMany(p => p.Boards)
                .HasForeignKey(d => d.ChipId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_firmware_boards_chip");

            // Uniqueness is enforced case-insensitively by a unique index on lower(name), created in
            // the AddCaseInsensitiveNameIndexes migration — see the chips comment above. A plain unique
            // index here would permit "ESP32-Core" and "esp32-core" to coexist, and case-insensitive
            // resolution would then silently pick one of them.
            entity.HasIndex(e => e.Name)
                .HasDatabaseName("ix_firmware_boards_name");
        });

        modelBuilder.Entity<FirmwareVersion>(entity =>
        {
            entity.HasKey(e => e.Version).HasName("firmware_versions_pkey");
            entity.ToTable("firmware_versions");

            entity.Property(e => e.Version)
                .HasMaxLength(64)
                .HasColumnName("version");
            entity.Property(e => e.Channel)
                .HasColumnName("channel");
            entity.Property(e => e.ReleaseDate)
                .HasColumnName("release_date");
            entity.Property(e => e.RepositoryId)
                .HasColumnName("repository_id");
            entity.Property(e => e.CommitHash)
                .HasMaxLength(40)
                .HasColumnName("commit_hash");
            entity.Property(e => e.Ref)
                .HasMaxLength(256)
                .HasColumnName("ref");
            entity.Property(e => e.RunId)
                .HasMaxLength(64)
                .HasColumnName("run_id");

            entity.HasOne(d => d.RepositoryNavigation).WithMany(p => p.FirmwareVersions)
                .HasForeignKey(d => d.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_firmware_versions_repository");

            entity.HasIndex(e => e.Channel).HasDatabaseName("ix_firmware_versions_channel");
            entity.HasIndex(e => e.ReleaseDate).HasDatabaseName("ix_firmware_versions_release_date");
        });

        modelBuilder.Entity<FirmwareArtifact>(entity =>
        {
            entity.HasKey(e => new { e.Version, e.BoardId, e.ArtifactType }).HasName("firmware_artifacts_pkey");
            entity.ToTable("firmware_artifacts");

            entity.Property(e => e.Version)
                .HasMaxLength(64)
                .HasColumnName("version");
            entity.Property(e => e.BoardId).HasColumnName("board_id");
            entity.Property(e => e.ArtifactType)
                .HasColumnName("artifact_type");
            entity.Property(e => e.HashSha256)
                .HasColumnName("hash_sha256");
            entity.Property(e => e.FileSize)
                .HasColumnName("file_size");

            entity.HasOne(d => d.VersionNavigation).WithMany(p => p.Artifacts)
                .HasForeignKey(d => d.Version)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_firmware_artifacts_version");

            entity.HasOne(d => d.BoardNavigation).WithMany(p => p.Artifacts)
                .HasForeignKey(d => d.BoardId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_firmware_artifacts_board");
        });

        modelBuilder.Entity<FirmwareReleaseNote>(entity =>
        {
            entity.HasKey(e => new { e.Version, e.Index }).HasName("firmware_release_notes_pkey");
            entity.ToTable("firmware_release_notes");

            entity.Property(e => e.Version)
                .HasMaxLength(64)
                .HasColumnName("version");
            entity.Property(e => e.Index)
                .HasColumnName("index");
            entity.Property(e => e.SectionType)
                .HasColumnName("type");
            entity.Property(e => e.Title)
                .HasMaxLength(256)
                .HasColumnName("title");
            entity.Property(e => e.Content)
                .HasColumnName("content");

            entity.HasOne(d => d.VersionNavigation).WithMany(p => p.ReleaseNotes)
                .HasForeignKey(d => d.Version)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_firmware_release_notes_version");
        });

        modelBuilder.Entity<FirmwareRelease>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("firmware_releases_pkey");
            entity.ToTable("firmware_releases");

            entity.Property(e => e.Id)
                .HasColumnName("id");
            entity.Property(e => e.Version)
                .HasMaxLength(64)
                .HasColumnName("version");
            entity.Property(e => e.Channel)
                .HasColumnName("channel");
            entity.Property(e => e.RepositoryId)
                .HasColumnName("repository_id");
            entity.Property(e => e.CommitHash)
                .HasMaxLength(40)
                .HasColumnName("commit_hash");
            entity.Property(e => e.Ref)
                .HasMaxLength(256)
                .HasColumnName("ref");
            entity.Property(e => e.RunId)
                .HasMaxLength(64)
                .HasColumnName("run_id");
            entity.Property(e => e.ReleaseDate)
                .HasColumnName("release_date");
            entity.Property(e => e.Status)
                .HasColumnName("status");
            entity.Property(e => e.DeclaredBoards)
                .HasColumnName("declared_boards");
            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at");

            entity.HasOne(d => d.RepositoryNavigation).WithMany(p => p.FirmwareReleases)
                .HasForeignKey(d => d.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_firmware_releases_repository");

            entity.HasIndex(e => new { e.Version, e.Status })
                .HasDatabaseName("ix_firmware_releases_version_status");
        });

        modelBuilder.Entity<FirmwareStagedArtifact>(entity =>
        {
            entity.HasKey(e => new { e.ReleaseId, e.BoardId, e.ArtifactType })
                .HasName("firmware_staged_artifacts_pkey");
            entity.ToTable("firmware_staged_artifacts");

            entity.Property(e => e.ReleaseId)
                .HasColumnName("release_id");
            entity.Property(e => e.BoardId).HasColumnName("board_id");
            entity.Property(e => e.ArtifactType)
                .HasColumnName("artifact_type");
            entity.Property(e => e.HashSha256)
                .HasColumnName("hash_sha256");
            entity.Property(e => e.FileSize)
                .HasColumnName("file_size");

            entity.HasOne(d => d.ReleaseNavigation).WithMany(p => p.StagedArtifacts)
                .HasForeignKey(d => d.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_firmware_staged_artifacts_release");
        });

        modelBuilder.Entity<FirmwareStagedReleaseNote>(entity =>
        {
            entity.HasKey(e => new { e.ReleaseId, e.Index })
                .HasName("firmware_staged_release_notes_pkey");
            entity.ToTable("firmware_staged_release_notes");

            entity.Property(e => e.ReleaseId)
                .HasColumnName("release_id");
            entity.Property(e => e.Index)
                .HasColumnName("index");
            entity.Property(e => e.SectionType)
                .HasColumnName("type");
            entity.Property(e => e.Title)
                .HasMaxLength(256)
                .HasColumnName("title");
            entity.Property(e => e.Content)
                .HasColumnName("content");

            entity.HasOne(d => d.ReleaseNavigation).WithMany(p => p.StagedReleaseNotes)
                .HasForeignKey(d => d.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_firmware_staged_release_notes_release");
        });

        // USB catalog entities
        modelBuilder.Entity<UsbDevice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("usb_devices_pkey");
            entity.ToTable("usb_devices");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Vid).HasColumnName("vid");
            entity.Property(e => e.Pid).HasColumnName("pid");
            entity.Property(e => e.Name).HasMaxLength(128).HasColumnName("name");

            entity.HasIndex(e => new { e.Vid, e.Pid })
                .IsUnique()
                .HasDatabaseName("ix_usb_devices_vid_pid");
        });

        modelBuilder.Entity<UsbSerialFilter>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("usb_serial_filters_pkey");
            entity.ToTable("usb_serial_filters");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Vid).HasColumnName("vid");
            entity.Property(e => e.Pid).HasColumnName("pid");
            entity.Property(e => e.Description).HasMaxLength(256).HasColumnName("description");

            // Unique (vid, pid) with NULLS NOT DISTINCT so at most one vendor-wide
            // (pid IS NULL) filter can exist per vid. Requires PostgreSQL 15+.
            entity.HasIndex(e => new { e.Vid, e.Pid })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName("ix_usb_serial_filters_vid_pid");
        });

        // Chip ↔ UsbDevice M:N via FirmwareChipUsbDevice junction
        modelBuilder.Entity<FirmwareChip>()
            .HasMany(c => c.UsbDevices)
            .WithMany(d => d.Chips)
            .UsingEntity<FirmwareChipUsbDevice>(
                right => right.HasOne(e => e.UsbDevice)
                    .WithMany()
                    .HasForeignKey(e => e.UsbDeviceId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("fk_firmware_chip_usb_devices_usb_device"),
                left => left.HasOne(e => e.Chip)
                    .WithMany()
                    .HasForeignKey(e => e.ChipId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("fk_firmware_chip_usb_devices_chip"),
                joinEntity =>
                {
                    joinEntity.HasKey(e => new { e.ChipId, e.UsbDeviceId })
                        .HasName("firmware_chip_usb_devices_pkey");
                    joinEntity.ToTable("firmware_chip_usb_devices");
                    joinEntity.Property(e => e.ChipId).HasColumnName("chip_id");
                    joinEntity.Property(e => e.UsbDeviceId).HasColumnName("usb_device_id");
                });

        // Firmware advisories (manifest-facing, admin-managed)
        modelBuilder.Entity<FirmwareAdvisory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("firmware_advisories_pkey");
            entity.ToTable("firmware_advisories");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Severity).HasColumnName("severity");
            entity.Property(e => e.Title).HasMaxLength(256).HasColumnName("title");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.AffectedVersions).HasMaxLength(128).HasColumnName("affected_versions");
            entity.Property(e => e.Url).HasMaxLength(512).HasColumnName("url");
        });

        // Board ↔ UsbDevice M:N via FirmwareBoardUsbDevice junction
        modelBuilder.Entity<FirmwareBoard>()
            .HasMany(b => b.UsbDevices)
            .WithMany(d => d.Boards)
            .UsingEntity<FirmwareBoardUsbDevice>(
                right => right.HasOne(e => e.UsbDevice)
                    .WithMany()
                    .HasForeignKey(e => e.UsbDeviceId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("fk_firmware_board_usb_devices_usb_device"),
                left => left.HasOne(e => e.Board)
                    .WithMany()
                    .HasForeignKey(e => e.BoardId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .HasConstraintName("fk_firmware_board_usb_devices_board"),
                joinEntity =>
                {
                    joinEntity.HasKey(e => new { e.BoardId, e.UsbDeviceId })
                        .HasName("firmware_board_usb_devices_pkey");
                    joinEntity.ToTable("firmware_board_usb_devices");
                    joinEntity.Property(e => e.BoardId).HasColumnName("board_id");
                    joinEntity.Property(e => e.UsbDeviceId).HasColumnName("usb_device_id");
                });
    }
}
