using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MemoryGame.Models;
using System.Collections.Concurrent;

namespace MemoryGame.Hubs
{
    public class MemoryHub : Hub
    {
        private static readonly ConcurrentDictionary<string, Game> Games = new ConcurrentDictionary<string, Game>();
        private static readonly ConcurrentDictionary<string, System.Timers.Timer> GameTimers = new ConcurrentDictionary<string, System.Timers.Timer>();
        private readonly IHubContext<MemoryHub> _hubContext;

        public MemoryHub(IHubContext<MemoryHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task StartGameWithImages(string playerName, List<string> imagePaths, int timeForTurn)
        {
            var game = InitializeGame(imagePaths, timeForTurn);
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
            await _hubContext.Clients.Group(game.GameId).SendAsync("WaitingForOpponent", game);
        }

        public async Task JoinGame(string playerName)
        {
            var game = Games.Values.FirstOrDefault(g => !g.IsGameOver);
            if (game == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz o ID {Context.ConnectionId} ({playerName}) próbował dołączyć, ale nie ma dostępnych gier.");
                await _hubContext.Clients.Client(Context.ConnectionId).SendAsync("NoGameAvailable", "Brak dostępnej gry. Dodaj zestaw kart i utwórz nową rozgrywkę.");
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
            await _hubContext.Clients.Group(game.GameId).SendAsync("WaitingForOpponent", game);
        }

        public async Task FlipCard(string gameId, int cardId)
        {
            if (!Games.ContainsKey(gameId)) return;

            var game = Games[gameId];

            if (game.CurrentPlayerId != Context.ConnectionId)
            {
                var player = game.Players.First(p => p.ConnectionId == Context.ConnectionId);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz {player.Name} (ID: {Context.ConnectionId}) próbował odkryć kartę, ale to nie jego tura.");
                await _hubContext.Clients.Client(Context.ConnectionId).SendAsync("NotYourTurn");
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

            await _hubContext.Clients.Group(gameId).SendAsync("CardFlipped", game);

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
                await _hubContext.Clients.Group(gameId).SendAsync("GameOver", game);
            }
        }

        private Game InitializeGame(List<string> imagePaths, int timeForTurn)
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
                ImagePaths = imagePaths,
                TimeForTurn = timeForTurn
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

                int currentIndex = game.PlayerOrder.FindIndex(p => p.ConnectionId == game.CurrentPlayerId);
                int nextIndex = (currentIndex + 1) % game.PlayerOrder.Count;
                var nextPlayer = game.PlayerOrder[nextIndex];
                game.CurrentPlayerId = nextPlayer.ConnectionId;

                StartTurnTimer(game); // Uruchom timer dla nowej tury po niedopasowaniu

                await _hubContext.Clients.Group(gameId).SendAsync("TurnChanged", game);

                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Zmiana tury. Teraz tura gracza {nextPlayer.Name} (ID: {nextPlayer.ConnectionId}).");
            }

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat CardFlipped do grupy {game.GameId} po sprawdzeniu dopasowania.");
            await _hubContext.Clients.Group(gameId).SendAsync("CardFlipped", game);
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var game = Games.Values.FirstOrDefault(g => g.Players.Any(p => p.ConnectionId == Context.ConnectionId));
            if (game != null)
            {
                var player = game.Players.First(p => p.ConnectionId == Context.ConnectionId);

                if (game.Players.Count > 1 && player.ConnectionId == game.CurrentPlayerId)
                {
                    var nextPlayer = GetNextPlayer(game);
                    game.CurrentPlayerId = nextPlayer.ConnectionId;
                    StartTurnTimer(game);
                    await _hubContext.Clients.Group(game.GameId).SendAsync("TurnChanged", game);
                }
                game.Players.Remove(player);
                game.PlayerOrder.Remove(player);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz {player.Name} (ID: {Context.ConnectionId}) rozłączył się.");
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Serwer wysyła komunikat PlayerDisconnected do grupy {game.GameId}.");

                if (GameTimers.TryRemove(game.GameId, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                }

                await _hubContext.Clients.Group(game.GameId).SendAsync("PlayerDisconnected", player.Name, game);
                if (game.Players.Count == 0)
                {
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gra {game.GameId} została usunięta, brak graczy.");
                    Games.Remove(game.GameId, out game);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task StartGameManually(string gameId)
        {
            if (!Games.TryGetValue(gameId, out var game)) return;

            game.PlayerOrder = game.Players.OrderBy(_ => Guid.NewGuid()).ToList();
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Wylosowano kolejność graczy dla gry {gameId}: " +
                string.Join(", ", game.PlayerOrder.Select(p => p.Name)));

            game.CurrentPlayerId = game.PlayerOrder.FirstOrDefault()?.ConnectionId;

            if (game.CurrentPlayerId == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Brak graczy w grze {gameId}. Nie można rozpocząć gry.");
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Host uruchamia grę {gameId}.");
            StartTurnTimer(game);
            await _hubContext.Clients.Group(gameId).SendAsync("GameStarted", game);
        }

        private void StartTurnTimer(Game game)
        {
            if (GameTimers.TryRemove(game.GameId, out var oldTimer))
            {
                oldTimer.Stop();
                oldTimer.Dispose();
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Usunięto poprzedni timer dla gry {game.GameId}.");
            }

            var timer = new System.Timers.Timer(game.TimeForTurn * 1000);
            timer.Elapsed += async (sender, e) =>
            {
                timer.Stop();
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Czas minął dla gry {game.GameId}. Pomijam turę.");
                // Przekazujemy null jako callerConnectionId, ponieważ to timer wywołuje SkipTurn
                await SkipTurn(game.GameId, null);
            };
            timer.AutoReset = false;

            if (GameTimers.TryAdd(game.GameId, timer))
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Dodano nowy timer dla gry {game.GameId} na {game.TimeForTurn} sekund.");
            }

            timer.Start();

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Wysłano TurnTimerStarted dla gry {game.GameId} z czasem {game.TimeForTurn} sekund.");
            _hubContext.Clients.Group(game.GameId).SendAsync("TurnTimerStarted", game.TimeForTurn);
        }

        public async Task SkipTurn(string gameId, string callerConnectionId = null)
        {
            if (!Games.TryGetValue(gameId, out var game))
            {
                if (callerConnectionId != null)
                {
                    await _hubContext.Clients.Client(callerConnectionId).SendAsync("GameNotFound", "Gra o podanym ID nie istnieje.");
                }
                return;
            }

            if (game.Players.Count == 0)
            {
                await _hubContext.Clients.Group(gameId).SendAsync("GameOver", game);
                return;
            }

            var currentPlayer = game.Players.FirstOrDefault(p => p.ConnectionId == game.CurrentPlayerId);
            if (currentPlayer == null)
            {
                game.CurrentPlayerId = game.Players[0].ConnectionId;
                await _hubContext.Clients.Group(gameId).SendAsync("TurnChanged", game);
                return;
            }

            if (callerConnectionId != null && game.CurrentPlayerId != callerConnectionId)
            {
                await _hubContext.Clients.Client(callerConnectionId).SendAsync("NotYourTurn");
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Gracz {currentPlayer.Name} pominął turę.");

            var currentPlayerIndex = game.PlayerOrder.FindIndex(p => p.ConnectionId == game.CurrentPlayerId);
            var nextPlayerIndex = (currentPlayerIndex + 1) % game.PlayerOrder.Count;
            game.CurrentPlayerId = game.PlayerOrder[nextPlayerIndex].ConnectionId;
            game.Moves++;

            Console.WriteLine($"{DateTime.Now:HH:mm:ss} Nowa tura gracza: {game.PlayerOrder[nextPlayerIndex].Name}");

            StartTurnTimer(game);

            await _hubContext.Clients.Group(gameId).SendAsync("TurnChanged", game);
        }

        private Player GetNextPlayer(Game game)
        {
            var currentIndex = game.PlayerOrder.FindIndex(p => p.ConnectionId == game.CurrentPlayerId);
            var nextIndex = (currentIndex + 1) % game.PlayerOrder.Count;
            return game.PlayerOrder[nextIndex];
        }
    }
}