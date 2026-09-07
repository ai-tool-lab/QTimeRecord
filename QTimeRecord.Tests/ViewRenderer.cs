using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QTimeRecord.Tests;

/// <summary>
/// 画面を PNG に書き出して目視で確認するための道具。
/// レイアウト崩れは寸法の検証では見つからないため、実際に描いて見る。
/// </summary>
public static class ViewRenderer
{
    public static void SaveAsPng<TView>(object dataContext, string outputPath, int width, int height)
        where TView : FrameworkElement, new()
    {
        var thread = new Thread(() =>
        {
            EnsureApplicationResources();

            var view = new TView { DataContext = dataContext };

            // 2回通す。DataGrid の可変幅列は、1度配置してビューポートの幅が決まるまで
            // 伸びない。1回だけだと最小幅のまま描かれ、実際の画面と違うものを確認することになる。
            for (var pass = 0; pass < 2; pass++)
            {
                view.InvalidateMeasure();
                view.InvalidateArrange();

                view.Measure(new Size(width, height));
                view.Arrange(new Rect(0, 0, width, height));
                view.UpdateLayout();

                // 読み込み時の処理（DataGrid の列幅の確定など）を進める。
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = File.Create(outputPath);
            encoder.Save(stream);
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    /// <summary>
    /// 画面は App.xaml のリソース（色・書体・変換器）に依存する。
    /// テスト用の Application を1つ作って同じものを読み込ませる。
    /// </summary>
    private static void EnsureApplicationResources()
    {
        if (Application.Current is not null)
        {
            return;
        }

        // 本番と同じ書式で描く。既定のままだと曜日が英語になり、見た目の確認にならない。
        var culture = new System.Globalization.CultureInfo("ja-JP");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(
                System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        var application = new Application();

        // App.xaml と同じ辞書を読む。変換器や DataTemplate をここで作り直すと、
        // 画面を足したときに片方だけ古くなり、確認したものと本番の見た目がずれる。
        foreach (var name in new[] { "Colors", "Typography", "Buttons", "Inputs", "ViewMap" })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/QTimeRecord.App;component/Resources/{name}.xaml"),
            });
        }
    }
}
