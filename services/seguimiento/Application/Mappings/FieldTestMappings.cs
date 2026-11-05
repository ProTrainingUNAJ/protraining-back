using Application.DTOs.FieldTests;
using Domain.Entities;

namespace Application.Mappings;

public static class FieldTestMappings
{
    public static FieldTestResultDto ToDto(FieldTestResult result) =>
        new(
            result.Id,
            result.CatalogTestId,
            result.AthleteId,
            result.Value,
            result.Attempt,
            result.RecordedAt);

    public static FieldTestSessionDto ToDto(
        FieldTestSession session,
        IReadOnlyList<FieldTestSessionTest> tests,
        IReadOnlyList<FieldTestResult> results) =>
        new(
            session.Id,
            session.CategoryId,
            session.Date,
            session.Name,
            session.Status.ToString(),
            tests.Select(test => new FieldTestSessionTestDto(test.CatalogTestId, test.Order)).ToList(),
            results.Select(ToDto).ToList());
}
