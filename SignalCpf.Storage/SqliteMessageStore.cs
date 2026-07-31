using Microsoft.Data.Sqlite;
using SignalCpf.Core.Models;
using SignalCpf.Core.Options;

namespace SignalCpf.Storage;

public interface IMessageStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task UpsertConversationAsync(Conversation conversation, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> ListConversationsAsync(int limit = 50, CancellationToken ct = default);
    Task AddMessageAsync(ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string conversationId, int limit = 50, CancellationToken ct = default);
    Task UpsertContactAsync(ContactRecord contact, CancellationToken ct = default);
    Task<IReadOnlyList<ContactRecord>> ListContactsAsync(CancellationToken ct = default);
    Task SaveSessionAsync(string address, byte[] record, CancellationToken ct = default);
    Task<byte[]?> LoadSessionAsync(string address, CancellationToken ct = default);
    Task SaveIdentityAsync(string address, byte[] identityKey, CancellationToken ct = default);
    Task<byte[]?> LoadIdentityAsync(string address, CancellationToken ct = default);
    Task SavePreKeyAsync(uint keyId, byte[] record, CancellationToken ct = default);
    Task<byte[]?> LoadPreKeyAsync(uint keyId, CancellationToken ct = default);
    Task RemovePreKeyAsync(uint keyId, CancellationToken ct = default);
    Task SaveSignedPreKeyAsync(uint keyId, byte[] record, CancellationToken ct = default);
    Task<byte[]?> LoadSignedPreKeyAsync(uint keyId, CancellationToken ct = default);
    Task SaveKyberPreKeyAsync(uint keyId, byte[] record, CancellationToken ct = default);
    Task<byte[]?> LoadKyberPreKeyAsync(uint keyId, CancellationToken ct = default);
    Task RemoveKyberPreKeyAsync(uint keyId, CancellationToken ct = default);
    Task<int> CountPreKeysAsync(CancellationToken ct = default);
    Task ClearSessionsAsync(CancellationToken ct = default);
    Task SaveSenderCertificateAsync(byte[] certificate, CancellationToken ct = default);
    Task<byte[]?> LoadSenderCertificateAsync(CancellationToken ct = default);
    Task SaveAttachmentMetaAsync(AttachmentRecord attachment, CancellationToken ct = default);
    Task<IReadOnlyList<AttachmentRecord>> ListAttachmentsAsync(string messageId, CancellationToken ct = default);
}

public sealed class ContactRecord
{
    public string ServiceId { get; set; } = "";
    public string? Number { get; set; }
    public string? ProfileName { get; set; }
    public byte[]? ProfileKey { get; set; }
    public string? About { get; set; }
}

public sealed class AttachmentRecord
{
    public string Id { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? LocalPath { get; set; }
    public string? CdnKey { get; set; }
}

public sealed class SqliteMessageStore : IMessageStore, IAsyncDisposable, IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _conn;

    public SqliteMessageStore(SignalServerOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        var dbPath = Path.Combine(options.DataDirectory, "signalcpf.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _conn = new SqliteConnection(_connectionString);
        await _conn.OpenAsync(ct);
        await ExecAsync("""
            CREATE TABLE IF NOT EXISTS conversations (
              id TEXT PRIMARY KEY,
              service_id TEXT,
              title TEXT NOT NULL,
              last_preview TEXT,
              last_at_ms INTEGER NOT NULL,
              unread INTEGER NOT NULL DEFAULT 0,
              is_group INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS messages (
              id TEXT PRIMARY KEY,
              conversation_id TEXT NOT NULL,
              sender_service_id TEXT,
              body TEXT,
              sent_at_ms INTEGER NOT NULL,
              received_at_ms INTEGER NOT NULL,
              is_outgoing INTEGER NOT NULL,
              status INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS contacts (
              service_id TEXT PRIMARY KEY,
              number TEXT,
              profile_name TEXT,
              profile_key BLOB,
              about TEXT
            );
            CREATE TABLE IF NOT EXISTS sessions (
              address TEXT PRIMARY KEY,
              record BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS identities (
              address TEXT PRIMARY KEY,
              identity_key BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS prekeys (
              key_id INTEGER PRIMARY KEY,
              record BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS signed_prekeys (
              key_id INTEGER PRIMARY KEY,
              record BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS kyber_prekeys (
              key_id INTEGER PRIMARY KEY,
              record BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS meta (
              key TEXT PRIMARY KEY,
              value BLOB NOT NULL
            );
            CREATE TABLE IF NOT EXISTS attachments (
              id TEXT PRIMARY KEY,
              message_id TEXT NOT NULL,
              file_name TEXT,
              content_type TEXT,
              size INTEGER NOT NULL,
              local_path TEXT,
              cdn_key TEXT
            );
            """, ct);
    }

    public async Task UpsertConversationAsync(Conversation conversation, CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversations(id, service_id, title, last_preview, last_at_ms, unread, is_group)
            VALUES($id, $sid, $title, $preview, $at, $unread, $group)
            ON CONFLICT(id) DO UPDATE SET
              service_id=excluded.service_id,
              title=excluded.title,
              last_preview=excluded.last_preview,
              last_at_ms=excluded.last_at_ms,
              unread=excluded.unread,
              is_group=excluded.is_group;
            """;
        cmd.Parameters.AddWithValue("$id", conversation.Id);
        cmd.Parameters.AddWithValue("$sid", (object?)conversation.ServiceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", conversation.Title);
        cmd.Parameters.AddWithValue("$preview", (object?)conversation.LastMessagePreview ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at", conversation.LastMessageAtMs);
        cmd.Parameters.AddWithValue("$unread", conversation.UnreadCount);
        cmd.Parameters.AddWithValue("$group", conversation.IsGroup ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<Conversation>> ListConversationsAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = """
            SELECT id, service_id, title, last_preview, last_at_ms, unread, is_group
            FROM conversations
            ORDER BY last_at_ms DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<Conversation>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new Conversation(
                Id: reader.GetString(0),
                ServiceId: reader.IsDBNull(1) ? null : reader.GetString(1),
                Title: reader.GetString(2),
                LastMessagePreview: reader.IsDBNull(3) ? null : reader.GetString(3),
                LastMessageAtMs: reader.GetInt64(4),
                UnreadCount: reader.GetInt32(5),
                IsGroup: reader.GetInt32(6) != 0));
        }

