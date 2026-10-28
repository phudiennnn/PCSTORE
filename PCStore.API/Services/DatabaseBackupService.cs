using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;

namespace PCStore.API.Services;

public sealed record DatabaseBackupInfo(string Id, DateTime CreatedAtUtc, long SizeBytes, bool IsSafetyBackup);

public sealed class DatabaseBackupService
{
    private static readonly Regex BackupIdPattern = new(
        @"^(backup|safety)-\d{17}-[a-f0-9]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly DatabaseConnectionProvider _connectionProvider;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly string _backupDirectory;
    private readonly string _pgDumpPath;
    private readonly string _pgRestorePath;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _requestLock = new();
    private int _isRestoring;
    private int _activeRequests;
    private TaskCompletionSource? _requestsDrained;

    public DatabaseBackupService(
        DatabaseConnectionProvider connectionProvider,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<DatabaseBackupService> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
        var configuredDirectory = configuration["Backup:Directory"];
        _backupDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "Backups")
            : Path.IsPathRooted(configuredDirectory)
                ? configuredDirectory
                : Path.Combine(environment.ContentRootPath, configuredDirectory));
        _pgDumpPath = configuration["Backup:PgDumpPath"] ?? "pg_dump";
        _pgRestorePath = configuration["Backup:PgRestorePath"] ?? "pg_restore";
    }

    public bool IsRestoring => Volatile.Read(ref _isRestoring) == 1;

    public bool TryEnterRequest()
    {
        lock (_requestLock)
        {
            if (_isRestoring == 1)
                return false;

            _activeRequests++;
            return true;
        }
    }

    public void ExitRequest()
    {
        lock (_requestLock)
        {
            _activeRequests--;
            if (_activeRequests == 0)
                _requestsDrained?.TrySetResult();
        }
    }

    public IReadOnlyList<DatabaseBackupInfo> GetBackups()
    {
        if (!Directory.Exists(_backupDirectory))
            return [];

        return Directory.EnumerateFiles(_backupDirectory, "*.dump")
            .Select(path => new FileInfo(path))
            .Where(file => BackupIdPattern.IsMatch(Path.GetFileNameWithoutExtension(file.Name)))
            .Select(file =>
            {
                var id = Path.GetFileNameWithoutExtension(file.Name);
                var isSafetyBackup = id.StartsWith("safety-", StringComparison.Ordinal);
                var timestamp = id[(isSafetyBackup ? "safety-" : "backup-").Length..^33];
                return DateTime.TryParseExact(
                    timestamp,
                    "yyyyMMddHHmmssfff",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var createdAt)
                    ? new DatabaseBackupInfo(id, createdAt, file.Length, isSafetyBackup)
                    : null;
            })
            .Where(backup => backup is not null)
            .Select(backup => backup!)
            .OrderByDescending(backup => backup.CreatedAtUtc)
            .ToList();
    }

    public FileStream OpenBackupRead(string backupId)
    {
        var path = GetBackupPath(backupId);
        if (!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy bản sao lưu cần tải xuống.");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public async Task<DatabaseBackupInfo> CreateBackupAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            return await CreateBackupCoreAsync(cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<(DatabaseBackupInfo RestoredBackup, DatabaseBackupInfo SafetyBackup)> RestoreAsync(
        string backupId,
        CancellationToken cancellationToken)
    {
        var backupPath = GetBackupPath(backupId);
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Không tìm thấy bản sao lưu cần phục hồi.");

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await BeginRestoreAndDrainRequestsAsync();
            await RunToolAsync(_pgRestorePath, ["--list", backupPath], cancellationToken);
            var safetyBackup = await CreateBackupCoreAsync(cancellationToken, isSafetyBackup: true);

            try
            {
                NpgsqlConnection.ClearAllPools();
                await ResetSchemasAsync(cancellationToken);
                await RestoreDumpAsync(backupPath, cancellationToken);
                NpgsqlConnection.ClearAllPools();
            }
            catch (Exception restoreException)
            {
                _logger.LogError(restoreException, "Restoring database backup {BackupId} failed; attempting recovery from {SafetyBackupId}.", backupId, safetyBackup.Id);
                try
                {
                    NpgsqlConnection.ClearAllPools();
                    await ResetSchemasAsync(CancellationToken.None);
                    await RestoreDumpAsync(GetBackupPath(safetyBackup.Id), CancellationToken.None);
                    NpgsqlConnection.ClearAllPools();
                }
                catch (Exception recoveryException)
                {
                    _logger.LogCritical(recoveryException, "Automatic recovery from safety backup {SafetyBackupId} failed.", safetyBackup.Id);
                    throw new InvalidOperationException(
                        $"Phục hồi thất bại và hệ thống không thể tự khôi phục từ bản an toàn {safetyBackup.Id}. Hãy giữ nguyên file sao lưu và khôi phục thủ công.",
                        recoveryException);
                }

                throw new InvalidOperationException(
                    $"Phục hồi thất bại. Dữ liệu trước thao tác đã được khôi phục từ bản an toàn {safetyBackup.Id}.",
                    restoreException);
            }

            var restoredFile = new FileInfo(backupPath);
            var restored = GetBackups().FirstOrDefault(backup => backup.Id == backupId)
                ?? new DatabaseBackupInfo(backupId, restoredFile.LastWriteTimeUtc, restoredFile.Length, false);
            return (restored, safetyBackup);
        }
        finally
        {
            EndRestore();
            _operationLock.Release();
        }
    }

    private async Task<DatabaseBackupInfo> CreateBackupCoreAsync(
        CancellationToken cancellationToken,
        bool isSafetyBackup = false)
    {
        Directory.CreateDirectory(_backupDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_backupDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var createdAt = DateTime.UtcNow;
        var prefix = isSafetyBackup ? "safety" : "backup";
        var id = $"{prefix}-{createdAt.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
        var backupPath = GetBackupPath(id);
        var temporaryPath = $"{backupPath}.tmp";
        try
        {
            await RunToolAsync(_pgDumpPath, ["--format=custom", "--no-owner", "--no-acl", "--file", temporaryPath], cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporaryPath, backupPath);

            var file = new FileInfo(backupPath);
            return new DatabaseBackupInfo(id, createdAt, file.Length, isSafetyBackup);
        }
        catch
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            throw;
        }
    }

    private async Task RestoreDumpAsync(string backupPath, CancellationToken cancellationToken)
    {
        await RunToolAsync(_pgRestorePath, [
            "--exit-on-error",
            "--no-owner",
            "--no-acl",
            "--single-transaction",
            "--dbname", GetLibpqConnectionString(),
            backupPath,
        ], cancellationToken);
    }

    private async Task ResetSchemasAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionProvider.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT nspname
            FROM pg_catalog.pg_namespace
            WHERE nspname <> 'information_schema'
              AND left(nspname, 3) <> 'pg_'
            """;
        var schemas = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                schemas.Add(reader.GetString(0));
        }

        foreach (var schema in schemas)
        {
            var quotedSchema = $"\"{schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        command.CommandText = """CREATE SCHEMA public""";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task BeginRestoreAndDrainRequestsAsync()
    {
        Task? drainTask = null;
        lock (_requestLock)
        {
            Volatile.Write(ref _isRestoring, 1);
            if (_activeRequests > 0)
            {
                _requestsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                drainTask = _requestsDrained.Task;
            }
        }

        if (drainTask is not null)
            await drainTask;
    }

    private void EndRestore()
    {
        lock (_requestLock)
        {
            Volatile.Write(ref _isRestoring, 0);
            _requestsDrained = null;
        }
    }

    private async Task RunToolAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            processStartInfo.ArgumentList.Add(argument);

        var connection = new NpgsqlConnectionStringBuilder(_connectionProvider.ConnectionString);
        var host = connection.Host;
        var username = connection.Username;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(connection.Database))
            throw new InvalidOperationException("Cấu hình kết nối PostgreSQL thiếu máy chủ, tên cơ sở dữ liệu hoặc tài khoản.");
        processStartInfo.ArgumentList.Add("--host");
        processStartInfo.ArgumentList.Add(host);
        processStartInfo.ArgumentList.Add("--port");
        processStartInfo.ArgumentList.Add(connection.Port.ToString(CultureInfo.InvariantCulture));
        processStartInfo.ArgumentList.Add("--username");
        processStartInfo.ArgumentList.Add(username);
        if (arguments.Contains("--file"))
        {
            processStartInfo.ArgumentList.Add("--dbname");
            processStartInfo.ArgumentList.Add(GetLibpqConnectionString());
        }

        processStartInfo.Environment["PGPASSWORD"] = connection.Password ?? string.Empty;
        processStartInfo.Environment["PGSSLMODE"] = GetLibpqSslMode(connection);
        processStartInfo.Environment["PGCONNECT_TIMEOUT"] = Math.Max(1, connection.Timeout).ToString(CultureInfo.InvariantCulture);
        if (connection.TryGetValue("Root Certificate", out var rootCertificate)
            && rootCertificate is string rootCertificatePath
            && !string.IsNullOrWhiteSpace(rootCertificatePath))
            processStartInfo.Environment["PGSSLROOTCERT"] = rootCertificatePath;

        using var process = new Process { StartInfo = processStartInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Không thể khởi chạy công cụ PostgreSQL.");

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "PostgreSQL tool {Executable} failed with exit code {ExitCode}. stdout: {StdOut}; stderr: {StdErr}",
                    executable,
                    process.ExitCode,
                    standardOutput,
                    standardError);
                throw new InvalidOperationException(
                    $"Công cụ PostgreSQL {Path.GetFileName(executable)} lỗi (mã {process.ExitCode}). Hãy kiểm tra log máy chủ.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _logger.LogError(exception, "Could not start PostgreSQL tool {Executable}.", executable);
            throw new InvalidOperationException(
                $"Không chạy được {Path.GetFileName(executable)}. Cài PostgreSQL client tools hoặc cấu hình Backup:{(executable == _pgDumpPath ? "PgDumpPath" : "PgRestorePath")}.",
                exception);
        }
    }

    private string GetLibpqConnectionString()
    {
        var connection = new NpgsqlConnectionStringBuilder(_connectionProvider.ConnectionString);
        var host = connection.Host;
        var database = connection.Database;
        var username = connection.Username;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("Cấu hình kết nối PostgreSQL thiếu máy chủ, tên cơ sở dữ liệu hoặc tài khoản.");
        var connectionString = $"host={QuoteLibpq(host)} port={connection.Port.ToString(CultureInfo.InvariantCulture)} dbname={QuoteLibpq(database)} user={QuoteLibpq(username)} sslmode={GetLibpqSslMode(connection)}";
        if (connection.TryGetValue("Root Certificate", out var rootCertificate)
            && rootCertificate is string rootCertificatePath
            && !string.IsNullOrWhiteSpace(rootCertificatePath))
            connectionString += $" sslrootcert={QuoteLibpq(rootCertificatePath)}";
        return connectionString;
    }

    private static string GetLibpqSslMode(NpgsqlConnectionStringBuilder connection)
    {
        var mode = connection.TryGetValue("SSL Mode", out var value) ? value?.ToString() : null;
        return mode?.ToLowerInvariant() switch
        {
            "disable" => "disable",
            "prefer" => "prefer",
            "require" => "require",
            "verifyca" or "verify-ca" => "verify-ca",
            "verifyfull" or "verify-full" => "verify-full",
            _ => "prefer",
        };
    }

    private static string QuoteLibpq(string value) =>
        $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}'";

    private string GetBackupPath(string backupId)
    {
        if (!BackupIdPattern.IsMatch(backupId))
            throw new ArgumentException("Mã bản sao lưu không hợp lệ.", nameof(backupId));
        return Path.Combine(_backupDirectory, $"{backupId}.dump");
    }
}
