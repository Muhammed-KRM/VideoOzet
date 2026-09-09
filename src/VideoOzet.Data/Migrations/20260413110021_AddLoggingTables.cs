using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VideoOzet.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoggingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EndpointLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TraceId = table.Column<string>(type: "text", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Query = table.Column<string>(type: "text", nullable: true),
                    RequestBody = table.Column<string>(type: "text", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserEmail = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndpointLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FunctionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ErrorCode = table.Column<string>(type: "text", nullable: false),
                    ClassName = table.Column<string>(type: "text", nullable: false),
                    MethodName = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "text", nullable: true),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    StackTrace = table.Column<string>(type: "text", nullable: true),
                    InputType = table.Column<string>(type: "text", nullable: true),
                    InputValue = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TraceId = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionLogs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EndpointLogs");

            migrationBuilder.DropTable(
                name: "FunctionLogs");
        }
    }
}
