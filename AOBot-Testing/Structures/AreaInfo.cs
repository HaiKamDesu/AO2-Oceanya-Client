namespace AOBot_Testing.Structures
{
    public class AreaInfo
    {
        public AreaInfo(string name, int players, string status, string caseManager, string lockState)
        {
            Name = name;
            Players = players;
            Status = status;
            CaseManager = caseManager;
            LockState = lockState;
        }

        public string Name { get; set; }

        public int Players { get; set; }

        public string Status { get; set; }

        public string CaseManager { get; set; }

        public string LockState { get; set; }
    }
}
