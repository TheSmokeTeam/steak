namespace Steak.Host.Configuration;

internal enum UiToastKind
{
    Info,
    Success,
    Warning,
    Danger
}

internal sealed record UiToastNotification(
    Guid Id,
    string Title,
    string Message,
    UiToastKind Kind,
    DateTimeOffset CreatedAtUtc);

internal interface IUiToastService
{
    event EventHandler? StateChanged;

    IReadOnlyList<UiToastNotification> Notifications { get; }

    UiToastNotification Show(UiToastKind kind, string title, string message);

    UiToastNotification ShowInfo(string title, string message);

    UiToastNotification ShowSuccess(string title, string message);

    void Dismiss(Guid id);
}

internal sealed class UiToastService : IUiToastService
{
    private const int MaxNotifications = 5;
    private readonly object _gate = new();
    private readonly List<UiToastNotification> _notifications = [];

    public event EventHandler? StateChanged;

    public IReadOnlyList<UiToastNotification> Notifications
    {
        get
        {
            lock (_gate)
            {
                return _notifications.ToArray();
            }
        }
    }

    public UiToastNotification Show(UiToastKind kind, string title, string message)
    {
        var notification = new UiToastNotification(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(title) ? "Notice" : title.Trim(),
            string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim(),
            kind,
            DateTimeOffset.UtcNow);

        lock (_gate)
        {
            _notifications.Insert(0, notification);
            if (_notifications.Count > MaxNotifications)
            {
                _notifications.RemoveRange(MaxNotifications, _notifications.Count - MaxNotifications);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return notification;
    }

    public UiToastNotification ShowInfo(string title, string message)
        => Show(UiToastKind.Info, title, message);

    public UiToastNotification ShowSuccess(string title, string message)
        => Show(UiToastKind.Success, title, message);

    public void Dismiss(Guid id)
    {
        var removed = false;
        lock (_gate)
        {
            removed = _notifications.RemoveAll(notification => notification.Id == id) > 0;
        }

        if (removed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
