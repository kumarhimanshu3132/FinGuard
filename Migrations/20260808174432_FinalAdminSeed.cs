using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinGuard.Migrations
{
    /// <inheritdoc />
    public partial class FinalAdminSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "AdminUsers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "AdminUsers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsVerified",
                table: "AdminUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OtpCode",
                table: "AdminUsers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpExpiry",
                table: "AdminUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "AdminUsers",
                keyColumn: "AdminId",
                keyValue: 1,
                columns: new[] { "Email", "IsVerified", "OtpCode", "OtpExpiry", "Password", "Username" },
                values: new object[] { "kumarhimanshu3132@gmail.com", false, null, null, null, "kumarhimanshu3132" });

            migrationBuilder.UpdateData(
                table: "AdminUsers",
                keyColumn: "AdminId",
                keyValue: 2,
                columns: new[] { "Email", "IsVerified", "OtpCode", "OtpExpiry", "Password", "Username" },
                values: new object[] { "aadityakr85390@gmail.com", false, null, null, null, "aadityakr85390" });

            migrationBuilder.UpdateData(
                table: "AdminUsers",
                keyColumn: "AdminId",
                keyValue: 3,
                columns: new[] { "Email", "IsVerified", "OtpCode", "OtpExpiry", "Password", "Username" },
                values: new object[] { "rounakkeshri79@gmail.com", false, null, null, null, "rounakkeshri79" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "IsVerified",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "OtpCode",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "OtpExpiry",
                table: "AdminUsers");

            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "AdminUsers",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AdminUsers",
                keyColumn: "AdminId",
                keyValue: 1,
                columns: new[] { "Password", "Username" },
                values: new object[] { "admin@123", "admin_main" });

            migrationBuilder.UpdateData(
                table: "AdminUsers",
                keyColumn: "AdminId",
                keyValue: 2,
                columns: new[] { "Password", "Username" },
                values: new object[] { "admin@123", "admin_friend1" });

            migrationBuilder.UpdateData(
                table: "AdminUsers",
                keyColumn: "AdminId",
                keyValue: 3,
                columns: new[] { "Password", "Username" },
                values: new object[] { "admin@123", "admin_friend2" });
        }
    }
}
