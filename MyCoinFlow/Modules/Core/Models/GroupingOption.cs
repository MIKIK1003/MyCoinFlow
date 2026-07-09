namespace MyCoinFlow.Models
{
    public class GroupingOption
    {
        public string Label { get; }
        public string Key { get; }

        public GroupingOption(string label, string key)
        {
            Label = label;
            Key = key;
        }

        public override string ToString() => Label;
    }
}
