using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yasda.Modules.Knowledge.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file)
        {
            return EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);
        }

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("knowledge.search_articles.v1.sql"));
            migrationBuilder.Sql(Sql("knowledge.get_article.v1.sql"));
            migrationBuilder.Sql(Sql("knowledge.search_published_articles.v1.sql"));
            migrationBuilder.Sql(Sql("knowledge.get_published_article.v1.sql"));
            migrationBuilder.Sql(Sql("knowledge.popular_articles.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.popular_articles(date, boolean, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.get_published_article(bigint, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.count_published_articles(text, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.search_published_articles(text, boolean, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.get_article_history(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.get_article_editors(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.get_article_versions(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.get_article(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.count_articles(text, text, text, bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS knowledge.search_articles(text, text, text, bigint, text, boolean, integer, integer);");
        }
    }
}
