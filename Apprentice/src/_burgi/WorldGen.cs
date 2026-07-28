using System;
using System.Collections.Generic;
using System.Text;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace Apprentice.src._burgi
{
	internal class WorldGen
	{
		internal class Vulcanic
		{
			private static ICoreServerAPI? serverApi = null;

			private static IBlockAccessor? chunkGenBlockAccessor = null;
			private static IBlockAccessor? worldBlockAccessor = null;

			// public static void Register(ICoreServerAPI api)
			// {
			// 	serverApi = api;
			// 	serverApi.Event.ChunkColumnGeneration(OnChunkColumnGeneration, EnumWorldGenPass.Terrain, "standard");
			// 	serverApi.Event.GetWorldgenBlockAccessor(OnWorldGenBlockAccessor);
			// 
			// 	worldBlockAccessor = serverApi.World.BlockAccessor;
			// 
			// 	// int airId = serverApi.World.GetBlock(new AssetLocation("air")).BlockId;
			// 	// int stoneId = serverApi.World.GetBlock(new AssetLocation("rock-granite")).BlockId;
			// }

			public Vulcanic(ICoreServerAPI api)
			{
				serverApi = api;
				serverApi.Event.ChunkColumnGeneration(OnChunkColumnGeneration, EnumWorldGenPass.Terrain, "standard");
				serverApi.Event.GetWorldgenBlockAccessor(OnWorldGenBlockAccessor);
				
				worldBlockAccessor = serverApi.World.BlockAccessor;
				
				// int airId = serverApi.World.GetBlock(new AssetLocation("air")).BlockId;
				// int stoneId = serverApi.World.GetBlock(new AssetLocation("rock-granite")).BlockId;
			}

			private void OnChunkColumnGeneration(IChunkColumnGenerateRequest request)
			{
				if (worldBlockAccessor == null) return;

				BlockPos blockPosition = new(Dimensions.NormalWorld);

				int chunkSize = GlobalConstants.ChunkSize;
				int mapSizeY = worldBlockAccessor.MapSizeY;

				for (int i = 0; i < request.Chunks.Length; i++)
				{
					for (int x = 0; x < chunkSize; x++)
					{
						for (int z = 0; z < chunkSize; z++)
						{
							for (int y = 0; y < mapSizeY; y++)
							{
								blockPosition.X = (request.ChunkX * chunkSize) + x;
								blockPosition.Y = y;
								blockPosition.Z = (request.ChunkZ * chunkSize) + z;

								// float temp = climate.GetTemperature(pos);
								// float rain = climate.GetRainfall(pos);

								NormalizedSimplexNoise simplexNoise = NormalizedSimplexNoise.FromDefaultOctaves(4, 0.01, 0.5, 1337);

								double density = simplexNoise.Noise(blockPosition.X, blockPosition.Y, blockPosition.Z);

								if (density > 0.5)
								{
									worldBlockAccessor.SetBlock(0, blockPosition);
								}
							}
						}
					}
				}
			}
			private static void OnWorldGenBlockAccessor(IChunkProviderThread chunkProvider)
			{
				chunkGenBlockAccessor = chunkProvider.GetBlockAccessor(true);
			}
		}
	}
}
