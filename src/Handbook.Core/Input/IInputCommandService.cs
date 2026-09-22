namespace Handbook.Core;

public interface IInputCommandService : IDisposable
{
    event Action<InputCommand>? Command;
    event Action<string>? Failed;
    bool Active { get; }
    void Start();
    void Stop();
    void SetBindings(IReadOnlyDictionary<InputCommand, InputBinding> bindings);
}


