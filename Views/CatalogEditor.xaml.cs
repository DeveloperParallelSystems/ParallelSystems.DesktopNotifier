using CheckBox = System.Windows.Controls.CheckBox;
using System.Windows;
using System.Windows.Controls;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class CatalogEditor : System.Windows.Controls.UserControl
{
    private DesktopApiClient? _api;
    private List<CatalogItem> _projects = [];
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
            if ((await _api!.GetPersonalInformationAsync(Environment.MachineName)).CanManageCatalogs) return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Unable to verify employee access. {ex.Message}";
        }
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
            var catalog = Catalog;
            var needsProject = catalog is "levels" or "package-names";
            ClientChoices.Visibility = catalog == "projects" ? Visibility.Visible : Visibility.Collapsed;
            ClientCheckboxes.Children.Clear();
            if (catalog == "projects") foreach (var client in await _api.GetClientsAsync())
                ClientCheckboxes.Children.Add(new CheckBox { Content = client.Name, Tag = client.Id, Margin = new Thickness(0,4,0,4) });
            ProjectLabel.Visibility = ProjectSelector.Visibility = needsProject ? Visibility.Visible : Visibility.Collapsed;
            _projects = needsProject ? await _api.GetCatalogAsync("projects") : [];
            ProjectSelector.ItemsSource = _projects;
            ItemsList.ItemsSource = await _api.GetCatalogAsync(Catalog);
            ProjectSelector.SelectedItem = null;
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
        var item = ItemsList.SelectedItem as CatalogItem;
        NameInput.Text = item?.Name ?? "";
        foreach (CheckBox choice in ClientCheckboxes.Children) choice.IsChecked = item?.ClientIds.Contains((Guid)choice.Tag) == true;
        if (item is not null && (Catalog is "levels" or "package-names"))
        {
            ProjectSelector.SelectedItem = _projects.FirstOrDefault(project => project.Id == item.ProjectId);
        }
        SaveButton.Content = ItemsList.SelectedItem is null ? "Create" : "Save changes";
    }

    private void NewClicked(object sender, RoutedEventArgs e)
    {
        ItemsList.SelectedItem = null;
        ProjectSelector.SelectedItem = null;
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
        var projectId = (ProjectSelector.SelectedItem as CatalogItem)?.Id;
        if ((Catalog is "levels" or "package-names") && projectId is null)
        {
            StatusText.Text = "Select a project.";
            return;
        }
        IsBusy = true;
        EditorForm.IsEnabled = false;
        StatusText.Text = "Saving…";
        try
        {
            if (!await CheckAccessAsync()) return;
            var item = ItemsList.SelectedItem as CatalogItem;
            var choices = ClientCheckboxes.Children.OfType<CheckBox>().ToList();
            var selectedClients = choices.Where(choice => choice.IsChecked == true).Select(choice => (Guid)choice.Tag)
                .Concat((item?.ClientIds ?? []).Where(id => !choices.Any(choice => (Guid)choice.Tag == id)))
                .Distinct().ToList();
            var saved = await _api.SaveCatalogItemAsync(Catalog, item?.Id, name, projectId,
                clientIds: Catalog == "projects" ? selectedClients : null);
            var items = await _api.GetCatalogAsync(Catalog);
            ItemsList.ItemsSource = items;
            ItemsList.SelectedItem = items.FirstOrDefault(entry => entry.Id == saved.Id);
            StatusText.Text = "Saved.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; await CheckAccessAsync(); }
        finally { IsBusy = false; EditorForm.IsEnabled = true; }
    }
}
