using System;
using Vintagestory.API.Server;

namespace Apprentice
{
    /// <summary>
    /// Initializes progression storage for every class defined in
    /// class.json. No class ID is hard-coded here.
    /// </summary>
    internal sealed class ClassesManager : IDisposable
    {
        private readonly ICoreServerAPI serverApi;
        private readonly ClassConfig classConfig;

        public ClassesManager(
            ICoreServerAPI serverApi,
            ClassConfig classConfig)
        {
            this.serverApi = serverApi
                ?? throw new ArgumentNullException(
                    nameof(serverApi)
                );

            this.classConfig = classConfig
                ?? throw new ArgumentNullException(
                    nameof(classConfig)
                );

            serverApi.Event.PlayerJoin += OnPlayerJoin;
        }

        public void InitializePlayer(IServerPlayer player)
        {
            ArgumentNullException.ThrowIfNull(player);

            foreach (ClassDefinition classDefinition
                     in classConfig.ClassTypes.Values)
            {
                ProgressionData.GetOrCreateClassProgress(
                    player,
                    classDefinition.Id
                );
            }
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            InitializePlayer(player);
        }

        public void Dispose()
        {
            serverApi.Event.PlayerJoin -= OnPlayerJoin;
        }
    }
}
