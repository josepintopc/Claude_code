namespace SupernovaBench.App.ViewModels;

/// <summary>Página que precisa saber quando aparece e quando sai da tela (ex.: liga/desliga uma medição).</summary>
public interface IPageLifecycle
{
    void Activated();
    void Deactivated();
}
