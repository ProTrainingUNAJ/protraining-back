using Application.DTOs.TrainingPlans;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.TrainingPlans.GetPlanPdf;
using Domain.Entities;
using Moq;
using Xunit;

namespace Tests;

public class TrainingPlanPdfUseCasesTests
{
    private readonly Mock<ITrainingPlanRepository> _planRepoMock = new();
    private readonly Mock<ITrainingPlanPdfGenerator> _pdfGeneratorMock = new();

    [Fact]
    public async Task Handle_DadoPlanExistente_CuandoSeEjecuta_EntoncesRetornaDtoConBytesYNombreSeguro()
    {
        // ARRANGE
        var planId = Guid.NewGuid();
        var plan = TrainingPlan.Create("Fuerza & Potencia: Pretemporada 2026/A", "Objetivo");
        var fakeBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 }; // %PDF

        _planRepoMock.Setup(r => r.GetWithDetailsNoTrackingByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        _pdfGeneratorMock.Setup(g => g.Generate(plan))
            .Returns(fakeBytes);

        var handler = new GetTrainingPlanPdfQueryHandler(_planRepoMock.Object, _pdfGeneratorMock.Object);

        // ACT
        var result = await handler.Handle(new GetTrainingPlanPdfQuery(planId));

        // ASSERT
        Assert.NotNull(result);
        Assert.Equal(fakeBytes, result.Content);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.EndsWith(".pdf", result.FileName);
        Assert.DoesNotContain("/", result.FileName);
        Assert.DoesNotContain(":", result.FileName);
    }

    [Fact]
    public async Task Handle_DadoPlanInexistenteUOtroPf_CuandoSeEjecuta_EntoncesLanzaNotFoundException()
    {
        // ARRANGE: Si la planificación pertenece a otro PF, el repositorio devuelve null por el filtro global
        var planId = Guid.NewGuid();
        _planRepoMock.Setup(r => r.GetWithDetailsNoTrackingByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingPlan?)null);

        var handler = new GetTrainingPlanPdfQueryHandler(_planRepoMock.Object, _pdfGeneratorMock.Object);

        // ACT & ASSERT: Debe lanzar NotFoundException (mapeada a HTTP 404)
        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetTrainingPlanPdfQuery(planId)));
        
        _pdfGeneratorMock.Verify(g => g.Generate(It.IsAny<TrainingPlan>()), Times.Never);
    }

    [Fact]
    public async Task Controller_GetPlanPdf_DadoPlanExistente_RetornaFileResultConContentTypePdf()
    {
        // ARRANGE
        var planId = Guid.NewGuid();
        var plan = TrainingPlan.Create("Rutina Potencia 2026", "Objetivo");
        var fakeBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 };

        _planRepoMock.Setup(r => r.GetWithDetailsNoTrackingByIdAsync(planId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        _pdfGeneratorMock.Setup(g => g.Generate(plan))
            .Returns(fakeBytes);

        var queryHandler = new GetTrainingPlanPdfQueryHandler(_planRepoMock.Object, _pdfGeneratorMock.Object);

        var controller = new API.Controllers.TrainingPlanController(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, queryHandler);

        // ACT
        var result = await controller.GetPlanPdf(planId);

        // ASSERT
        var fileResult = Assert.IsType<Microsoft.AspNetCore.Mvc.FileContentResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.Equal("Rutina_Potencia_2026.pdf", fileResult.FileDownloadName);
        Assert.Equal(fakeBytes, fileResult.FileContents);
    }

    [Fact]
    public void Controller_GetPlanPdf_TieneAtributoAuthorizeConRolPreparadorFisico()
    {
        var method = typeof(API.Controllers.TrainingPlanController).GetMethod(nameof(API.Controllers.TrainingPlanController.GetPlanPdf));
        Assert.NotNull(method);

        var authAttr = method!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .FirstOrDefault();

        Assert.NotNull(authAttr);
        Assert.Equal("PreparadorFisico", authAttr!.Roles);
    }
}
