// -----------------------------
// File: tests/BosVesWebApi.IntegrationTests/Tests/IncomingWagonGetByDateTimeNvagTests.cs
// -----------------------------
using System.Net;
using System.Net.Http.Headers;
using BosVesWebApi.IntegrationTests.Infrastructure;
using Xunit;

namespace BosVesWebApi.IntegrationTests.Tests;

public sealed class IncomingWagonGetByDateTimeNvagTests
{
    private const string ApiVersion = "1";

    [Fact]
    public async Task INC_R_07_Get_ByDateTimeNvag_WhenExists_ReturnsOk_AndBodyNotEmpty()
    {
        // Arrange
        using var host = new TestHost(TestSettings.Default);
        using var http = host.CreateClient();

        var tokenClient = new JwtTokenClient(http);
        var token = await tokenClient.GetDevTokenAsync();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1) Create a fresh wagon (we need dt/vr/nvag/vesy to query it back)
        var dt = DateTime.Today;
        var vr = DateTime.Now.ToString("HH:mm:ss");
        var nvag = Random.Shared.Next(10_000_000, 99_999_999).ToString();
        var npp = Random.Shared.Next(1, 30_000);
        short vesy = 2;
        int tn = 7001378;

        // Reuse your existing Create body builder (the same one you used in create tests)
        string createBody = BuildEncodedGpriEnvelopeJson(dt, vr, nvag, npp, vesy, tn);

        using (var createContent = new StringContent(createBody, System.Text.Encoding.UTF8, "application/json"))
        using (var createResponse = await http.PostAsync(ApiRouters.IncomingWagons(ApiVersion), createContent))
        {
            Assert.True(
                createResponse.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                $"Expected 201/200 from Create, got {(int)createResponse.StatusCode} {createResponse.StatusCode}"
            );
        }

        // 2) GET by-date-time-nvag (max params)
        // IMPORTANT: parameter names may differ in your API. If they do, change them here only.
        var url =
            $"/api/v{ApiVersion}/incoming/wagons/by-date-time-nvag" +
            $"?dt={Uri.EscapeDataString(dt: dt.ToString("yyyy-MM-dd"))}" +
            $"&vr={Uri.EscapeDataString(vr)}" +
            $"&nvag={Uri.EscapeDataString(nvag)}" +
            $"&vesy={vesy}";

        // Act
        using var response = await http.GetAsync(url);

        // Assert (contract-minimum)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(body), "Expected non-empty response body for existing record.");
    }

    // -------------------------------------------------------
    // Keep this helper consistent with your existing builders.
    // If you already have it in another test class, reuse it.
    // -------------------------------------------------------
    private static string BuildEncodedGpriEnvelopeJson(DateTime dt, string vr, string nvag, int npp, short vesy, int tn)
    {
        string gpriJson = BosVesWebApi.IntegrationTests.Infrastructure.PayloadBuilders.Gp.GpriPayloadBuilder
            .BuildMinimalJson(dt, vr, nvag, npp, vesy, tn);

        string gpriBase64 = BosVesWebApi.IntegrationTests.Infrastructure.Encoding.Base64Codec.EncodeUtf8(gpriJson);

        return BosVesWebApi.IntegrationTests.Infrastructure.PayloadBuilders.Db.DbResultEnvelopeBuilder
            .BuildRequestBodyWithBase64Data(gpriBase64);
    }
}

using System.Globalization;

public static ApiValidationResult ValidateDate(string date, string parameterName = "date")
{
    if (string.IsNullOrWhiteSpace(date))
        return ApiValidationResult.Failure(
            parameterName,
            $"Параметр {parameterName} не может быть пустым",
            "не указана дата");

    // Разрешаем ISO + legacy. Серверная культура НЕ участвует.
    var formats = new[]
    {
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "dd.MM.yyyy",               // legacy
        "dd.MM.yyyy HH:mm:ss"       // если вдруг прилетает так
    };

    if (!DateTime.TryParseExact(
            date,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out var parsed))
    {
        return ApiValidationResult.Failure(
            parameterName,
            $"Неверный формат даты в параметре {parameterName}: {date}",
            "неверный формат даты",
            date);
    }

    return ApiValidationResult.Success(parsed, parameterName);
}

public async Task<IActionResult> GetWagonsByDate(
    [FromQuery, Required] string date,
    [FromQuery, Required] string vesy)
{
    var vDate = ApiValidator.ValidateDate(date, "date");
    if (!vDate.IsValid)
        return BadRequest(JsonHelper.SerializeAndEncode(DbResult<object>.Fail(vDate.ErrorMessage, vDate.UserMessage)));

    var result = await _data.GetByDt((DateTime)vDate.Value!, vesy);
    var encoded = JsonHelper.SerializeAndEncode(result);
    return result.Success ? Ok(encoded) : BadRequest(encoded);
}
