using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghmr.Core.Bridge;

public static class BridgeProtocol
{
    public const int Version = 1;
    public const string PipeName = "GHMR.Control.v1";
    public const int MaxMessageUtf8Bytes = 254;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(BridgeEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        string json = JsonSerializer.Serialize(message, JsonOptions);
        ValidateLine(json);
        return json;
    }

    public static BridgeEnvelope Deserialize(string json)
    {
        ValidateLine(json);
        return JsonSerializer.Deserialize<BridgeEnvelope>(json, JsonOptions)
               ?? throw new InvalidDataException("Bridge message is empty.");
    }

    private static void ValidateLine(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Bridge message is empty.");
        }

        if (json.Contains('\n') || json.Contains('\r'))
        {
            throw new InvalidDataException("Bridge messages must contain exactly one line.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxMessageUtf8Bytes)
        {
            throw new InvalidDataException(
                $"Bridge message exceeds {MaxMessageUtf8Bytes} UTF-8 bytes.");
        }
    }
}

public sealed record BridgeEnvelope
{
    public int Protocol { get; init; } = BridgeProtocol.Version;
    public string Type { get; init; } = string.Empty;
    public string MessageId { get; init; } = string.Empty;
    public string Game { get; init; } = string.Empty;
    public string BridgeSessionId { get; init; } = string.Empty;
    public string? RunId { get; init; }
    public string? MissionId { get; init; }
    public string? BridgeVersion { get; init; }
    public string? ExecutableVersion { get; init; }
    public string? Reason { get; init; }
}

public static class BridgeEventTypes
{
    public const string BridgeReady = "bridgeReady";
    public const string GameReady = "gameReady";
    public const string MissionPrepared = "missionPrepared";
    public const string PlayerControlGained = "playerControlGained";
    public const string PlayerControlLost = "playerControlLost";
    public const string MissionFailed = "missionFailed";
    public const string MissionCompleted = "missionCompleted";
    public const string Heartbeat = "heartbeat";
    public const string BridgeError = "bridgeError";
}

public static class ControllerCommandTypes
{
    public const string PrepareMission = "prepareMission";
    public const string StartMission = "startMission";
    public const string RestartMission = "restartMission";
    public const string ReleaseBridge = "releaseBridge";
    public const string AbortRun = "abortRun";
    public const string Ping = "ping";
}
