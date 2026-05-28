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

        public static string EncryptOpenSSLCtr(string plainText, string passphrase)
        {
            // Generate random 8-byte salt
            byte[] salt = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(salt);

            // Derive key and IV using same PBKDF2 method
            byte[] pass = Encoding.UTF8.GetBytes(passphrase);
            byte[] derived = DeriveKeyPBKDF2(pass, salt);

            byte[] key = new byte[32];
            byte[] iv = new byte[16];
            Array.Copy(derived, 0, key, 0, 32);
            Array.Copy(derived, 32, iv, 0, 16);

            // Encrypt the plain text
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherText = AesCtrDecrypt(plainBytes, key, iv); // CTR encrypt = decrypt (XOR is symmetric)

            // Build output: "Salted__" + salt (8 bytes) + ciphertext
            byte[] magic = Encoding.ASCII.GetBytes("Salted__");
            byte[] result = new byte[8 + 8 + cipherText.Length];
            Array.Copy(magic, 0, result, 0, 8);
            Array.Copy(salt, 0, result, 8, 8);
            Array.Copy(cipherText, 0, result, 16, cipherText.Length);

            return Convert.ToBase64String(result);
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