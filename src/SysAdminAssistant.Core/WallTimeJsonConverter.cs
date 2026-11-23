using System.Text.Json;
using System.Text.Json.Serialization;
namespace SysAdminAssistant.Core;
public sealed class WallTimeJsonConverter:JsonConverter<DateTime>
{
 public override DateTime Read(ref Utf8JsonReader reader,Type t,JsonSerializerOptions options){var d=reader.GetDateTime();return d.Kind==DateTimeKind.Unspecified?d:AppTime.FromUtc(d.Kind==DateTimeKind.Utc?d:d.ToUniversalTime());}
 public override void Write(Utf8JsonWriter writer,DateTime d,JsonSerializerOptions options)=>writer.WriteStringValue(AppTime.ToUtc(d));
}
