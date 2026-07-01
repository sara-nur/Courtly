using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Courtly.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationFeedbackUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recommendation_feedbacks_user_id",
                table: "recommendation_feedbacks");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_feedback_user_court",
                table: "recommendation_feedbacks",
                columns: new[] { "user_id", "court_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recommendation_feedback_user_court",
                table: "recommendation_feedbacks");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_feedbacks_user_id",
                table: "recommendation_feedbacks",
                column: "user_id");
        }
    }
}
