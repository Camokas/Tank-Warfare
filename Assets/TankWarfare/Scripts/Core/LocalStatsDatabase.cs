using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TankWarfare.Core
{
    /// <summary>
    /// A small encrypted local record. In WebGL PlayerPrefs is persisted by Unity in IndexedDB.
    /// AES hides the contents and HMAC rejects accidental or casual manual modification.
    /// Match results themselves are accepted only from the authoritative match server.
    /// </summary>
    public sealed class LocalStatsDatabase
    {
        private const string DataKey = "tw.stats.v1";
        private const string SecretKey = "tw.device-key.v1";
        private const string BackupKey = "tw.stats.backup.v1";

        private readonly byte[] secret;

        public LocalStatsDatabase()
        {
            secret = LoadOrCreateSecret();
        }

        public PlayerStatistics Load()
        {
            PlayerStatistics statistics = TryRead(PlayerPrefs.GetString(DataKey, string.Empty));
            if (statistics != null)
                return statistics;

            statistics = TryRead(PlayerPrefs.GetString(BackupKey, string.Empty));
            return statistics ?? new PlayerStatistics();
        }

        public void Save(PlayerStatistics statistics)
        {
            if (statistics == null)
                throw new ArgumentNullException(nameof(statistics));

            string current = PlayerPrefs.GetString(DataKey, string.Empty);
            if (!string.IsNullOrEmpty(current))
                PlayerPrefs.SetString(BackupKey, current);

            PlayerPrefs.SetString(DataKey, Encrypt(JsonUtility.ToJson(statistics)));
            PlayerPrefs.Save();
        }

        private PlayerStatistics TryRead(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return null;

            try
            {
                string json = Decrypt(payload);
                PlayerStatistics result = JsonUtility.FromJson<PlayerStatistics>(json);
                return result != null && result.schemaVersion == 1 ? result : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Локальная статистика повреждена и будет восстановлена: {exception.Message}");
                return null;
            }
        }

        private string Encrypt(string plainText)
        {
            using Aes aes = Aes.Create();
            aes.Key = DeriveKey("encryption");
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            byte[] plain = Encoding.UTF8.GetBytes(plainText);
            byte[] cipher;
            using (ICryptoTransform encryptor = aes.CreateEncryptor())
                cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

            byte[] signed = Combine(aes.IV, cipher);
            byte[] signature;
            using (var hmac = new HMACSHA256(DeriveKey("authentication")))
                signature = hmac.ComputeHash(signed);

            return $"1.{Convert.ToBase64String(aes.IV)}.{Convert.ToBase64String(cipher)}.{Convert.ToBase64String(signature)}";
        }

        private string Decrypt(string payload)
        {
            string[] parts = payload.Split('.');
            if (parts.Length != 4 || parts[0] != "1")
                throw new CryptographicException("Неизвестный формат записи");

            byte[] iv = Convert.FromBase64String(parts[1]);
            byte[] cipher = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual;
            using (var hmac = new HMACSHA256(DeriveKey("authentication")))
                actual = hmac.ComputeHash(Combine(iv, cipher));

            if (!FixedTimeEquals(actual, expected))
                throw new CryptographicException("Подпись записи не совпала");

            using Aes aes = Aes.Create();
            aes.Key = DeriveKey("encryption");
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }

        private byte[] DeriveKey(string purpose)
        {
            using var hmac = new HMACSHA256(secret);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes("TankWarfare/" + purpose));
        }

        private static byte[] LoadOrCreateSecret()
        {
            string encoded = PlayerPrefs.GetString(SecretKey, string.Empty);
            if (!string.IsNullOrEmpty(encoded))
            {
                try { return Convert.FromBase64String(encoded); }
                catch (FormatException) { /* Create a new device key below. */ }
            }

            var bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            PlayerPrefs.SetString(SecretKey, Convert.ToBase64String(bytes));
            PlayerPrefs.Save();
            return bytes;
        }

        private static byte[] Combine(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int difference = 0;
            for (int i = 0; i < left.Length; i++)
                difference |= left[i] ^ right[i];
            return difference == 0;
        }
    }
}
