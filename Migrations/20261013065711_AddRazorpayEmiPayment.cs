using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddRazorpayEmiPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "CollectedBy",
                table: "Repayments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountPaid",
                table: "Repayments",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Repayments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTestMode",
                table: "Repayments",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Repayments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Repayments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Success");

            migrationBuilder.AddColumn<string>(
                name: "RazorpayOrderId",
                table: "Repayments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RazorpayPaymentId",
                table: "Repayments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RazorpaySignature",
                table: "Repayments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RazorpayOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    LoanId = table.Column<int>(type: "integer", nullable: false),
                    RazorpayOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AmountInPaise = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Receipt = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsTestMode = table.Column<bool>(type: "boolean", nullable: false),
                    RazorpayPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RazorpayOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RazorpayOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RazorpayOrders_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "LoanId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Repayments_LoanId",
                table: "Repayments",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_Repayments_RazorpayOrderId",
                table: "Repayments",
                column: "RazorpayOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Repayments_RazorpayPaymentId",
                table: "Repayments",
                column: "RazorpayPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_RazorpayOrders_CustomerId",
                table: "RazorpayOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RazorpayOrders_LoanId",
                table: "RazorpayOrders",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_RazorpayOrders_RazorpayOrderId",
                table: "RazorpayOrders",
                column: "RazorpayOrderId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Repayments_Loans_LoanId",
                table: "Repayments",
                column: "LoanId",
                principalTable: "Loans",
                principalColumn: "LoanId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repayments_Loans_LoanId",
                table: "Repayments");

            migrationBuilder.DropTable(
                name: "RazorpayOrders");

            migrationBuilder.DropIndex(
                name: "IX_Repayments_LoanId",
                table: "Repayments");

            migrationBuilder.DropIndex(
                name: "IX_Repayments_RazorpayOrderId",
                table: "Repayments");

            migrationBuilder.DropIndex(
                name: "IX_Repayments_RazorpayPaymentId",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "IsTestMode",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "RazorpayOrderId",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "RazorpayPaymentId",
                table: "Repayments");

            migrationBuilder.DropColumn(
                name: "RazorpaySignature",
                table: "Repayments");

            migrationBuilder.AlterColumn<string>(
                name: "CollectedBy",
                table: "Repayments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<decimal>(
                name: "AmountPaid",
                table: "Repayments",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");
        }
    }
}
