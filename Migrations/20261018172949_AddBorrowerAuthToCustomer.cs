using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowerAuthToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OtpCode",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpExpiry",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "Customers",
                type: "text",
                nullable: true);

            // Data Migration: Copy credentials from AdminUsers into Customers
            migrationBuilder.Sql(@"
                UPDATE ""Customers"" c
                SET ""Password"" = a.""Password"",
                    ""IsVerified"" = a.""IsVerified"",
                    ""OtpCode"" = a.""OtpCode"",
                    ""OtpExpiry"" = a.""OtpExpiry""
                FROM ""AdminUsers"" a
                WHERE LOWER(c.""Email"") = LOWER(a.""Email"")
                  AND a.""Role"" = 'Borrower';
            ");

            // Purge all non-admin / borrower records from AdminUsers
            migrationBuilder.Sql(@"
                DELETE FROM ""AdminUsers""
                WHERE ""Role"" = 'Borrower';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OtpCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OtpExpiry",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Password",
                table: "Customers");
        }
    }
}
