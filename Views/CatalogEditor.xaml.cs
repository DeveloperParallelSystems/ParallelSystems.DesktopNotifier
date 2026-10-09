using System.Windows;
using System.Windows.Controls;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class CatalogEditor : System.Windows.Controls.UserControl
{
    private DesktopApiClient? _api;
    public bool IsBusy { get; private set; }
    public event EventHandler? AccessLost;
    private string Catalog => ((ComboBoxItem)CatalogSelector.SelectedItem).Tag.ToString()!;

    public CatalogEditor() => InitializeComponent();

    public async Task InitializeAsync(DesktopApiClient api)
    {
        _api = api;
        await ReloadAsync();
    }

    private async Task<bool> CheckAccessAsync()
    {
        try
        {
            if ((await _api!.GetAccountAsync()).CanManageCatalogs) return true;
        }
        catch { /* Fail closed when the session cannot be verified. */ }
        ItemsList.ItemsSource = null;
        NameInput.Clear();
        AccessLost?.Invoke(this, EventArgs.Empty);
        return false;
    }

    private async Task ReloadAsync()
    {
        if (_api is null || IsBusy) return;
        IsBusy = true;
        EditorForm.IsEnabled = false;
        StatusText.Text = "Loading…";
        try
        {
            if (!await CheckAccessAsync()) return;
            ItemsList.ItemsSource = await _api.GetCatalogAsync(Catalog);
            NameInput.Clear();
            SaveButton.Content = "Create";
            StatusText.Text = "Select an item to edit, or enter a name to create one.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; await CheckAccessAsync(); }
        finally { IsBusy = false; EditorForm.IsEnabled = true; }
    }

    private async void CatalogChanged(object sender, SelectionChangedEventArgs e) => await ReloadAsync();
    private async void ReloadClicked(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void ItemChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NameInput is null) return;
        NameInput.Text = (ItemsList.SelectedItem as CatalogItem)?.Name ?? "";
        SaveButton.Content = ItemsList.SelectedItem is null ? "Create" : "Save changes";
    }

    private void NewClicked(object sender, RoutedEventArgs e)
    {
        ItemsList.SelectedItem = null;
        NameInput.Clear();
        NameInput.Focus();
    }

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (_api is null || IsBusy) return;
        var name = NameInput.Text.Trim();
        var maxLength = Catalog == "projects" ? 500 : 300;
        if (name.Length == 0 || name.Length > maxLength)
        {
            StatusText.Text = $"Enter a name of 1–{maxLength} characters.";
            return;
        }
        IsBusy = true;
        EditorForm.IsEnabled = false;
        StatusText.Text = "Saving…";
        try
        {
            if (!await CheckAccessAsync()) return;
            var item = ItemsList.SelectedItem as CatalogItem;
            var saved = await _api.SaveCatalogItemAsync(Catalog, item?.Id, name);
            var items = await _api.GetCatalogAsync(Catalog);
            ItemsList.ItemsSource = items;
            ItemsList.SelectedItem = items.FirstOrDefault(entry => entry.Id == saved.Id);
            StatusText.Text = "Saved.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; await CheckAccessAsync(); }
        finally { IsBusy = false; EditorForm.IsEnabled = true; }
    }
}
