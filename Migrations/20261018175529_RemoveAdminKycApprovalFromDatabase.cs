using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinGuard.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAdminKycApprovalFromDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminApprovedAtUtc",
                table: "KycVerificationRecords");

            migrationBuilder.DropColumn(
                name: "AdminApprovedBy",
                table: "KycVerificationRecords");

            migrationBuilder.DropColumn(
                name: "AdminNotes",
                table: "KycVerificationRecords");

            migrationBuilder.Sql("DELETE FROM \"KycAuditLogs\" WHERE \"Action\" LIKE 'AdminKyc%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AdminApprovedAtUtc",
                table: "KycVerificationRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminApprovedBy",
                table: "KycVerificationRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminNotes",
                table: "KycVerificationRecords",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }
    }
}
