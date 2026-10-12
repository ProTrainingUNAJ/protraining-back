namespace Domain.Common;

public sealed class CatalogTest : BaseEntity
{
    public string Name { get; private set; }
    public string MotorCapacity { get; private set; }
    public string MeasurementUnit { get; private set; }
    public int DifficultyLevel { get; private set; }
    public bool Active { get; private set; }

    private CatalogTest() { }

    public CatalogTest(string name, string motorCapacity, string measurementUnit, int difficultyLevel = 1, bool active = true)
    {
        Name = name;
        MotorCapacity = motorCapacity;
        MeasurementUnit = measurementUnit;
        DifficultyLevel = difficultyLevel;
        Active = active;
    }

    public void Update(string name, string motorCapacity, string measurementUnit, int difficultyLevel, bool active)
    {
        Name = name;
        MotorCapacity = motorCapacity;
        MeasurementUnit = measurementUnit;
        DifficultyLevel = difficultyLevel;
        Active = active;
    }

    public void Deactivate() => Active = false;
}