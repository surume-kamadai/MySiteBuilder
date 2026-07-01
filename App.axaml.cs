using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MySiteBuilder.ViewModels;
using MySiteBuilder.Views;

namespace MySiteBuilder;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ここでView（画面）とViewModel（データ）を結びつけて起動します。
            // 本物の画面（Inspector＋キャンバス）は Views 名前空間側。明示的に指定する。
            desktop.MainWindow = new Views.MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}