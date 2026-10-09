namespace Ember;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, $@"Global\Ember-{Environment.UserName}", out var firstInstance);
        if (!firstInstance) return;

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(Theme.Current == Theme.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);

        using var app = new TrayApp();
        Application.Run(app);
    }
}
