using System;
using System.Collections.Generic;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OpenShock.RepositoryServer.RepoServerDb;

/// <summary>
/// This is meant for use in migrations only.
/// </summary>
public sealed class MigrationOpenShockContext : RepoServerContext
{
    private readonly string? _connectionString = null;
    private readonly bool _debug;
    private readonly bool _migrationTool;
    private readonly ILoggerFactory? _loggerFactory = null;

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
            ConfigureOptionsBuilder(optionsBuilder, "Host=localhost;Database=repo-server;Username=openshock;Password=openshock", true);
            return;
        }
        if(string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("Connection string is not set.");
        ConfigureOptionsBuilder(optionsBuilder, _connectionString, _debug);

        if (_loggerFactory != null)
            optionsBuilder.UseLoggerFactory(_loggerFactory);
    }
}

public partial class RepoServerContext : DbContext
{
    public RepoServerContext()
    {
    }

    public RepoServerContext(DbContextOptions<RepoServerContext> options)
        : base(options)
    {
    }

    public static void ConfigureOptionsBuilder(DbContextOptionsBuilder optionsBuilder, string connectionString,
        bool debug)
    {
        optionsBuilder.UseNpgsql(connectionString, npgsqlBuilder =>
        {
            npgsqlBuilder.MapEnum<FirmwareChannel>("firmware_channel");
            npgsqlBuilder.MapEnum<FirmwareArtifactType>("firmware_artifact_type");
            npgsqlBuilder.MapEnum<FirmwareReleaseNoteType>("firmware_release_note_type");
            npgsqlBuilder.MapEnum<FirmwareChipArchitecture>("firmware_chip_architecture");
        });

        optionsBuilder.UseExceptionProcessor();

        if (debug)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
        }
    }

    // Desktop module tables
    public virtual DbSet<Module> Modules { get; set; }
    public virtual DbSet<Version> Versions { get; set; }

    // Firmware tables
    public virtual DbSet<FirmwareChip> FirmwareChips { get; set; }
    public virtual DbSet<FirmwareBoard> FirmwareBoards { get; set; }
    public virtual DbSet<FirmwareVersion> FirmwareVersions { get; set; }
    public virtual DbSet<FirmwareArtifact> FirmwareArtifacts { get; set; }
    public virtual DbSet<FirmwareReleaseNote> FirmwareReleaseNotes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // PostgreSQL enum mappings
        modelBuilder.HasPostgresEnum<FirmwareChannel>("firmware_channel");
        modelBuilder.HasPostgresEnum<FirmwareArtifactType>("firmware_artifact_type");
        modelBuilder.HasPostgresEnum<FirmwareReleaseNoteType>("firmware_release_note_type");
        modelBuilder.HasPostgresEnum<FirmwareChipArchitecture>("firmware_chip_architecture");

        // Desktop module entities (unchanged)
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

            entity.Property(e => e.Id)
                .HasMaxLength(32)
                .HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(64)
                .HasColumnName("name");
            entity.Property(e => e.Architecture)
                .HasColumnName("architecture");
        });

        modelBuilder.Entity<FirmwareBoard>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("firmware_boards_pkey");
            entity.ToTable("firmware_boards");

            entity.Property(e => e.Id)
                .HasMaxLength(64)
                .HasColumnName("id");
            entity.Property(e => e.ChipId)
                .HasMaxLength(32)
                .HasColumnName("chip_id");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Discontinued)
                .HasDefaultValue(false)
                .HasColumnName("discontinued");

            entity.HasOne(d => d.ChipNavigation).WithMany(p => p.Boards)
                .HasForeignKey(d => d.ChipId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_firmware_boards_chip");
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
            entity.Property(e => e.CommitHash)
                .HasMaxLength(40)
                .HasColumnName("commit_hash");
            entity.Property(e => e.ReleaseUrl)
                .HasMaxLength(256)
                .HasColumnName("release_url");

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
            entity.Property(e => e.BoardId)
                .HasMaxLength(64)
                .HasColumnName("board_id");
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
            entity.Property(e => e.Type)
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
    }
}
