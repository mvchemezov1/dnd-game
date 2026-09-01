namespace dnd_game.domain.value_objects
{
    /// <summary>
    /// Уровень освещения в игровой зоне.
    /// </summary>
    public enum LightLevel
    {
        /// <summary>Яркий свет — обычная видимость.</summary>
        Bright,

        /// <summary>Тусклый свет — слабое освещение, лёгкая помеха для восприятия.</summary>
        Dim,

        /// <summary>Темнота — полное отсутствие света, сильная помеха или невозможность видеть.</summary>
        Darkness
    }
}