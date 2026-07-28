using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIG.ClientCard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReconcileTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_reconcile_at",
                table: "sync_state",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_reconcile_at",
                table: "sync_state");
        }
    }
}
