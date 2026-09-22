namespace Handbook.Desktop;

public enum PresentationMode { Viewing, Browsing, Adjusting }
public interface IPresentationHost
{
    void Present(Handbook.Core.Entry? entry);
    void SetMode(PresentationMode mode);
    void HandleCommand(Handbook.Core.InputCommand command);
    void SetTopmost(bool enabled);
    void ToggleVisibility();
}


