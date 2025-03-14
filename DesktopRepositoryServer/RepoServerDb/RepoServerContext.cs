using System;
using System.Collections.Generic;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;

namespace OpenShock.Desktop.RepositoryServer.RepoServerDb;

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
            ConfigureOptionsBuilder(optionsBuilder, "Host=localhost;Database=desktop-repo-server;Username=openshock;Password=openshock", true);
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
            // Map Enums to their string values
        });
        
        optionsBuilder.UseExceptionProcessor();

        if (debug)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
        }
    }

    public virtual DbSet<Module> Modules { get; set; }

    public virtual DbSet<Version> Versions { get; set; }
    

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    }
}
