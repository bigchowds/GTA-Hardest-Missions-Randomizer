namespace Ghmr.Core.Launch;

public interface IGameProcessExitMonitor
{
    // True means the identified gameplay process exited. False means no unique
    // process could be observed; uncertainty must not invalidate a running game.
    Task<bool> WaitForExitAsync(
        GameLaunchProfile profile,
        CancellationToken cancellationToken = default);
}
