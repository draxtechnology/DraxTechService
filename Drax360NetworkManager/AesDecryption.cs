using System;
using System.Security.Cryptography;
using System.Text;

namespace CryptoModule
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;

    public class AesDecryptor
    {
        private const int AES_BLOCK_SIZE = 16;
        private const int AES_KEYLEN = 32;           // 32 -> AES-256, 24 -> AES-196, 16 -> AES-128
        private const int AES_IVLEN = AES_BLOCK_SIZE;
        private const int KDF_SALTLEN = 8;
        private const int KDF_ITER = 10000;
        private const string OPENSSL_MAGIC = "Salted__";
        private const int OPENSSL_MAGICLEN = 8;
        private const int HMAC_HASHLEN = 32;         // SHA256 output length

        public static string DecryptString(string encryptedBase64, string password)
        {
            try
            {
                // Convert base64 string to bytes
                byte[] encryptedData = Convert.FromBase64String(encryptedBase64);

                // Verify OpenSSL magic header
                byte[] magic = new byte[OPENSSL_MAGICLEN];
                Array.Copy(encryptedData, 0, magic, 0, OPENSSL_MAGICLEN);

                if (Encoding.ASCII.GetString(magic) != OPENSSL_MAGIC)
                {
                    throw new ArgumentException("Invalid encrypted data format");
                }

                // Extract salt
                byte[] salt = new byte[KDF_SALTLEN];
                Array.Copy(encryptedData, OPENSSL_MAGICLEN, salt, 0, KDF_SALTLEN);

                // Extract ciphertext (remaining bytes after magic + salt)
                int cipherTextLen = encryptedData.Length - OPENSSL_MAGICLEN - KDF_SALTLEN;
                byte[] cipherText = new byte[cipherTextLen];
                Array.Copy(encryptedData, OPENSSL_MAGICLEN + KDF_SALTLEN, cipherText, 0, cipherTextLen);

                // Derive key and IV using PBKDF2
                byte[] keyIv = DeriveKeyAndIV(password, salt, AES_KEYLEN, AES_IVLEN);
                byte[] key = new byte[AES_KEYLEN];
                byte[] iv = new byte[AES_IVLEN];
                Array.Copy(keyIv, 0, key, 0, AES_KEYLEN);
                Array.Copy(keyIv, AES_KEYLEN, iv, 0, AES_IVLEN);

                // Decrypt using AES
                using (Aes aes = Aes.Create())
                {
                    aes.KeySize = AES_KEYLEN * 8;
                    aes.BlockSize = AES_BLOCK_SIZE * 8;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;

                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Decryption failed: " + ex.Message, ex);
            }
        }

        private static byte[] DeriveKeyAndIV(string password, byte[] salt, int keyLen, int ivLen)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

            using (var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, salt, KDF_ITER, HashAlgorithmName.SHA256))
            {
                byte[] keyIv = new byte[keyLen + ivLen];
                byte[] derivedKey = pbkdf2.GetBytes(keyLen + ivLen);
                Array.Copy(derivedKey, keyIv, keyLen + ivLen);
                return keyIv;
            }
        }
    }
}