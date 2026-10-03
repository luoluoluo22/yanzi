namespace OpenQuickHost.Sync;

internal sealed class DeviceMessageRejectedException(int statusCode) : InvalidOperationException("device_message_rejected:" + statusCode)
{
    public int StatusCode { get; } = statusCode;
}
