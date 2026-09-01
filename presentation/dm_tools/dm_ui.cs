#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dnd_game.application.projections;
using dnd_game.application.security;
using dnd_game.domain.commands;
using dnd_game.infrastructure.message_bus;
using ProjQuestStatus = dnd_game.application.projections.QuestStatus;

namespace dnd_game.presentation.dm_tools
{
    /// <summary>
    /// Консольный интерфейс инструментов Мастера (DM Tools).
    /// Предоставляет доступ к управлению кампанией, группой, боем, квестами и мировым состоянием.
    /// Все операции требуют прав Мастера или Администратора.
    /// </summary>
    public sealed class DmUi(
        ICommandBus commandBus,
        CharacterProjection characterProjection,
        CombatProjection combatProjection,
        CampaignProjection campaignProjection,
        PermissionChecker permissionChecker)
    {
        private readonly ICommandBus _commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        private readonly CharacterProjection _characterProjection = characterProjection ?? throw new ArgumentNullException(nameof(characterProjection));
        private readonly CombatProjection _combatProjection = combatProjection ?? throw new ArgumentNullException(nameof(combatProjection));
        private readonly CampaignProjection _campaignProjection = campaignProjection ?? throw new ArgumentNullException(nameof(campaignProjection));
        private readonly PermissionChecker _permissionChecker = permissionChecker ?? throw new ArgumentNullException(nameof(permissionChecker));

        private Guid _currentCampaignId;

        /// <summary>
        /// Проверяет, что текущий пользователь является Мастером или Администратором.
        /// </summary>
        private async Task EnsureGameMasterAccessAsync(CancellationToken ct = default)
        {
            if (!await _permissionChecker.IsGameMasterAsync(ct).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Только Мастер может получить доступ к DM Tools.");
        }

        /// <summary>
        /// Безопасное чтение строки: возвращает <c>null</c>, если строка пустая.
        /// </summary>
        private static string? ReadLine(string prompt)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        }

        /// <summary>
        /// Безопасное чтение целого числа: возвращает <c>null</c> при ошибке парсинга.
        /// </summary>
        private static int? ReadInt(string prompt)
        {
            var input = ReadLine(prompt);
            if (input == null)
                return null;
            if (int.TryParse(input, out int result))
                return result;
            Console.WriteLine("⚠️ Ожидалось целое число. Попробуйте ещё раз.");
            return null;
        }

        /// <summary>
        /// Отображает главный экран инструментов Мастера и запускает цикл меню.
        /// </summary>
        public async Task Render(CancellationToken cancellationToken = default)
        {
            try
            {
                await EnsureGameMasterAccessAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"[!] {ex.Message}");
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                Console.Clear();
                Console.WriteLine("=== ИНСТРУМЕНТЫ МАСТЕРА ===");
                Console.WriteLine("1. Обзор кампании");
                Console.WriteLine("2. Состояние группы");
                Console.WriteLine("3. Боевой трекер");
                Console.WriteLine("4. Быстрые действия (урон/лечение/состояния)");
                Console.WriteLine("5. Создать монстра / NPC");
                Console.WriteLine("6. Управление квестами");
                Console.WriteLine("7. Мировое состояние (время/погода/флаги)");
                Console.WriteLine("8. Инспекция персонажа");
                Console.WriteLine("9. Выход");
                Console.Write("Выберите пункт: ");

                var key = Console.ReadKey().Key;
                Console.WriteLine();

                try
                {
                    switch (key)
                    {
                        case ConsoleKey.D1:
                            await ShowCampaignOverview(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D2:
                            await ShowPartyStatus(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D3:
                            await ShowCombatTracker(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D4:
                            await QuickActionsMenu(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D5:
                            await SpawnMenu(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D6:
                            await ManageQuests(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D7:
                            await WorldStateMenu(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D8:
                            await InspectCharacter(cancellationToken).ConfigureAwait(false);
                            break;
                        case ConsoleKey.D9:
                            return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Ошибка: {ex.Message}");
                }
            }
        }

        // --------------------------------------------------------------------------
        // 1. Обзор кампании
        // --------------------------------------------------------------------------
        private async Task ShowCampaignOverview(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            if (_currentCampaignId == Guid.Empty)
            {
                var campaignIdStr = ReadLine("Введите ID кампании: ");
                if (campaignIdStr == null || !Guid.TryParse(campaignIdStr, out _currentCampaignId))
                {
                    Console.WriteLine("⚠️ Неверный ID кампании.");
                    return;
                }
            }

            var state = await _campaignProjection.GetCampaignState(_currentCampaignId, ct).ConfigureAwait(false);
            if (state == null)
            {
                Console.WriteLine("Кампания не найдена.");
                Pause();
                return;
            }

            Console.WriteLine($"=== {state.CampaignName} ===");
            Console.WriteLine($"День {state.Day}, {state.Hour}:{state.Minute:D2} | Погода: {state.Weather}");
            Console.WriteLine($"Акт: {state.CurrentAct}");
            Console.WriteLine("Регионы: " + string.Join(", ", state.DiscoveredRegions));
            Console.WriteLine("Флаги: " + string.Join(", ", state.GlobalFlags.Select(kv => $"{kv.Key}={kv.Value}")));
            Pause();
        }

        // --------------------------------------------------------------------------
        // 2. Состояние группы
        // --------------------------------------------------------------------------
        private async Task ShowPartyStatus(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            var characters = await _characterProjection.GetAll(ct).ConfigureAwait(false);
            foreach (var c in characters)
            {
                string status = c.IsDead
                    ? "МЁРТВ"
                    : c.HitPoints <= 0
                        ? (c.IsStable ? "Стабилен" : "При смерти")
                        : "Жив";

                Console.WriteLine(
                    $"{c.Name} (Ур.{c.Level} {c.Race} {c.Class}) " +
                    $"HP: {c.HitPoints}/{c.MaxHitPoints} AC:{c.ArmorClass} | {status}");

                if (c.Conditions.Count > 0)
                    Console.WriteLine("  Состояния: " + string.Join(", ", c.Conditions));
            }
            Pause();
        }

        // --------------------------------------------------------------------------
        // 3. Боевой трекер
        // --------------------------------------------------------------------------
        private async Task ShowCombatTracker(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            var combatIdStr = ReadLine("Введите ID боя: ");
            if (combatIdStr == null || !Guid.TryParse(combatIdStr, out var combatId))
            {
                Console.WriteLine("⚠️ Неверный ID боя.");
                Pause();
                return;
            }

            var status = await _combatProjection.GetStatus(combatId, ct).ConfigureAwait(false);
            if (status == null)
            {
                Console.WriteLine("Бой не найден.");
                Pause();
                return;
            }

            Console.WriteLine($"Бой {status.CombatId} | Раунд {status.Round} | Активен: {status.IsActive}");
            foreach (var p in status.Participants)
            {
                var character = await _characterProjection.GetById(p.CharacterId, ct).ConfigureAwait(false);
                string name = character?.Name ?? p.CharacterId.ToString();
                string turnMarker = p.IsCurrentTurn ? " <= ТЕКУЩИЙ ХОД" : "";
                Console.WriteLine(
                    $"  {name} Иниц: {p.Initiative} " +
                    $"HP: {character?.HitPoints}/{character?.MaxHitPoints}{turnMarker}");
            }
            Pause();
        }

        // --------------------------------------------------------------------------
        // 4. Быстрые действия
        // --------------------------------------------------------------------------
        private async Task QuickActionsMenu(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            var characterIdStr = ReadLine("Введите ID персонажа: ");
            if (characterIdStr == null || !Guid.TryParse(characterIdStr, out var characterId))
            {
                Console.WriteLine("⚠️ Неверный ID персонажа.");
                Pause();
                return;
            }

            var character = await _characterProjection.GetById(characterId, ct).ConfigureAwait(false);
            if (character == null)
            {
                Console.WriteLine("Персонаж не найден.");
                Pause();
                return;
            }

            Console.WriteLine($"Цель: {character.Name} (HP {character.HitPoints}/{character.MaxHitPoints})");
            Console.WriteLine("1. Нанести урон");
            Console.WriteLine("2. Лечить");
            Console.WriteLine("3. Наложить состояние");
            Console.WriteLine("4. Снять состояние");
            Console.Write("Действие: ");
            var key = Console.ReadKey().Key;
            Console.WriteLine();

            switch (key)
            {
                case ConsoleKey.D1:
                    var dmg = ReadInt("Величина урона: ");
                    if (dmg == null) break;
                    var dtype = ReadLine("Тип урона [дробящий]: ") ?? "дробящий";
                    await _commandBus.SendAsync(new DealDamage(characterId, dmg.Value, dtype), ct);
                    Console.WriteLine($"Нанесено {dmg} урона ({dtype}) персонажу {character.Name}.");
                    break;

                case ConsoleKey.D2:
                    var heal = ReadInt("Величина лечения: ");
                    if (heal == null) break;
                    await _commandBus.SendAsync(new HealCharacter(characterId, heal.Value), ct);
                    Console.WriteLine($"{character.Name} вылечен на {heal} HP.");
                    break;

                case ConsoleKey.D3:
                    var cond = ReadLine("Состояние: ");
                    if (cond == null) break;
                    var dur = ReadInt("Длительность (раундов): ");
                    if (dur == null) break;
                    await _commandBus.SendAsync(new ApplyCondition(characterId, cond, dur.Value), ct);
                    Console.WriteLine($"Наложено состояние {cond} на {character.Name}.");
                    break;

                case ConsoleKey.D4:
                    var remCond = ReadLine("Состояние: ");
                    if (remCond == null) break;
                    await _commandBus.SendAsync(new RemoveCondition(characterId, remCond), ct);
                    Console.WriteLine($"Снято состояние {remCond} с {character.Name}.");
                    break;
            }
            Pause();
        }

        // --------------------------------------------------------------------------
        // 5. Создание монстра / NPC
        // --------------------------------------------------------------------------
        private async Task SpawnMenu(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            var name = ReadLine("Введите имя: ");
            if (name == null)
                return;

            var hp = ReadInt("Максимальные HP: ");
            if (hp == null)
                return;

            var newId = Guid.NewGuid();
            await _commandBus.SendAsync(new CreateCharacter(newId, name, hp.Value), ct);
            Console.WriteLine($"Создан {name} (ID: {newId}).");
            Pause();
        }

        // --------------------------------------------------------------------------
        // 6. Управление квестами
        // --------------------------------------------------------------------------
        private async Task ManageQuests(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            var quests = await _campaignProjection.GetQuests(_currentCampaignId, null, ct).ConfigureAwait(false);
            foreach (var q in quests)
                Console.WriteLine($"[{q.QuestId}] {q.Title} ({q.Status})");

            var qidStr = ReadLine("Введите ID квеста: ");
            if (qidStr == null || !Guid.TryParse(qidStr, out var qid))
            {
                Console.WriteLine("⚠️ Неверный ID квеста.");
                Pause();
                return;
            }

            var quest = quests.FirstOrDefault(q => q.QuestId == qid);
            if (quest == null)
            {
                Console.WriteLine("Квест не найден.");
                Pause();
                return;
            }

            Console.WriteLine($"Текущий статус: {quest.Status}");
            Console.WriteLine("Выберите действие:");

            switch (quest.Status)
            {
                case ProjQuestStatus.Available:
                    Console.WriteLine("1. Принять квест");
                    break;
                case ProjQuestStatus.Active:
                    Console.WriteLine("1. Завершить квест");
                    Console.WriteLine("2. Провалить квест");
                    break;
                case ProjQuestStatus.Completed:
                    Console.WriteLine("1. Пометить как проваленный (тест)");
                    break;
                case ProjQuestStatus.Failed:
                    Console.WriteLine("1. Перезапустить квест (принять снова)");
                    break;
            }
            Console.WriteLine("0. Отмена");
            Console.Write("> ");
            var key = Console.ReadKey().Key;
            Console.WriteLine();

            switch (quest.Status)
            {
                case ProjQuestStatus.Available:
                    if (key == ConsoleKey.D1)
                        await _commandBus.SendAsync(new AcceptQuestCommand(_currentCampaignId, qid), ct);
                    break;
                case ProjQuestStatus.Active:
                    if (key == ConsoleKey.D1)
                        await _commandBus.SendAsync(new CompleteQuestCommand(_currentCampaignId, qid), ct);
                    else if (key == ConsoleKey.D2)
                        await _commandBus.SendAsync(new FailQuestCommand(_currentCampaignId, qid), ct);
                    break;
                case ProjQuestStatus.Completed:
                    if (key == ConsoleKey.D1)
                        await _commandBus.SendAsync(new FailQuestCommand(_currentCampaignId, qid), ct);
                    break;
                case ProjQuestStatus.Failed:
                    if (key == ConsoleKey.D1)
                        await _commandBus.SendAsync(new AcceptQuestCommand(_currentCampaignId, qid), ct);
                    break;
            }

            Console.WriteLine("Готово.");
            Pause();
        }

        // --------------------------------------------------------------------------
        // 7. Мировое состояние
        // --------------------------------------------------------------------------
        private async Task WorldStateMenu(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            Console.WriteLine("1. Продвинуть время");
            Console.WriteLine("2. Изменить погоду");
            Console.Write("Выберите: ");
            var key = Console.ReadKey().Key;
            Console.WriteLine();

            if (key == ConsoleKey.D1)
            {
                var mins = ReadInt("Минут для продвижения: ");
                if (mins == null) return;
                await _commandBus.SendAsync(new AdvanceTimeCommand(_currentCampaignId, mins.Value), ct);
                Console.WriteLine($"Время продвинуто на {mins} минут.");
            }
            else if (key == ConsoleKey.D2)
            {
                var w = ReadLine("Новая погода: ");
                if (w == null) return;
                await _commandBus.SendAsync(new ChangeWeatherCommand(_currentCampaignId, w), ct);
                Console.WriteLine($"Погода изменена на {w}.");
            }
            Pause();
        }

        // --------------------------------------------------------------------------
        // 8. Инспекция персонажа
        // --------------------------------------------------------------------------
        private async Task InspectCharacter(CancellationToken ct)
        {
            await EnsureGameMasterAccessAsync(ct).ConfigureAwait(false);

            var characterIdStr = ReadLine("Введите ID персонажа: ");
            if (characterIdStr == null || !Guid.TryParse(characterIdStr, out var characterId))
            {
                Console.WriteLine("⚠️ Неверный ID персонажа.");
                Pause();
                return;
            }

            var character = await _characterProjection.GetById(characterId, ct).ConfigureAwait(false);
            if (character == null)
            {
                Console.WriteLine("Персонаж не найден.");
                Pause();
                return;
            }

            Console.WriteLine($"=== {character.Name} (Ур.{character.Level} {character.Race} {character.Class}) ===");
            Console.WriteLine($"HP: {character.HitPoints}/{character.MaxHitPoints} (Врем: {character.TemporaryHitPoints})");
            Console.WriteLine($"AC: {character.ArmorClass}  Скорость: {character.Speed} фт");
            Console.WriteLine($"Опыт: {character.ExperiencePoints}  Бонус мастерства: +{character.ProficiencyBonus}");
            Console.WriteLine("Характеристики: " + string.Join(", ", character.AbilityScores.Select(kv => $"{kv.Key}:{kv.Value}")));
            Console.WriteLine("Навыки: " + string.Join(", ", character.SkillProficiencies));
            Console.WriteLine("Состояния: " + string.Join(", ", character.Conditions));
            Console.WriteLine("Сопротивления: " + string.Join(", ", character.Resistances));
            Pause();
        }

        /// <summary>
        /// Ожидает нажатия клавиши для продолжения.
        /// </summary>
        private static void Pause()
        {
            Console.WriteLine("Нажмите любую клавишу для продолжения...");
            Console.ReadKey();
        }
    }
}