namespace MyCoinFlow.Models
{
    public class StweSchluesselLine
    {
        public int Id { get; set; }
        public int SchluesselId { get; set; }

        public int EigentuemerId { get; set; }
        public string EigentuemerName { get; set; } = "";

        public decimal AnteilProzent { get; set; }  // 0..100
    }
}
