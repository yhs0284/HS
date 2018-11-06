using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CounselingChatBot
{
    public class UserProfile
    {
        public string Name { get; set; }

        public int SuicidalRisk = 0;

        public bool TrySuicide = true;
    }
}
