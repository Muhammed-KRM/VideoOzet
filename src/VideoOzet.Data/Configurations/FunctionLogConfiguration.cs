using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class FunctionLogConfiguration : IEntityTypeConfiguration<FunctionLog>
{
    public void Configure(EntityTypeBuilder<FunctionLog> builder)
    {
        builder.ToTable("function_logs");
        builder.HasKey(f => f.Id);
        
        builder.Property(f => f.Id).HasColumnName("id").UseIdentityColumn();
        builder.Property(f => f.ErrorCode).HasColumnName("error_code").HasMaxLength(50).IsRequired();
        builder.Property(f => f.ClassName).HasColumnName("class_name").HasMaxLength(200).IsRequired();
        builder.Property(f => f.MethodName).HasColumnName("method_name").HasMaxLength(200).IsRequired();
        builder.Property(f => f.FilePath).HasColumnName("file_path").HasMaxLength(500);
        builder.Property(f => f.LineNumber).HasColumnName("line_number").HasDefaultValue(0).IsRequired();
        builder.Property(f => f.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        builder.Property(f => f.StackTrace).HasColumnName("stack_trace").HasColumnType("text");
        builder.Property(f => f.InputType).HasColumnName("input_type").HasMaxLength(200);
        builder.Property(f => f.InputValue).HasColumnName("input_value").HasColumnType("text");

        builder.Property(f => f.TraceId).HasColumnName("trace_id").HasMaxLength(50);
        builder.Property(f => f.Severity).HasColumnName("severity").HasMaxLength(20).HasDefaultValue("Error").IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(f => f.CreatedAt).HasDatabaseName("idx_function_logs_created_at").IsDescending();
        builder.HasIndex(f => f.Severity).HasDatabaseName("idx_function_logs_severity");
        builder.HasIndex(f => f.ErrorCode).HasDatabaseName("idx_function_logs_error_code");
        builder.HasIndex(f => new { f.ClassName, f.MethodName }).HasDatabaseName("idx_function_logs_class_method");
    }
}
