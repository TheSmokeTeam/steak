using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Steak.Host.Components.Layout;
using Steak.Host.Configuration;

namespace Steak.Tests.Host;

public sealed class ToastTrayComponentTests : IDisposable
{
    private readonly BunitContext _context = new();

    [Fact]
    public void ToastTray_RendersNotificationsAndAllowsDismiss()
    {
        var toastService = new UiToastService();

        _context.Services.AddLogging();
        _context.Services.AddSingleton<IUiToastService>(toastService);

        var cut = _context.Render<ToastTray>();

        toastService.ShowInfo("Consume stopped", "Stopped consuming orders after exporting 7 messages.");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Consume stopped", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("toast-dismiss", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find(".toast-dismiss").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Consume stopped", cut.Markup, StringComparison.Ordinal));
    }

    public void Dispose() => _context.Dispose();
}
