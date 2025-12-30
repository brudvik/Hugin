using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hugin.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_suspended = table.Column<bool>(type: "boolean", nullable: false),
                    suspension_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_operator = table.Column<bool>(type: "boolean", nullable: false),
                    operator_privileges = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nickname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ident = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    realname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, defaultValue: "services.bot"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uid = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channel_bots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    greet_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    auto_greet = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_bots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "memos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_nickname = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sender_hostmask = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sender_account = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    target = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    tags = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registered_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    founder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    modes = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    keep_topic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    secure = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    successor_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registered_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "server_bans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ban_type = table.Column<int>(type: "integer", nullable: false),
                    pattern = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    set_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_server_bans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "server_links",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sid = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    send_password = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    receive_password = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    auto_connect = table.Column<bool>(type: "boolean", nullable: false),
                    use_tls = table.Column<bool>(type: "boolean", nullable: false),
                    certificate_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    link_class = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_server_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "virtual_hosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hostname = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_virtual_hosts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "certificate_fingerprints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_certificate_fingerprints", x => x.id);
                    table.ForeignKey(
                        name: "FK_certificate_fingerprints_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "registered_nicknames",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nickname = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registered_nicknames", x => x.id);
                    table.ForeignKey(
                        name: "FK_registered_nicknames_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_email",
                table: "accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_name",
                table: "accounts",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bots_nickname",
                table: "bots",
                column: "nickname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bots_uid",
                table: "bots",
                column: "uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_certificate_fingerprints_account_id",
                table: "certificate_fingerprints",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_certificate_fingerprints_fingerprint",
                table: "certificate_fingerprints",
                column: "fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_bots_bot_id",
                table: "channel_bots",
                column: "bot_id");

            migrationBuilder.CreateIndex(
                name: "IX_channel_bots_bot_id_channel_name",
                table: "channel_bots",
                columns: new[] { "bot_id", "channel_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_bots_channel_name",
                table: "channel_bots",
                column: "channel_name");

            migrationBuilder.CreateIndex(
                name: "IX_memos_recipient_id",
                table: "memos",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "IX_memos_recipient_id_read_at",
                table: "memos",
                columns: new[] { "recipient_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "IX_memos_sender_id",
                table: "memos",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_message_id",
                table: "messages",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_target_timestamp",
                table: "messages",
                columns: new[] { "target", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_timestamp",
                table: "messages",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_registered_channels_founder_id",
                table: "registered_channels",
                column: "founder_id");

            migrationBuilder.CreateIndex(
                name: "IX_registered_channels_name",
                table: "registered_channels",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_registered_nicknames_account_id",
                table: "registered_nicknames",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_registered_nicknames_nickname",
                table: "registered_nicknames",
                column: "nickname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_server_bans_ban_type",
                table: "server_bans",
                column: "ban_type");

            migrationBuilder.CreateIndex(
                name: "IX_server_bans_expires_at",
                table: "server_bans",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_server_bans_pattern",
                table: "server_bans",
                column: "pattern");

            migrationBuilder.CreateIndex(
                name: "IX_server_links_name",
                table: "server_links",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_server_links_sid",
                table: "server_links",
                column: "sid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_virtual_hosts_account_id",
                table: "virtual_hosts",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_virtual_hosts_account_id_is_active",
                table: "virtual_hosts",
                columns: new[] { "account_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_virtual_hosts_approved_at",
                table: "virtual_hosts",
                column: "approved_at");

            migrationBuilder.CreateIndex(
                name: "IX_virtual_hosts_hostname",
                table: "virtual_hosts",
                column: "hostname");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bots");

            migrationBuilder.DropTable(
                name: "certificate_fingerprints");

            migrationBuilder.DropTable(
                name: "channel_bots");

            migrationBuilder.DropTable(
                name: "memos");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "registered_channels");

            migrationBuilder.DropTable(
                name: "registered_nicknames");

            migrationBuilder.DropTable(
                name: "server_bans");

            migrationBuilder.DropTable(
                name: "server_links");

            migrationBuilder.DropTable(
                name: "virtual_hosts");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
