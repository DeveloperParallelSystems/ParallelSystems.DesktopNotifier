using System.Windows;
using System.Windows.Controls;
using ParallelSystems.DesktopNotifier.Models;
using ParallelSystems.DesktopNotifier.Services;

namespace ParallelSystems.DesktopNotifier.Views;

public partial class CatalogEditor : System.Windows.Controls.UserControl
{
    private DesktopApiClient? _api;
    private List<CatalogItem> _projects = [];
    private List<CatalogItem> _levels = [];
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
            var needsLevel = catalog == "package-names";
            ProjectLabel.Visibility = ProjectSelector.Visibility = needsProject ? Visibility.Visible : Visibility.Collapsed;
            LevelLabel.Visibility = LevelSelector.Visibility = needsLevel ? Visibility.Visible : Visibility.Collapsed;
            if (needsProject)
            {
                var projectTask = _api.GetCatalogAsync("projects");
                var levelTask = needsLevel ? _api.GetCatalogAsync("levels") : Task.FromResult(new List<CatalogItem>());
                await Task.WhenAll(projectTask, levelTask);
                _projects = await projectTask;
                _levels = await levelTask;
                ProjectSelector.ItemsSource = _projects;
                LevelSelector.ItemsSource = Array.Empty<CatalogItem>();
            }
            else
            {
                ProjectSelector.ItemsSource = null;
                LevelSelector.ItemsSource = null;
            }
            ItemsList.ItemsSource = await _api.GetCatalogAsync(Catalog);
            ProjectSelector.SelectedItem = null;
            LevelSelector.SelectedItem = null;
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
        if (item is not null && (Catalog is "levels" or "package-names"))
        {
            ProjectSelector.SelectedItem = _projects.FirstOrDefault(project => project.Id == item.ProjectId);
            if (Catalog == "package-names")
                LevelSelector.SelectedItem = _levels.FirstOrDefault(level => level.Id == item.LevelId);
        }
        SaveButton.Content = ItemsList.SelectedItem is null ? "Create" : "Save changes";
    }

    private void ProjectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Catalog != "package-names") return;
        var projectId = (ProjectSelector.SelectedItem as CatalogItem)?.Id;
        LevelSelector.ItemsSource = _levels.Where(level => level.ProjectId == projectId).ToList();
        LevelSelector.SelectedItem = null;
    }

    private void NewClicked(object sender, RoutedEventArgs e)
    {
        ItemsList.SelectedItem = null;
        ProjectSelector.SelectedItem = null;
        LevelSelector.SelectedItem = null;
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
        var levelId = (LevelSelector.SelectedItem as CatalogItem)?.Id;
        if (Catalog == "levels" && projectId is null)
        {
            StatusText.Text = "Select a project for this level.";
            return;
        }
        if (Catalog == "package-names" && levelId is null)
        {
            StatusText.Text = "Select a level for this package name.";
            return;
        }
        IsBusy = true;
        EditorForm.IsEnabled = false;
        StatusText.Text = "Saving…";
        try
        {
            if (!await CheckAccessAsync()) return;
            var item = ItemsList.SelectedItem as CatalogItem;
            var saved = await _api.SaveCatalogItemAsync(Catalog, item?.Id, name, projectId, levelId);
            var items = await _api.GetCatalogAsync(Catalog);
            ItemsList.ItemsSource = items;
            ItemsList.SelectedItem = items.FirstOrDefault(entry => entry.Id == saved.Id);
            StatusText.Text = "Saved.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; await CheckAccessAsync(); }
        finally { IsBusy = false; EditorForm.IsEnabled = true; }
    }
}
