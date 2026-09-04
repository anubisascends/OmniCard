using System.Windows;
using System.Windows.Controls;

namespace OmniCard.Helpers;

/// <summary>
/// Shared "Delete Location" confirmation dialog: asks the user to confirm and whether to move the
/// location's cards into Bulk (default on) or delete them. Used by both the Manage Storage Locations
/// dialog and the location-tile context menu so the delete UX is identical everywhere.
/// </summary>
public static class DeleteLocationPrompt
{
    /// <summary>Shows the confirmation. Returns <c>true</c> if the user confirmed the delete, with
    /// <paramref name="moveToBulk"/> set to whether cards should be moved to Bulk. Returns
    /// <c>false</c> (and leaves <paramref name="moveToBulk"/> irrelevant) if cancelled.</summary>
    public static bool Confirm(Window? owner, string locationName, out bool moveToBulk)
    {
        var dialog = new Window
        {
            Title = "Delete Location",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("MaterialDesign.Brush.Background"),
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("MaterialDesign.Brush.Foreground"),
        };

        var moveCheckBox = new CheckBox
        {
            Content = "Move cards to Bulk",
            IsChecked = true,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var yesButton = new Button { Content = "Yes", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var noButton = new Button { Content = "No", Padding = new Thickness(16, 6, 16, 6), IsCancel = true };

        yesButton.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        noButton.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttonPanel.Children.Add(yesButton);
        buttonPanel.Children.Add(noButton);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Are you sure you want to delete \"{locationName}\"?", FontSize = 14 });
        panel.Children.Add(moveCheckBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        var confirmed = dialog.ShowDialog() == true;
        moveToBulk = moveCheckBox.IsChecked == true;
        return confirmed;
    }
}
