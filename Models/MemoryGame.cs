using System.Numerics;

namespace MemoryGame.Models
{
    public class Game
    {
        public List<Card> Cards { get; set; }
        public string GameId { get; set; }
        public int Moves { get; set; }
        public bool IsGameOver { get; set; }
        public int TimeForTurn { get; set; }
        public List<Player> Players { get; set; } = new List<Player>();
        public string CurrentPlayerId { get; set; }
        public List<string> ImagePaths { get; set; }
    }

    public class Card
    {
        public int Id { get; set; }
        public string ImagePath { get; set; }
        public bool IsFlipped { get; set; }
        public bool IsMatched { get; set; }
    }

    public class Player
    {
        public string ConnectionId { get; set; }
        public string Name { get; set; }
        public int Score { get; set; }
    }
}
