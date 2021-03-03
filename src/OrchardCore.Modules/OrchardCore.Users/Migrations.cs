using OrchardCore.Data.Migration;
using OrchardCore.Users.Indexes;

namespace OrchardCore.Users
{
    public class Migrations : DataMigration
    {
        public int Create()
        {
            SchemaBuilder.CreateMapIndexTable(nameof(UserIndex), table => table
                .Column<string>("NormalizedUserName")
                .Column<string>("NormalizedEmail")
            );

            SchemaBuilder.CreateReduceIndexTable(nameof(UserByRoleNameIndex), table => table
                .Column<string>("RoleName")
                .Column<int>("Count")
            );

            return UpdateFrom1();
        }

        public int UpdateFrom1()
        {
            SchemaBuilder.CreateMapIndexTable(nameof(UserByLoginInfoIndex), table => table
                .Column<string>("LoginProvider")
                .Column<string>("ProviderKey"));
            return 2;
        }

        public int UpdateFrom2()
        {
            SchemaBuilder.CreateMapIndexTable(nameof(UserByClaimIndex), table => table
                .Column<string>(nameof(UserByClaimIndex.ClaimType))
                .Column<string>(nameof(UserByClaimIndex.ClaimValue)));
            return 3;
        }

        public int UpdateFrom3()
        {
            SchemaBuilder.AlterTable(nameof(UserIndex), table => table
                .AddColumn<bool>(nameof(UserIndex.IsEnabled), c => c.NotNull().WithDefault(true)));

            SchemaBuilder.AlterTable(nameof(UserIndex), table => table
                .CreateIndex($"IDX_{nameof(UserIndex)}_{nameof(UserIndex.IsEnabled)}", "DocumentId", nameof(UserIndex.IsEnabled))
            );

            return 4;
        }
        public int UpdateFrom4()
        {
            SchemaBuilder.AlterTable(nameof(UserIndex), table =>
            {
                table.CreateIndex($"IDX_{nameof(UserIndex)}_{nameof(UserIndex.NormalizedUserName)}", "DocumentId", nameof(UserIndex.NormalizedUserName));
                table.CreateIndex($"IDX_{nameof(UserIndex)}_{nameof(UserIndex.NormalizedEmail)}", "DocumentId", nameof(UserIndex.NormalizedEmail));
            });
            SchemaBuilder.AlterTable(nameof(UserByRoleNameIndex), table =>
            {
                table.CreateIndex($"IDX_{nameof(UserByRoleNameIndex)}_{nameof(UserByRoleNameIndex.RoleName)}", nameof(UserByRoleNameIndex.RoleName));
            });
            SchemaBuilder.AlterTable($"{nameof(UserByRoleNameIndex)}_Document", table =>
            {
                table.CreateIndex($"IDX_{nameof(UserByRoleNameIndex)}_Document_{nameof(UserByRoleNameIndex.RoleName)}", "DocumentId", $"{nameof(UserByRoleNameIndex)}Id");
            });
            SchemaBuilder.AlterTable(nameof(UserByLoginInfoIndex), table =>
            {
                table.CreateIndex($"IDX_{nameof(UserByLoginInfoIndex)}_{nameof(UserByLoginInfoIndex.LoginProvider)}_{nameof(UserByLoginInfoIndex.ProviderKey)}",
                    new[] { "DocumentId", nameof(UserByLoginInfoIndex.LoginProvider), nameof(UserByLoginInfoIndex.ProviderKey) });
            });
            SchemaBuilder.AlterTable(nameof(UserByClaimIndex), table =>
            {
                table.CreateIndex($"IDX_{nameof(UserByClaimIndex)}_{nameof(UserByClaimIndex.ClaimType)}_{nameof(UserByClaimIndex.ClaimValue)}",
                    new[] { "DocumentId", nameof(UserByClaimIndex.ClaimType), nameof(UserByClaimIndex.ClaimValue) });
            });
            return 5;
        }
    }
}
