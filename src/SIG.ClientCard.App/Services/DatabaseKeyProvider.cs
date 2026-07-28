using System.Security.Cryptography;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// SQLCipher key: generated once, held in SecureStorage.
///
/// Android edge case: SecureStorage reads fail after a device-to-device restore
/// because the Keystore key doesn't transfer. Every read is wrapped so that
/// failure clears storage and rebuilds — the old database is unreadable with a
/// lost key, so it is removed and repopulated by sync (or re-entry in Phase 0)
/// rather than bricking the app on new handsets.
/// </summary>
public sealed class DatabaseKeyProvider
{
    private const string KeyName = "clientcard_db_key";

    public string GetOrCreateKey(string dbPath)
    {
        string? key;
        try
        {
            key = SecureStorage.Default.GetAsync(KeyName).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Keystore is unusable (restored device). Start clean.
            SecureStorage.Default.RemoveAll();
            DeleteDatabase(dbPath);
            key = null;
        }

        if (string.IsNullOrEmpty(key))
        {
            key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            try
            {
                SecureStorage.Default.SetAsync(KeyName, key).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                SecureStorage.Default.RemoveAll();
                SecureStorage.Default.SetAsync(KeyName, key).GetAwaiter().GetResult();
            }

            // A fresh key can never open an existing encrypted file.
            DeleteDatabase(dbPath);
        }

        return key;
    }

    private static void DeleteDatabase(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
