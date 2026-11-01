using System.Text.Json;
using NJsonSchema;

namespace LtiIntegrationGateway.Tests.Infrastructure.OpenApi;

/// <summary>
/// Пробный тест, фиксирующий фактический API валидации тела: компиляция JSON-схемы и проверка
/// документа, а также поведение ограничения глубины JSON.
/// </summary>
public class SchemaValidationProbeTests
{
    private const string Schema = """
    {
      "type": "object",
      "required": ["contractNumber"],
      "properties": {
        "contractNumber": { "type": "string" },
        "amount": { "type": "number", "minimum": 0 }
      },
      "additionalProperties": false
    }
    """;

    [Test]
    public async Task Schema_is_compiled_and_valid_document_passes()
    {
        var schema = await JsonSchema.FromJsonAsync(Schema);

        var errors = schema.Validate("""{ "contractNumber": "DKP-456", "amount": 10 }""");

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Schema_reports_missing_required_property()
    {
        var schema = await JsonSchema.FromJsonAsync(Schema);

        var errors = schema.Validate("""{ "amount": 10 }""");

        await Assert.That(errors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Schema_reports_unknown_property()
    {
        var schema = await JsonSchema.FromJsonAsync(Schema);

        var errors = schema.Validate("""{ "contractNumber": "x", "extra": 1 }""");

        await Assert.That(errors.Count > 0).IsTrue();
    }

    [Test]
    public async Task Json_depth_limit_rejects_deep_document()
    {
        var deep = new string('[', 100) + "1" + new string(']', 100);
        var rejected = false;

        try
        {
            JsonDocument.Parse(deep, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            rejected = true;
        }

        await Assert.That(rejected).IsTrue();
    }
}
