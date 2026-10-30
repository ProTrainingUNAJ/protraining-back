
namespace Domain.Entities
{
    public static class Equipment
    {
        public const string Barbell = "barra con pesas";
        public const string BarbellEn = "barbell";
        public static bool IsBarbell(string? equipment) =>
            string.Equals(equipment, Barbell, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(equipment, BarbellEn, StringComparison.OrdinalIgnoreCase);
    }
}
