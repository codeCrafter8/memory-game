using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MemoryGame.Models;

namespace MemoryGame.Hubs
{
    public class MemoryHub : Hub
    {
        private static Dictionary<string, Game> Games = new Dictionary<string, Game>();

        public async Task StartGameWithImages(string playerName, List<string> imagePaths)
        {
            var game = InitializeGame(imagePaths);
            Games[game.GameId] = game;

            var player = new Player
            {
                ConnectionId = Context.ConnectionId,
                Name = playerName,
                Score = 0
            };
            game.Players.Add(player);
            game.CurrentPlayerId = player.ConnectionId;

            await Groups.AddToGroupAsync(Context.ConnectionId, game.GameId);
            await Clients.Group(game.GameId).SendAsync("WaitingForOpponent", game);
        }

        public async Task JoinGame(string playerName)
        {
            var game = Games.Values.FirstOrDefault(g => g.Players.Count < 2 && !g.IsGameOver);
            if (game == null)
            {
                await Clients.Caller.SendAsync("NoGameAvailable", "Brak dostępnej gry. Dodaj zestaw kart i utwórz nową rozgrywkę.");
                return;
            }

            var player = new Player
            {
                ConnectionId = Context.ConnectionId,
                Name = playerName,
                Score = 0
            };
            game.Players.Add(player);

            await Groups.AddToGroupAsync(Context.ConnectionId, game.GameId);
            await Clients.Group(game.GameId).SendAsync("GameUpdated", game);

            if (game.Players.Count == 2)
            {
                await Clients.Group(game.GameId).SendAsync("GameStarted", game);
            }
        }

        public async Task FlipCard(string gameId, int cardId)
        {
            if (!Games.ContainsKey(gameId)) return;

            var game = Games[gameId];

            if (game.CurrentPlayerId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("NotYourTurn");
                return;
            }

            var card = game.Cards.FirstOrDefault(c => c.Id == cardId);
            if (card == null || card.IsFlipped || card.IsMatched) return;

            card.IsFlipped = true;
            game.Moves++;

            await Clients.Group(gameId).SendAsync("CardFlipped", game);

            var flippedCards = game.Cards.Where(c => c.IsFlipped && !c.IsMatched).ToList();
            if (flippedCards.Count == 2)
            {
                await CheckMatch(gameId, flippedCards);
            }

            if (game.Cards.All(c => c.IsMatched))
            {
                game.IsGameOver = true;
                await Clients.Group(gameId).SendAsync("GameOver", game);
            }
        }

        private Game InitializeGame(List<string> imagePaths)
        {
            var cards = new List<Card>();
            int id = 0;

            foreach (var imagePath in imagePaths)
            {
                cards.Add(new Card { Id = id++, ImagePath = imagePath });
                cards.Add(new Card { Id = id++, ImagePath = imagePath });
            }

            cards = cards.OrderBy(x => Guid.NewGuid()).ToList();

            return new Game
            {
                GameId = Guid.NewGuid().ToString(),
                Cards = cards,
                Moves = 0,
                ImagePaths = imagePaths
            };
        }

        private async Task CheckMatch(string gameId, List<Card> flippedCards)
        {
            await Task.Delay(1000);

            var game = Games[gameId];
            var currentPlayer = game.Players.First(p => p.ConnectionId == game.CurrentPlayerId);

            if (flippedCards[0].ImagePath == flippedCards[1].ImagePath)
            {
                flippedCards.ForEach(c => c.IsMatched = true);
                currentPlayer.Score++;
            }
            else
            {
                flippedCards.ForEach(c => c.IsFlipped = false);
                var nextPlayer = game.Players.First(p => p.ConnectionId != game.CurrentPlayerId);
                game.CurrentPlayerId = nextPlayer.ConnectionId;
            }

            await Clients.Group(gameId).SendAsync("CardFlipped", game);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var game = Games.Values.FirstOrDefault(g => g.Players.Any(p => p.ConnectionId == Context.ConnectionId));
            if (game != null)
            {
                var player = game.Players.First(p => p.ConnectionId == Context.ConnectionId);
                game.Players.Remove(player);
                await Clients.Group(game.GameId).SendAsync("PlayerDisconnected", player.Name);
                if (game.Players.Count == 0)
                {
                    Games.Remove(game.GameId);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}