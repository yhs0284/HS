using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChatbotHS
{
    public class UserProfile
    {
        public string Name {get; set;}

        public string FeelingIntent { get; set; }

        public int SuicidalRisk = 0;

        public string Frequency { get; set; }
    }
}
