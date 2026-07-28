using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIG.ClientCard.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    last_name = table.Column<string>(type: "TEXT", nullable: false),
                    first_name = table.Column<string>(type: "TEXT", nullable: false),
                    address = table.Column<string>(type: "TEXT", nullable: false),
                    phone = table.Column<string>(type: "TEXT", nullable: false),
                    phone_raw = table.Column<string>(type: "TEXT", nullable: false),
                    email = table.Column<string>(type: "TEXT", nullable: true),
                    acquisition_source = table.Column<int>(type: "INTEGER", nullable: false),
                    acquisition_detail = table.Column<string>(type: "TEXT", nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    medical_flag = table.Column<bool>(type: "INTEGER", nullable: false),
                    gp_details = table.Column<string>(type: "TEXT", nullable: true),
                    patch_test_on = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    patch_test_result = table.Column<int>(type: "INTEGER", nullable: false),
                    card_photo_path = table.Column<string>(type: "TEXT", nullable: true),
                    salon_id = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by_device = table.Column<string>(type: "TEXT", nullable: false),
                    deleted_at = table.Column<long>(type: "INTEGER", nullable: true),
                    sync_seq = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "salon",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    retention_years = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_salon", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_dead_letter",
                columns: table => new
                {
                    op_id = table.Column<string>(type: "TEXT", nullable: false),
                    entity = table.Column<string>(type: "TEXT", nullable: false),
                    entity_id = table.Column<string>(type: "TEXT", nullable: false),
                    operation = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    parked_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_dead_letter", x => x.op_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_outbox",
                columns: table => new
                {
                    op_id = table.Column<string>(type: "TEXT", nullable: false),
                    entity = table.Column<string>(type: "TEXT", nullable: false),
                    entity_id = table.Column<string>(type: "TEXT", nullable: false),
                    operation = table.Column<string>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    last_error = table.Column<string>(type: "TEXT", nullable: true),
                    next_attempt_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_outbox", x => x.op_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    cursor = table.Column<long>(type: "INTEGER", nullable: false),
                    last_pull_at = table.Column<long>(type: "INTEGER", nullable: true),
                    last_push_at = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "client_consent",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    client_id = table.Column<string>(type: "TEXT", nullable: false),
                    purpose = table.Column<int>(type: "INTEGER", nullable: false),
                    granted_at = table.Column<long>(type: "INTEGER", nullable: false),
                    withdrawn_at = table.Column<long>(type: "INTEGER", nullable: true),
                    captured_by_user_id = table.Column<string>(type: "TEXT", nullable: true),
                    signature_blob_ref = table.Column<string>(type: "TEXT", nullable: true),
                    salon_id = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by_device = table.Column<string>(type: "TEXT", nullable: false),
                    deleted_at = table.Column<long>(type: "INTEGER", nullable: true),
                    sync_seq = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_consent", x => x.id);
                    table.ForeignKey(
                        name: "FK_client_consent_client_client_id",
                        column: x => x.client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_note",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    client_id = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    author_user_id = table.Column<string>(type: "TEXT", nullable: true),
                    body = table.Column<string>(type: "TEXT", nullable: false),
                    salon_id = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by_device = table.Column<string>(type: "TEXT", nullable: false),
                    deleted_at = table.Column<long>(type: "INTEGER", nullable: true),
                    sync_seq = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_note", x => x.id);
                    table.ForeignKey(
                        name: "FK_client_note_client_client_id",
                        column: x => x.client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_record",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    client_id = table.Column<string>(type: "TEXT", nullable: false),
                    performed_on = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    service_description = table.Column<string>(type: "TEXT", nullable: false),
                    price = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    currency_code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    service_catalog_id = table.Column<string>(type: "TEXT", nullable: true),
                    photo_path = table.Column<string>(type: "TEXT", nullable: true),
                    salon_id = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_by_device = table.Column<string>(type: "TEXT", nullable: false),
                    deleted_at = table.Column<long>(type: "INTEGER", nullable: true),
                    sync_seq = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_record", x => x.id);
                    table.ForeignKey(
                        name: "FK_service_record_client_client_id",
                        column: x => x.client_id,
                        principalTable: "client",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_last_name_first_name",
                table: "client",
                columns: new[] { "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "IX_client_salon_id_sync_seq",
                table: "client",
                columns: new[] { "salon_id", "sync_seq" });

            migrationBuilder.CreateIndex(
                name: "IX_client_consent_client_id_purpose",
                table: "client_consent",
                columns: new[] { "client_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_client_consent_salon_id_sync_seq",
                table: "client_consent",
                columns: new[] { "salon_id", "sync_seq" });

            migrationBuilder.CreateIndex(
                name: "IX_client_note_client_id_created_at",
                table: "client_note",
                columns: new[] { "client_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_client_note_salon_id_sync_seq",
                table: "client_note",
                columns: new[] { "salon_id", "sync_seq" });

            migrationBuilder.CreateIndex(
                name: "IX_service_record_client_id_performed_on",
                table: "service_record",
                columns: new[] { "client_id", "performed_on" });

            migrationBuilder.CreateIndex(
                name: "IX_service_record_salon_id_sync_seq",
                table: "service_record",
                columns: new[] { "salon_id", "sync_seq" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_created",
                table: "sync_outbox",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_consent");

            migrationBuilder.DropTable(
                name: "client_note");

            migrationBuilder.DropTable(
                name: "salon");

            migrationBuilder.DropTable(
                name: "service_record");

            migrationBuilder.DropTable(
                name: "sync_dead_letter");

            migrationBuilder.DropTable(
                name: "sync_outbox");

            migrationBuilder.DropTable(
                name: "sync_state");

            migrationBuilder.DropTable(
                name: "client");
        }
    }
}
