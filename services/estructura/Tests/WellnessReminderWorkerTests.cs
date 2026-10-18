using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using API.BackgroundServices;
using Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Estructura.Tests;

public class WellnessReminderWorkerTests
{
    [Fact]
    public async Task Given_CurrentHourOutsideAllowedWindow_When_ProcessReminders_Then_DoesNotCreateScopeOrSendReminders()
    {
        // arrange: configuramos la ventana para que la hora actual de argentina quede garantizada afuera
        var currentHour = ArgentinaTime.Now.Hour;
        int windowStart;
        int windowEnd;

        if (currentHour < 12)
        {
            // si es de manana, configuramos ventana vespertina (16 a 20 hs)
            windowStart = 16;
            windowEnd = 20;
        }
        else
        {
            // si es de tarde o noche, configuramos ventana matutina (8 a 12 hs)
            windowStart = 8;
            windowEnd = 12;
        }

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Reminders:WindowStart", windowStart.ToString() },
            { "Reminders:WindowEnd", windowEnd.ToString() }
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockLogger = new Mock<ILogger<WellnessReminderWorker>>();

        var worker = new WellnessReminderWorker(mockScopeFactory.Object, configuration, mockLogger.Object);

        // act
        await worker.ProcessRemindersAsync(CancellationToken.None);

        // assert: al estar fuera de la ventana horaria, se omite de inmediato y no abre ningun scope
        mockScopeFactory.Verify(s => s.CreateScope(), Times.Never);
    }
}
