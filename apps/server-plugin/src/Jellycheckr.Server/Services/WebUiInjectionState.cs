namespace Jellycheckr.Server.Services;

public interface IWebUiInjectionState
{
    bool IsRegistered { get; }

    void SetRegistered(bool isRegistered);
}

public sealed class WebUiInjectionState : IWebUiInjectionState
{
    private volatile bool _isRegistered;

    public bool IsRegistered => _isRegistered;

    public void SetRegistered(bool isRegistered)
    {
        _isRegistered = isRegistered;
    }
}
