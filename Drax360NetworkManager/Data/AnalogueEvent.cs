namespace DraxTechnology.Data
{
    public class AnalogueEvent
    {
        public int Id { get; set; }
        public string Panel { get; set; }
        public int Node { get; set; }
        public int Loop { get; set; }
        public string Address { get; set; }
        public int Value { get; set; }
        public string DateCreated { get; set; }
    }
}
