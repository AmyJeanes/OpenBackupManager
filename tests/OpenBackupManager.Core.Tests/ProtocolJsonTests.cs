using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Core.Tests;

public class ProtocolJsonTests
{
    // Nulls are left out when writing, so one without a default would be missing, and refused, when read
    [Test]
    public void EveryNullableParameter_DefaultsToNull()
    {
        // Every type the context reads, including those inside a message, such as ConfigProblem
        var withoutDefault = typeof(ProtocolJson).GetProperties()
            .Select(property => property.GetValue(ProtocolJson.Default))
            .OfType<JsonTypeInfo>()
            .SelectMany(message => message.Properties
                .Select(property => property.AssociatedParameter)
                .OfType<JsonParameterInfo>()
                .Where(parameter => parameter.IsNullable && !parameter.HasDefaultValue)
                .Select(parameter => $"{message.Type.Name}.{parameter.Name}"));

        Assert.That(withoutDefault, Is.Empty);
    }

    // As the engine replies on Linux when no store was chosen before
    [Test]
    public void AReplyWithANullLeftOut_IsRead()
    {
        var json = JsonSerializer.Serialize(new MigrateSecretsResult(0, [], "secrets.json"), ProtocolJson.Default.MigrateSecretsResult);

        Assert.That(JsonSerializer.Deserialize(json, ProtocolJson.Default.MigrateSecretsResult)?.From, Is.Null);
    }
}
