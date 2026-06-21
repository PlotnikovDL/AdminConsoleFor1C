using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AdminConsoleFor1C.App;

public sealed class AgentStatusKindToBrushConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<AgentStatusKind, string[]> ResourceKeys =
        new Dictionary<AgentStatusKind, string[]>
        {
            [AgentStatusKind.Neutral] =
            [
                "TextFillColorSecondaryBrush",
                "TextFillColorPrimaryBrush"
            ],
            [AgentStatusKind.Information] =
            [
                "TextFillColorSecondaryBrush",
                "TextFillColorPrimaryBrush"
            ],
            [AgentStatusKind.Success] =
            [
                "SystemFillColorSuccessBrush",
                "AccentTextFillColorPrimaryBrush",
                "TextFillColorPrimaryBrush"
            ],
            [AgentStatusKind.Attention] =
            [
                "SystemFillColorCautionBrush",
                "AccentTextFillColorPrimaryBrush",
                "TextFillColorPrimaryBrush"
            ],
            [AgentStatusKind.Critical] =
            [
                "SystemFillColorCriticalBrush",
                "TextFillColorPrimaryBrush"
            ]
        };

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var statusKind = value is AgentStatusKind kind
            ? kind
            : AgentStatusKind.Neutral;

        return ResolveBrush(statusKind);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static Brush ResolveBrush(AgentStatusKind statusKind)
    {
        if (!ResourceKeys.TryGetValue(statusKind, out var resourceKeys))
        {
            resourceKeys = ResourceKeys[AgentStatusKind.Neutral];
        }

        foreach (var resourceKey in resourceKeys)
        {
            if (Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true
                && resource is Brush brush)
            {
                return brush;
            }
        }

        return new SolidColorBrush(Colors.Gray);
    }
}
