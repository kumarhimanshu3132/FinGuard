using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinGuard.Migrations
{
    /// <inheritdoc />
    public partial class AddKycDocsToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedIdDocument",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedPan",
                table: "Customers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedIdDocument",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EncryptedPan",
                table: "Customers");
        }
    }
}
