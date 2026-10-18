using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinGuard.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRazorpayOrdersAndHardenBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RazorpayOrders");

            migrationBuilder.DropColumn(
                name: "RazorpaySignature",
                table: "Repayments");

            migrationBuilder.RenameColumn(
                name: "RazorpayPaymentId",
                table: "Repayments",
                newName: "TransactionReference");

            migrationBuilder.RenameColumn(
                name: "RazorpayOrderId",
                table: "Repayments",
                newName: "BankReference");

            migrationBuilder.RenameIndex(
                name: "IX_Repayments_RazorpayPaymentId",
                table: "Repayments",
                newName: "IX_Repayments_TransactionReference");

            migrationBuilder.RenameIndex(
                name: "IX_Repayments_RazorpayOrderId",
                table: "Repayments",
                newName: "IX_Repayments_BankReference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TransactionReference",
                table: "Repayments",
                newName: "RazorpayPaymentId");

            migrationBuilder.RenameColumn(
                name: "BankReference",
                table: "Repayments",
                newName: "RazorpayOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Repayments_TransactionReference",
                table: "Repayments",
                newName: "IX_Repayments_RazorpayPaymentId");

            migrationBuilder.RenameIndex(
                name: "IX_Repayments_BankReference",
                table: "Repayments",
                newName: "IX_Repayments_RazorpayOrderId");

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
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AmountInPaise = table.Column<long>(type: "bigint", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsTestMode = table.Column<bool>(type: "boolean", nullable: false),
                    RazorpayOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RazorpayPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Receipt = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
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
        }
    }
}
