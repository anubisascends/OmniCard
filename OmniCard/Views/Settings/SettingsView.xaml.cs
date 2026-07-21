using System;
using System.Windows;
using OmniCard.Views.Root;

namespace OmniCard.Views.Settings;

public partial class SettingsView : Window
{
    public SettingsView(RootViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (DataContext is RootViewModel rvm)
            await rvm.Settings.Load();
    }
}
