using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIG.ClientCard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attachment_queue",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    relative_path = table.Column<string>(type: "TEXT", nullable: false),
                    content_type = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    uploaded_at = table.Column<long>(type: "INTEGER", nullable: true),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    next_attempt_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachment_queue", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_catalog",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    default_price = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    salon_id = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by_device = table.Column<string>(type: "TEXT", nullable: false),
                    deleted_at = table.Column<long>(type: "INTEGER", nullable: true),
                    sync_seq = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_catalog", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attachment_queue_uploaded_at",
                table: "attachment_queue",
                column: "uploaded_at");

            migrationBuilder.CreateIndex(
                name: "IX_service_catalog_name",
                table: "service_catalog",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "IX_service_catalog_salon_id_sync_seq",
                table: "service_catalog",
                columns: new[] { "salon_id", "sync_seq" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachment_queue");

            migrationBuilder.DropTable(
                name: "service_catalog");
        }
    }
}
