using Metro.Application.Abstractions.Catalog;

namespace Metro.API.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Catalog").AllowAnonymous();

        group.MapGet("/lines", (string? status, string? operatorCode, ICatalogService catalog, CancellationToken ct) =>
                catalog.GetLinesAsync(status, operatorCode, ct))
            .WithName("GetLines")
            .WithSummary("UC07 — Danh sách tuyến metro và thứ tự ga trên tuyến")
            .Produces<LineCatalogResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/stations", (Guid? lineId, string? status, string? search, string? operatorCode, ICatalogService catalog, CancellationToken ct) =>
                catalog.GetStationsAsync(lineId, status, search, operatorCode, ct))
            .WithName("GetStations")
            .WithSummary("UC07 — Danh sách nhà ga metro (lọc theo tuyến, trạng thái, từ khóa)")
            .Produces<StationCatalogResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/journeys", (Guid? originStationId, Guid? destinationStationId, string? operatorCode, ICatalogService catalog, CancellationToken ct) =>
                catalog.SearchJourneysAsync(originStationId ?? Guid.Empty, destinationStationId ?? Guid.Empty, operatorCode, ct))
            .WithName("SearchJourneys")
            .WithSummary("UC08 — Tra cứu hành trình tối ưu giữa hai ga (BR-050, BR-051)")
            .Produces<JourneySearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/service-status", (Guid? lineId, Guid? stationId, string? operatorCode, ICatalogService catalog, CancellationToken ct) =>
                catalog.GetServiceStatusAsync(lineId, stationId, operatorCode, ct))
            .WithName("GetServiceStatus")
            .WithSummary("UC10 — Tình trạng dịch vụ, sự cố, thông báo và ETA cập nhật tay (BR-052)")
            .Produces<ServiceStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
