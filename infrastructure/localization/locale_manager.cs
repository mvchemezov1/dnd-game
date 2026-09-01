#nullable enable
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace dnd_game.infrastructure.localization
{
    /// <summary>
    /// Интерфейс менеджера локализации для игры DnD.
    /// </summary>
    public interface ILocaleManager
    {
        /// <summary>Возвращает строку по ключу для текущей локали.</summary>
        string GetString(string key);

        /// <summary>Возвращает строку по ключу для указанной локали.</summary>
        string GetString(string key, string locale);

        /// <summary>Возвращает строку с подстановкой параметров.</summary>
        string Format(string key, params object[] args);

        /// <summary>Возвращает строку с учётом множественного числа.</summary>
        string Pluralize(string key, int count, params object[] args);

        /// <summary>Устанавливает текущую локаль.</summary>
        void SetLocale(string locale);

        /// <summary>Текущая локаль.</summary>
        string CurrentLocale { get; }
    }

    /// <summary>
    /// Провайдер переводов для загрузки локализационных файлов.
    /// </summary>
    public interface ILocaleProvider
    {
        Task<Dictionary<string, string>> LoadTranslationsAsync(string locale);
    }

    /// <summary>
    /// Ключи локализации для часто используемых игровых терминов.
    /// </summary>
    public static class LocaleKeys
    {
        // Характеристики
        public const string CharacterStrength = "character.strength";
        public const string CharacterDexterity = "character.dexterity";
        public const string CharacterConstitution = "character.constitution";
        public const string CharacterIntelligence = "character.intelligence";
        public const string CharacterWisdom = "character.wisdom";
        public const string CharacterCharisma = "character.charisma";

        // Действия
        public const string ActionAttack = "action.attack";
        public const string ActionDash = "action.dash";
        public const string ActionDisengage = "action.disengage";
        public const string ActionDodge = "action.dodge";
        public const string ActionHelp = "action.help";
        public const string ActionHide = "action.hide";
        public const string ActionReady = "action.ready";
        public const string ActionSearch = "action.search";
        public const string ActionUseObject = "action.use_object";

        // Состояния
        public const string ConditionBlinded = "condition.blinded";
        public const string ConditionCharmed = "condition.charmed";
        public const string ConditionDeafened = "condition.deafened";
        public const string ConditionFrightened = "condition.frightened";
        public const string ConditionGrappled = "condition.grappled";
        public const string ConditionIncapacitated = "condition.incapacitated";
        public const string ConditionInvisible = "condition.invisible";
        public const string ConditionParalyzed = "condition.paralyzed";
        public const string ConditionPetrified = "condition.petrified";
        public const string ConditionPoisoned = "condition.poisoned";
        public const string ConditionProne = "condition.prone";
        public const string ConditionRestrained = "condition.restrained";
        public const string ConditionStunned = "condition.stunned";
        public const string ConditionUnconscious = "condition.unconscious";
        public const string ConditionExhaustion = "condition.exhaustion";

        // Типы урона
        public const string DamageTypeBludgeoning = "damage.bludgeoning";
        public const string DamageTypePiercing = "damage.piercing";
        public const string DamageTypeSlashing = "damage.slashing";
        public const string DamageTypeFire = "damage.fire";
        public const string DamageTypeCold = "damage.cold";
        public const string DamageTypeLightning = "damage.lightning";
        public const string DamageTypeThunder = "damage.thunder";
        public const string DamageTypeAcid = "damage.acid";
        public const string DamageTypePoison = "damage.poison";
        public const string DamageTypeRadiant = "damage.radiant";
        public const string DamageTypeNecrotic = "damage.necrotic";
        public const string DamageTypePsychic = "damage.psychic";
        public const string DamageTypeForce = "damage.force";

        // Навыки
        public const string SkillAcrobatics = "skill.acrobatics";
        public const string SkillAnimalHandling = "skill.animal_handling";
        public const string SkillArcana = "skill.arcana";
        public const string SkillAthletics = "skill.athletics";
        public const string SkillDeception = "skill.deception";
        public const string SkillHistory = "skill.history";
        public const string SkillInsight = "skill.insight";
        public const string SkillIntimidation = "skill.intimidation";
        public const string SkillInvestigation = "skill.investigation";
        public const string SkillMedicine = "skill.medicine";
        public const string SkillNature = "skill.nature";
        public const string SkillPerception = "skill.perception";
        public const string SkillPerformance = "skill.performance";
        public const string SkillPersuasion = "skill.persuasion";
        public const string SkillReligion = "skill.religion";
        public const string SkillSleightOfHand = "skill.sleight_of_hand";
        public const string SkillStealth = "skill.stealth";
        public const string SkillSurvival = "skill.survival";
    }

    /// <summary>
    /// Основная реализация менеджера локализации.
    /// </summary>
    public class LocaleManager(ILocaleProvider provider, ILogger<LocaleManager>? logger = null) : ILocaleManager
    {
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILocaleProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        private readonly ILogger<LocaleManager> _logger = logger ?? NullLogger<LocaleManager>.Instance;
        private string _currentLocale = "ru"; // русский по умолчанию, так как целевая аудитория русскоязычная

        public string CurrentLocale => _currentLocale;

        /// <inheritdoc />
        public void SetLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                throw new ArgumentException("Локаль не может быть пустой.", nameof(locale));
            _currentLocale = locale.ToLowerInvariant();
        }

        /// <inheritdoc />
        public string GetString(string key) => GetString(key, _currentLocale);

        /// <inheritdoc />
        public string GetString(string key, string locale)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            locale = locale?.ToLowerInvariant() ?? _currentLocale;

            // Ленивая загрузка локали при первом обращении
            if (!_translations.ContainsKey(locale))
            {
                // Синхронная загрузка допустима только в случае отсутствия контекста синхронизации.
                // В веб-приложении лучше использовать предварительную загрузку всех локалей при старте.
                LoadLocaleSync(locale);
            }

            if (_translations.TryGetValue(locale, out var dict))
            {
                if (dict.TryGetValue(key, out var value))
                    return value;

                // Fallback на английский, если ключ не найден
                if (locale != "en" && _translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enValue))
                    return enValue;
            }

            _logger.LogWarning("Ключ локализации '{Key}' не найден для локали '{Locale}'.", key, locale);
            return key; // возвращаем сам ключ
        }

        /// <inheritdoc />
        public string Format(string key, params object[] args)
        {
            var template = GetString(key);
            return args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, template, args) : template;
        }

        /// <inheritdoc />
        public string Pluralize(string key, int count, params object[] args)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Количество не может быть отрицательным.");

            string pluralKey = GetPluralKey(key, count);
            var template = GetString(pluralKey);

            // Если конкретная форма не найдена, используем базовый ключ
            if (template == pluralKey)
                template = GetString(key);

            var allArgs = new object[args.Length + 1];
            allArgs[0] = count;
            Array.Copy(args, 0, allArgs, 1, args.Length);
            return string.Format(CultureInfo.InvariantCulture, template, allArgs);
        }

        /// <summary>
        /// Определяет правильный суффикс для ключа множественного числа
        /// в зависимости от локали и числа.
        /// </summary>
        private string GetPluralKey(string key, int count)
        {
            if (_currentLocale == "ru")
            {
                // Правила русского языка: one, few, many
                if (count % 10 == 1 && count % 100 != 11)
                    return $"{key}.one";
                if (count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 10 || count % 100 >= 20))
                    return $"{key}.few";
                return $"{key}.many";
            }

            // Для остальных локалей: one/other
            return count == 1 ? $"{key}.one" : $"{key}.other";
        }

        /// <summary>
        /// Синхронная загрузка переводов для указанной локали.
        /// Используется только когда невозможно выполнить асинхронную инициализацию.
        /// </summary>
        private void LoadLocaleSync(string locale)
        {
            try
            {
                var dict = _provider.LoadTranslationsAsync(locale).GetAwaiter().GetResult();
                _translations[locale] = dict ?? [];
                _logger.LogInformation("Локаль '{Locale}' загружена успешно.", locale);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось загрузить локаль '{Locale}'.", locale);
                // Добавляем пустой словарь, чтобы не пытаться повторно загрузить сразу же,
                // но можно предусмотреть повторную попытку позже (например, по таймеру).
                _translations[locale] = [];
            }
        }

        /// <summary>
        /// Асинхронная загрузка переводов для указанной локали (рекомендуется для предзагрузки).
        /// </summary>
        public async Task LoadLocaleAsync(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                throw new ArgumentException("Локаль не может быть пустой.", nameof(locale));

            locale = locale.ToLowerInvariant();
            if (_translations.ContainsKey(locale))
                return;

            try
            {
                var dict = await _provider.LoadTranslationsAsync(locale).ConfigureAwait(false);
                _translations[locale] = dict ?? [];
                _logger.LogInformation("Локаль '{Locale}' загружена успешно.", locale);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось загрузить локаль '{Locale}'.", locale);
                _translations[locale] = [];
            }
        }
    }

    /// <summary>
    /// Провайдер переводов из JSON-файлов.
    /// </summary>
    public class JsonFileLocaleProvider(string resourcesPath, ILogger<JsonFileLocaleProvider>? logger = null) : ILocaleProvider
    {
        private readonly string _resourcesPath = resourcesPath ?? throw new ArgumentNullException(nameof(resourcesPath));
        private readonly ILogger<JsonFileLocaleProvider> _logger = logger ?? NullLogger<JsonFileLocaleProvider>.Instance;

        public async Task<Dictionary<string, string>> LoadTranslationsAsync(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                throw new ArgumentException("Локаль не может быть пустой.", nameof(locale));

            var filePath = Path.Combine(_resourcesPath, $"{locale}.json");
            if (!File.Exists(filePath))
            {
                _logger.LogError("Файл перевода не найден: {FilePath}", filePath);
                throw new FileNotFoundException($"Файл перевода не найден: {filePath}");
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return dict ?? [];
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Ошибка десериализации файла локализации {FilePath}", filePath);
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Ошибка чтения файла локализации {FilePath}", filePath);
                throw;
            }
        }
    }
}