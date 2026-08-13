using BlazorBlueprint.Components;
using Microsoft.AspNetCore.Components;

namespace OpenShock.RepositoryServer.Components;

/// <summary>
/// Shared plumbing for the admin pages: scoped service access, result reporting and confirmation
/// prompts.
/// </summary>
/// <remarks>
/// The scoping is the reason this exists. A circuit is one DI scope that lasts as long as the
/// browser tab, so an injected <c>RepoServerContext</c> would be shared by every action on the
/// page: a change tracker accumulating stale entities, and concurrent use that a DbContext does not
/// allow. Nothing here holds a service — each operation takes a fresh scope and disposes it. The
/// context factory is pooled, so a scope per click is cheap.
/// </remarks>
public abstract class AdminPageBase : ComponentBase
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = null!;
    [Inject] private ToastService Toasts { get; set; } = null!;
    [Inject] private DialogService Dialogs { get; set; } = null!;

    /// <summary>
    /// Opens a scope for one operation. Pair with <c>await using</c>, and resolve services from it
    /// with <see cref="AdminScopeExtensions.Get{T}"/>. A page needing two services in one operation
    /// should take them from the same scope so they share a DbContext and therefore a transaction
    /// boundary.
    /// </summary>
    protected AsyncServiceScope CreateScope() => ScopeFactory.CreateAsyncScope();

    /// <summary>
    /// Reports the outcome of an operation. <paramref name="error"/> being null means it succeeded,
    /// which mirrors how the admin services fold their result unions into a nullable message.
    /// </summary>
    protected void Report(string? error, string success)
    {
        if (error is not null)
        {
            ReportError(error);
            return;
        }

        Toasts.Success(success, string.Empty);
    }

    /// <summary>Reports a refusal the page decided on itself, without calling a service.</summary>
    protected void ReportError(string error) => Toasts.Error(error, string.Empty);

    /// <summary>
    /// Asks before something destructive. Every removal here is irreversible from the browser, and
    /// the rows are dense enough that a mis-click is easy.
    /// </summary>
    protected async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Delete")
    {
        var result = await Dialogs.ConfirmAsync(title, message, new ConfirmDialogOptions
        {
            ConfirmText = confirmText,
            CancelText = "Cancel",
            Destructive = true
        });

        // Confirmed rather than !Cancelled: dismissing with Escape or the overlay sets neither, and
        // that must not read as approval for a delete.
        return result.Confirmed;
    }
}

public static class AdminScopeExtensions
{
    /// <summary>Resolves a service from a scope opened by <see cref="AdminPageBase.CreateScope"/>.</summary>
    public static T Get<T>(this AsyncServiceScope scope) where T : notnull =>
        scope.ServiceProvider.GetRequiredService<T>();
}
