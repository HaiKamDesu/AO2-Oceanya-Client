using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOBot_Testing.Structures
{
    public class Player
    {
        public bool IsCM { get; set; } // Whether the player has [CM] or not
        public int PlayerID { get; set; } // Numeric Player ID
        public string ICCharacterName { get; set; } = string.Empty; // The in-character name
        public string? OOCShowname { get; set; } // The OOC showname (optional)
    }
}
