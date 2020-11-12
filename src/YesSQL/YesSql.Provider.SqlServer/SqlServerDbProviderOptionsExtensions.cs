using System;
using System.Data;
using Microsoft.Data.SqlClient;

namespace YesSql.Provider.SqlServer
{
    public static class SqlServerDbProviderOptionsExtensions
    {
        public static IConfiguration RegisterSqlServer(this IConfiguration configuration)
        {
            SqlDialectFactory.Register(typeof(SqlConnection), new SqlServerDialect());
            CommandInterpreterFactory.Register(typeof(SqlConnection), d => new SqlServerCommandInterpreter(d));

            return configuration;
        }

        public static IConfiguration UseSqlServer(
            this IConfiguration configuration,
            string connectionString)
        {
            return UseSqlServer(configuration, connectionString, IsolationLevel.ReadUncommitted);
        }

        public static IConfiguration UseSqlServer(
            this IConfiguration configuration,
            string connectionString,
            IsolationLevel isolationLevel)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (String.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException(nameof(connectionString));
            }
            try
            {


                RegisterSqlServer(configuration);
            }catch(Exception ex)
            {
                throw ex;
            }
            configuration.ConnectionFactory = new DbConnectionFactory<SqlConnection>(connectionString);
            configuration.IsolationLevel = isolationLevel;

            return configuration;
        }
    }
}
