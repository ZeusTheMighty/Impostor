﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Impostor.Server.Data;
using Impostor.Server.Net.Redirector;
using Impostor.Server.Net.State;
using Impostor.Shared.Innersloth;
using Impostor.Shared.Innersloth.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impostor.Server.Net.Manager
{
    internal class GameManager
    {
        private readonly ILogger<GameManager> _logger;
        private readonly INodeProvider _nodeProvider;
        private readonly IPEndPoint _publicIp;
        private readonly ConcurrentDictionary<int, Game> _games;

        public GameManager(ILogger<GameManager> logger, IOptions<ServerConfig> config, INodeProvider nodeProvider)
        {
            _logger = logger;
            _nodeProvider = nodeProvider;
            _publicIp = new IPEndPoint(IPAddress.Parse(config.Value.PublicIp), config.Value.PublicPort);
            _games = new ConcurrentDictionary<int, Game>();
        }
        
        public Game Create(GameOptionsData options)
        {
            // TODO: Prevent duplicates when using server redirector using INodeProvider.
            
            var gameCode = GameCode.GenerateCode(6);
            var gameCodeStr = GameCode.IntToGameName(gameCode);
            var game = new Game(this, _nodeProvider, _publicIp, gameCode, options);

            if (_nodeProvider.Find(gameCodeStr) == null && 
                _games.TryAdd(gameCode, game))
            {
                _nodeProvider.Save(gameCodeStr, _publicIp);
                _logger.LogDebug("Created game with code {0} ({1}).", game.CodeStr, gameCode);        
                return game;
            }

            _logger.LogWarning("Failed to create game.");
            return null;
        }

        public Game Find(int gameCode)
        {
            _games.TryGetValue(gameCode, out var game);
            return game;
        }

        public IEnumerable<Game> FindListings(byte mapId, int impostorCount, GameKeywords language, int count = 10)
        {
            var results = 0;
            
            // Find games that have not started yet.
            foreach (var (code, game) in _games.Where(x => 
                x.Value.IsPublic &&
                x.Value.GameState == GameStates.NotStarted && 
                x.Value.PlayerCount < 10)) // TODO: Do "< x.Value.Options.MaxPlayers" when GameData packets are done.
            {
                // Check for options.
                // TODO: Re-enable map filter when GameData packets are done.
                if (/* game.Options.MapId != mapId || */ 
                    game.Options.Keywords != language ||
                    (impostorCount != 0 && game.Options.NumImpostors != impostorCount))
                {
                    continue;
                }
                
                // Add to result.
                yield return game;

                // Break out if we have enough.
                if (++results == count)
                {
                    yield break;
                }
            }
        }

        public void Remove(int gameCode)
        {
            _logger.LogDebug("Remove game with code {0} ({1}).", GameCode.IntToGameName(gameCode), gameCode);
            _nodeProvider.Remove(GameCode.IntToGameName(gameCode));
            _games.TryRemove(gameCode, out _);
        }
    }
}