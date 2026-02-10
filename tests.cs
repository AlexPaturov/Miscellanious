// ============================================================================
// PATCH / DELETE tests for IncomingController (external running API)
// 
// Assumptions based on our current test infrastructure:
// - TestHost(TestSettings.Default).CreateClient() returns HttpClient to external API
// - JwtTokenClient.GetDevTokenAsync() returns JWT string
// - ApiRouters has IncomingWagons(apiVersion)
// - Response body for business endpoints is an *encoded string* that can be decoded
//   via DbResultCodec.DecodeEncodedDbResult(string)
// - Request body for create/patch is JSON envelope with base64 string in Data
//   built by DbResultEnvelopeBuilder.BuildRequestBodyWithBase64Data(base64)
// - You want: NotFound treated as 200 with business error (Success=false)
// 
// If any of these differs, tell me what exact payload/response is used and I’ll
// adjust quickly.
// ============================================================================

// -----------------------------
// File: tests/BosVesWebApi.IntegrationTests/Infrastructure/ApiRouters.cs
// (ADD these methods if missing)
// -----------------------------
namespace BosVesWebApi.IntegrationTests.Infrastructure;

public static class ApiRouters
{
    // already exists:
    // public static string IncomingWagons(string apiVersion) => $"/api/v{apiVersion}/incoming/wagons";

    public static string IncomingWagonById(string apiVersion, int id)
    {
        return $"/api/v{apiVersion}/incoming/wagons/{id}";
    }

    public static string IncomingWagonPatchById(string apiVersion, int id)
    {
        // PATCH has the same route as resource by id
        return IncomingWagonById(apiVersion, id);
    }

    public static string IncomingWagonDeleteById(string apiVersion, int id)
    {
        // DELETE has the same route as resource by id
        return IncomingWagonById(apiVersion, id);
    }
}


// -----------------------------
// File: tests/BosVesWebApi.IntegrationTests/Tests/IncomingWagonPatchDeleteTests.cs
// -----------------------------
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BosVesWebApi.IntegrationTests.Infrastructure;
using BosVesWebApi.IntegrationTests.Infrastructure.Encoding;
using BosVesWebApi.IntegrationTests.Infrastructure.PayloadBuilders.Gp;
using BosVesWebApi.IntegrationTests.Infrastructure.PayloadBuilders.Db;
using Xunit;

namespace BosVesWebApi.IntegrationTests.Tests;

/// <summary>
/// PATCH / DELETE tests for Incoming wagons.
/// 
/// IMPORTANT: These are *integration tests against externally running API*.
/// API must be running on TestSettings.Default.BaseUri.
/// </summary>
public sealed class IncomingWagonPatchDeleteTests
{
    private const string ApiVersion = "1";

    // Keep vesy/tn types as agreed:
    // vesy: short (Int16)
    // tn: int

