#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace dnd_game.infrastructure.security
{
    /// <summary>
    /// Интерфейс сервиса хэширования и проверки паролей.
    /// </summary>
    public interface IPasswordHasher
    {
        /// <summary>
        /// Хэширует пароль для безопасного хранения.
        /// </summary>
        /// <param name="password">Открытый пароль (не может быть пустым).</param>
        /// <returns>Строка в формате «{iterations}.{base64(salt)}.{base64(hash)}».</returns>
        string Hash(string password);

        /// <summary>
        /// Проверяет, соответствует ли пароль сохранённому хэшу.
        /// </summary>
        /// <param name="password">Открытый пароль.</param>
        /// <param name="hash">Хранимый хэш (в формате Hash).</param>
        /// <returns><c>true</c>, если пароль корректен.</returns>
        bool Verify(string password, string hash);

        /// <summary>
        /// Проверяет, удовлетворяет ли пароль минимальным требованиям сложности.
        /// </summary>
        /// <param name="password">Открытый пароль.</param>
        /// <returns><c>true</c>, если пароль достаточно сложный.</returns>
        bool IsStrongPassword(string password);
    }

    /// <summary>
    /// Реализация хэширования паролей на основе PBKDF2 (RFC 2898) с использованием SHA-512.
    /// Обеспечивает высокую стойкость за счёт соли и большого числа итераций.
    /// </summary>
    public class PasswordHasher : IPasswordHasher
    {
        // Рекомендуемые параметры безопасности (по состоянию на 2024 год)
        private const int SaltSize = 16;        // 128 бит соли
        private const int HashSize = 32;        // 256 бит хэша
        private const int Iterations = 100_000; // количество итераций PBKDF2

        /// <inheritdoc />
        /// <exception cref="ArgumentException">Если пароль пуст или состоит только из пробелов.</exception>
        public string Hash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Пароль не может быть пустым.", nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = GenerateHash(password, salt, Iterations, HashSize);

            // Формат хранения: {iterations}.{base64(salt)}.{base64(hash)}
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        /// <inheritdoc />
        public bool Verify(string password, string hash)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
                return false;

            var parts = hash.Split('.');
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[0], out int iterations) || iterations <= 0)
                return false;

            byte[] salt;
            byte[] storedHash;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                storedHash = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false; // некорректный base64
            }

            if (salt.Length == 0 || storedHash.Length == 0)
                return false;

            byte[] computedHash = GenerateHash(password, salt, iterations, storedHash.Length);
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }

        /// <inheritdoc />
        public bool IsStrongPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return false;

            // Минимальные требования:
            // - не менее 8 символов
            // - хотя бы одна заглавная буква
            // - хотя бы одна строчная буква
            // - хотя бы одна цифра
            // - хотя бы один специальный символ (не буква и не цифра)
            return password.Length >= 8
                   && password.Any(char.IsUpper)
                   && password.Any(char.IsLower)
                   && password.Any(char.IsDigit)
                   && password.Any(static ch => !char.IsLetterOrDigit(ch));
        }

        /// <summary>
        /// Генерирует хэш пароля с использованием PBKDF2 и SHA-512.
        /// </summary>
        /// <param name="password">Открытый пароль.</param>
        /// <param name="salt">Соль.</param>
        /// <param name="iterations">Количество итераций.</param>
        /// <param name="outputLength">Длина выходного хэша в байтах.</param>
        /// <returns>Массив байтов хэша.</returns>
        private static byte[] GenerateHash(string password, byte[] salt, int iterations, int outputLength)
        {
            using var deriveBytes = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA512);

            return deriveBytes.GetBytes(outputLength);
        }
    }
}