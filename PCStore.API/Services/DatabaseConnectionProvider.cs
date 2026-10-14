using Npgsql;

namespace PCStore.API.Services;

public sealed class DatabaseConnectionProvider(IConfiguration configuration)
{
    private string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

    public string ConnectionString => Volatile.Read(ref _connectionString);

    public void SetConnectionString(string connectionString)
    {
        _ = new NpgsqlConnectionStringBuilder(connectionString);
        Volatile.Write(ref _connectionString, connectionString);
    }
}
