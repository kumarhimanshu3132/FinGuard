using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddKycVerificationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KycAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TransactionReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KycVerificationRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    AadhaarStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MaskedAadhaar = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    AadhaarTransactionRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AadhaarVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AadhaarFailureReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PanStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MaskedPan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PanTransactionRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PanHolderName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    PanVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PanFailureReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OtpStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActiveOtpChallengeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OtpTransactionRef = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OtpExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OtpAttemptsCount = table.Column<int>(type: "integer", nullable: false),
                    MaxOtpAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastOtpSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OtpVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OtpFailureReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OverallKycStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminApprovedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AdminNotes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    VerificationMode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycVerificationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KycVerificationRecords_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KycAuditLogs_CustomerId",
                table: "KycAuditLogs",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_KycVerificationRecords_CustomerId",
                table: "KycVerificationRecords",
                column: "CustomerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KycAuditLogs");

            migrationBuilder.DropTable(
                name: "KycVerificationRecords");
        }
    }
}
