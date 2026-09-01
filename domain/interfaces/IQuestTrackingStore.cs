#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace dnd_game.domain.interfaces
{
    /// <summary>
    /// Хранилище связей между персонажами/предметами и активными квестами.
    /// Используется для маршрутизации событий (например, <c>CharacterDied</c>, <c>ItemAcquired</c>) к соответствующим сагам.
    /// </summary>
    public interface IQuestTrackingStore
    {
        /// <summary>
        /// Регистрирует персонажа как участника указанного квеста.
        /// </summary>
        /// <param name="questId">Идентификатор квеста.</param>
        /// <param name="characterId">Идентификатор персонажа.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task AddParticipantAsync(Guid questId, Guid characterId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает список идентификаторов квестов, в которых участвует указанный персонаж.
        /// </summary>
        /// <param name="characterId">Идентификатор персонажа.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Коллекция идентификаторов квестов.</returns>
        Task<IEnumerable<Guid>> GetQuestsForCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает список идентификаторов квестов, для которых требуется указанный предмет.
        /// </summary>
        /// <param name="itemId">Идентификатор предмета.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Коллекция идентификаторов квестов.</returns>
        Task<IEnumerable<Guid>> GetQuestsForItemAsync(string itemId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Удаляет квест из отслеживания (например, при его завершении или провале).
        /// </summary>
        /// <param name="questId">Идентификатор квеста.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task RemoveQuestAsync(Guid questId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Регистрирует предмет как необходимый для завершения квеста.
        /// </summary>
        /// <param name="questId">Идентификатор квеста.</param>
        /// <param name="itemId">Идентификатор требуемого предмета.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task AddRequiredItemAsync(Guid questId, string itemId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Привязывает квест к кампании.
        /// </summary>
        /// <param name="questId">Идентификатор квеста.</param>
        /// <param name="campaignId">Идентификатор кампании.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task SetCampaignAsync(Guid questId, Guid campaignId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Возвращает идентификатор кампании, к которой относится указанный квест.
        /// </summary>
        /// <param name="questId">Идентификатор квеста.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <returns>Идентификатор кампании или <c>null</c>, если привязка не задана.</returns>
        Task<Guid?> GetCampaignAsync(Guid questId, CancellationToken cancellationToken = default);
    }
}