        return list;
    }

    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO messages
            (id, conversation_id, sender_service_id, body, sent_at_ms, received_at_ms, is_outgoing, status)
            VALUES($id, $cid, $sid, $body, $sent, $recv, $out, $status);
            """;
        cmd.Parameters.AddWithValue("$id", message.Id);
        cmd.Parameters.AddWithValue("$cid", message.ConversationId);
        cmd.Parameters.AddWithValue("$sid", (object?)message.SenderServiceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$body", (object?)message.Body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sent", message.SentAtMs);
        cmd.Parameters.AddWithValue("$recv", message.ReceivedAtMs);
        cmd.Parameters.AddWithValue("$out", message.IsOutgoing ? 1 : 0);
        cmd.Parameters.AddWithValue("$status", (int)message.Status);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        string conversationId,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = """
            SELECT id, conversation_id, sender_service_id, body, sent_at_ms, received_at_ms, is_outgoing, status
            FROM messages
            WHERE conversation_id = $cid
            ORDER BY sent_at_ms DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$cid", conversationId);
        cmd.Parameters.AddWithValue("$limit", limit);
        var list = new List<ChatMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ChatMessage(
                Id: reader.GetString(0),
                ConversationId: reader.GetString(1),
                SenderServiceId: reader.IsDBNull(2) ? null : reader.GetString(2),
                Body: reader.IsDBNull(3) ? null : reader.GetString(3),
                SentAtMs: reader.GetInt64(4),
                ReceivedAtMs: reader.GetInt64(5),
                IsOutgoing: reader.GetInt32(6) != 0,
                Status: (MessageStatus)reader.GetInt32(7)));
        }

        list.Reverse();
        return list;
    }

    public async Task UpsertContactAsync(ContactRecord contact, CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = """
            INSERT INTO contacts(service_id, number, profile_name, profile_key, about)
            VALUES($sid, $num, $name, $pk, $about)
            ON CONFLICT(service_id) DO UPDATE SET
              number=excluded.number,
              profile_name=excluded.profile_name,
              profile_key=excluded.profile_key,
              about=excluded.about;
            """;
        cmd.Parameters.AddWithValue("$sid", contact.ServiceId);
        cmd.Parameters.AddWithValue("$num", (object?)contact.Number ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", (object?)contact.ProfileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pk", (object?)contact.ProfileKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$about", (object?)contact.About ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ContactRecord>> ListContactsAsync(CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = "SELECT service_id, number, profile_name, profile_key, about FROM contacts ORDER BY profile_name;";
        var list = new List<ContactRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ContactRecord
            {
                ServiceId = reader.GetString(0),
                Number = reader.IsDBNull(1) ? null : reader.GetString(1),
                ProfileName = reader.IsDBNull(2) ? null : reader.GetString(2),
                ProfileKey = reader.IsDBNull(3) ? null : (byte[])reader[3],
                About = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return list;
    }

    public Task SaveSessionAsync(string address, byte[] record, CancellationToken ct = default) =>
        UpsertBlobAsync("sessions", "address", address, "record", record, ct);

    public Task<byte[]?> LoadSessionAsync(string address, CancellationToken ct = default) =>
        LoadBlobAsync("sessions", "address", address, "record", ct);

    public Task SaveIdentityAsync(string address, byte[] identityKey, CancellationToken ct = default) =>
        UpsertBlobAsync("identities", "address", address, "identity_key", identityKey, ct);

    public Task<byte[]?> LoadIdentityAsync(string address, CancellationToken ct = default) =>
        LoadBlobAsync("identities", "address", address, "identity_key", ct);

    public Task SavePreKeyAsync(uint keyId, byte[] record, CancellationToken ct = default) =>
        UpsertIntBlobAsync("prekeys", keyId, record, ct);

    public Task<byte[]?> LoadPreKeyAsync(uint keyId, CancellationToken ct = default) =>
        LoadIntBlobAsync("prekeys", keyId, ct);

    public async Task RemovePreKeyAsync(uint keyId, CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = "DELETE FROM prekeys WHERE key_id=$id;";
        cmd.Parameters.AddWithValue("$id", (long)keyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task SaveSignedPreKeyAsync(uint keyId, byte[] record, CancellationToken ct = default) =>
        UpsertIntBlobAsync("signed_prekeys", keyId, record, ct);

    public Task<byte[]?> LoadSignedPreKeyAsync(uint keyId, CancellationToken ct = default) =>
        LoadIntBlobAsync("signed_prekeys", keyId, ct);

    public Task SaveKyberPreKeyAsync(uint keyId, byte[] record, CancellationToken ct = default) =>
        UpsertIntBlobAsync("kyber_prekeys", keyId, record, ct);

    public Task<byte[]?> LoadKyberPreKeyAsync(uint keyId, CancellationToken ct = default) =>
        LoadIntBlobAsync("kyber_prekeys", keyId, ct);

    public async Task RemoveKyberPreKeyAsync(uint keyId, CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = "DELETE FROM kyber_prekeys WHERE key_id=$id;";
        cmd.Parameters.AddWithValue("$id", (long)keyId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountPreKeysAsync(CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM prekeys;";
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task ClearSessionsAsync(CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = "DELETE FROM sessions;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task SaveSenderCertificateAsync(byte[] certificate, CancellationToken ct = default) =>
        UpsertBlobAsync("meta", "key", "sender_certificate", "value", certificate, ct);

    public Task<byte[]?> LoadSenderCertificateAsync(CancellationToken ct = default) =>
        LoadBlobAsync("meta", "key", "sender_certificate", "value", ct);

    public async Task SaveAttachmentMetaAsync(AttachmentRecord attachment, CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO attachments
            (id, message_id, file_name, content_type, size, local_path, cdn_key)
            VALUES($id, $mid, $fn, $ct, $size, $path, $cdn);
            """;
        cmd.Parameters.AddWithValue("$id", attachment.Id);
        cmd.Parameters.AddWithValue("$mid", attachment.MessageId);
        cmd.Parameters.AddWithValue("$fn", (object?)attachment.FileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ct", (object?)attachment.ContentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", attachment.Size);
        cmd.Parameters.AddWithValue("$path", (object?)attachment.LocalPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cdn", (object?)attachment.CdnKey ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AttachmentRecord>> ListAttachmentsAsync(
        string messageId,
        CancellationToken ct = default)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = """
            SELECT id, message_id, file_name, content_type, size, local_path, cdn_key
            FROM attachments WHERE message_id=$mid;
            """;
        cmd.Parameters.AddWithValue("$mid", messageId);
        var list = new List<AttachmentRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new AttachmentRecord
            {
                Id = reader.GetString(0),
                MessageId = reader.GetString(1),
                FileName = reader.IsDBNull(2) ? null : reader.GetString(2),
                ContentType = reader.IsDBNull(3) ? null : reader.GetString(3),
                Size = reader.GetInt64(4),
                LocalPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                CdnKey = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }

        return list;
    }

    private async Task UpsertBlobAsync(
        string table, string keyCol, string key, string blobCol, byte[] blob, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {table}({keyCol}, {blobCol}) VALUES($k, $b)
            ON CONFLICT({keyCol}) DO UPDATE SET {blobCol}=excluded.{blobCol};
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$b", blob);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<byte[]?> LoadBlobAsync(
        string table, string keyCol, string key, string blobCol, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"SELECT {blobCol} FROM {table} WHERE {keyCol}=$k LIMIT 1;";
        cmd.Parameters.AddWithValue("$k", key);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is byte[] b ? b : null;
    }

    private async Task UpsertIntBlobAsync(string table, uint keyId, byte[] blob, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {table}(key_id, record) VALUES($id, $b)
            ON CONFLICT(key_id) DO UPDATE SET record=excluded.record;
            """;
        cmd.Parameters.AddWithValue("$id", (long)keyId);
        cmd.Parameters.AddWithValue("$b", blob);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<byte[]?> LoadIntBlobAsync(string table, uint keyId, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = $"SELECT record FROM {table} WHERE key_id=$id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", (long)keyId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is byte[] b ? b : null;
    }

    private async Task ExecAsync(string sql, CancellationToken ct)
    {
        await using var cmd = Conn().CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private SqliteConnection Conn() =>
        _conn ?? throw new InvalidOperationException("Message store not initialized");

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_conn is null)
            return;
        await _conn.DisposeAsync();
        _conn = null;
    }
}
