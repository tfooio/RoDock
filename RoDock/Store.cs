using System.Security.Cryptography;
using System.Text.Json;

namespace RoDock;

/// Encrypted-at-rest JSON using Windows DPAPI: only this Windows user on this PC can read the file.
public static class Vault
{
    public static T? Read<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : default;

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value), null, DataProtectionScope.CurrentUser));
    }
}

public class Store(string path)
{
    public List<Account> Load() => Vault.Read<List<Account>>(path) ?? [];
    public void Save(IEnumerable<Account> accounts) => Vault.Write(path, accounts);
}