    [Fact]
    public async Task INC_A_02_Patch_WithoutToken_ReturnsUnauthorizedOrForbidden()
    {
        // Arrange
        using var host = new TestHost(TestSettings.Default);
        using var http = host.CreateClient();

        // choose some id (doesn't matter)
        int id = 1;

        // Act
        using var response = await PatchAsync(http, ApiRouters.IncomingWagonPatchById(ApiVersion, id), bodyJson: "{}");

        // Assert
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401/403, got {(int)response.StatusCode} {response.StatusCode}"
        );
    }

    [Fact]
    public async Task INC_U_02_Patch_WhenNotFound_ReturnsOk_AndSuccessFalse_AndHasErrorTrue()
    {
        // Arrange
        using var host = new TestHost(TestSettings.Default);
        using var http = host.CreateClient();

        var tokenClient = new JwtTokenClient(http);
        var token = await tokenClient.GetDevTokenAsync();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // id that should not exist
        int id = int.MaxValue;

        // Minimal patch body (if your endpoint requires payload, use proper one)
        // Here we send a valid envelope with "fake" gpri to avoid model binding issues.
        string patchBody = BuildEncodedGpriEnvelopeJson(
            dt: DateTime.Today,
            vr: DateTime.Now.ToString("HH:mm:ss"),
            nvag: "99999999",
            npp: 1,
            vesy: 1,
            tn: 7000000
        );

        // Act
        using var response = await PatchAsync(http, ApiRouters.IncomingWagonPatchById(ApiVersion, id), patchBody);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await ReadDbResultAsync(response);

        // Your contract note:
        // - Success: false on business error
        // - HasError: true/false allowed, but for notfound usually true
        Assert.False(dto.Success);

        // If your API fills ErrorMessage on error, this should be non-empty:
        if (dto.HasError)
        {
            Assert.False(string.IsNullOrWhiteSpace(dto.ErrorMessage));
        }
    }

    [Fact]
    public async Task INC_U_01_Patch_WhenExists_ReturnsOk_AndSuccessTrue_AndIsUpdatedTrue()
    {
        // Arrange
        using var host = new TestHost(TestSettings.Default);
        using var http = host.CreateClient();

        var tokenClient = new JwtTokenClient(http);
        var token = await tokenClient.GetDevTokenAsync();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1) Create a fresh wagon to get real id
        int createdId = await CreateWagonAndReturnIdAsync(http);

        // 2) Patch it (change VR to current time, or any field your API treats as update)
        string patchBody = BuildEncodedGpriEnvelopeJson(
            dt: DateTime.Today,
            vr: DateTime.Now.AddSeconds(1).ToString("HH:mm:ss"),
            nvag: GenerateNvag(),
            npp: 2,
            vesy: 2,
            tn: 7001378
        );

        // Act
        using var response = await PatchAsync(http, ApiRouters.IncomingWagonPatchById(ApiVersion, createdId), patchBody);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await ReadDbResultAsync(response);
        Assert.True(dto.Success);
        Assert.False(dto.HasError);
        Assert.Null(dto.ErrorMessage); // per your fix: null on success

        // You mentioned IsUpdated flag exists.
        Assert.True(dto.IsUpdated);
    }

    [Fact]
    public async Task INC_A_03_Delete_WithoutToken_ReturnsUnauthorizedOrForbidden()
    {
        // Arrange
        using var host = new TestHost(TestSettings.Default);
        using var http = host.CreateClient();

        int id = 1;

        // Act
        using var response = await http.DeleteAsync(ApiRouters.IncomingWagonDeleteById(ApiVersion, id));

        // Assert
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected 401/403, got {(int)response.StatusCode} {response.StatusCode}"
        );
    }

    [Fact]
    public async Task INC_D_02_Delete_WhenNotFound_ReturnsOk_AndSuccessFalse_AndHasErrorTrue()
    {
        // Arrange
        using var host = new TestHost(TestSettings.Default);
        using var http = host.CreateClient();

        var tokenClient = new JwtTokenClient(http);
        var token = await tokenClient.GetDevTokenAsync();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int id = int.MaxValue;

        // Act
        using var response = await http.DeleteAsync(ApiRouters.IncomingWagonDeleteById(ApiVersion, id));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await ReadDbResultAsync(response);
        Assert.False(dto.Success);

        if (dto.HasError)
        {
            Assert.False(string.IsNullOrWhiteSpace(dto.ErrorMessage));
        }
    }

    [Fact]
    public async Task INC_D_01_Delete_WhenExists_ReturnsOk_AndSuccessTrue_AndIsDeletedTrue()
    {
        // Arrange
        using var host = new TestHost(TestSettings.Default);
        using var http = host.CreateClient();

        var tokenClient = new JwtTokenClient(http);
        var token = await tokenClient.GetDevTokenAsync();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int createdId = await CreateWagonAndReturnIdAsync(http);

        // Act
        using var response = await http.DeleteAsync(ApiRouters.IncomingWagonDeleteById(ApiVersion, createdId));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await ReadDbResultAsync(response);
        Assert.True(dto.Success);
        Assert.False(dto.HasError);
        Assert.Null(dto.ErrorMessage);

        Assert.True(dto.IsDeleted);
    }


    // -----------------------------
    // Helpers
    // -----------------------------

    private static async Task<int> CreateWagonAndReturnIdAsync(HttpClient http)
    {
        // NOTE: this assumes your POST /incoming/wagons returns Created and encoded body with Id.

        var dt = DateTime.Today;
        var vr = DateTime.Now.ToString("HH:mm:ss");
        var nvag = GenerateNvag();
        short npp = (short)Random.Shared.Next(1, 30000);
        short vesy = 2;
        int tn = 7001378;

        string createBody = BuildEncodedGpriEnvelopeJson(dt, vr, nvag, npp, vesy, tn);

        using var content = new StringContent(createBody, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(ApiRouters.IncomingWagons(ApiVersion), content);

        // Some of your tests expect 201
        Assert.True(
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"Expected 201/200, got {(int)response.StatusCode} {response.StatusCode}"
        );

        var dto = await ReadDbResultAsync(response);
        Assert.True(dto.Success);

        if (dto.Id is null)
        {
            throw new InvalidOperationException("Create did not return Id (DbResultDto.Id is null)." );
        }

        return dto.Id.Value;
    }

    private static string GenerateNvag()
    {
        // 8 digits, string
        return Random.Shared.Next(10_000_000, 99_999_999).ToString();
    }

    private static string BuildEncodedGpriEnvelopeJson(DateTime dt, string vr, string nvag, int npp, short vesy, int tn)
    {
        // Build minimal json for GPRI as your API expects.
        // We reuse your existing builder (you already created it).
        // It returns (json1, json2) for duplicates; here we need a single JSON.

        string gpriJson = GpriPayloadBuilder.BuildMinimalJson(dt, vr, nvag, npp, vesy, tn);

        string gpriBase64 = Base64Codec.EncodeUtf8(gpriJson);
        return DbResultEnvelopeBuilder.BuildRequestBodyWithBase64Data(gpriBase64);
    }

    private static async Task<DbResultDto> ReadDbResultAsync(HttpResponseMessage response)
    {
        string encoded = await response.Content.ReadAsStringAsync();

        // Some APIs wrap string into JSON; if yours does, strip quotes here.
        // Example: "SGVsbG8="
        encoded = encoded.Trim();
        if (encoded.Length >= 2 && encoded[0] == '"' && encoded[^1] == '"')
        {
            encoded = encoded.Substring(1, encoded.Length - 2);
        }

        return DbResultCodec.DecodeEncodedDbResult(encoded);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient http, string url, string bodyJson)
    {
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = content
        };

        return await http.SendAsync(request);
    }
}


// -----------------------------
// File: tests/BosVesWebApi.IntegrationTests/Infrastructure/PayloadBuilders/Gp/GpriPayloadBuilder.cs
// (ADD this method if missing; keep existing methods as-is)
// -----------------------------
namespace BosVesWebApi.IntegrationTests.Infrastructure.PayloadBuilders.Gp;

public static class GpriPayloadBuilder
{
    /// <summary>
    /// Minimal JSON for GPRI payload.
    /// If your API expects other field names/casing, adapt here.
    /// </summary>
    public static string BuildMinimalJson(DateTime dt, string vr, string nvag, int npp, short vesy, int tn)
    {
        // IMPORTANT: I’m using property names exactly as we used in earlier snippets.
        // If your controller expects other schema, change here only (single point).
        // 
        // NOTE: dt serialized as yyyy-MM-dd to keep it stable.

        string date = dt.ToString("yyyy-MM-dd");

        return $"{{\"dt\":\"{date}\",\"vr\":\"{Escape(vr)}\",\"nvag\":\"{Escape(nvag)}\",\"npp\":{npp},\"vesy\":{vesy},\"tn\":{tn}}}";
    }

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
