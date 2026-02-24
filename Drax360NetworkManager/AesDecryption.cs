using System;
using System.Security.Cryptography;
using System.Text;

namespace CryptoModule
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Linq;

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

        private static byte[] DeriveKeyPBKDF2(byte[] password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA512))
            {
                return pbkdf2.GetBytes(48); // 32 key + 16 IV
            }
        }

        public static string DecryptOpenSSLCtr(string base64Input, string passphrase)
        {
            byte[] data = Convert.FromBase64String(base64Input.Trim());

            if (data.Length < 16)
                return base64Input;

            // Check magic "Salted__"
            byte[] magicBytes = Encoding.ASCII.GetBytes("Salted__");
            bool hasMagic = true;
            for (int i = 0; i < 8; i++)
                if (data[i] != magicBytes[i]) { hasMagic = false; break; }

            if (!hasMagic)
                return base64Input;

            byte[] salt = new byte[8];
            byte[] cipherText = new byte[data.Length - 16];
            Array.Copy(data, 8, salt, 0, 8);
            Array.Copy(data, 16, cipherText, 0, cipherText.Length);

            byte[] pass = Encoding.UTF8.GetBytes(passphrase);
            byte[] derived = DeriveKeyPBKDF2(pass, salt);

            byte[] key = new byte[32];
            byte[] iv = new byte[16];
            Array.Copy(derived, 0, key, 0, 32);
            Array.Copy(derived, 32, iv, 0, 16);

            byte[] decrypted = AesCtrDecrypt(cipherText, key, iv);
            return Encoding.UTF8.GetString(decrypted).TrimEnd('\0');
        }

        private static byte[] AesCtrDecrypt(byte[] cipherText, byte[] key, byte[] iv)
        {
            byte[] output = new byte[cipherText.Length];
            byte[] counter = new byte[16];
            byte[] block = new byte[16];

            Array.Copy(iv, counter, 16);

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                using (var encryptor = aes.CreateEncryptor())
                {
                    int pos = 0;
                    while (pos < cipherText.Length)
                    {
                        encryptor.TransformBlock(counter, 0, 16, block, 0);

                        int bytesToProcess = Math.Min(16, cipherText.Length - pos);
                        for (int i = 0; i < bytesToProcess; i++)
                            output[pos + i] = (byte)(cipherText[pos + i] ^ block[i]);

                        pos += bytesToProcess;

                        // Try big-endian this time
                        for (int i = 15; i >= 0; i--)
                        {
                            if (++counter[i] != 0) break;
                        }
                    }
                }
            }

            return output;
        }

    }
}