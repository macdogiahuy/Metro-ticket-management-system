namespace Metro.Application.Abstractions.Network;

/// <summary>Stable machine-readable error for BE-13 admin network, schedule, load, ETA, and notice APIs.</summary>
public sealed class NetworkAdminException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
