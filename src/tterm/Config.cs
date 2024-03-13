namespace tterm
{
    public class Config
    {
        public bool AllowTransparancy { get; set; }
        public int Columns { get; set; } = 82;
        public int Rows { get; set; } = 17;
        public Profile Profile { get; set; } = new Profile();
    }

}
