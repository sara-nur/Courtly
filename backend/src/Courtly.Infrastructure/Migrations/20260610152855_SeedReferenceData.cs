using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Courtly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "id", "concurrency_stamp", "name", "normalized_name" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "r-admin-stamp", "Admin", "ADMIN" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "r-staff-stamp", "Staff", "STAFF" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "r-user-stamp", "User", "USER" }
                });

            migrationBuilder.InsertData(
                table: "amenities",
                columns: new[] { "id", "icon_key", "name" },
                values: new object[,]
                {
                    { 1L, "lights", "Floodlights" },
                    { 2L, "showers", "Showers" },
                    { 3L, "locker", "Lockers" },
                    { 4L, "parking", "Parking" },
                    { 5L, "proshop", "Pro Shop" }
                });

            migrationBuilder.InsertData(
                table: "countries",
                columns: new[] { "id", "iso_code", "name" },
                values: new object[] { 1L, "BIH", "Bosnia and Herzegovina" });

            migrationBuilder.InsertData(
                table: "court_types",
                columns: new[] { "id", "description", "name" },
                values: new object[,]
                {
                    { 1L, "Standard singles court.", "Singles" },
                    { 2L, "Wider doubles court.", "Doubles" },
                    { 3L, "Practice / coaching court.", "Training" },
                    { 4L, "Multi-purpose court.", "Multi" }
                });

            migrationBuilder.InsertData(
                table: "surface_types",
                columns: new[] { "id", "description", "name" },
                values: new object[,]
                {
                    { 1L, "Slow surface, high bounce.", "Clay" },
                    { 2L, "Fast surface, low bounce.", "Grass" },
                    { 3L, "Medium-fast, consistent bounce.", "Hard" },
                    { 4L, "Indoor textile surface.", "Carpet" }
                });

            migrationBuilder.InsertData(
                table: "cities",
                columns: new[] { "id", "country_id", "name" },
                values: new object[,]
                {
                    { 1L, 1L, "Sarajevo" },
                    { 2L, 1L, "Mostar" },
                    { 3L, 1L, "Tuzla" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"));

            migrationBuilder.DeleteData(
                table: "amenities",
                keyColumn: "id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "amenities",
                keyColumn: "id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "amenities",
                keyColumn: "id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "amenities",
                keyColumn: "id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "amenities",
                keyColumn: "id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "cities",
                keyColumn: "id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "court_types",
                keyColumn: "id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "court_types",
                keyColumn: "id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "court_types",
                keyColumn: "id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "court_types",
                keyColumn: "id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "surface_types",
                keyColumn: "id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "surface_types",
                keyColumn: "id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "surface_types",
                keyColumn: "id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "surface_types",
                keyColumn: "id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "countries",
                keyColumn: "id",
                keyValue: 1L);
        }
    }
}
