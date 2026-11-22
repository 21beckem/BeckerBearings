namespace BeckerBearings.App;

public partial class App : Application
{
    private readonly MainPage _page;
    public App(MainPage page)
    {
        try { InitializeComponent(); _page = page; }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "BeckerBearings-startup.log"), error + Environment.NewLine);
            throw;
        }
    }
    protected override Window CreateWindow(IActivationState? activationState) => new(_page);
}
