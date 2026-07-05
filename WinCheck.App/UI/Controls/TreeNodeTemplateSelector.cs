using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml;

namespace WinCheck.UI.Controls;

public sealed class TreeNodeTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate GroupTemplate = BuildGroupTemplate();
    private static readonly DataTemplate ProcessTemplate = BuildProcessTemplate();

    protected override DataTemplate SelectTemplateCore(object item)
    {
        var node = (item as TreeViewNode)?.Content as TreeNode ?? item as TreeNode;
        if (node is { IsGroup: true }) return GroupTemplate;
        return ProcessTemplate;
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);

    private static DataTemplate BuildGroupTemplate()
    {
        var xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Border Background="{ThemeResource SubtleFillColorSecondaryBrush}"
                        BorderBrush="{ThemeResource SurfaceStrokeColorDefaultBrush}"
                        BorderThickness="0,0,0,1" Padding="8,5">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="52"/><ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="72"/><ColumnDefinition Width="86"/><ColumnDefinition Width="58"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Grid.ColumnSpan="3" FontWeight="SemiBold"
                                   Foreground="{ThemeResource AccentFillColorDefaultBrush}"
                                   TextTrimming="CharacterEllipsis" VerticalAlignment="Center">
                            <Run Text="{Binding Content.Company}"/><Run Text="  ("/><Run Text="{Binding Content.Count}"/><Run Text=")"/>
                        </TextBlock>
                        <TextBlock Grid.Column="3" Text="{Binding Content.TotalMemoryFormatted}"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}"
                                   HorizontalAlignment="Right" VerticalAlignment="Center"/>
                        <TextBlock Grid.Column="4" Text="{Binding Content.Count}"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}"
                                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Grid>
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)XamlReader.Load(xaml);
    }

    private static DataTemplate BuildProcessTemplate()
    {
        var xaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        BorderBrush="{ThemeResource SurfaceStrokeColorDefaultBrush}"
                        BorderThickness="0,0,0,1" Padding="8,3">
                    <Grid MinHeight="26">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="52"/><ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="72"/><ColumnDefinition Width="86"/><ColumnDefinition Width="58"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                            <Grid Width="16" Height="16">
                                <FontIcon Glyph="{Binding Content.Process.TagGlyph}" FontSize="14"
                                          Foreground="{Binding Content.Process.TagColor}"
                                          HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                <Image Source="{Binding Content.Process.IconSource}"
                                       Width="16" Height="16" Stretch="Uniform"/>
                            </Grid>
                        </StackPanel>
                        <TextBlock Grid.Column="1" Text="{Binding Content.Process.Name}"
                                   VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Grid.Column="2" Text="{Binding Content.Process.Id}"
                                   Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                   VerticalAlignment="Center" HorizontalAlignment="Right"/>
                        <TextBlock Grid.Column="3" Text="{Binding Content.Process.WorkingSetFormatted}"
                                   Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                   VerticalAlignment="Center" HorizontalAlignment="Right"/>
                        <TextBlock Grid.Column="4" Text="{Binding Content.Process.ThreadCount}"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}"
                                   VerticalAlignment="Center" HorizontalAlignment="Center"/>
                    </Grid>
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)XamlReader.Load(xaml);
    }
}
