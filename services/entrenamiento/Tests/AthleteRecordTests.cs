using Domain.Entities;
using Domain.Exceptions;
using System;
using Xunit;

namespace Entrenamiento.Tests;

public class AthleteRecordTests
{
    [Fact]
    public void Deactivate_DeberiaPasarAInactivo()
    {
        var record = AthleteRecord.Create(Guid.NewGuid(), Guid.NewGuid(), 100m, RecordSource.Tested, DateTime.UtcNow);

        record.Deactivate();

        Assert.False(record.IsActive);
    }

    [Fact]
    public void Create_ConPesoCero_DeberiaLanzarDomainException()
    {
        Assert.Throws<DomainException>(() =>
            AthleteRecord.Create(Guid.NewGuid(), Guid.NewGuid(), 0m, RecordSource.Tested, DateTime.UtcNow)
        );
    }
}