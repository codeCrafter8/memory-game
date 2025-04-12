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

            var ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "(nieznane IP)";
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz o ID {Context.ConnectionId} ({playerName}) z IP {ipAddress} przesłał zestaw {imagePaths.Count} kart.");

            await Groups.AddToGroupAsync(Context.ConnectionId, game.GameId);

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat WaitingForOpponent do gracza {playerName} (ID: {Context.ConnectionId}).");
            await Clients.Group(game.GameId).SendAsync("WaitingForOpponent", game);
        }

        public async Task JoinGame(string playerName)
        {
            var game = Games.Values.FirstOrDefault(g => !g.IsGameOver);
            if (game == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz o ID {Context.ConnectionId} ({playerName}) próbował dołączyć, ale nie ma dostępnych gier.");
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

            var ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "(nieznane IP)";
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz o ID {Context.ConnectionId} ({playerName}) z IP {ipAddress} dołączył do gry {game.GameId}.");

            await Groups.AddToGroupAsync(Context.ConnectionId, game.GameId);

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat WaitingForOpponent do grupy {game.GameId}.");
            await Clients.Group(game.GameId).SendAsync("WaitingForOpponent", game);
        }

        public async Task FlipCard(string gameId, int cardId)
        {
            if (!Games.ContainsKey(gameId)) return;

            var game = Games[gameId];

            if (game.CurrentPlayerId != Context.ConnectionId)
            {
                var player = game.Players.First(p => p.ConnectionId == Context.ConnectionId);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz {player.Name} (ID: {Context.ConnectionId}) próbował odkryć kartę, ale to nie jego tura.");
                await Clients.Caller.SendAsync("NotYourTurn");
                return;
            }

            var card = game.Cards.FirstOrDefault(c => c.Id == cardId);
            if (card == null || card.IsFlipped || card.IsMatched) return;

            card.IsFlipped = true;
            game.Moves++;

            var currentPlayer = game.Players.First(p => p.ConnectionId == Context.ConnectionId);
            var ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "(nieznane IP)";
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz {currentPlayer.Name} (ID: {Context.ConnectionId}) z IP {ipAddress} odkrył kartę o ID {cardId}.");
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat CardFlipped do grupy {game.GameId}.");

            await Clients.Group(gameId).SendAsync("CardFlipped", game);

            var flippedCards = game.Cards.Where(c => c.IsFlipped && !c.IsMatched).ToList();
            if (flippedCards.Count == 2)
            {
                await CheckMatch(gameId, flippedCards);
            }

            if (game.Cards.All(c => c.IsMatched))
            {
                game.IsGameOver = true;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gra {game.GameId} zakończona.");
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat GameOver do grupy {game.GameId}.");
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
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz {currentPlayer.Name} (ID: {currentPlayer.ConnectionId}) dopasował parę kart. Wynik: {currentPlayer.Score}.");
            }
            else
            {
                flippedCards.ForEach(c => c.IsFlipped = false);

                int currentIndex = game.Players.FindIndex(p => p.ConnectionId == game.CurrentPlayerId);
                int nextIndex = (currentIndex + 1) % game.Players.Count;
                var nextPlayer = game.Players[nextIndex];
                game.CurrentPlayerId = nextPlayer.ConnectionId;

                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Zmiana tury. Teraz tura gracza {nextPlayer.Name} (ID: {nextPlayer.ConnectionId}).");
            }

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat CardFlipped do grupy {game.GameId} po sprawdzeniu dopasowania.");
            await Clients.Group(gameId).SendAsync("CardFlipped", game);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var game = Games.Values.FirstOrDefault(g => g.Players.Any(p => p.ConnectionId == Context.ConnectionId));
            if (game != null)
            {
                var player = game.Players.First(p => p.ConnectionId == Context.ConnectionId);
                game.Players.Remove(player);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz {player.Name} (ID: {Context.ConnectionId}) rozłączył się.");
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat PlayerDisconnected do grupy {game.GameId}.");
                await Clients.Group(game.GameId).SendAsync("PlayerDisconnected", player.Name);
                if (game.Players.Count == 0)
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gra {game.GameId} została usunięta, brak graczy.");
                    Games.Remove(game.GameId);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task StartGameManually(string gameId)
        {
            if (!Games.TryGetValue(gameId, out var game)) return;

            game.CurrentPlayerId = game.Players.FirstOrDefault()?.ConnectionId;

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Host uruchamia grę {gameId}.");
            await Clients.Group(gameId).SendAsync("GameStarted", game);
        }

    }
}