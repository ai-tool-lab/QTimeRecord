using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QTimeRecord.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    store_code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    store_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    company_name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    announcement_title = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    business_day_start = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    business_day_end = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "admin_credentials",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    salt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    iterations = table.Column<int>(type: "INTEGER", nullable: false),
                    failed_count = table.Column<int>(type: "INTEGER", nullable: false),
                    locked_until = table.Column<DateTime>(type: "TEXT", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_credentials", x => x.store_id);
                    table.ForeignKey(
                        name: "fk_admin_credentials_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    store_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    display_order = table.Column<int>(type: "INTEGER", nullable: false),
                    heading = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    body = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                    table.ForeignKey(
                        name: "fk_announcements_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_settings",
                columns: table => new
                {
                    store_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    com_port = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    baud_rate = table.Column<int>(type: "INTEGER", nullable: false),
                    data_bits = table.Column<int>(type: "INTEGER", nullable: false),
                    parity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    stop_bits = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_settings", x => x.store_id);
                    table.ForeignKey(
                        name: "fk_device_settings_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    store_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    staff_no = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name_kana = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    employment_type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_qr_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    store_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    staff_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    token = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    issued_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_qr_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_qr_tokens_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "time_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    store_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    staff_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    record_type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    work_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    entry_method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    is_outside_business_hours = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_time_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_time_records_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_store_id_display_order",
                table: "announcements",
                columns: new[] { "store_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_store_id_staff_no",
                table: "staff",
                columns: new[] { "store_id", "staff_no" },
                unique: true,
                filter: "staff_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_staff_store_id_status",
                table: "staff",
                columns: new[] { "store_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_qr_tokens_staff_id_revoked_at",
                table: "staff_qr_tokens",
                columns: new[] { "staff_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_qr_tokens_token",
                table: "staff_qr_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_time_records_staff_id_recorded_at",
                table: "time_records",
                columns: new[] { "staff_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_time_records_staff_id_work_date",
                table: "time_records",
                columns: new[] { "staff_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ix_time_records_store_id_work_date",
                table: "time_records",
                columns: new[] { "store_id", "work_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_credentials");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "device_settings");

            migrationBuilder.DropTable(
                name: "staff_qr_tokens");

            migrationBuilder.DropTable(
                name: "time_records");

            migrationBuilder.DropTable(
                name: "staff");

            migrationBuilder.DropTable(
                name: "stores");
        }
    }
}
